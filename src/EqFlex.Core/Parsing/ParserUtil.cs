using System.Globalization;

namespace EqFlex.Core.Parsing;

internal static class ParserUtil
{
    private static readonly HashSet<string> SecondPerson = new(StringComparer.OrdinalIgnoreCase)
        { "you", "yourself", "your" };

    private static readonly HashSet<string> ThirdPerson = new(StringComparer.OrdinalIgnoreCase)
        { "himself", "herself", "itself" };

    internal static string ReplacePlayer(string name, string playerName, string attacker)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (ThirdPerson.Contains(name))
            return !string.IsNullOrEmpty(attacker) ? attacker : name;

        if (SecondPerson.Contains(name))
            return playerName;

        return name;
    }

    internal static string UpdateAttacker(string attacker, string playerName, string subType)
    {
        attacker = StripTrailingPunct(attacker);

        if (string.IsNullOrEmpty(attacker))
        {
            attacker = subType;
        }
        else if (attacker.EndsWith("'s corpse", StringComparison.Ordinal) ||
                 attacker.EndsWith("`s corpse", StringComparison.Ordinal))
        {
            attacker = attacker[..^9];
        }
        else if (!string.IsNullOrEmpty(playerName))
        {
            attacker = ReplacePlayer(attacker, playerName, attacker);
        }

        return CapitalizeFirst(attacker);
    }

    internal static string UpdateDefender(string defender, string playerName, string attacker)
    {
        defender = StripTrailingPunct(defender);
        defender = ReplacePlayer(defender, playerName, attacker);
        return CapitalizeFirst(defender);
    }

    // Strip trailing comma/semicolon/exclamation that EQ appends in some line formats (e.g. "YOU,")
    internal static string StripTrailingPunct(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var end = s.Length - 1;
        while (end >= 0 && s[end] is ',' or ';' or '!')
            end--;
        return end < s.Length - 1 ? s[..(end + 1)] : s;
    }

    internal static int FindStop(string[] split)
    {
        var stop = split.Length - 1;
        if (!string.IsNullOrEmpty(split[stop]) && split[stop][^1] == ')')
        {
            for (var i = stop; i >= 0 && stop > 2; i--)
            {
                if (!string.IsNullOrEmpty(split[i]) && split[i][0] == '(')
                {
                    stop = i - 1;
                    break;
                }
            }
        }
        return stop;
    }

    internal static unsafe string JoinWords(string[] split, int start, int count)
    {
        if (count <= 0) return string.Empty;
        if (count == 1) return split[start] ?? string.Empty;

        var totalLength = 0;
        var wordCount = 0;
        for (var i = 0; i < count; i++)
        {
            var word = split[start + i];
            if (word is { Length: > 0 }) { totalLength += word.Length; wordCount++; }
        }
        totalLength += wordCount > 1 ? wordCount - 1 : 0;

        if (totalLength <= 256)
        {
            var buffer = stackalloc char[totalLength];
            var pos = 0;
            var first = true;
            for (var i = 0; i < count; i++)
            {
                var word = split[start + i];
                if (word is { Length: > 0 })
                {
                    if (!first) buffer[pos++] = ' ';
                    foreach (var c in word) buffer[pos++] = c;
                    first = false;
                }
            }
            return new string(buffer, 0, pos);
        }

        return string.Join(" ", split, start, count);
    }

    internal static uint ParseUInt(string[] split, int index) => ParseUInt(split[index].AsSpan());

    internal static uint ParseUInt(ReadOnlySpan<char> span, uint defValue = uint.MaxValue)
    {
        if (span.IsEmpty) return defValue;
        uint value = 0;
        foreach (var c in span)
        {
            var digit = (uint)(c - '0');
            if (digit > 9) return defValue;
            if (value > 429496729u || (value == 429496729u && digit > 5)) return defValue;
            value = value * 10 + digit;
        }
        return value;
    }

    internal static string CapitalizeFirst(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var first = char.ToUpper(text[0], CultureInfo.InvariantCulture);
        if (first == text[0]) return text;
        return string.Create(text.Length, (text, first), static (span, state) =>
        {
            span[0] = state.first;
            state.text.AsSpan(1).CopyTo(span[1..]);
        });
    }

    internal static string? GetHitType(string word) => word switch
    {
        "bash" => "bashes", "backstab" => "backstabs", "bite" => "bites",
        "claw" => "claws", "crush" => "crushes", "frenzy" => "frenzies",
        "gore" => "gores", "hit" => "hits", "kick" => "kicks",
        "learn" => "learns", "maul" => "mauls", "punch" => "punches",
        "pierce" => "pierces", "rend" => "rends", "shoot" => "shoots",
        "slash" => "slashes", "slam" => "slams", "slice" => "slices",
        "smash" => "smashes", "stab" => "stabs", "sting" => "stings",
        "strike" => "strikes", "sweep" => "sweeps",
        "bashes" or "backstabs" or "bites" or "claws" or "crushes" or "frenzies" or
        "gores" or "hits" or "kicks" or "learns" or "mauls" or "punches" or "pierces" or
        "rends" or "shoots" or "slashes" or "slams" or "slices" or "smashes" or "stabs" or
        "stings" or "strikes" or "sweeps" => word,
        _ => null
    };

    internal static bool IsHitTypeAddition(string word) => word is "frenzy" or "frenzies";
}
