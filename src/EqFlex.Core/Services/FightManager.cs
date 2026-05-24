using System.Collections.Concurrent;
using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;

namespace EqFlex.Core.Services;

public sealed class FightManager : IFightRegistry
{
    private const long IdleTimeoutSec = 30;
    private const long MaxTimeoutSec = 60;

    private static long _nextId = 1;

    private readonly ISpellDataService _spells;
    private PlayerRegistry? _registry;

    private readonly ConcurrentDictionary<string, Fight> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Fight> _completed = [];
    private readonly Lock _completedLock = new();

    // Global heal store — keyed by timestamp, queried per fight time window.
    // Mirrors EQLP's approach: heals are NOT attributed to specific NPC fights at ingest time.
    private readonly List<(long Timestamp, HealingRecord Record)> _healStore = [];
    private readonly Lock _healLock = new();

    // Cache of recently seen player spells — used to identify spell-attacker records as player-sourced.
    // Mirrors EQLP's RecentSpellCache.
    private readonly ConcurrentDictionary<string, bool> _recentSpellCache = new(StringComparer.OrdinalIgnoreCase);
    private long _lastProcessedTime;

    // When true, FightUpdated/FightExpired events are suppressed.
    // Set during log replay so the WPF dispatcher queue doesn't flood with per-record callbacks.
    // Call EndReplay() after the channel drains to fire a single batch update.
    private volatile bool _suppressEvents;

    public event EventHandler<Fight>? FightUpdated;
    public event EventHandler<Fight>? FightExpired;
    public event EventHandler? SessionStarted;

    public FightManager(ISpellDataService spells) => _spells = spells;

    /// <summary>Suppress FightUpdated/FightExpired events for the duration of a log replay.</summary>
    public void BeginReplay() => _suppressEvents = true;

    /// <summary>
    /// Re-enable events and fire one FightUpdated for every fight accumulated during replay.
    /// Idempotent — safe to call in a finally block even if already called in the happy path.
    /// Must be called on the UI thread so subscribers receive events on the correct thread.
    /// </summary>
    public void EndReplay()
    {
        if (!_suppressEvents) return;   // already ended — don't fire twice
        _suppressEvents = false;
        foreach (var fight in _active.Values)
            FightUpdated?.Invoke(this, fight);
        lock (_completedLock)
            foreach (var fight in _completed)
                FightExpired?.Invoke(this, fight);
    }

    /// <summary>Called by LogViewModel when a new parse session starts.</summary>
    public void BeginSession(PlayerRegistry registry)
    {
        _registry = registry;
        _recentSpellCache.Clear();
        foreach (var (_, fight) in _active)
        {
            fight.IsActive = false;
            fight.EndTime = fight.LastTime;
        }
        _active.Clear();
        lock (_completedLock) _completed.Clear();
        lock (_healLock) _healStore.Clear();
        SessionStarted?.Invoke(this, EventArgs.Empty);
    }

    public Fight GetOrCreate(string npcName, long timestamp)
    {
        if (_active.TryGetValue(npcName, out var existing)) return existing;
        var newFight = new Fight
        {
            Id = Interlocked.Increment(ref _nextId),
            NpcName = npcName,
            StartTime = timestamp,
            LastTime = timestamp,
            IsActive = true
        };
        // GetOrAdd is atomic — if another thread won the race, return their version
        return _active.GetOrAdd(npcName, newFight);
    }

    public void RecordDamage(DamageRecord record)
    {
        var now = record.Timestamp;

        // Clear spell cache when time advances (mirrors EQLP's RecentSpellTime logic)
        if (now != _lastProcessedTime)
        {
            if (now - _lastProcessedTime > 15) _recentSpellCache.Clear();
            _lastProcessedTime = now;
            CheckExpiry(now);
        }

        // Cache player spells so we can attribute spell-named attacker records correctly
        var isAttackerPlayer = _registry?.IsPetOrPlayerOrMerc(record.Attacker) == true ||
                               record.Attacker == _registry?.PlayerName;
        if (isAttackerPlayer && record.Type is DamageType.Spell or DamageType.Dot or DamageType.Proc)
            _recentSpellCache[record.Ability] = true;

        // Classify this record: is the NPC the defender (taking damage) or attacker (dealing damage)?
        if (!IsValidAttack(record, isAttackerPlayer, out var npcIsDefender))
            return;

        // Self-attacks (NPC vs NPC same entity) are skipped by IsValidAttack
        var npcName = npcIsDefender ? record.Defender : record.Attacker;
        var fight = GetOrCreate(npcName, now);
        fight.LastTime = now;

        // A real hit means actual damage landed — not misses, parries, dodges, blocks, ripostes, absorbs.
        // Matches EQLP's IsHitType which returns false for those Labels.
        var isRealHit = record.Total > 0;

        if (npcIsDefender)
        {
            // Attribute pet damage to its owner.
            // AttackerOwner covers summoned pets ("Player`s warder").
            // GetPlayerFromPet covers charmed pets registered via AddPetToPlayer.
            var dealerName = record.AttackerOwner
                ?? _registry?.GetPlayerFromPet(record.Attacker)
                ?? record.Attacker;
            var stats = fight.PlayerStats.GetOrAdd(dealerName, n => new PlayerStats { Name = n });

            // Update the player's active time window for ALL attack attempts (including misses).
            // EQLP calls UpdateTimeSegments before the IsHitType check for the same reason:
            // if you swing and miss, that time still counts toward your DPS window.
            if (stats.FirstDamageTime < 0) stats.FirstDamageTime = now;
            stats.LastDamageTime = now;
            stats.Attempts++;

            // Only count actual hits toward damage totals and hit counters
            if (isRealHit)
            {
                fight.DamageHits++;
                fight.DamageTotal += record.Total;
                stats.Damage += record.Total;
                stats.Hits++;
                if (record.IsCritical) stats.Crits++;

                // When damage is attributed to the pet owner (charmer/summoner) rather than
                // the attacker itself, label the ability so the abilities breakdown shows
                // which damage came from a pet vs the player directly.
                var isPetDamage = !string.Equals(dealerName, record.Attacker,
                    StringComparison.OrdinalIgnoreCase);
                var abilityKey = isPetDamage ? $"{record.Ability} (Pet)" : record.Ability;
                var abilStats = stats.Abilities.GetOrAdd(abilityKey, n => new AbilityStats { Name = n });
                abilStats.Damage += record.Total;
                abilStats.Hits++;
                if (record.IsCritical) abilStats.Crits++;
                if (record.Total > abilStats.Max) abilStats.Max = record.Total;
            }
        }
        else
        {
            // NPC dealing damage to a player/pet (tanking side)
            var tankerName = record.Defender;
            var tankStats = fight.PlayerTankStats.GetOrAdd(tankerName, n => new TankStats { Name = n });

            // Track all attempts on the tanking side too
            if (isRealHit)
            {
                fight.TankHits++;
                fight.TankTotal += record.Total;
                tankStats.Damage += record.Total;
                tankStats.Hits++;
            }
        }

        if (!_suppressEvents)
            FightUpdated?.Invoke(this, fight);
    }

    /// <summary>
    /// Store the heal record globally with its timestamp.
    /// Heals are NOT attributed to a specific NPC fight at ingest time — this mirrors EQLP.
    /// Attribution happens later via <see cref="ComputeHealingForRange"/> when a fight is selected.
    /// </summary>
    public void RecordHealing(HealingRecord record)
    {
        // Only store heals where the target looks like a player/pet (filter out NPC-on-NPC heals)
        if (record.Amount == 0 && record.OverHeal == 0) return;

        // Merc inference from healing: "a X" healing a known player is almost certainly a
        // mercenary healer. Register before the IsLikelyPlayer gate so the heal is retained.
        if (LooksLikeMercName(record.Healer) && !_spells.IsNpc(record.Healer)
            && IsLikelyPlayer(record.Target))
            _registry?.AddMerc(record.Healer);

        if (!IsLikelyPlayer(record.Target) && !IsLikelyPlayer(record.Healer)) return;

        lock (_healLock)
            _healStore.Add((record.Timestamp, record));
    }

    /// <summary>
    /// Build per-healer stats for all heal records whose timestamp falls in [startTime, endTime].
    /// Called by DamageViewModel when the user selects a fight or combines multiple fights.
    /// </summary>
    public Dictionary<string, HealerStats> ComputeHealingForRange(long startTime, long endTime)
    {
        var result = new Dictionary<string, HealerStats>(StringComparer.OrdinalIgnoreCase);

        // Take a snapshot under the lock and release immediately.
        // This prevents the UI thread from blocking the consumer thread's RecordHealing calls
        // for the entire iteration duration (which can be tens of ms for large sessions).
        (long Timestamp, HealingRecord Record)[] snapshot;
        lock (_healLock)
            snapshot = [.. _healStore];

        foreach (var (ts, record) in snapshot)
        {
                if (ts < startTime || ts > endTime) continue;

                if (!result.TryGetValue(record.Healer, out var stats))
                {
                    stats = new HealerStats { Name = record.Healer };
                    result[record.Healer] = stats;
                }

                stats.Total += record.Amount;
                stats.OverHeal += record.OverHeal;
                stats.Hits++;
                if (record.IsCritical) stats.Crits++;
                if (stats.FirstHealTime < 0) stats.FirstHealTime = ts;
                stats.LastHealTime = ts;

                var spellKey = string.IsNullOrEmpty(record.Spell) ? "Self-Heal" : record.Spell;
                if (!stats.Spells.TryGetValue(spellKey, out var spellStats))
                {
                    spellStats = new HealSpellStats { Name = spellKey };
                    stats.Spells[spellKey] = spellStats;
                }
                spellStats.Total += record.Amount;
                spellStats.OverHeal += record.OverHeal;
                spellStats.Hits++;
                if (record.IsCritical) spellStats.Crits++;
                if (record.Amount > spellStats.Max) spellStats.Max = record.Amount;
        }

        return result;
    }

    private bool IsLikelyPlayer(string name) =>
        _registry?.IsPetOrPlayerOrMerc(name) == true || PlayerRegistry.IsPossiblePlayerName(name);

    public void RemoveFight(string npcName)
    {
        if (_active.TryRemove(npcName, out var fight))
        {
            fight.IsActive = false;
            fight.EndTime = fight.LastTime; // log timestamp, not wall clock
            lock (_completedLock) _completed.Add(fight);
            if (!_suppressEvents)
                FightExpired?.Invoke(this, fight);
        }
    }

    public string? GetPlayerClass(string name) =>
        _registry?.IsMerc(name) == true ? "MERC" : _registry?.GetPlayerClass(name);

    public IReadOnlyList<Fight> GetActiveFights() => _active.Values.ToList();

    /// <summary>
    /// Move every currently active fight to completed and fire FightExpired for each.
    /// Call this after EndReplay() so the overlay doesn't treat historical fights as live.
    /// </summary>
    public void ExpireAllActive()
    {
        foreach (var name in _active.Keys.ToList())
            RemoveFight(name);
    }

    public IReadOnlyList<Fight> GetCompletedFights()
    {
        lock (_completedLock) return [.. _completed];
    }

    public void CheckExpiry(long nowSeconds)
    {
        foreach (var (name, fight) in _active)
        {
            var idle = nowSeconds - fight.LastTime;
            if (idle >= MaxTimeoutSec || (idle >= IdleTimeoutSec && fight.DamageHits > 0))
                RemoveFight(name);
        }
    }

    // ── Classification (port of EQLP's IsValidAttack) ────────────────────────

    /// <summary>
    /// Determines whether this damage record represents a valid combat event and
    /// which side is the NPC. Returns false to discard the record.
    /// <paramref name="npcIsDefender"/> true  = NPC is being hit (players dealing damage)
    ///                                  false = NPC is hitting (players taking damage / tanking)
    /// </summary>
    private bool IsValidAttack(DamageRecord record, bool isAttackerPlayer, out bool npcIsDefender)
    {
        npcIsDefender = false;

        // Same-name check: normally a self-attack, but if the attacker is a registered pet
        // (a charmed mob) attacking an enemy of the same type, allow it and treat the
        // defender as the NPC. This is the only way to capture charm-pet damage when the
        // charmed mob and its target share a name (e.g. "a skeleton" vs "a skeleton").
        if (record.Attacker.Equals(record.Defender, StringComparison.OrdinalIgnoreCase))
        {
            if (_registry?.IsVerifiedPet(record.Attacker) == true)
            {
                npcIsDefender = true;
                return true;
            }
            return false;
        }

        // AttackerIsSpell=true means the "attacker" field is a spell name, not a person.
        // Check the recent spell cache to see if a player cast that spell name recently.
        var isAttackerPlayerSpell = record.AttackerIsSpell && _recentSpellCache.ContainsKey(record.Attacker);
        isAttackerPlayer = isAttackerPlayer || isAttackerPlayerSpell;

        // Unattributed NPC DoT: "X has taken N damage by SpellName." with no known caster.
        // We cannot attribute this to any fight, and the spell name must never become a fight
        // entry. The attributed form ("from SpellName by MobName") arrives with AttackerIsSpell=false
        // and a real mob attacker, so that path is unaffected.
        if (record.AttackerIsSpell && !isAttackerPlayerSpell)
            return false;

        var isDefenderPlayer = _registry?.IsPetOrPlayerOrMerc(record.Defender) == true;

        // Behavioral charm break: a registered pet attacking a verified player means the
        // charm has broken. Un-register the pet so subsequent events classify correctly.
        if (isAttackerPlayer && isDefenderPlayer
            && _registry?.IsVerifiedPet(record.Attacker) == true
            && _registry?.IsVerifiedPlayer(record.Defender) == true)
        {
            _registry!.RemovePet(record.Attacker);
            isAttackerPlayer = false;
        }

        var isAttackerNpc = IsAttackerNpc(record, isAttackerPlayerSpell, isAttackerPlayer);
        var isDefenderNpc = IsDefenderNpc(record, isAttackerPlayerSpell, isDefenderPlayer) || isAttackerPlayer;

        // Player hitting a player — skip (PvP or log noise)
        if (isAttackerPlayer && isDefenderPlayer) return false;

        if (isDefenderNpc)
        {
            if (!isAttackerNpc)
            {
                npcIsDefender = true;
                if (isAttackerPlayer || PlayerRegistry.IsPossiblePlayerName(record.Attacker))
                    return true;
                // Merc inference: "a X" / "an X" attacking an NPC that already has an active
                // player fight is almost certainly your mercenary, not a random NPC bystander.
                if (LooksLikeMercName(record.Attacker) && !_spells.IsNpc(record.Attacker)
                    && _active.ContainsKey(record.Defender))
                {
                    _registry?.AddMerc(record.Attacker);
                    return true;
                }
                return false;
            }

            // Both look like NPCs: use whichever already has an active fight
            if (_active.ContainsKey(record.Defender) && !_active.ContainsKey(record.Attacker))
            {
                npcIsDefender = true;
                return true;
            }
        }
        else
        {
            // NPC is the attacker (tanking scenario)
            if (isAttackerNpc)
            {
                if (isDefenderPlayer || PlayerRegistry.IsPossiblePlayerName(record.Defender))
                    return true;
                // Merc inference: NPC from an active fight attacking "a X" / "an X" → merc is tanking.
                if (LooksLikeMercName(record.Defender) && !_spells.IsNpc(record.Defender)
                    && _active.ContainsKey(record.Attacker))
                {
                    _registry?.AddMerc(record.Defender);
                    return true;
                }
                return false;
            }

            if (isDefenderPlayer) return true;

            if (isAttackerPlayer) { npcIsDefender = true; return true; }

            // Unknown: use heuristics
            if (!PlayerRegistry.IsPossiblePlayerName(record.Defender)) { npcIsDefender = true; return true; }
            if (!PlayerRegistry.IsPossiblePlayerName(record.Attacker)) return true;

            npcIsDefender = true;
        }

        return true;
    }

    private bool IsAttackerNpc(DamageRecord record, bool isAttackerPlayerSpell, bool isAttackerPlayer) =>
        (!isAttackerPlayer && _spells.IsNpc(record.Attacker)) ||
        (record.AttackerIsSpell && !isAttackerPlayerSpell);

    private bool IsDefenderNpc(DamageRecord record, bool isAttackerPlayerSpell, bool isDefenderPlayer) =>
        (!isDefenderPlayer && _spells.IsNpc(record.Defender)) || isAttackerPlayerSpell;

    // EQ mercenary names always start with "a " or "an " (e.g. "a bloodbone mender").
    // This distinguishes them from player names (single word) and most NPC names (in npcs.txt).
    private static bool LooksLikeMercName(string name) =>
        name.StartsWith("a ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("an ", StringComparison.OrdinalIgnoreCase);

    // IFightRegistry explicit impl
    Fight IFightRegistry.GetOrCreate(string npcName, long timestamp) => GetOrCreate(npcName, timestamp);
    IReadOnlyList<Fight> IFightRegistry.GetActiveFights() => GetActiveFights();
    void IFightRegistry.CheckExpiry(long nowSeconds) => CheckExpiry(nowSeconds);
}
