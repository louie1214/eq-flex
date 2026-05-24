using System.Globalization;
using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;

namespace EqFlex.Infrastructure.Data;

public sealed class SpellDataService : ISpellDataService
{
    private static readonly HashSet<string> RankWords = new(StringComparer.OrdinalIgnoreCase)
        { "Rk.", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    // Class abbreviations indexed by ClassMask bit position (bit 0 = Warrior, bit 1 = Cleric, …)
    private static readonly string[] ClassByBit = [
        "WAR", "CLR", "PAL", "RNG", "SHD", "DRU", "MNK", "BRD",
        "ROG", "SHM", "NEC", "WIZ", "MAG", "ENC", "BST", "BER"
    ];

    private readonly Dictionary<string, List<SpellData>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpellData> _byAbbrv = new(StringComparer.OrdinalIgnoreCase);
    // title→class from titles.txt (e.g. "Warlord" → "WAR") used for /who title resolution
    private readonly Dictionary<string, string> _titleToClass = new(StringComparer.OrdinalIgnoreCase);
    // spell name → class abbreviation for spells with a single ClassMask bit set
    private readonly Dictionary<string, string> _spellClassMap = new(StringComparer.OrdinalIgnoreCase);
    // Charm/control spell detection: trimmed LandsOnOther suffix → spell name
    private readonly Dictionary<string, string> _charmLandingMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _charmSpellNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _npcNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _petSyllables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _procNames = new(StringComparer.OrdinalIgnoreCase);

    public void Load(string dataDirectory)
    {
        LoadTitles(Path.Combine(dataDirectory, "titles.txt"));
        LoadProcs(Path.Combine(dataDirectory, "procs.txt"));   // must load before spells
        LoadSpells(Path.Combine(dataDirectory, "spells.txt"));
        LoadNpcs(Path.Combine(dataDirectory, "npcs.txt"));
        LoadPetNames(Path.Combine(dataDirectory, "petnames.txt"));
    }

    public SpellData? GetByName(string name)
    {
        if (_byName.TryGetValue(name, out var list) && list.Count > 0)
            return list[0];
        return null;
    }

    public SpellData? GetByAbbrv(string abbrv)
    {
        _byAbbrv.TryGetValue(abbrv, out var spell);
        return spell;
    }

    public IReadOnlyList<SpellData> GetAllByName(string name)
    {
        if (_byName.TryGetValue(name, out var list))
            return list;
        return Array.Empty<SpellData>();
    }

    public bool IsNpc(string name) => _npcNames.Contains(name);

    public bool IsPetName(string name)
    {
        if (name.Length < 4) return false;
        // Pet names are combinations of two syllables from petnames.txt
        for (var i = 2; i < name.Length - 1; i++)
        {
            var prefix = name[..i];
            var suffix = name[i..];
            if (_petSyllables.Contains(prefix) && _petSyllables.Contains(suffix))
                return true;
        }
        return false;
    }

    public string? GetClassForSpell(string spellName)
    {
        if (_spellClassMap.TryGetValue(spellName, out var cls)) return cls;
        // Try abbreviated form (strips rank suffixes)
        var abbr = AbbreviateSpellName(spellName);
        if (!abbr.Equals(spellName, StringComparison.OrdinalIgnoreCase) &&
            _spellClassMap.TryGetValue(abbr, out cls)) return cls;
        return null;
    }

    public string? GetClassFromTitle(string title)
    {
        _titleToClass.TryGetValue(title, out var cls);
        return cls;
    }

    public bool IsCharmSpell(string spellName) => _charmSpellNames.Contains(spellName);

    public bool TryMatchCharmLanding(string action, out string entityName)
    {
        entityName = string.Empty;
        foreach (var (suffix, _) in _charmLandingMap)
        {
            if (!action.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = action[..^suffix.Length].TrimEnd();
            if (candidate.Length < 2) continue;
            entityName = candidate;
            return true;
        }
        return false;
    }

    private void LoadSpells(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrEmpty(line)) continue;
            var data = line.Split('^');
            if (data.Length < 20) continue;

            try
            {
                var durationTicks = int.Parse(data[3], CultureInfo.InvariantCulture);
                var durationSec = Math.Clamp(durationTicks * 6, 0, ushort.MaxValue);

                var spellName = string.Intern(data[1]);
                var spell = new SpellData(
                    Id: string.Intern(data[0]),
                    Name: spellName,
                    NameAbbrv: string.Intern(AbbreviateSpellName(spellName)),
                    Level: byte.Parse(data[2], CultureInfo.InvariantCulture),
                    Duration: (ushort)durationSec,
                    IsBeneficial: data[4] != "0",
                    Target: byte.Parse(data[6], CultureInfo.InvariantCulture),
                    ClassMask: ushort.Parse(data[7], CultureInfo.InvariantCulture),
                    Damaging: short.Parse(data[8], CultureInfo.InvariantCulture),
                    Resist: int.Parse(data[10], CultureInfo.InvariantCulture),
                    SongWindow: data[11] is "1" or "-1",
                    Adps: byte.Parse(data[12], CultureInfo.InvariantCulture),
                    Mgb: data[13] == "1",
                    Rank: byte.Parse(data[14], CultureInfo.InvariantCulture),
                    HasAmbiguity: data[15] == "1" || data[16] == "1",
                    LandsOnYou: string.Intern(data[17]),
                    LandsOnOther: string.Intern(data[18]),
                    WearOff: string.Intern(data[19]),
                    Proc: (_procNames.Contains(spellName) || _procNames.Contains($"New {spellName}")) ? (byte)1 : (byte)0);

                if (!_byName.TryGetValue(spell.Name, out var nameList))
                    _byName[spell.Name] = nameList = [];
                nameList.Add(spell);

                if (!_byAbbrv.ContainsKey(spell.NameAbbrv) ||
                    string.Compare(_byAbbrv[spell.NameAbbrv].Name, spell.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _byAbbrv[spell.NameAbbrv] = spell;
                }

                // Class-exclusive spell detection: ClassMask must have exactly one bit set.
                // Skip illusion spells — they're sold as clickies and don't indicate class.
                var cm = spell.ClassMask;
                if (cm > 0 && (cm & (cm - 1)) == 0 &&
                    !spell.Name.Contains("Illusion", StringComparison.OrdinalIgnoreCase))
                {
                    var bit = 0;
                    var m = cm;
                    while (m > 1) { m >>= 1; bit++; }
                    if (bit < ClassByBit.Length)
                        _spellClassMap.TryAdd(spell.Name, ClassByBit[bit]);
                }

                // Charm/control spell detection via LandsOnOther strings.
                // We match the suffix against lines like "a skeleton has been charmed."
                if (!string.IsNullOrWhiteSpace(spell.LandsOnOther) && IsCharmEffect(spell.LandsOnOther))
                {
                    var suffix = spell.LandsOnOther.TrimStart();
                    _charmLandingMap.TryAdd(suffix, spell.Name);
                    _charmSpellNames.Add(spell.Name);
                }
            }
            catch { /* malformed line — skip */ }
        }
    }

    private void LoadProcs(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
            if (!string.IsNullOrWhiteSpace(line) && line[0] != '#')
            {
                _procNames.Add(line.Trim());
                _procNames.Add($"New {line.Trim()}");
            }
    }

    private void LoadNpcs(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
            if (!string.IsNullOrWhiteSpace(line))
                _npcNames.Add(line.Trim());
    }

    private void LoadPetNames(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
            if (!string.IsNullOrWhiteSpace(line))
                _petSyllables.Add(line.Trim());
    }

    private void LoadTitles(string path)
    {
        if (!File.Exists(path)) return;
        // Format: ClassName=Title1,Title2,...
        // We store title→class mapping for /who parsing (used by MiscLineParser)
        foreach (var line in File.ReadLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var className = line[..eq].Trim();
            var titles = line[(eq + 1)..].Split(',');
            foreach (var title in titles)
                if (!string.IsNullOrWhiteSpace(title))
                    _titleToClass[title.Trim()] = className;
        }
    }

    // Charm break is only visible in the caster's own log as:
    //   "Your Cajoling Whispers spell has worn off of a Mucktail rock breaker."
    // The third-person WearOff approach (bystander visible) does not exist in practice —
    // confirmed by reviewing actual EQ log files.
    private const string WornOffOf = " spell has worn off of ";

    public bool TryMatchCharmBreak(string action, out string entityName)
    {
        entityName = string.Empty;
        if (!action.StartsWith("Your ", StringComparison.OrdinalIgnoreCase)) return false;
        var midIdx = action.IndexOf(WornOffOf, 5, StringComparison.OrdinalIgnoreCase);
        if (midIdx < 0) return false;

        var spellName = action[5..midIdx]; // between "Your " and " spell has worn off of "
        if (!_charmSpellNames.Contains(spellName)) return false;

        var candidate = action[(midIdx + WornOffOf.Length)..].TrimEnd('.');
        if (candidate.Length < 2) return false;

        entityName = candidate;
        return true;
    }

    // Detects charm/control LandsOnOther strings so we can identify charmed pets.
    // Covers all known EQ charm spell families: enchanter, bard, druid, necromancer.
    private static bool IsCharmEffect(string landsOnOther) =>
        landsOnOther.Contains("charm", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("captivat", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("enthrall", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("beguile", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("servant of", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("under your control", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("thrall", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("befriend", StringComparison.OrdinalIgnoreCase) ||
        landsOnOther.Contains("tame", StringComparison.OrdinalIgnoreCase);

    private static string AbbreviateSpellName(string spell)
    {
        var parts = spell.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = parts.Length;

        // Strip "Rk. <Roman>"
        if (count >= 3 &&
            parts[count - 2].Equals("Rk.", StringComparison.OrdinalIgnoreCase) &&
            IsRoman(parts[count - 1]))
        {
            count -= 2;
        }

        // Strip trailing rank words / roman numerals / numbers
        while (count > 1)
        {
            var last = parts[count - 1];
            if (RankWords.Contains(last) || IsRoman(last) || int.TryParse(last, out _))
            {
                count--;
                continue;
            }
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
}
