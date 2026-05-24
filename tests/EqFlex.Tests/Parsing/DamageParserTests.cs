using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;
using EqFlex.Core.Parsing;
using EqFlex.Core.Services;

namespace EqFlex.Tests.Parsing;

public class DamageParserTests
{
    private const string PlayerName = "TestPlayer";
    private readonly DamageParser _parser;
    private DamageRecord? _lastRecord;

    public DamageParserTests()
    {
        var spells = new NullSpellDataService();
        var registry = new PlayerRegistry(PlayerName, spells);
        _parser = new DamageParser(PlayerName, spells, registry);
        _parser.DamageProcessed += r => _lastRecord = r;
    }

    private DamageRecord? Parse(string action)
    {
        _lastRecord = null;
        _parser.Process(action, 0);
        return _lastRecord;
    }

    // ── Melee ──────────────────────────────────────────────────────────────

    [Fact]
    public void Melee_Crushes_WithStrikethroughCritical()
    {
        var r = Parse("Astralx crushes Sontalak for 126225 points of damage. (Strikethrough Critical)");
        Assert.NotNull(r);
        Assert.Equal("Astralx", r.Attacker);
        Assert.Equal("Sontalak", r.Defender);
        Assert.Equal(126225u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.Equal("Crushes", r.Ability);
        Assert.True(r.IsCritical);
    }

    [Fact]
    public void Melee_Crushes_NoModifiers()
    {
        var r = Parse("Useless crushes an abyssal terror for 9022 points of damage.");
        Assert.NotNull(r);
        Assert.Equal("Useless", r.Attacker);
        Assert.Equal("An abyssal terror", r.Defender);
        Assert.Equal(9022u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.Equal("Crushes", r.Ability);
        Assert.False(r.IsCritical);
    }

    [Fact]
    public void Melee_Claws_WildRampage()
    {
        var r = Parse("Susarrak the Crusader claws Villette for 27699 points of damage. (Strikethrough Wild Rampage)");
        Assert.NotNull(r);
        Assert.Equal("Susarrak the Crusader", r.Attacker);
        Assert.Equal("Villette", r.Defender);
        Assert.Equal(27699u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.Equal("Claws", r.Ability);
        Assert.True(r.IsRampage);
    }

    [Fact]
    public void Melee_YouCrush_LuckyCritical()
    {
        var r = Parse("You crush Ogna, Artisan of War for 20581 points of damage. (Lucky Critical)");
        Assert.NotNull(r);
        Assert.Equal(PlayerName, r.Attacker);
        Assert.Equal("Ogna, Artisan of War", r.Defender);
        Assert.Equal(20581u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.True(r.IsCritical);
        Assert.True(r.IsLucky);
    }

    [Fact]
    public void Melee_Bashes_Riposte()
    {
        var r = Parse("An ice giant bashes Shmid for 39969 points of damage. (Riposte Strikethrough)");
        Assert.NotNull(r);
        Assert.Equal("An ice giant", r.Attacker);
        Assert.Equal("Shmid", r.Defender);
        Assert.Equal(39969u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
    }

    [Fact]
    public void Melee_Kicks_LuckyCritical()
    {
        var r = Parse("Nniki kicks an ice giant for 672875 points of damage. (Lucky Critical)");
        Assert.NotNull(r);
        Assert.Equal("Nniki", r.Attacker);
        Assert.Equal("An ice giant", r.Defender);
        Assert.Equal(672875u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.Equal("Kicks", r.Ability);
        Assert.True(r.IsCritical);
        Assert.True(r.IsLucky);
    }

    // ── Spell DD ────────────────────────────────────────────────────────────

    [Fact]
    public void SpellDD_HitForFireDamage()
    {
        var r = Parse("Sonozen hit Jortreva the Crusader for 38948 points of fire damage by Burst of Flames. (Lucky Critical Twincast)");
        Assert.NotNull(r);
        Assert.Equal("Sonozen", r.Attacker);
        Assert.Equal("Jortreva the Crusader", r.Defender);
        Assert.Equal(38948u, (uint)r.Total);
        Assert.Equal(DamageType.Spell, r.Type);
        Assert.Equal("Burst of Flames", r.Ability);
        Assert.True(r.IsCritical);
        Assert.True(r.IsLucky);
    }

    [Fact]
    public void SpellDD_YouHitMagic()
    {
        var r = Parse("You hit a treant for 1633489 points of magic damage by Chromospheric Vortex Rk. II. (Lucky Critical)");
        Assert.NotNull(r);
        Assert.Equal(PlayerName, r.Attacker);
        Assert.Equal("A treant", r.Defender);
        Assert.Equal(1633489u, (uint)r.Total);
        Assert.Equal(DamageType.Spell, r.Type);
        Assert.True(r.IsCritical);
        Assert.True(r.IsLucky);
    }

    // ── DoT / Taken Damage ──────────────────────────────────────────────────

    [Fact]
    public void Dot_HasTakenFromBy()
    {
        var r = Parse("Dovhesi has taken 173674 damage from Curse of the Shrine by Grendish the Crusader.");
        Assert.NotNull(r);
        Assert.Equal("Grendish the Crusader", r.Attacker);
        Assert.Equal("Dovhesi", r.Defender);
        Assert.Equal(173674u, (uint)r.Total);
        Assert.Equal(DamageType.Dot, r.Type);
    }

    [Fact]
    public void Dot_YouHaveTakenFromBy()
    {
        var r = Parse("You have taken 4852 damage from Nectar of Misery by Commander Gartik.");
        Assert.NotNull(r);
        Assert.Equal("Commander Gartik", r.Attacker);
        Assert.Equal(PlayerName, r.Defender);
        Assert.Equal(4852u, (uint)r.Total);
        Assert.Equal(DamagePerspective.Incoming, r.Perspective);
    }

    [Fact]
    public void Dot_GnollFromYour()
    {
        var r = Parse("A gnoll has taken 108790 damage from your Mind Coil Rk. II.");
        Assert.NotNull(r);
        Assert.Equal(PlayerName, r.Attacker);
        Assert.Equal("A gnoll", r.Defender);
        Assert.Equal(108790u, (uint)r.Total);
        Assert.Equal(DamagePerspective.Outgoing, r.Perspective);
    }

    // ── Non-Melee / DS ──────────────────────────────────────────────────────

    [Fact]
    public void DS_PiercedByThorns()
    {
        var r = Parse("Tantor is pierced by Tolzol's thorns for 6718 points of non-melee damage.");
        Assert.NotNull(r);
        Assert.Equal("Tolzol", r.Attacker);
        Assert.Equal("Tantor", r.Defender);
        Assert.Equal(6718u, (uint)r.Total);
        Assert.Equal(DamageType.DamageShield, r.Type);
    }

    [Fact]
    public void DS_BurnedByYOUR()
    {
        var r = Parse("Test One Hundred Three is burned by YOUR flames for 5224 points of non-melee damage.");
        Assert.NotNull(r);
        Assert.Equal(PlayerName, r.Attacker);
        Assert.Equal("Test One Hundred Three", r.Defender);
        Assert.Equal(5224u, (uint)r.Total);
        Assert.Equal(DamageType.DamageShield, r.Type);
    }

    // ── Miss / Dodge ─────────────────────────────────────────────────────────

    [Fact]
    public void Miss_TryToHitButDodges()
    {
        var r = Parse("Nniki tries to slash Fume but Fume dodges!");
        Assert.NotNull(r);
        Assert.Equal("Nniki", r.Attacker);
        Assert.Equal("Fume", r.Defender);
        Assert.Equal(0u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
        Assert.Equal("Dodge", r.Ability);
    }

    // ── Death / Slain ─────────────────────────────────────────────────────────

    [Fact]
    public void Slain_IsSlainBy()
    {
        var slain = string.Empty;
        var killer = string.Empty;
        _parser.EntitySlain += (s, k) => { slain = s; killer = k; };

        _parser.Process("Fume is slain by Nniki!", 0);
        Assert.Equal("Fume", slain);
        Assert.Equal("Nniki", killer);
    }

    [Fact]
    public void Slain_YouHaveSlain()
    {
        var slain = string.Empty;
        var killer = string.Empty;
        _parser.EntitySlain += (s, k) => { slain = s; killer = k; };

        _parser.Process("You have slain a failed bodyguard!", 0);
        Assert.Equal("A failed bodyguard", slain);
        Assert.Equal(PlayerName, killer);
    }

    [Fact]
    public void Slain_YouHaveBeenSlain()
    {
        var slain = string.Empty;
        var killer = string.Empty;
        _parser.EntitySlain += (s, k) => { slain = s; killer = k; };

        _parser.Process("You have been slain by an armed flyer!", 0);
        Assert.Equal(PlayerName, slain);
        Assert.Equal("An armed flyer", killer);
    }

    // ── Frenzy "on X" patterns ────────────────────────────────────────────────

    [Fact]
    public void Melee_Frenzies_OnTarget_Hit()
    {
        // "frenzies on X" — hitTypeAdd mod must skip "on" so defender isn't "on a Darkfell shaman"
        var r = Parse("Novokain frenzies on a Darkfell shaman for 166 points of damage.");
        Assert.NotNull(r);
        Assert.Equal("Novokain", r.Attacker);
        Assert.Equal("A darkfell shaman", r.Defender, ignoreCase: true);
        Assert.Equal(166u, (uint)r.Total);
        Assert.Equal(DamageType.Melee, r.Type);
    }

    [Fact]
    public void Miss_FrenzyOn_SkipsOnWord()
    {
        // "tries to frenzy on X, but misses!" — miss branch must also apply the frenzy offset
        var r = Parse("Novokain tries to frenzy on an eggtender drone, but misses!");
        Assert.NotNull(r);
        Assert.Equal("Novokain", r.Attacker);
        Assert.Equal("An eggtender drone", r.Defender, ignoreCase: true);
        Assert.Equal(0u, (uint)r.Total);
    }

    [Fact]
    public void Miss_FrenzyOn_PlayerTarget_ResolvesToPlayer()
    {
        // "tries to frenzy on YOU, but misses!" — should resolve to playerName, not "on TestPlayer"
        var r = Parse("A large rat tries to frenzy on YOU, but misses!");
        Assert.NotNull(r);
        Assert.Equal("A large rat", r.Attacker);
        Assert.Equal(PlayerName, r.Defender);
        Assert.Equal(DamagePerspective.Incoming, r.Perspective);
    }

    // ── Bug regression: "YOU," trailing comma ────────────────────────────────

    [Fact]
    public void Miss_YouWithTrailingComma_ResolvesToPlayer()
    {
        // "A large rat tries to bite YOU, but misses!" — "YOU," must resolve to playerName
        var r = Parse("A large rat tries to bite YOU, but misses!");
        Assert.NotNull(r);
        Assert.Equal("A large rat", r.Attacker);
        Assert.Equal(PlayerName, r.Defender);   // "YOU," → playerName after comma-strip
        Assert.Equal(DamagePerspective.Incoming, r.Perspective);
    }

    [Fact]
    public void Miss_YouWithTrailingComma_DecayingSkeleton()
    {
        var r = Parse("A decaying skeleton tries to punch YOU, but misses!");
        Assert.NotNull(r);
        Assert.Equal("A decaying skeleton", r.Attacker);
        Assert.Equal(PlayerName, r.Defender);
        Assert.Equal(DamagePerspective.Incoming, r.Perspective);
    }

    // ── Bug regression: "has taken N damage by SpellName" (no "from") ────────

    [Fact]
    public void Dot_HasTakenBySpell_NoFrom()
    {
        // "Brat has taken 4 damage by Feeblemind." — previously silently dropped
        var r = Parse("Brat has taken 4 damage by Feeblemind.");
        Assert.NotNull(r);
        Assert.Equal("Brat", r.Defender);
        Assert.Equal(4u, (uint)r.Total);
        Assert.Equal(DamageType.Dot, r.Type);
        Assert.Equal("Feeblemind", r.Ability);
    }

    [Fact]
    public void Dot_HasTakenBySpell_Pet()
    {
        var r = Parse("Finly`s warder has taken 3 damage by Spreading Crud.");
        Assert.NotNull(r);
        Assert.Equal("Finly`s warder", r.Defender);
        Assert.Equal(3u, (uint)r.Total);
        Assert.Equal("Spreading Crud", r.Ability);
    }

    // ── Null data service stub ────────────────────────────────────────────────
    private sealed class NullSpellDataService : ISpellDataService
    {
        public SpellData? GetByName(string name) => null;
        public SpellData? GetByAbbrv(string abbrv) => null;
        public IReadOnlyList<SpellData> GetAllByName(string name) => Array.Empty<SpellData>();
        public bool IsNpc(string name) => false;
        public bool IsPetName(string name) => false;
        public string? GetClassForSpell(string spellName) => null;
        public bool IsCharmSpell(string spellName) => false;
        public bool TryMatchCharmLanding(string action, out string entityName) { entityName = string.Empty; return false; }
        public bool TryMatchCharmBreak(string action, out string entityName) { entityName = string.Empty; return false; }
        public void Load(string dataDirectory) { }
    }
}
