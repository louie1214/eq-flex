using System.Text.RegularExpressions;
using EqFlex.Core.Models;

namespace EqFlex.Core.Services;

public sealed record TriggerFiredArgs(
    Trigger Trigger,
    TriggerAction Action,
    string Line,
    IReadOnlyDictionary<string, string> Captures);

public sealed class TriggerEngine
{
    private readonly List<Trigger> _active = [];
    private readonly Lock _lock = new();
    // trigger id → unix timestamp when cooldown expires
    private readonly Dictionary<int, long> _cooldowns = [];

    public string PlayerName { get; set; } = string.Empty;

    public event Action<TriggerFiredArgs>? TriggerFired;

    public void LoadTriggers(IEnumerable<Trigger> triggers)
    {
        lock (_lock)
        {
            _active.Clear();
            _active.AddRange(triggers.Where(t => t.IsEnabled));
        }
    }

    public void Process(string action, long timestamp)
    {
        List<Trigger> snapshot;
        lock (_lock) snapshot = [.. _active];

        foreach (var trigger in snapshot)
        {
            if (_cooldowns.TryGetValue(trigger.Id, out var coolUntil) && timestamp < coolUntil)
                continue;

            IReadOnlyDictionary<string, string>? captures = null;
            var matched = false;

            foreach (var phrase in trigger.Phrases)
            {
                if (string.IsNullOrEmpty(phrase.Pattern)) continue;

                if (phrase.UseRegex)
                {
                    try
                    {
                        var m = Regex.Match(action, phrase.Pattern, RegexOptions.IgnoreCase);
                        if (m.Success) { matched = true; captures = ExtractCaptures(m); break; }
                    }
                    catch { /* malformed regex — skip */ }
                }
                else if (action.Contains(phrase.Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    captures = new Dictionary<string, string>();
                    break;
                }
            }

            if (!matched) continue;

            if (trigger.CooldownSec > 0)
                _cooldowns[trigger.Id] = timestamp + (long)trigger.CooldownSec;

            var allCaptures = new Dictionary<string, string>(
                captures ?? new Dictionary<string, string>())
            {
                ["{C}"] = PlayerName,
                ["{L}"] = action
            };

            foreach (var ta in trigger.Actions)
                TriggerFired?.Invoke(new TriggerFiredArgs(trigger, ta, action, allCaptures));
        }
    }

    private static IReadOnlyDictionary<string, string> ExtractCaptures(Match m)
    {
        var caps = new Dictionary<string, string>();
        for (var i = 0; i < m.Groups.Count; i++)
            if (m.Groups[i].Success)
                caps[$"{{S{i}}}"] = m.Groups[i].Value;
        foreach (Group g in m.Groups)
            if (!int.TryParse(g.Name, out _) && g.Success)
                caps[$"{{{g.Name}}}"] = g.Value;
        return caps;
    }

    /// <summary>Replace {S0}, {GroupName}, {C}, {L} tokens in text with captured values.</summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> captures)
    {
        foreach (var (key, value) in captures)
            template = template.Replace(key, value, StringComparison.OrdinalIgnoreCase);
        return template;
    }
}
