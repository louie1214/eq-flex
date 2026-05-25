using System.Text.RegularExpressions;
using System.Threading.Channels;
using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;
using EqFlex.Core.Parsing;
using EqFlex.Core.Services;

namespace EqFlex.Infrastructure.Logging;

/// <summary>
/// Consumes raw log lines from a LogTailer, strips the timestamp header,
/// and routes the action text through each parser.
/// </summary>
public sealed class LogProcessor : IDisposable
{
    // EQ log line format: [Day Mon DD HH:MM:SS YYYY] Action text
    // Groups: 1=month, 2=day, 3=hour, 4=min, 5=sec, 6=year
    private static readonly Regex TimestampRegex = new(
        @"^\[(?:Sun|Mon|Tue|Wed|Thu|Fri|Sat) (\w+) (\d{1,2}) (\d{2}):(\d{2}):(\d{2}) (\d{4})\] ",
        RegexOptions.Compiled);

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    // Class names as they appear in /who output → abbreviation
    private static readonly Dictionary<string, string> WhoClassMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Warrior"] = "WAR", ["Cleric"] = "CLR", ["Paladin"] = "PAL", ["Ranger"] = "RNG",
        ["Shadow Knight"] = "SHD", ["Druid"] = "DRU", ["Monk"] = "MNK", ["Bard"] = "BRD",
        ["Rogue"] = "ROG", ["Shaman"] = "SHM", ["Necromancer"] = "NEC", ["Wizard"] = "WIZ",
        ["Magician"] = "MAG", ["Enchanter"] = "ENC", ["Beastlord"] = "BST", ["Berserker"] = "BER"
    };

    private static int ParseMonth(string s) => s switch
    {
        "Jan" => 1, "Feb" => 2, "Mar" => 3, "Apr" => 4,
        "May" => 5, "Jun" => 6, "Jul" => 7, "Aug" => 8,
        "Sep" => 9, "Oct" => 10, "Nov" => 11, _ => 12
    };

    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly DamageParser? _damage;
    private readonly HealingParser? _healing;
    private readonly CastParser? _cast;
    private readonly PlayerRegistry? _registry;
    private readonly ISpellDataService? _spells;
    private readonly Action<string>? _onPetCharmed;
    private readonly TriggerEngine? _triggerEngine;

    // caster name → timestamp of most recent charm spell cast (begin or activate)
    private readonly Dictionary<string, long> _recentCharmCasts = new(StringComparer.OrdinalIgnoreCase);
    private const long CharmCastWindowSec = 30;

    // Raised when a {FLEX:share/XXXXXXXX} code appears in a live log line.
    public event Action<string>? ShareCodeDetected;

    private Task? _consumer;
    private CancellationTokenSource? _cts;

    public bool ParseDamage { get; set; } = true;
    public bool ParseHealing { get; set; } = true;
    public bool ParseCasting { get; set; } = true;

    public LogProcessor(DamageParser? damage, HealingParser? healing, CastParser? cast,
        PlayerRegistry? registry = null, ISpellDataService? spells = null,
        Action<string>? onPetCharmed = null, TriggerEngine? triggerEngine = null)
    {
        _damage = damage;
        _healing = healing;
        _cast = cast;
        _registry = registry;
        _spells = spells;
        _onPetCharmed = onPetCharmed;
        _triggerEngine = triggerEngine;

        if (_cast is not null)
            _cast.CastDetected += OnCastDetected;
    }

    private void OnCastDetected(CastRecord record)
    {
        if (_spells is null || record.IsInterrupted) return;
        if (_spells.IsCharmSpell(record.Spell))
            _recentCharmCasts[record.Caster] = record.Timestamp;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _consumer = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>
    /// Complete the channel writer and wait for the consumer to process every queued line.
    /// Unlike Stop(), does NOT cancel the consumer token — all enqueued lines are guaranteed
    /// to be processed before this returns. Use this after a replay loop to drain cleanly.
    /// </summary>
    public void Drain(int timeoutMs = 30_000)
    {
        _channel.Writer.TryComplete();          // no more lines will be written
        try { _consumer?.Wait(timeoutMs); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _channel.Writer.TryComplete();
        try { _consumer?.Wait(3000); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public void Enqueue(string line) => _channel.Writer.TryWrite(line);

    private async Task ConsumeAsync(CancellationToken token)
    {
        await foreach (var line in _channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            ProcessLine(line);
    }

    private void ProcessLine(string line)
    {
        var match = TimestampRegex.Match(line);
        if (!match.Success) return;

        var action = line[match.Length..];
        if (string.IsNullOrWhiteSpace(action)) return;

        // Build a Unix timestamp from the full date so Fight.StartTimeDisplay can show date+time
        // and durations remain correct even across midnight.
        var mon = ParseMonth(match.Groups[1].Value);
        var day = int.Parse(match.Groups[2].Value);
        var h   = int.Parse(match.Groups[3].Value);
        var m   = int.Parse(match.Groups[4].Value);
        var s   = int.Parse(match.Groups[5].Value);
        var yr  = int.Parse(match.Groups[6].Value);
        var dt  = new DateTime(yr, mon, day, h, m, s, DateTimeKind.Unspecified);
        var timestamp = (long)(dt - UnixEpoch).TotalSeconds;

        // Group membership → register players/mercs before damage parsing
        if (_registry is not null)
        {
            ProcessGroupLine(action);
            if (action.Length > 0 && action[0] == '[')
                ProcessWhoLine(action);
            // Charmed pet communication — register pet before damage lines arrive
            if (action.Contains("told you,", StringComparison.OrdinalIgnoreCase))
                TryDetectPetCommunication(action);
        }

        // Route: damage → healing → cast in priority order (mirrors EQLP)
        var parsedByCombat = (ParseDamage && _damage?.Process(action, timestamp) == true)
                          || (ParseHealing && _healing?.Process(action, timestamp) == true);
        if (!parsedByCombat && ParseCasting)
            _cast?.Process(action, timestamp);

        // Trigger engine evaluates every line — independent of combat parse routing
        _triggerEngine?.Process(action, timestamp);

        // Share code detection — look for {FLEX:share/XXXXXXXX} in any live log line
        if (ShareCodeDetected is not null && action.Contains("{FLEX:share/", StringComparison.Ordinal))
            TryDetectShareCode(action);

        // Charm landing / break detection
        if (_spells is not null && _registry is not null)
        {
            TryDetectCharmLanding(action, timestamp);
            TryDetectCharmBreak(action);
        }
    }

    // Parse group/raid join lines to populate PlayerRegistry.
    // Patterns (from EQLP MiscLineParser):
    //   "X has joined the group."          → RegisterAllied(X)
    //   "a hired X has joined the group."  → AddMerc("a hired X")
    //   "X has left the group."            → (no-op; keep tracking)
    //   "X joined the raid."               → RegisterAllied(X)
    private void ProcessGroupLine(string action)
    {
        // "X has joined the group." / "a hired X has joined the group."
        const string joinedGroup = " has joined the group.";
        if (action.EndsWith(joinedGroup, StringComparison.Ordinal))
        {
            var name = action[..^joinedGroup.Length];
            // Names starting with an article ("a bloodbone mender", "a hired Pyrilen",
            // "a clockwork defender CDXV", "an X") are mercenaries, not players.
            if (name.StartsWith("a ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
                _registry!.AddMerc(name);
            else
                _registry!.AddVerifiedPlayer(name);
            return;
        }

        // "X joined the raid."
        const string joinedRaid = " joined the raid.";
        if (action.EndsWith(joinedRaid, StringComparison.Ordinal))
        {
            var name = action[..^joinedRaid.Length];
            _registry!.AddVerifiedPlayer(name);
            return;
        }

        // "a clockwork defender CDXV is now group Main Tank." — merc identity
        const string nowGroupTank = " is now group Main Tank.";
        if (action.EndsWith(nowGroupTank, StringComparison.OrdinalIgnoreCase))
        {
            var name = action[..^nowGroupTank.Length];
            if (name.StartsWith("a ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
                _registry!.AddMerc(name);
        }
    }

    // Parse /who output lines to get high-confidence class information.
    // Format: "[Level ClassName] PlayerName (Race)  ..."
    // e.g.   "[120 Bard] Louie (High Elf)  ZONE: guildlobby"
    private void ProcessWhoLine(string action)
    {
        var bracket = action.IndexOf(']');
        if (bracket < 4) return;

        var content = action.AsSpan(1, bracket - 1);
        if (content.StartsWith("ANON ", StringComparison.OrdinalIgnoreCase)) return;

        var space = content.IndexOf(' ');
        if (space < 1) return;
        if (!uint.TryParse(content[..space], out var level) || level == 0 || level > 125) return;

        var className = content[(space + 1)..].ToString();
        if (!WhoClassMap.TryGetValue(className, out var classAbbr)) return;

        var afterBracket = action.AsSpan(bracket + 1).TrimStart();
        var nameEnd = afterBracket.IndexOf(' ');
        var playerName = (nameEnd > 0 ? afterBracket[..nameEnd] : afterBracket).ToString();

        if (!PlayerRegistry.IsPossiblePlayerName(playerName)) return;

        _registry!.AddVerifiedPlayer(playerName);
        _registry!.SetPlayerClass(playerName, classAbbr, highConfidence: true);
    }

    // Detect charmed pet from the tell it sends when attacking:
    //   "A Mucktail rock breaker told you, 'Attacking a Mucktail rock breaker Master.'"
    // This fires before most combat lines and gives us an earlier registration point
    // than the LandsOnOther charm landing message.
    private void TryDetectPetCommunication(string action)
    {
        const string toldYouAttacking = " told you, 'Attacking ";
        var idx = action.IndexOf(toldYouAttacking, StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return;

        var petName = action[..idx].Trim();
        if (string.IsNullOrEmpty(petName)) return;

        _registry!.AddPetToPlayer(petName, _registry.PlayerName);
        _onPetCharmed?.Invoke(petName);
    }

    // Match charm "lands on other" messages and register the target as a pet of the most
    // recent charm-spell caster. Mirrors EQLP's TryGetLandsOnOther → AddPetToPlayer path.
    // Known limitation: charm breaks are not detected, so once registered as a pet the entity
    // stays in the pet registry for the session. Attacks from a broken-charm mob on players
    // will be dropped (classified as pet-on-player PvP) rather than recorded as tank damage.
    private void TryDetectCharmLanding(string action, long timestamp)
    {
        // Quick bail: charm landing strings always contain at least one of these keywords.
        if (!action.Contains("charm", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("captivat", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("enthrall", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("beguile", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("servant", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("control", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("thrall", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("tame", StringComparison.OrdinalIgnoreCase) &&
            !action.Contains("befriend", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_spells!.TryMatchCharmLanding(action, out var entityName)) return;

        // Find the most recent charm spell cast within the window.
        string? charmer = null;
        long bestTime = long.MinValue;
        foreach (var (caster, castTime) in _recentCharmCasts)
        {
            if (timestamp - castTime <= CharmCastWindowSec && castTime > bestTime)
            {
                bestTime = castTime;
                charmer = caster;
            }
        }

        if (charmer is null) return;

        _registry!.AddPetToPlayer(entityName, charmer);
        _onPetCharmed?.Invoke(entityName);
    }

    // Detect charm break from the caster-only log line:
    //   "Your Cajoling Whispers spell has worn off of a Mucktail rock breaker."
    // Only visible in the charmer's own log — bystanders see nothing.
    // Complements the behavioral detection in FightManager.IsValidAttack.
    private void TryDetectCharmBreak(string action)
    {
        // Quick bail: these lines always start with "Your " and contain "worn off".
        if (!action.StartsWith("Your ", StringComparison.OrdinalIgnoreCase)) return;
        if (!action.Contains("worn off", StringComparison.OrdinalIgnoreCase)) return;

        if (!_spells!.TryMatchCharmBreak(action, out var entityName)) return;
        if (_registry!.IsVerifiedPet(entityName))
            _registry.RemovePet(entityName);
    }

    private void TryDetectShareCode(string action)
    {
        const string prefix = "{FLEX:share/";
        var start = action.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        if (start < prefix.Length) return;
        var end = action.IndexOf('}', start);
        if (end <= start) return;
        var code = action[start..end];
        if (code.Length == 8) ShareCodeDetected?.Invoke(code);
    }

    public void Dispose() => Stop();
}
