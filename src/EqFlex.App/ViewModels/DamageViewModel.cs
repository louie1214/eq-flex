using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Storage;

namespace EqFlex.App.ViewModels;

public sealed record FightRow(long Id, string StartTime, string NpcName, long Damage, double Dps, int Hits, long TankDamage, int TankHits, string Duration, Fight Fight);
public sealed record PlayerDamageRow(string Name, string Class, long Damage, double Percent, double Dps, double Sdps, int Hits, int Crits, string CritPct, string HitRate, string ParsedSec);
public sealed record AbilityRow(string Name, long Damage, double Percent, int Hits, int Crits, long Max);
public sealed record TankRow(string Name, long Damage, double Percent, int Hits);
public sealed record HealerRow(string Name, string Class, long Total, double Percent, double Hps, double Shps, int Hits, int Crits, string CritPct, long OverHeal, string OverHealPct);
public sealed record HealSpellRow(string Name, long Total, double Percent, int Hits, int Crits, long Max, long OverHeal, string OverHealPct);

public sealed partial class DamageViewModel : ObservableObject
{
    private readonly FightManager    _fm;
    private readonly SettingsStore   _settings;

    // ── Copy Damage settings ───────────────────────────────────────────────
    [ObservableProperty] private int  _copyParsePlayerCount;
    [ObservableProperty] private bool _copyParseShowDps;
    [ObservableProperty] private bool _copyParseShowDuration;
    [ObservableProperty] private bool _copyParseShowPercent;
    [ObservableProperty] private bool _copyParseShowCrit;

    partial void OnCopyParsePlayerCountChanged(int value)  => SaveCopyParseSettings();
    partial void OnCopyParseShowDpsChanged(bool value)     => SaveCopyParseSettings();
    partial void OnCopyParseShowDurationChanged(bool value)=> SaveCopyParseSettings();
    partial void OnCopyParseShowPercentChanged(bool value) => SaveCopyParseSettings();
    partial void OnCopyParseShowCritChanged(bool value)    => SaveCopyParseSettings();

    private void SaveCopyParseSettings()
    {
        var s = _settings.Load();
        s.CopyParsePlayerCount  = CopyParsePlayerCount;
        s.CopyParseShowDps      = CopyParseShowDps;
        s.CopyParseShowDuration = CopyParseShowDuration;
        s.CopyParseShowPercent  = CopyParseShowPercent;
        s.CopyParseShowCrit     = CopyParseShowCrit;
        _settings.Save(s);
    }

    // ── Copy Tanking settings ──────────────────────────────────────────────
    [ObservableProperty] private int  _copyTankPlayerCount;
    [ObservableProperty] private bool _copyTankShowPercent;
    [ObservableProperty] private bool _copyTankShowHits;

    partial void OnCopyTankPlayerCountChanged(int value) => SaveCopyTankSettings();
    partial void OnCopyTankShowPercentChanged(bool value)=> SaveCopyTankSettings();
    partial void OnCopyTankShowHitsChanged(bool value)   => SaveCopyTankSettings();

    private void SaveCopyTankSettings()
    {
        var s = _settings.Load();
        s.CopyTankPlayerCount = CopyTankPlayerCount;
        s.CopyTankShowPercent = CopyTankShowPercent;
        s.CopyTankShowHits    = CopyTankShowHits;
        _settings.Save(s);
    }

    // ── Copy Healing settings ──────────────────────────────────────────────
    [ObservableProperty] private int  _copyHealPlayerCount;
    [ObservableProperty] private bool _copyHealShowHps;
    [ObservableProperty] private bool _copyHealShowDuration;
    [ObservableProperty] private bool _copyHealShowPercent;
    [ObservableProperty] private bool _copyHealShowOverheal;
    [ObservableProperty] private bool _copyHealShowCrit;

    partial void OnCopyHealPlayerCountChanged(int value)  => SaveCopyHealSettings();
    partial void OnCopyHealShowHpsChanged(bool value)     => SaveCopyHealSettings();
    partial void OnCopyHealShowDurationChanged(bool value)=> SaveCopyHealSettings();
    partial void OnCopyHealShowPercentChanged(bool value) => SaveCopyHealSettings();
    partial void OnCopyHealShowOverhealChanged(bool value)=> SaveCopyHealSettings();
    partial void OnCopyHealShowCritChanged(bool value)    => SaveCopyHealSettings();

    private void SaveCopyHealSettings()
    {
        var s = _settings.Load();
        s.CopyHealPlayerCount  = CopyHealPlayerCount;
        s.CopyHealShowHps      = CopyHealShowHps;
        s.CopyHealShowDuration = CopyHealShowDuration;
        s.CopyHealShowPercent  = CopyHealShowPercent;
        s.CopyHealShowOverheal = CopyHealShowOverheal;
        s.CopyHealShowCrit     = CopyHealShowCrit;
        _settings.Save(s);
    }

    // ── Copy mode (shared selector) ───────────────────────────────────────────
    [ObservableProperty] private string _copyMode = "Damage";

    partial void OnCopyModeChanged(string value)
    {
        var s = _settings.Load();
        s.CopyMode = value;
        _settings.Save(s);
        OnPropertyChanged(nameof(CopyModeIsDamage));
        OnPropertyChanged(nameof(CopyModeIsTanking));
        OnPropertyChanged(nameof(CopyModeIsHealing));
        OnPropertyChanged(nameof(CurrentCopyPlayerCount));
    }

    public bool CopyModeIsDamage  => CopyMode == "Damage";
    public bool CopyModeIsTanking => CopyMode == "Tanking";
    public bool CopyModeIsHealing => CopyMode == "Healing";

    public int CurrentCopyPlayerCount
    {
        get => CopyMode switch { "Tanking" => CopyTankPlayerCount, "Healing" => CopyHealPlayerCount, _ => CopyParsePlayerCount };
        set
        {
            switch (CopyMode)
            {
                case "Tanking": CopyTankPlayerCount  = value; break;
                case "Healing": CopyHealPlayerCount  = value; break;
                default:        CopyParsePlayerCount = value; break;
            }
        }
    }

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

    public DamageViewModel(FightManager fm, SettingsStore settings)
    {
        _fm       = fm;
        _settings = settings;

        var s = settings.Load();
        _copyParsePlayerCount  = s.CopyParsePlayerCount > 0 ? s.CopyParsePlayerCount : 10;
        _copyParseShowDps      = s.CopyParseShowDps;
        _copyParseShowDuration = s.CopyParseShowDuration;
        _copyParseShowPercent  = s.CopyParseShowPercent;
        _copyParseShowCrit     = s.CopyParseShowCrit;

        _copyTankPlayerCount = s.CopyTankPlayerCount > 0 ? s.CopyTankPlayerCount : 10;
        _copyTankShowPercent = s.CopyTankShowPercent;
        _copyTankShowHits    = s.CopyTankShowHits;

        _copyHealPlayerCount  = s.CopyHealPlayerCount > 0 ? s.CopyHealPlayerCount : 10;
        _copyHealShowHps      = s.CopyHealShowHps;
        _copyHealShowDuration = s.CopyHealShowDuration;
        _copyHealShowPercent  = s.CopyHealShowPercent;
        _copyHealShowOverheal = s.CopyHealShowOverheal;
        _copyHealShowCrit     = s.CopyHealShowCrit;

        _copyMode = s.CopyMode ?? "Damage";

        _fm.FightUpdated  += OnFightUpdated;
        _fm.FightExpired  += OnFightExpired;
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
    private void CopyActiveParse()
    {
        switch (CopyMode)
        {
            case "Tanking": CopyTanking(); break;
            case "Healing": CopyHealing(); break;
            default:        CopyParse();   break;
        }
    }

    [RelayCommand]
    private void CopyParse()
    {
        if (_selectedFights.Count == 0) return;

        var fights = _selectedFights.Select(r => r.Fight).ToList();

        // Aggregate damage across selected fights (mirrors RefreshFromSelection)
        var agg = new Dictionary<string, (long dmg, double parsedSecs, int hits, int crits)>(StringComparer.OrdinalIgnoreCase);
        long totalDmg = 0;
        double totalDuration = 0;

        foreach (var f in fights)
        {
            totalDmg     += f.DamageTotal;
            totalDuration += f.DurationSeconds;
            foreach (var (name, ps) in f.PlayerStats)
            {
                agg.TryGetValue(name, out var e);
                agg[name] = (e.dmg + ps.Damage, e.parsedSecs + ps.ParsedSeconds,
                             e.hits + ps.Hits, e.crits + ps.Crits);
            }
        }

        var dur   = Math.Max(1, totalDuration);
        var total = Math.Max(1L, totalDmg);

        // Header: "FightName in 284s, 66.96K Damage @235"
        var fightTitle = fights.Count == 1
            ? fights[0].NpcName
            : $"{fights.Count} Fights";
        var header = $"{fightTitle} in {(int)dur}s, {FormatK(totalDmg)} Damage @{(long)(totalDmg / dur)}";

        // Players: "{rank}. {name} = {dmgK}[@{dps}][ in {parsedSec}s][ ({pct}%)][ {crit}%c]"
        var count = Math.Max(1, CopyParsePlayerCount);
        var playerParts = agg
            .Where(kv => kv.Value.dmg > 0)
            .OrderByDescending(kv => kv.Value.dmg)
            .Take(count)
            .Select((kv, i) =>
            {
                var (name, e) = (kv.Key, kv.Value);
                var sb = new StringBuilder();
                sb.Append($"{i + 1}. {name} = {FormatK(e.dmg)}");
                if (CopyParseShowDps)
                    sb.Append($"@{(long)(e.dmg / dur)}");
                if (CopyParseShowDuration)
                    sb.Append($" in {(int)e.parsedSecs}s");
                if (CopyParseShowPercent)
                    sb.Append($" ({e.dmg * 100.0 / total:F1}%)");
                if (CopyParseShowCrit && e.hits > 0)
                    sb.Append($" {e.crits * 100.0 / e.hits:F1}%c");
                return sb.ToString();
            });

        Clipboard.SetText(header + ", " + string.Join(" | ", playerParts));
    }

    [RelayCommand]
    private void CopyTanking()
    {
        if (_selectedFights.Count == 0 || Tanking.Count == 0) return;

        var dur        = Math.Max(1, _selectedFights.Sum(r => r.Fight.DurationSeconds));
        var fightTitle = _selectedFights.Count == 1 ? _selectedFights[0].NpcName : $"{_selectedFights.Count} Fights";
        var totalTank  = Math.Max(1L, Tanking.Sum(t => t.Damage));
        var header     = $"{fightTitle} in {(int)dur}s, {FormatK(totalTank)} Tank Damage";

        var count = Math.Max(1, CopyTankPlayerCount);
        var parts = Tanking.Take(count).Select((t, i) =>
        {
            var sb = new StringBuilder();
            sb.Append($"{i + 1}. {t.Name} = {FormatK(t.Damage)}");
            if (CopyTankShowPercent) sb.Append($" ({t.Percent:F1}%)");
            if (CopyTankShowHits)    sb.Append($" {t.Hits} hits");
            return sb.ToString();
        });

        Clipboard.SetText(header + ", " + string.Join(" | ", parts));
    }

    [RelayCommand]
    private void CopyHealing()
    {
        if (_selectedFights.Count == 0 || Healers.Count == 0) return;

        var dur        = Math.Max(1, _selectedFights.Sum(r => r.Fight.DurationSeconds));
        var fightTitle = _selectedFights.Count == 1 ? _selectedFights[0].NpcName : $"{_selectedFights.Count} Fights";
        var totalHeal  = Math.Max(1L, Healers.Sum(h => h.Total));
        var header     = $"{fightTitle} in {(int)dur}s, {FormatK(totalHeal)} Healing";

        var count = Math.Max(1, CopyHealPlayerCount);
        var parts = Healers.Take(count).Select((h, i) =>
        {
            var sb = new StringBuilder();
            sb.Append($"{i + 1}. {h.Name} = {FormatK(h.Total)}");
            if (CopyHealShowHps) sb.Append($"@{(long)h.Shps}");
            if (CopyHealShowDuration && _healerMap != null && _healerMap.TryGetValue(h.Name, out var hs))
                sb.Append($" in {(int)hs.ParsedSeconds}s");
            if (CopyHealShowPercent)  sb.Append($" ({h.Percent:F1}%)");
            if (CopyHealShowOverheal) sb.Append($" OH:{h.OverHealPct}");
            if (CopyHealShowCrit)     sb.Append($" {h.CritPct}c");
            return sb.ToString();
        });

        Clipboard.SetText(header + ", " + string.Join(" | ", parts));
    }

    private static string FormatK(long n) =>
        n >= 1_000_000 ? $"{Math.Round(n / 1_000_000m, 2)}M"
        : n >= 1_000   ? $"{Math.Round(n / 1_000m, 2)}K"
        : n.ToString();

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
