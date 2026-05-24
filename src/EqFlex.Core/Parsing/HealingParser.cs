using EqFlex.Core.Models;

namespace EqFlex.Core.Parsing;

public sealed class HealingParser
{
    private readonly string _playerName;

    public event Action<HealingRecord>? HealProcessed;

    public HealingParser(string playerName) => _playerName = playerName;

    public bool Process(string action, long timestamp)
    {
        if (action.Length < 23) return false;

        var index = action.LastIndexOf(" healed ", StringComparison.Ordinal);
        if (index < 0) return false;

        var record = HandleHealed(action, index, timestamp);
        if (record is null) return false;

        HealProcessed?.Invoke(record);
        return true;
    }

    private HealingRecord? HandleHealed(string part, int healedPos, long timestamp)
    {
        // Patterns:
        // X healed Y for N hit points [by Spell]. [(modifiers)]
        // X healed Y over time for N hit points [by Spell].
        // Y has been healed [over time] for N hit points by Spell.
        // Y have been healed [over time] for N hit points by Spell.

        var test = part[..healedPos];
        var healer = string.Empty;
        var healed = string.Empty;
        var isHot = false;
        var done = false;

        var previous = test.Length >= 2 ? test.LastIndexOf(' ', test.Length - 2) : -1;
        if (previous > -1)
        {
            if (test.IndexOf("are ", previous + 1, StringComparison.Ordinal) > -1)
            {
                done = true;
            }
            else if ((previous >= 1 && (test[previous - 1] == '.' || test[previous - 1] == '!')) ||
                     (previous >= 9 && test.IndexOf("fulfilled", previous - 9, StringComparison.Ordinal) > -1))
            {
                healer = test[(previous + 1)..];
            }
            else if (previous >= 3 && test.IndexOf("has been", previous - 3, StringComparison.Ordinal) > -1)
            {
                healed = test[..(previous - 4)];
                isHot = part.Length > healedPos + 17 &&
                        part.IndexOf("over time", healedPos + 8, 9, StringComparison.Ordinal) > -1;
            }
            else if (previous >= 0 && test.IndexOf("has", previous, StringComparison.Ordinal) > -1)
            {
                healer = test[..previous];
            }
            else if (previous >= 4 && test.IndexOf("have been", previous - 4, StringComparison.Ordinal) > -1)
            {
                healed = test[..(previous - 5)];
                isHot = part.Length > healedPos + 17 &&
                        part.IndexOf("over time", healedPos + 8, 9, StringComparison.Ordinal) > -1;
            }
            else
            {
                var wardIdx = test.IndexOf("`s ward", StringComparison.OrdinalIgnoreCase);
                if (wardIdx > 0)
                    healer = test[..wardIdx];
                else
                    // Simple "EntityName healed Target" format — e.g. "a bloodbone mender healed you".
                    // None of the special patterns (has/have been/fulfilled/etc.) apply, so the
                    // entire text before " healed " is the healer name.
                    healer = test;
            }
        }
        else
        {
            healer = test[..healedPos];
        }

        if (done) return null;

        var amountIndex = -1;
        if (healed.Length == 0)
        {
            var afterHealed = healedPos + 8;
            var forIndex = part.IndexOf(" for ", afterHealed, StringComparison.Ordinal);
            if (forIndex < 1) return null;

            if (forIndex >= 9 && part.IndexOf("over time", forIndex - 9, StringComparison.Ordinal) > -1)
            {
                isHot = true;
                healed = part.Substring(afterHealed, forIndex - afterHealed - 10);
            }
            else
            {
                healed = part[afterHealed..forIndex];
            }
            amountIndex = forIndex + 5;
        }
        else
        {
            amountIndex = isHot ? healedPos + 22 : healedPos + 12;
        }

        if (amountIndex < 0) return null;

        var amountEnd = part.IndexOf(' ', amountIndex);
        if (amountEnd < 0) return null;

        var amount = ParserUtil.ParseUInt(part.AsSpan(amountIndex, amountEnd - amountIndex));
        if (amount == uint.MaxValue) return null;

        // Check for overheal: (N)
        uint overHeal = 0;
        var overEnd = -1;
        if (part.Length > amountEnd + 1 && part[amountEnd + 1] == '(')
        {
            overEnd = part.IndexOf(')', amountEnd + 2);
            if (overEnd > -1)
            {
                var ov = ParserUtil.ParseUInt(part.AsSpan(amountEnd + 2, overEnd - amountEnd - 2));
                if (ov != uint.MaxValue) overHeal = ov;
            }
        }

        // Extract spell name: "by Spell Name."
        string spell = string.Empty;
        var rest = overEnd > -1 ? overEnd : amountEnd;
        var byIdx = part.IndexOf(" by ", rest, StringComparison.Ordinal);
        if (byIdx > -1)
        {
            var periodIdx = part.LastIndexOf('.');
            if (periodIdx > -1 && periodIdx - byIdx - 4 > 0)
                spell = part.Substring(byIdx + 4, periodIdx - byIdx - 4);
        }

        if (string.IsNullOrEmpty(healed) || string.IsNullOrEmpty(healer) && string.IsNullOrEmpty(healed))
            return null;

        if (string.IsNullOrEmpty(healer))
        {
            if (spell.StartsWith("Theft of Essence", StringComparison.OrdinalIgnoreCase))
                healer = "Unknown";
            else
                return null;
        }

        if (healer.Length > 64) return null;

        healer = ParserUtil.ReplacePlayer(healer, _playerName, healed);
        healed = ParserUtil.ReplacePlayer(healed, _playerName, healer);

        // Modifiers: (Critical) / (Lucky Critical) etc.
        short modMask = -1;
        if (part.Length > 4 && part[^1] == ')')
        {
            var firstParen = part.LastIndexOf('(', part.Length - 4);
            if (firstParen > -1)
                modMask = LineModifiersParser.Parse(part.Substring(firstParen + 1, part.Length - 1 - firstParen - 1));
        }

        return new HealingRecord(
            Healer: ParserUtil.CapitalizeFirst(healer),
            Target: ParserUtil.CapitalizeFirst(healed),
            Spell: spell,
            Amount: amount,
            OverHeal: overHeal,
            IsHot: isHot,
            IsCritical: LineModifiersParser.IsCrit(modMask),
            Timestamp: timestamp);
    }
}
