using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;
using EqFlex.Core.Services;

namespace EqFlex.Core.Parsing;

public sealed class CastParser
{
    private readonly string _playerName;
    private readonly PlayerRegistry? _registry;
    private readonly ISpellDataService? _spells;

    public event Action<CastRecord>? CastDetected;
    public event Action<string>? ZoneChanged;

    public CastParser(string playerName, PlayerRegistry? registry = null, ISpellDataService? spells = null)
    {
        _playerName = playerName;
        _registry = registry;
        _spells = spells;
    }

    public bool Process(string action, long timestamp)
    {
        if (string.IsNullOrEmpty(action)) return false;

        var split = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 2) return false;

        // Skip lines with periods mid-word (NPC names sometimes) or trailing modifier parens
        if (split[0].Contains('.') || split[^1].EndsWith(')')) return false;

        // Zone entry: "You have entered ZoneName."
        if (split is ["You", "have", "entered", ..])
        {
            var zone = ParserUtil.JoinWords(split, 3, split.Length - 3).TrimEnd('.');
            if (!zone.StartsWith("an area", StringComparison.OrdinalIgnoreCase))
                ZoneChanged?.Invoke(zone);
            return true;
        }

        string? player = null;
        string? spellName = null;
        var isBeginCast = false;
        var isInterrupted = false;

        // "You activate SpellName."
        // "You begin casting SpellName."
        // "You begin singing SpellName."
        if (split[0] == "You")
        {
            player = _playerName;
            if (split[1] == "activate" && split.Length > 2)
            {
                spellName = ParseSpellName(split, 2);
            }
            else if (split[1] == "begin" && split.Length > 3)
            {
                if (split[2] == "casting") { spellName = ParseSpellName(split, 3); isBeginCast = true; }
                else if (split[2] == "singing") { spellName = ParseSpellName(split, 3); }
            }
        }
        // "PlayerName activates SpellName."
        else if (split[1] == "activates")
        {
            player = split[0];
            spellName = ParseSpellName(split, 2);
        }
        // "PlayerName begins casting/singing SpellName."
        // "PlayerName begins to cast/sing a spell/song. <OldSpellName>"
        else if (split.Length > 3)
        {
            var beginsIdx = Array.FindIndex(split, 1, split.Length - 1, s => s == "begins");
            if (beginsIdx > -1 && beginsIdx + 2 < split.Length)
            {
                if (split[beginsIdx + 1] == "casting")
                {
                    player = ParserUtil.JoinWords(split, 0, beginsIdx);
                    spellName = ParseSpellName(split, beginsIdx + 2);
                    isBeginCast = true;
                }
                else if (split[beginsIdx + 1] == "singing")
                {
                    player = ParserUtil.JoinWords(split, 0, beginsIdx);
                    spellName = ParseSpellName(split, beginsIdx + 2);
                }
                else if (split.Length > 5 && split[beginsIdx + 1] == "to")
                {
                    if (split[beginsIdx + 2] == "cast" && split.Length > beginsIdx + 4 && split[beginsIdx + 4] == "spell.")
                    {
                        player = ParserUtil.JoinWords(split, 0, beginsIdx);
                        spellName = ParseOldSpellName(split, beginsIdx + 5);
                        isBeginCast = true;
                    }
                    else if (split[beginsIdx + 2] == "sing" && split.Length > beginsIdx + 4 && split[beginsIdx + 4] == "song.")
                    {
                        player = ParserUtil.JoinWords(split, 0, beginsIdx);
                        spellName = ParseOldSpellName(split, beginsIdx + 5);
                    }
                }
            }
        }

        // "SpellName spell is interrupted." / "Your SpellName spell is interrupted."
        if (player is null && split.Length > 4 &&
            split[^1] == "interrupted." && split[^2] == "is" && split[^3] == "spell")
        {
            isInterrupted = true;
            if (split[0] == "Your")
            {
                player = _playerName;
                spellName = ParserUtil.JoinWords(split, 1, split.Length - 4);
            }
            else if (split[0].Length > 2 && split[0][^1] == 's' && split[0][^2] == '\'')
            {
                player = split[0][..^2];
                spellName = ParserUtil.JoinWords(split, 1, split.Length - 4);
            }
        }

        if (player is null || spellName is null) return false;

        var caster = ParserUtil.CapitalizeFirst(player);

        // Register casters as verified players only if the name looks like a player name.
        // IsPossiblePlayerName rejects multi-word names like "A Darkfell shaman" — NPCs cast
        // spells constantly and must not pollute the registry or IsValidAttack will treat
        // subsequent damage to them as PvP and drop the fight entirely.
        if (!isInterrupted && _registry is not null
            && !caster.Equals(_playerName, StringComparison.OrdinalIgnoreCase)
            && PlayerRegistry.IsPossiblePlayerName(caster))
            _registry.AddVerifiedPlayer(caster);

        // Low-confidence class detection from class-exclusive spells (threshold: 8 observations)
        if (!isInterrupted && _registry is not null && _spells is not null && spellName is not null)
        {
            var isKnownPlayer = caster.Equals(_playerName, StringComparison.OrdinalIgnoreCase)
                || PlayerRegistry.IsPossiblePlayerName(caster);
            if (isKnownPlayer)
            {
                var cls = _spells.GetClassForSpell(spellName);
                if (cls is not null)
                    _registry.SetPlayerClass(caster, cls, highConfidence: false);
            }
        }

        CastDetected?.Invoke(new CastRecord(
            Caster: caster,
            Spell: spellName,
            IsBeginCast: isBeginCast,
            IsInterrupted: isInterrupted,
            Timestamp: timestamp));

        return true;
    }

    private static string ParseSpellName(string[] split, int startIndex)
    {
        var name = ParserUtil.JoinWords(split, startIndex, split.Length - startIndex);
        // Strip trailing period (but not "Rk." abbreviation endings)
        if (name.Length > 0 && name[^1] == '.' &&
            !(name.Length > 3 && name[^3..].StartsWith("Rk.", StringComparison.Ordinal)))
        {
            name = name[..^1];
        }
        return name;
    }

    private static string ParseOldSpellName(string[] split, int startIndex)
    {
        var name = ParserUtil.JoinWords(split, startIndex, split.Length - startIndex);
        return name.Trim('<', '>');
    }
}
