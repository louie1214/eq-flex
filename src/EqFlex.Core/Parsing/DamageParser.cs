using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;
using EqFlex.Core.Services;

namespace EqFlex.Core.Parsing;

public sealed class DamageParser
{
    private readonly string _playerName;
    private readonly ISpellDataService _spells;
    private readonly PlayerRegistry _registry;

    // Slain queue: deferred until next line so fight expiry triggers correctly
    private readonly List<string> _slainQueue = [];
    private long _slainTime = long.MinValue;

    public event Action<DamageRecord>? DamageProcessed;
    public event Action<string, string>? EntitySlain; // (slain, killer)

    public DamageParser(string playerName, ISpellDataService spells, PlayerRegistry registry)
    {
        _playerName = playerName;
        _spells = spells;
        _registry = registry;
    }

    public bool Process(string action, long timestamp)
    {
        if (string.IsNullOrEmpty(action)) return false;

        var split = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 2) return false;

        FlushSlainQueue(timestamp);

        var stop = ParserUtil.FindStop(split);

        // Quick-exit: death messages ("X died.")
        if (stop >= 1 && split[stop] == "died.")
        {
            var deceased = ParserUtil.JoinWords(split, 0, stop);
            if (!string.IsNullOrEmpty(deceased))
                QueueSlain(ParserUtil.ReplacePlayer(deceased, _playerName, deceased), string.Empty, timestamp);
            return true;
        }

        var record = ParseLine(split, stop, timestamp);
        if (record is null) return false;

        DamageProcessed?.Invoke(record);
        return true;
    }

    private DamageRecord? ParseLine(string[] split, int stop, long timestamp)
    {
        var isYou = split[0] is "You" or "Your";

        int byIndex = -1, forIndex = -1, pointsOfIndex = -1, endDamage = -1, byDamage = -1;
        int fromDamage = -1, hasIndex = -1, haveIndex = -1, hitTypeIndex = -1, hitTypeAdd = -1;
        int slainIndex = -1, takenIndex = -1, tryIndex = -1, isIndex = -1, nonMeleeIndex = -1;
        int butIndex = -1, extraIndex = -1, harmedIndex = -1, absorbsIndex = -1, yourIndex = -1;
        int missType = -1;
        string? subType = null;
        var foundType = false;

        for (var i = 0; i <= stop; i++)
        {
            var word = split[i];
            if (string.IsNullOrEmpty(word)) continue;

            if (word[0] == '(') return null; // short-circuit: inside parens = overheal or modifier

            switch (word)
            {
                case "absorbs":
                    if (i > 2 && split[i - 1] == "skin" && split[i - 2] == "magical")
                        absorbsIndex = i - 2;
                    break;
                case "healed": case "casting":
                    return null;
                case "but":
                    butIndex = i;
                    break;
                case "are": case "is": case "was": case "were":
                    isIndex = i;
                    break;
                case "has":
                    hasIndex = i;
                    break;
                case "have":
                    haveIndex = i;
                    break;
                case "by":
                    byIndex = i;
                    if (slainIndex > -1) goto shortCircuit;
                    if (i > 4 && split[i - 1] == "damage") byDamage = i - 1;
                    break;
                case "from":
                    if (i > 3 && split[i - 1] == "damage")
                    {
                        fromDamage = i - 1;
                        if (pointsOfIndex > -1 && extraIndex > -1) goto shortCircuit;
                        if (stop > i + 1 && split[i + 1] == "your") yourIndex = i + 1;
                    }
                    break;
                case "damage.": case "damage":
                    if (i == stop) endDamage = i;
                    break;
                case "harmed":
                    if (i > 0 && split[i - 1] == "has") harmedIndex = i + 1;
                    break;
                case "non-melee":
                    nonMeleeIndex = i;
                    break;
                case "point": case "points":
                    if (stop >= i + 1 && split[i + 1] == "of")
                    {
                        pointsOfIndex = i;
                        if (i > 2 && split[i - 2] == "for") forIndex = i - 2;
                    }
                    break;
                case "blocks!":
                    if (stop == i && butIndex > -1 && i > tryIndex) missType = 0;
                    break;
                case "shield!": case "staff!":
                    if (i > 5 && stop == i && butIndex > -1 && i > tryIndex &&
                        split[i - 2] == "with" && split[i - 3].StartsWith("block", StringComparison.OrdinalIgnoreCase))
                        missType = 0;
                    break;
                case "dodge!": case "dodges!":
                    if (stop == i && butIndex > -1 && i > tryIndex) missType = 1;
                    break;
                case "miss!": case "misses!":
                    if (stop == i && butIndex > -1 && i > tryIndex) missType = 2;
                    break;
                case "parry!": case "parries!":
                    if (stop == i && butIndex > -1 && i > tryIndex) missType = 3;
                    break;
                case "INVULNERABLE!":
                    if (stop == i && butIndex > -1 && i > tryIndex) missType = 4;
                    break;
                case "riposte!": case "ripostes!":
                    if (stop == i && butIndex > -1 && i > tryIndex && split[^1] != "(Strikethrough)") missType = 5;
                    break;
                case "slain":
                    slainIndex = i;
                    break;
                case "taken":
                    if (i > 1 && (hasIndex == i - 1 || haveIndex == i - 1))
                    {
                        takenIndex = i - 1;
                        if (stop > i + 2 && split[i + 1] == "an" && split[i + 2] == "extra") extraIndex = i + 2;
                    }
                    break;
                default:
                    if (slainIndex == -1 && i > 0 && i < stop && tryIndex == -1 && !foundType)
                    {
                        var hitType = ParserUtil.GetHitType(word);
                        if (hitType is not null)
                        {
                            hitTypeIndex = i;
                            subType = hitType;
                            foundType = true;

                            if (i > 2 && split[i - 1] == "to" && (split[i - 2] == "tries" || split[i - 2] == "try"))
                                tryIndex = i - 2;

                            if (subType == "hits") { hitTypeIndex = i; foundType = false; }
                        }

                        if (hitTypeIndex > -1 && ParserUtil.IsHitTypeAddition(word)) hitTypeAdd = i;
                    }
                    break;
            }
        }

        shortCircuit:

        // ── Pattern: non-melee DS / thorn damage (X is pierced by Y's thorns for N pts non-melee damage)
        // "Y is [verb] by X's [name] for N points of non-melee damage."
        if (isIndex > -1 && byIndex > isIndex && (forIndex + 2) == pointsOfIndex && nonMeleeIndex > pointsOfIndex && endDamage > -1)
        {
            for (var i = byIndex + 1; i < forIndex; i++)
            {
                string? attacker = null;
                if (split[i] == "YOUR")
                {
                    attacker = _playerName;
                }
                else if (split[i].EndsWith("'s", StringComparison.OrdinalIgnoreCase) && (forIndex - byIndex - 2) > 0)
                {
                    var a = ParserUtil.JoinWords(split, byIndex + 1, forIndex - byIndex - 2);
                    attacker = a[..^2];
                }

                if (attacker is not null)
                {
                    var defender = ParserUtil.JoinWords(split, 0, isIndex);
                    var damage = ParserUtil.ParseUInt(split, pointsOfIndex - 1);
                    attacker = ParserUtil.UpdateAttacker(attacker, _playerName, "Damage Shield");
                    defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
                    return MakeRecord(split, stop, attacker, defender, damage, DamageType.DamageShield, "Damage Shield", false, timestamp);
                }
            }
        }

        // ── Pattern: bane/extra non-melee (Y has taken an extra N pts of non-melee damage from X's spell)
        if (extraIndex > -1 && pointsOfIndex == extraIndex + 2 && fromDamage == pointsOfIndex + 3 && split[stop] == "spell.")
        {
            var attackerSplit = split[fromDamage + 2];
            string? attacker = null;
            string? defender = null;

            if (attackerSplit.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            {
                attacker = split[fromDamage + 2][..^2];
                defender = ParserUtil.JoinWords(split, 0, takenIndex);
            }
            else if (attackerSplit == "your")
            {
                if (harmedIndex > 1 && split[harmedIndex - 2] == "has" && harmedIndex < takenIndex - 1)
                {
                    attacker = ParserUtil.JoinWords(split, 0, harmedIndex - 2);
                    defender = ParserUtil.JoinWords(split, harmedIndex, takenIndex - harmedIndex - 1).Trim('.');
                }
                else
                {
                    attacker = _playerName;
                    defender = ParserUtil.JoinWords(split, 0, takenIndex);
                }
            }

            if (attacker is not null && defender is not null)
            {
                var spell = ParserUtil.JoinWords(split, fromDamage + 3, stop - fromDamage - 3);
                var damage = ParserUtil.ParseUInt(split, extraIndex + 1);
                attacker = ParserUtil.UpdateAttacker(attacker, _playerName, spell);
                defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
                return MakeRecord(split, stop, attacker, defender, damage,
                    GetTypeFromSpell(spell, DamageType.Bane), spell, false, timestamp);
            }
        }

        // ── Pattern: melee with verb ("X slashes Y for N points of damage")
        if (!string.IsNullOrEmpty(subType) && isIndex == -1 &&
            pointsOfIndex == endDamage - 2 && forIndex > -1 && hitTypeIndex < forIndex && nonMeleeIndex == -1)
        {
            var mod = hitTypeAdd > 0 ? 1 : 0;
            var attacker = ParserUtil.JoinWords(split, 0, hitTypeIndex);
            var defender = ParserUtil.JoinWords(split, hitTypeIndex + mod + 1, forIndex - hitTypeIndex - mod - 1);
            var damage = ParserUtil.ParseUInt(split, pointsOfIndex - 1);
            attacker = ParserUtil.UpdateAttacker(attacker, _playerName, ParserUtil.CapitalizeFirst(subType));
            defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
            return MakeRecord(split, stop, attacker, defender, damage, DamageType.Melee, ParserUtil.CapitalizeFirst(subType), false, timestamp);
        }

        // ── Pattern: spell DD ("X hit Y for N points of fire damage by SpellName")
        if (byDamage > 3 && pointsOfIndex == byDamage - 3 && byIndex == byDamage + 1 &&
            forIndex > -1 && hitTypeIndex > 0 && split[hitTypeIndex] == "hit" &&
            hitTypeIndex < forIndex && split[stop].Length > 0 && split[stop][^1] == '.')
        {
            var spell = ParserUtil.JoinWords(split, byIndex + 1, stop - byIndex);
            if (!string.IsNullOrEmpty(spell) && spell[^1] == '.')
            {
                spell = spell[..^1];
                var attacker = ParserUtil.JoinWords(split, 0, hitTypeIndex);
                var defender = ParserUtil.JoinWords(split, hitTypeIndex + 1, forIndex - hitTypeIndex - 1);
                var damage = ParserUtil.ParseUInt(split, pointsOfIndex - 1);
                attacker = ParserUtil.UpdateAttacker(attacker, _playerName, spell);
                defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
                var dmgType = GetTypeFromSpell(spell, DamageType.Spell);
                return MakeRecord(split, stop, attacker, defender, damage, dmgType, spell, false, timestamp);
            }
        }

        // ── Pattern: DoT/received damage ("Y has taken N damage from SpellName by X")
        if (fromDamage > 3 && takenIndex == fromDamage - 3 && (byIndex > fromDamage || yourIndex > fromDamage || isYou))
        {
            string spell;
            string attacker;
            string? defender = null;

            if (byIndex > -1)
            {
                spell = ParserUtil.JoinWords(split, fromDamage + 2, byIndex - fromDamage - 2);
                attacker = ParserUtil.JoinWords(split, byIndex + 1, stop - byIndex);
                if (attacker == ".") attacker = spell;
                else if (!string.IsNullOrEmpty(attacker) && attacker[^1] == '.') attacker = attacker[..^1];
            }
            else if (yourIndex > -1)
            {
                // "Y has taken N damage from your SpellName." — player's DoT on NPC
                attacker = _playerName;
                spell = ParserUtil.JoinWords(split, yourIndex + 1, stop - yourIndex);
                if (!string.IsNullOrEmpty(spell) && spell[^1] == '.') spell = spell[..^1];
            }
            else
            {
                // "You have taken N damage from SpellName." — self (isYou)
                spell = ParserUtil.JoinWords(split, fromDamage + 2, stop - fromDamage - 1);
                if (!string.IsNullOrEmpty(spell) && spell[^1] == '.') spell = spell[..^1];
                attacker = spell;
            }

            if (!string.IsNullOrEmpty(attacker) && !string.IsNullOrEmpty(spell))
            {
                defender = ParserUtil.JoinWords(split, 0, takenIndex);
                var damage = ParserUtil.ParseUInt(split, fromDamage - 1);
                attacker = ParserUtil.UpdateAttacker(attacker, _playerName, spell);
                defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
                var spellData = _spells.GetByName(spell);
                // attackerIsSpell=true when attacker is a spell name, not a person (e.g. "from Flashbroil Singe III")
                var attackerIsSpell = !ReferenceEquals(attacker, _playerName) && attacker == spell;
                return MakeRecord(split, stop, attacker, defender, damage,
                    GetTypeFromSpell(spell, DamageType.Dot), spell, false, timestamp,
                    attackerIsSpell: attackerIsSpell);
            }
        }

        // ── Pattern: "Y has taken N damage by SpellName." (no "from", spell is the attacker)
        // e.g. "Brat has taken 4 damage by Feeblemind."
        // byDamage = position of "damage" (preceded immediately by "by" at byDamage+1)
        if (byDamage > -1 && takenIndex == byDamage - 3 && fromDamage == -1)
        {
            var defender = ParserUtil.JoinWords(split, 0, takenIndex);
            var damage = ParserUtil.ParseUInt(split, byDamage - 1);
            var spell = ParserUtil.JoinWords(split, byDamage + 2, stop - byDamage - 1);
            if (!string.IsNullOrEmpty(spell) && spell[^1] == '.') spell = spell[..^1];
            if (!string.IsNullOrEmpty(spell) && !string.IsNullOrEmpty(defender))
            {
                var attacker = ParserUtil.UpdateAttacker(spell, _playerName, spell);
                defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
                return MakeRecord(split, stop, attacker, defender, damage,
                    GetTypeFromSpell(spell, DamageType.Dot), spell, false, timestamp, attackerIsSpell: true);
            }
        }

        // ── Pattern: self-inflicted/DS reverse ("Y was chilled to the bone for N pts non-melee damage")
        if (isIndex > -1 && byIndex == -1 && (forIndex + 2) == pointsOfIndex && nonMeleeIndex > pointsOfIndex
            && split[stop].StartsWith("damage", StringComparison.OrdinalIgnoreCase))
        {
            var defender = ParserUtil.JoinWords(split, 0, isIndex);
            var damage = ParserUtil.ParseUInt(split, pointsOfIndex - 1);
            defender = ParserUtil.UpdateDefender(defender, _playerName, "Reverse Damage Shield");
            return MakeRecord(split, stop, "Reverse DS", defender, damage, DamageType.DamageShield, "Reverse DS", false, timestamp);
        }

        // ── Pattern: miss/dodge/block/parry ("X tries to hit Y but Y dodges!")
        // Also handles "X tries to frenzy on Y, but misses!" where hitTypeAdd is set for frenzy/frenzies
        // and we need to skip the extra "on" word before the target name.
        if (missType > -1 && tryIndex > -1 && hitTypeIndex > tryIndex && butIndex > hitTypeIndex)
        {
            var attacker = ParserUtil.JoinWords(split, 0, tryIndex);
            attacker = ParserUtil.UpdateAttacker(attacker, _playerName, "Miss");
            var defOffset = hitTypeAdd >= 0 ? 1 : 0; // skip "on" after frenzy/frenzies
            var defStart = hitTypeIndex + 1 + defOffset;
            var defender = ParserUtil.JoinWords(split, defStart, butIndex - defStart);
            defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
            var missLabel = missType switch { 0 => "Block", 1 => "Dodge", 3 => "Parry", 4 => "Invulnerable", 5 => "Riposte", _ => "Miss" };
            return MakeRecord(split, stop, attacker, defender, 0, DamageType.Melee, missLabel, true, timestamp);
        }

        // ── Pattern: slain ("X is slain by Y!" / "You have slain X!" / "You have been slain by X!")
        if (slainIndex > -1 && byIndex == slainIndex + 1 && isIndex > -1 && stop > slainIndex + 1)
        {
            // "X is slain by Y!"
            var killer = ParserUtil.JoinWords(split, byIndex + 1, stop - byIndex);
            if (killer.Length > 1 && killer[^1] == '!') killer = killer[..^1];
            var slain = ParserUtil.JoinWords(split, 0, isIndex);
            slain = ParserUtil.ReplacePlayer(slain, _playerName, slain);
            killer = ParserUtil.ReplacePlayer(killer, _playerName, killer);
            QueueSlain(slain, killer, 0);
            return null;
        }

        if (!isYou && slainIndex > -1 && byIndex == slainIndex + 1 && hasIndex > 0 && split[hasIndex + 1] == "been" && stop > slainIndex + 1)
        {
            var killer = ParserUtil.JoinWords(split, byIndex + 1, stop - byIndex);
            if (killer.Length > 1 && killer[^1] == '!') killer = killer[..^1];
            var slain = ParserUtil.JoinWords(split, 0, hasIndex);
            QueueSlain(slain, killer, 0);
            return null;
        }

        if (isYou && slainIndex == 3 && byIndex == 4 && split[1] == "have" && split[2] == "been" && stop > 4)
        {
            // "You have been slain by X!"
            var killer = ParserUtil.JoinWords(split, 5, stop - 4);
            if (killer.Length > 1 && killer[^1] == '!') killer = killer[..^1];
            QueueSlain(_playerName, killer, 0);
            return null;
        }

        if (isYou && slainIndex == 2 && split[1] == "have")
        {
            // "You have slain X!"
            var slain = ParserUtil.JoinWords(split, 3, stop - 2);
            if (slain.Length > 1 && slain[^1] == '!') slain = slain[..^1];
            QueueSlain(slain, _playerName, 0);
            return null;
        }

        // ── Pattern: absorb (0-damage record for shield absorption)
        if (absorbsIndex > -1 && split.Length > absorbsIndex + 6 &&
            split[absorbsIndex + 4] == "damage" && split[absorbsIndex + 5] == "of" &&
            split[stop - 1].EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            var defender = ParserUtil.JoinWords(split, 0, absorbsIndex);
            if (defender.EndsWith("'s", StringComparison.OrdinalIgnoreCase)) defender = defender[..^2];
            var attacker = ParserUtil.JoinWords(split, absorbsIndex + 6, stop - absorbsIndex - 6);
            attacker = attacker[..^2];
            attacker = ParserUtil.UpdateAttacker(attacker, _playerName, string.Empty);
            defender = ParserUtil.UpdateDefender(defender, _playerName, attacker);
            return MakeRecord(split, stop, attacker, defender, 0, DamageType.Other, "Absorb", false, timestamp);
        }

        return null;
    }

    private DamageRecord MakeRecord(string[] split, int stop, string attacker, string defender,
        uint damage, DamageType type, string ability, bool isMiss, long timestamp,
        string? attackerOwner = null, bool attackerIsSpell = false)
    {
        // Detect pet/warder ownership: "Player`s warder" → owner = "Player"
        attackerOwner ??= _registry.CheckOwner(attacker);

        short modMask = -1;
        if (!isMiss && split[stop] != split[^1] && split[^1].EndsWith(')'))
        {
            var modStr = ParserUtil.JoinWords(split, stop + 1, split.Length - stop - 1);
            if (modStr.StartsWith('(') && modStr.EndsWith(')'))
                modMask = LineModifiersParser.Parse(modStr[1..^1]);
        }

        // High-confidence class detection from ability modifiers (Assassinate→ROG, Headshot/DoubleBowShot→RNG, SlayUndead→PAL)
        if (!isMiss && modMask != LineModifiersParser.None &&
            (string.Equals(attacker, _playerName, StringComparison.OrdinalIgnoreCase)
             || PlayerRegistry.IsPossiblePlayerName(attacker)))
        {
            string? detectedClass = null;
            if (LineModifiersParser.IsAssassinate(modMask)) detectedClass = "ROG";
            else if (LineModifiersParser.IsHeadshot(modMask) || LineModifiersParser.IsDoubleBowShot(modMask)) detectedClass = "RNG";
            else if (LineModifiersParser.IsSlayUndead(modMask)) detectedClass = "PAL";
            if (detectedClass is not null) _registry.SetPlayerClass(attacker, detectedClass, highConfidence: true);
        }

        var perspective = DerivePerspecive(attacker, defender);
        return new DamageRecord(
            Attacker: attacker,
            AttackerOwner: attackerOwner,
            Defender: defender,
            Ability: ability,
            Total: damage,
            Blocked: 0,
            Type: type,
            Perspective: perspective,
            IsCritical: LineModifiersParser.IsCrit(modMask),
            IsLucky: LineModifiersParser.IsLucky(modMask),
            IsRampage: LineModifiersParser.IsRampage(modMask),
            IsRiposte: LineModifiersParser.IsRiposte(modMask),
            AttackerIsSpell: attackerIsSpell,
            Timestamp: timestamp);
    }

    private DamagePerspective DerivePerspecive(string attacker, string defender)
    {
        if (string.Equals(attacker, _playerName, StringComparison.OrdinalIgnoreCase))
            return DamagePerspective.Outgoing;
        if (string.Equals(defender, _playerName, StringComparison.OrdinalIgnoreCase))
            return DamagePerspective.Incoming;
        return DamagePerspective.Observed;
    }

    // Port of EQLP's GetTypeFromSpell: classifies spell DD/DoT as Bane or Proc if data indicates it.
    // defaultType is what we fall back to when spell data isn't found.
    private DamageType GetTypeFromSpell(string spellName, DamageType defaultType)
    {
        var abbrv = _spells.GetByAbbrv(AbbreviateSpellName(spellName));
        var data = abbrv ?? _spells.GetByName(spellName);
        if (data != null)
        {
            if (data.Damaging == 2) return DamageType.Bane;
            if (data.Proc == 1) return DamageType.Proc;
        }
        return defaultType;
    }

    private static string AbbreviateSpellName(string spell)
    {
        // Strip rank suffix: "Rk. II", "Rk. III", trailing roman numerals, trailing numbers
        var parts = spell.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = parts.Length;
        if (count >= 3 && parts[count - 2].Equals("Rk.", StringComparison.OrdinalIgnoreCase) && IsRoman(parts[count - 1]))
            count -= 2;
        while (count > 1)
        {
            var last = parts[count - 1];
            if (IsRoman(last) || int.TryParse(last, out _)) { count--; continue; }
            break;
        }
        return count == parts.Length ? spell : string.Join(' ', parts[..count]);
    }

    private static bool IsRoman(string s)
    {
        foreach (var c in s)
            if (c is not ('I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M'
                       or 'i' or 'v' or 'x' or 'l' or 'c' or 'd' or 'm'))
                return false;
        return s.Length > 0;
    }

    private void QueueSlain(string slain, string killer, long timestamp)
    {
        slain = ParserUtil.CapitalizeFirst(slain);
        killer = ParserUtil.CapitalizeFirst(killer);
        _slainQueue.Add(slain);
        _slainTime = timestamp > 0 ? timestamp + 1 : long.MinValue;
        EntitySlain?.Invoke(slain, killer);
    }

    private void FlushSlainQueue(long timestamp)
    {
        if (_slainTime != long.MinValue && timestamp >= _slainTime)
        {
            _slainQueue.Clear();
            _slainTime = long.MinValue;
        }
    }
}
