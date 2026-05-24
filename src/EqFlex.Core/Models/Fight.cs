using System.Collections.Concurrent;

namespace EqFlex.Core.Models;

public sealed class Fight
{
    public long Id { get; init; }
    public string NpcName { get; init; } = string.Empty;
    public long StartTime { get; set; }
    public long LastTime { get; set; }
    public long EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    // Outgoing damage (players hitting the NPC)
    public long DamageTotal { get; set; }
    public int DamageHits { get; set; }

    // Incoming damage (NPC hitting players)
    public long TankTotal { get; set; }
    public int TankHits { get; set; }

    // ConcurrentDictionary so the consumer thread can write while the UI thread iterates
    // without throwing InvalidOperationException.
    public ConcurrentDictionary<string, PlayerStats> PlayerStats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, TankStats> PlayerTankStats { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Log-derived start time formatted as "MMM dd HH:mm:ss" (e.g. "Feb 19 15:59:16").</summary>
    public string StartTimeDisplay =>
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddSeconds(StartTime)
            .ToString("MMM dd HH:mm:ss");

    // +1 matches EQLP's TimeSegment.Total = EndTime - BeginTime + 1
    public double DurationSeconds => IsActive
        ? Math.Max(1, LastTime - StartTime + 1)
        : Math.Max(1, EndTime - StartTime + 1);

    public double Dps => DamageTotal / DurationSeconds;
}

public sealed class PlayerStats
{
    public string Name { get; init; } = string.Empty;
    public long Damage { get; set; }
    public int Hits { get; set; }       // real hits only (Total > 0)
    public int Attempts { get; set; }   // all swings including misses/parries/dodges
    public int Crits { get; set; }

    // ConcurrentDictionary for the same reason as Fight.PlayerStats
    public ConcurrentDictionary<string, AbilityStats> Abilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Per-player parsed time window (first hit → last hit in this fight)
    public long FirstDamageTime { get; set; } = -1;
    public long LastDamageTime { get; set; }

    public double CritPercent => Hits > 0 ? Crits * 100.0 / Hits : 0;

    /// <summary>
    /// Seconds the player was in active combat (DPS denominator).
    /// Includes miss/parry/dodge attempts — matches EQLP's TimeSegment.Total = end - begin + 1.
    /// </summary>
    public double ParsedSeconds => FirstDamageTime >= 0
        ? Math.Max(1, LastDamageTime - FirstDamageTime + 1)
        : 1;
}

public sealed class AbilityStats
{
    public string Name { get; init; } = string.Empty;
    public long Damage { get; set; }
    public int Hits { get; set; }
    public int Crits { get; set; }
    public long Max { get; set; }
}

public sealed class TankStats
{
    public string Name { get; init; } = string.Empty;
    public long Damage { get; set; }
    public int Hits { get; set; }
}
