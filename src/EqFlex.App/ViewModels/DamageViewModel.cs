using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Core.Services;

namespace EqFlex.App.ViewModels;

public sealed record FightRow(long Id, string StartTime, string NpcName, long Damage, double Dps, int Hits, long TankDamage, int TankHits, string Duration, Fight Fight);
public sealed record PlayerDamageRow(string Name, string Class, long Damage, double Percent, double Dps, double Sdps, int Hits, int Crits, string CritPct, string HitRate, string ParsedSec);
public sealed record AbilityRow(string Name, long Damage, double Percent, int Hits, int Crits, long Max);
public sealed record TankRow(string Name, long Damage, double Percent, int Hits);
public sealed record HealerRow(string Name, string Class, long Total, double Percent, double Hps, double Shps, int Hits, int Crits, string CritPct, long OverHeal, string OverHealPct);
public sealed record HealSpellRow(string Name, long Total, double Percent, int Hits, int Crits, long Max, long OverHeal, string OverHealPct);

public sealed partial class DamageViewModel : ObservableObject
{
    private readonly FightManager _fm;

    [ObservableProperty] private ObservableCollection<FightRow> _fights = [];
    [ObservableProperty] private ObservableCollection<PlayerDamageRow> _players = [];
    [ObservableProperty] private PlayerDamageRow? _selectedPlayer;
    [ObservableProperty] private ObservableCollection<AbilityRow> _abilities = [];
    [ObservableProperty] private ObservableCollection<AbilityRow> _petAbilities = [];

    public bool HasPetAbilities => PetAbilities.Count > 0;

    partial void OnPetAbilitiesChanged(ObservableCollection<AbilityRow> value) =>
        OnPropertyChanged(nameof(HasPetAbilities));
    [ObservableProperty] private ObservableCollection<TankRow> _tanking = [];
    [ObservableProperty] private ObservableCollection<HealerRow> _healers = [];
    [ObservableProperty] private HealerRow? _selectedHealer;
    [ObservableProperty] private ObservableCollection<HealSpellRow> _healSpells = [];
    [ObservableProperty] private string _selectionSummary = "No fight selected";

    // The DataGrid sets this via binding on SelectionChanged
    private IList<FightRow> _selectedFights = [];

    public DamageViewModel(FightManager fm)
    {
        _fm = fm;
        _fm.FightUpdated += OnFightUpdated;
        _fm.FightExpired += OnFightExpired;
        _fm.SessionStarted += (_, _) => ClearFights();
    }

    public void OnFightSelectionChanged(IList<FightRow> selected)
    {
        _selectedFights = selected;
        RefreshFromSelection();
    }

    partial void OnSelectedPlayerChanged(PlayerDamageRow? value) => RefreshAbilities();
    partial void OnSelectedHealerChanged(HealerRow? value) => RefreshHealSpells();

    private void OnFightUpdated(object? sender, Fight fight)
    {
        // EndReplay fires events synchronously on the UI thread; InvokeAsync is only needed
        // when called from the consumer thread during live tailing.
        if (Application.Current.Dispatcher.CheckAccess())
            RefreshFight(fight);
        else
            Application.Current.Dispatcher.InvokeAsync(() => RefreshFight(fight));
    }

    private void OnFightExpired(object? sender, Fight fight)
    {
        if (Application.Current.Dispatcher.CheckAccess())
            RefreshFight(fight);
        else
            Application.Current.Dispatcher.InvokeAsync(() => RefreshFight(fight));
    }

    private void RefreshFight(Fight fight)
    {
        var row = MakeFightRow(fight);
        var existing = Fights.FirstOrDefault(f => f.Id == fight.Id);
        if (existing is not null)
        {
            var idx = Fights.IndexOf(existing);
            Fights[idx] = row;

            // If this fight was selected, update derived views
            if (_selectedFights.Any(r => r.Id == fight.Id))
                RefreshFromSelection();
        }
        else
        {
            Fights.Insert(0, row);
        }
    }

    private void RefreshFromSelection()
    {
        var selected = _selectedFights.ToList();

        if (selected.Count == 0)
        {
            Players.Clear();
            Tanking.Clear();
            Abilities.Clear();
            SelectionSummary = "No fight selected";
            return;
        }

        var fights = selected.Select(r => r.Fight).ToList();

        // Aggregate damage across all selected fights
        // dmg, parsedSecs (player active time), hits, crits, heal, abilities
        var aggDamage = new Dictionary<string, (long dmg, double parsedSecs, int hits, int attempts, int crits, long heal, Dictionary<string, (long d, int h, int c, long max)> abilities)>(StringComparer.OrdinalIgnoreCase);
        var aggTank = new Dictionary<string, (long dmg, int hits)>(StringComparer.OrdinalIgnoreCase);
        long totalDmg = 0;
        long totalTankDmg = 0;
        double totalDuration = 0;

        foreach (var f in fights)
        {
            totalDmg += f.DamageTotal;
            totalTankDmg += f.TankTotal;
            totalDuration += f.DurationSeconds;

            foreach (var (name, ps) in f.PlayerStats)
            {
                if (!aggDamage.TryGetValue(name, out var agg))
                    agg = (0, 0, 0, 0, 0, 0, new Dictionary<string, (long, int, int, long)>(StringComparer.OrdinalIgnoreCase));
                // Sum per-player parsed time across fights for DPS denominator
                agg = (agg.dmg + ps.Damage, agg.parsedSecs + ps.ParsedSeconds,
                    agg.hits + ps.Hits, agg.attempts + ps.Attempts, agg.crits + ps.Crits, 0L, agg.abilities);

                foreach (var (ab, abStats) in ps.Abilities)
                {
                    agg.abilities.TryGetValue(ab, out var ex);
                    agg.abilities[ab] = (ex.d + abStats.Damage, ex.h + abStats.Hits, ex.c + abStats.Crits, Math.Max(ex.max, abStats.Max));
                }
                aggDamage[name] = agg;
            }

            foreach (var (name, ts) in f.PlayerTankStats)
            {
                aggTank.TryGetValue(name, out var t);
                aggTank[name] = (t.dmg + ts.Damage, t.hits + ts.Hits);
            }
        }

        // SDPS denominator = total fight clock time (all selected fights combined)
        var sdpsDur = Math.Max(1, totalDuration);
        var total = Math.Max(1L, totalDmg);

        Players = new ObservableCollection<PlayerDamageRow>(
            aggDamage
                .Where(kv => kv.Value.dmg > 0)
                .OrderByDescending(kv => kv.Value.dmg)
                .Select(kv =>
                {
                    var (name, a) = (kv.Key, kv.Value);
                    var dpsSecs = Math.Max(1, a.parsedSecs);   // player's personal active window
                    var parsedMin = (int)(a.parsedSecs / 60);
                    var parsedSec = (int)(a.parsedSecs % 60);
                    var parsedStr = parsedMin > 0 ? $"{parsedMin}m {parsedSec}s" : $"{(int)a.parsedSecs}s";
                    return new PlayerDamageRow(
                        Name: name,
                        Class: _fm.GetPlayerClass(name) ?? string.Empty,
                        Damage: a.dmg,
                        Percent: Math.Round(a.dmg * 100.0 / total, 1),
                        Dps: Math.Round(a.dmg / dpsSecs, 0),   // DPS = damage / player parsed time
                        Sdps: Math.Round(a.dmg / sdpsDur, 0),  // SDPS = damage / full fight duration
                        Hits: a.hits,
                        Crits: a.crits,
                        CritPct: a.hits > 0 ? $"{a.crits * 100.0 / a.hits:F1}%" : "0%",
                        HitRate: a.attempts > 0 ? $"{a.hits * 100.0 / a.attempts:F1}%" : "—",
                        ParsedSec: parsedStr);
                }));

        Tanking = new ObservableCollection<TankRow>(
            aggTank.Values.Count > 0
                ? aggTank
                    .OrderByDescending(kv => kv.Value.dmg)
                    .Select(kv => new TankRow(
                        Name: kv.Key,
                        Damage: kv.Value.dmg,
                        Percent: totalTankDmg > 0 ? Math.Round(kv.Value.dmg * 100.0 / totalTankDmg, 1) : 0,
                        Hits: kv.Value.hits))
                : []);

        var label = selected.Count == 1
            ? selected[0].NpcName
            : $"{selected.Count} fights selected";
        SelectionSummary = $"{label}  |  {totalDmg:N0} dmg  |  {totalDmg / sdpsDur:N0} SDPS  |  {sdpsDur:F0}s";

        // ── Healing ──────────────────────────────────────────────────────────────
        // Aggregate per-healer stats across all selected fights using the time-window approach.
        // Mirrors EQLP: heals are queried by fight time range, not attributed at ingest.
        var aggHeal = new Dictionary<string, HealerStats>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in fights)
        {
            var fightHealing = _fm.ComputeHealingForRange(f.StartTime, f.LastTime);
            foreach (var (healer, hs) in fightHealing)
            {
                if (!aggHeal.TryGetValue(healer, out var agg))
                {
                    agg = new HealerStats { Name = healer };
                    aggHeal[healer] = agg;
                }
                agg.Total += hs.Total;
                agg.OverHeal += hs.OverHeal;
                agg.Hits += hs.Hits;
                agg.Crits += hs.Crits;
                if (hs.FirstHealTime >= 0 &&
                    (agg.FirstHealTime < 0 || hs.FirstHealTime < agg.FirstHealTime))
                    agg.FirstHealTime = hs.FirstHealTime;
                if (hs.LastHealTime > agg.LastHealTime)
                    agg.LastHealTime = hs.LastHealTime;

                foreach (var (spell, ss) in hs.Spells)
                {
                    if (!agg.Spells.TryGetValue(spell, out var aggSpell))
                    {
                        aggSpell = new HealSpellStats { Name = spell };
                        agg.Spells[spell] = aggSpell;
                    }
                    aggSpell.Total += ss.Total;
                    aggSpell.OverHeal += ss.OverHeal;
                    aggSpell.Hits += ss.Hits;
                    aggSpell.Crits += ss.Crits;
                    if (ss.Max > aggSpell.Max) aggSpell.Max = ss.Max;
                }
            }
        }

        var totalHeal = Math.Max(1L, aggHeal.Values.Sum(h => h.Total));
        _healerMap = aggHeal;
        Healers = new ObservableCollection<HealerRow>(
            aggHeal.Values
                .Where(h => h.Total > 0)
                .OrderByDescending(h => h.Total)
                .Select(h => new HealerRow(
                    Name: h.Name,
                    Class: _fm.GetPlayerClass(h.Name) ?? string.Empty,
                    Total: h.Total,
                    Percent: Math.Round(h.Total * 100.0 / totalHeal, 1),
                    Hps: Math.Round(h.Total / h.ParsedSeconds, 0),
                    Shps: Math.Round(h.Total / sdpsDur, 0),
                    Hits: h.Hits,
                    Crits: h.Crits,
                    CritPct: h.Hits > 0 ? $"{h.CritPercent:F1}%" : "0%",
                    OverHeal: h.OverHeal,
                    OverHealPct: $"{h.OverHealPercent:F1}%")));

        // Store abilities for the previously selected player if it's still valid
        _abilityMap = aggDamage;
        RefreshAbilities();
        RefreshHealSpells();
    }

    private Dictionary<string, (long dmg, double parsedSecs, int hits, int attempts, int crits, long heal, Dictionary<string, (long d, int h, int c, long max)> abilities)>? _abilityMap;
    private Dictionary<string, HealerStats>? _healerMap;

    private void RefreshAbilities()
    {
        if (_abilityMap is null || SelectedPlayer is null) { Abilities.Clear(); PetAbilities.Clear(); return; }
        if (!_abilityMap.TryGetValue(SelectedPlayer.Name, out var agg)) { Abilities.Clear(); PetAbilities.Clear(); return; }

        var total = Math.Max(1L, agg.dmg);
        var player = new List<AbilityRow>();
        var pet = new List<AbilityRow>();

        foreach (var kv in agg.abilities.OrderByDescending(kv => kv.Value.d))
        {
            var isPet = kv.Key.EndsWith(" (Pet)", StringComparison.OrdinalIgnoreCase);
            var displayName = isPet ? kv.Key[..^6] : kv.Key;
            var row = new AbilityRow(
                Name: displayName,
                Damage: kv.Value.d,
                Percent: Math.Round(kv.Value.d * 100.0 / total, 1),
                Hits: kv.Value.h,
                Crits: kv.Value.c,
                Max: kv.Value.max);
            (isPet ? pet : player).Add(row);
        }

        Abilities = new ObservableCollection<AbilityRow>(player);
        PetAbilities = new ObservableCollection<AbilityRow>(pet);
    }

    private void RefreshHealSpells()
    {
        if (_healerMap is null || SelectedHealer is null) { HealSpells.Clear(); return; }
        if (!_healerMap.TryGetValue(SelectedHealer.Name, out var hs)) { HealSpells.Clear(); return; }

        var total = Math.Max(1L, hs.Total);
        HealSpells = new ObservableCollection<HealSpellRow>(
            hs.Spells.Values
                .OrderByDescending(s => s.Total)
                .Select(s => new HealSpellRow(
                    Name: s.Name,
                    Total: s.Total,
                    Percent: Math.Round(s.Total * 100.0 / total, 1),
                    Hits: s.Hits,
                    Crits: s.Crits,
                    Max: s.Max,
                    OverHeal: s.OverHeal,
                    OverHealPct: $"{s.OverHealPercent:F1}%")));
    }

    [RelayCommand]
    private void CopyParse()
    {
        if (_selectedFights.Count == 0) return;

        var fights = _selectedFights.Select(r => r.Fight).ToList();
        var sb = new StringBuilder();

        foreach (var fight in fights)
        {
            var dur = Math.Max(1, fight.DurationSeconds);
            sb.AppendLine($"--- {fight.NpcName} | {fight.DamageTotal:N0} dmg | {fight.Dps:N0} DPS | {dur:F0}s ---");
            var total = Math.Max(1L, fight.DamageTotal);
            var rank = 1;
            foreach (var p in fight.PlayerStats.Values.OrderByDescending(p => p.Damage))
            {
                sb.AppendLine($"  {rank++}. {p.Name} -- {p.Damage:N0} ({p.Damage * 100.0 / total:F1}%) | {p.Damage / dur:N0} DPS | {p.Hits} hits | {p.CritPercent:F1}% crit");
            }
        }

        Clipboard.SetText(sb.ToString());
    }

    [RelayCommand]
    private void ClearFights()
    {
        Fights.Clear();
        Players.Clear();
        Tanking.Clear();
        Abilities.Clear();
        PetAbilities.Clear();
        Healers.Clear();
        HealSpells.Clear();
        _abilityMap = null;
        _healerMap = null;
        _selectedFights = [];
        SelectionSummary = "No fight selected";
    }

    private static FightRow MakeFightRow(Fight f)
    {
        var dur = f.DurationSeconds;
        var durStr = dur >= 60 ? $"{(int)(dur / 60)}m {(int)(dur % 60)}s" : $"{(int)dur}s";
        return new FightRow(f.Id, f.StartTimeDisplay, f.NpcName, f.DamageTotal, Math.Round(f.Dps, 0),
            f.DamageHits, f.TankTotal, f.TankHits, durStr, f);
    }
}
