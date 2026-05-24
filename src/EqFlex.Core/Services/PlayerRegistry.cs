using System.Collections.Concurrent;
using EqFlex.Core.Interfaces;

namespace EqFlex.Core.Services;

/// <summary>
/// Tracks which names in the log are players, pets, or mercs vs NPCs.
/// Ported from EQLP's PlayerRegistry with DI instead of singletons.
/// </summary>
public sealed class PlayerRegistry
{
    private readonly ISpellDataService _spells;
    private readonly ConcurrentDictionary<string, byte> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _mercs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _petToPlayer = new(StringComparer.OrdinalIgnoreCase);

    // Class detection: (abbreviation, isHighConfidence, spellCastCount)
    private readonly ConcurrentDictionary<string, (string Cls, bool HiConf, int Count)> _classMap
        = new(StringComparer.OrdinalIgnoreCase);

    public string PlayerName { get; }

    public PlayerRegistry(string playerName, ISpellDataService spells)
    {
        PlayerName = playerName;
        _spells = spells;
        _players[playerName] = 1;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>True if name is a known player, pet, or mercenary (i.e. allied, not an NPC).</summary>
    public bool IsPetOrPlayerOrMerc(string name) =>
        !string.IsNullOrEmpty(name) &&
        (_players.ContainsKey(name) || _pets.ContainsKey(name) || _mercs.ContainsKey(name));

    public bool IsVerifiedPlayer(string name) => !string.IsNullOrEmpty(name) && _players.ContainsKey(name);
    public bool IsVerifiedPet(string name) => !string.IsNullOrEmpty(name) && _pets.ContainsKey(name);
    public bool IsMerc(string name) => !string.IsNullOrEmpty(name) && _mercs.ContainsKey(name);

    public string? GetPlayerFromPet(string pet) =>
        _petToPlayer.TryGetValue(pet, out var owner) ? owner : null;

    // ── Registration ─────────────────────────────────────────────────────────

    public void AddVerifiedPlayer(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Equals("You", StringComparison.OrdinalIgnoreCase))
            return;
        _pets.TryRemove(name, out _);       // can't be both player and pet
        _mercs.TryRemove(name, out _);
        _players[name] = 1;
    }

    public void AddVerifiedPet(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _players.TryRemove(name, out _);
        _pets[name] = 1;
    }

    public void AddPetToPlayer(string pet, string player)
    {
        if (string.IsNullOrEmpty(pet) || string.IsNullOrEmpty(player)) return;
        AddVerifiedPet(pet);
        _petToPlayer[pet] = player;
    }

    public void AddMerc(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _mercs[name] = 1;
    }

    /// <summary>Removes a pet from the registry (e.g. charm broke).</summary>
    public void RemovePet(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _pets.TryRemove(name, out _);
        _petToPlayer.TryRemove(name, out _);
    }

    // ── Pet owner detection (port of EQLP's CheckOwner) ──────────────────────

    /// <summary>
    /// If name looks like "Player`s pet" or "Player`s warder", returns the owner player name
    /// and registers the pet. Returns null if no owner detected.
    /// </summary>
    public string? CheckOwner(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // Pattern: "Player`s pet" / "Player`s warder"
        var tickIdx = name.IndexOf("`s ", StringComparison.Ordinal);
        if (tickIdx > 0)
        {
            var suffix = name[(tickIdx + 3)..];
            if (suffix.StartsWith("pet", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("warder", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("ward", StringComparison.OrdinalIgnoreCase))
            {
                var ownerCandidate = name[..tickIdx];
                if (IsPossiblePlayerName(ownerCandidate) && IsVerifiedPlayer(ownerCandidate))
                {
                    AddPetToPlayer(name, ownerCandidate);
                    return ownerCandidate;
                }
            }
        }

        // Pattern: "Player pet" (space-separated, no backtick)
        var spaceIdx = name.LastIndexOf(" pet", StringComparison.OrdinalIgnoreCase);
        if (spaceIdx > 0 && spaceIdx == name.Length - 4)
        {
            var ownerCandidate = name[..spaceIdx];
            if (IsPossiblePlayerName(ownerCandidate) && IsVerifiedPlayer(ownerCandidate))
            {
                AddPetToPlayer(name, ownerCandidate);
                return ownerCandidate;
            }
        }

        return null;
    }

    // ── Class detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Record a class observation for a player.
    /// highConfidence=true (from /who or ability modifiers) locks immediately.
    /// highConfidence=false (spell cast inference) commits after 8 observations.
    /// </summary>
    public void SetPlayerClass(string name, string classAbbr, bool highConfidence)
    {
        _classMap.AddOrUpdate(name,
            _ => (classAbbr, highConfidence, 1),
            (_, existing) =>
            {
                if (existing.HiConf) return existing;  // high-conf already locked
                if (highConfidence) return (classAbbr, true, existing.Count);
                // Low-conf: only increment if same class
                if (existing.Cls.Equals(classAbbr, StringComparison.OrdinalIgnoreCase))
                    return (existing.Cls, false, existing.Count + 1);
                return existing; // different class vote — keep first
            });
    }

    /// <summary>Returns the player's class abbreviation if confidence threshold is met, else null.</summary>
    public string? GetPlayerClass(string name)
    {
        if (!_classMap.TryGetValue(name, out var cls)) return null;
        return (cls.HiConf || cls.Count >= 8) ? cls.Cls : null;
    }

    // ── Static heuristics (port of EQLP's IsPossiblePlayerName) ─────────────

    /// <summary>
    /// Returns true if name could be a player name: all letters (plus at most one '.' for
    /// cross-server names), length 3–64, no spaces or special chars.
    /// EQ NPCs almost always have multi-word names; player names are single words.
    /// </summary>
    public static bool IsPossiblePlayerName(string? name)
    {
        if (name is null || name.Length < 3 || name.Length > 64) return false;

        var dotCount = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetter(c)) continue;

            // Allow one '.' for cross-server names (e.g. "Name.Server")
            if (c == '.' && i > 2 && ++dotCount <= 1) continue;

            return false; // space, backtick, digit, etc. → not a player name
        }
        return true;
    }

    /// <summary>
    /// Returns true if name looks like a pet name (possible player name and not a known NPC).
    /// Warder pets always end with "`s warder".
    /// </summary>
    public bool IsPossiblePetName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (_spells.IsNpc(name)) return false;
        return IsPossiblePlayerName(name) ||
               name.EndsWith("`s warder", StringComparison.OrdinalIgnoreCase);
    }
}
