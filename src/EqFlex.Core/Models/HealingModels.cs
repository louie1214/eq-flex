namespace EqFlex.Core.Models;

public sealed class HealerStats
{
    public string Name { get; init; } = string.Empty;
    public long Total { get; set; }         // actual healing landed
    public long OverHeal { get; set; }      // wasted overheal
    public int Hits { get; set; }           // heal events
    public int Crits { get; set; }
    public long FirstHealTime { get; set; } = -1;
    public long LastHealTime { get; set; }
    public Dictionary<string, HealSpellStats> Spells { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Mirrors PlayerStats.ParsedSeconds — time from first to last heal event (+1 per EQLP convention)
    public double ParsedSeconds => FirstHealTime >= 0
        ? Math.Max(1, LastHealTime - FirstHealTime + 1)
        : 1;

    public double OverHealPercent => (Total + OverHeal) > 0
        ? OverHeal * 100.0 / (Total + OverHeal)
        : 0;

    public double CritPercent => Hits > 0 ? Crits * 100.0 / Hits : 0;
}

public sealed class HealSpellStats
{
    public string Name { get; init; } = string.Empty;
    public long Total { get; set; }
    public long OverHeal { get; set; }
    public int Hits { get; set; }
    public int Crits { get; set; }
    public long Max { get; set; }

    public double OverHealPercent => (Total + OverHeal) > 0
        ? OverHeal * 100.0 / (Total + OverHeal)
        : 0;
}
