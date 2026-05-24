using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Storage;

namespace EqFlex.App.ViewModels;

public enum OverlayMode { Dps, Tank, Heal }

public sealed record OverlayPlayerRow(string Name, string DamageText, long Dps, double Percent);

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly FightManager _fm;
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _timerTick;
    private Fight? _lastFight;
    private long _currentFightId = -1;
    private DateTime _fightStartWallClock;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLocked = true;
    [ObservableProperty] private bool _autoShow;
    [ObservableProperty] private int _autoHideDelaySec = 5;
    [ObservableProperty] private OverlayMode _mode = OverlayMode.Dps;
    [ObservableProperty] private string _fightLabel = "No active fight";
    [ObservableProperty] private string _fightTimer = string.Empty;
    [ObservableProperty] private ObservableCollection<OverlayPlayerRow> _players = [];

    // Restored from settings; -1 means "no saved position, use default"
    public double InitialLeft { get; private set; } = -1;
    public double InitialTop { get; private set; } = -1;
    public double InitialWidth { get; private set; } = 280;
    public double InitialHeight { get; private set; } = 260;

    public OverlayViewModel(FightManager fm, SettingsStore settingsStore)
    {
        _fm = fm;
        _settingsStore = settingsStore;

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_autoHideDelaySec) };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            IsOpen = false;
        };

        _timerTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerTick.Tick += (_, _) => FightTimer = ElapsedStr();

        var s = settingsStore.Load();
        InitialLeft = s.OverlayLeft;
        InitialTop = s.OverlayTop;
        _isLocked = s.OverlayLocked;
        _autoShow = s.OverlayAutoShow;
        _autoHideDelaySec = Math.Clamp(s.OverlayAutoHideDelay, 1, 300);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(_autoHideDelaySec);
        InitialWidth = s.OverlayWidth > 0 ? s.OverlayWidth : 280;
        InitialHeight = s.OverlayHeight > 0 ? s.OverlayHeight : 260;

        fm.FightUpdated += OnFightUpdated;
        fm.FightExpired += OnFightExpired;
        fm.SessionStarted += (_, _) =>
        {
            _lastFight = null;
            _currentFightId = -1;
            _timerTick.Stop();
            FightLabel = "No active fight";
            FightTimer = string.Empty;
            Players = [];
        };
    }

    [RelayCommand]
    private void ToggleAutoShow() => AutoShow = !AutoShow;

    partial void OnModeChanged(OverlayMode value) => DoRefresh();

    partial void OnAutoHideDelaySecChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, 300);
        if (clamped != value) { AutoHideDelaySec = clamped; return; }
        _autoHideTimer.Interval = TimeSpan.FromSeconds(clamped);
        var s = _settingsStore.Load();
        s.OverlayAutoHideDelay = clamped;
        _settingsStore.Save(s);
    }

    partial void OnAutoShowChanged(bool value)
    {
        if (!value)
        {
            _autoHideTimer.Stop();
        }
        else if (_fm.GetActiveFights().Count > 0)
        {
            _autoHideTimer.Stop();
            IsOpen = true;
        }

        var s = _settingsStore.Load();
        s.OverlayAutoShow = value;
        _settingsStore.Save(s);
    }

    private void OnFightUpdated(object? sender, Fight fight)
    {
        if (Application.Current.Dispatcher.CheckAccess()) DoRefresh();
        else Application.Current.Dispatcher.InvokeAsync(DoRefresh);
    }

    private void OnFightExpired(object? sender, Fight fight)
    {
        _lastFight = fight;
        if (Application.Current.Dispatcher.CheckAccess()) DoRefresh();
        else Application.Current.Dispatcher.InvokeAsync(DoRefresh);
    }

    private void DoRefresh()
    {
        var active = _fm.GetActiveFights();
        Fight? target;

        if (active.Count > 0)
        {
            target = active.MaxBy(f => f.LastTime)!;
            _lastFight = target;

            if (AutoShow && !IsOpen) { _autoHideTimer.Stop(); IsOpen = true; }
            else _autoHideTimer.Stop();

            // Start (or keep) the live timer for this fight.
            if (target.Id != _currentFightId)
            {
                _currentFightId = target.Id;
                _fightStartWallClock = DateTime.Now;
            }
            if (!_timerTick.IsEnabled) _timerTick.Start();
            FightTimer = ElapsedStr();
        }
        else
        {
            target = _lastFight;
            if (AutoShow && IsOpen && !_autoHideTimer.IsEnabled)
                _autoHideTimer.Start();

            // Fight ended — freeze the timer at the final log-derived duration.
            _timerTick.Stop();
            FightTimer = target is not null
                ? FormatDuration((long)target.DurationSeconds)
                : string.Empty;
        }

        if (target is null || (target.DamageTotal == 0 && target.TankTotal == 0))
        {
            FightLabel = "No active fight";
            Players = [];
            return;
        }

        var dur = Math.Max(1, target.DurationSeconds);

        switch (Mode)
        {
            case OverlayMode.Tank:
                RefreshTank(target, dur);
                break;
            case OverlayMode.Heal:
                RefreshHeal(target, dur);
                break;
            default:
                RefreshDps(target, dur);
                break;
        }
    }

    private void RefreshDps(Fight target, double dur)
    {
        var total = Math.Max(1L, target.DamageTotal);
        FightLabel = $"{target.NpcName}  •  {target.DamageTotal / dur:N0} DPS";
        Players = BuildRows(
            target.PlayerStats.Values
                .Where(p => p.Damage > 0)
                .OrderByDescending(p => p.Damage)
                .Take(16),
            total,
            p => (p.Name, p.Damage, (long)(p.Damage / Math.Max(1, p.ParsedSeconds))));
    }

    private void RefreshTank(Fight target, double dur)
    {
        var total = Math.Max(1L, target.TankTotal);
        FightLabel = $"{target.NpcName}  •  {target.TankTotal / dur:N0} Inc/s";
        Players = BuildRows(
            target.PlayerTankStats.Values
                .Where(t => t.Damage > 0)
                .OrderByDescending(t => t.Damage)
                .Take(16),
            total,
            t => (t.Name, t.Damage, (long)(t.Damage / dur)));
    }

    private void RefreshHeal(Fight target, double dur)
    {
        var healData = _fm.ComputeHealingForRange(target.StartTime, target.LastTime);
        var total = Math.Max(1L, healData.Values.Sum(h => h.Total));
        var totalHps = (long)(healData.Values.Sum(h => h.Total) / dur);
        FightLabel = $"{target.NpcName}  •  {totalHps:N0} HPS";
        Players = BuildRows(
            healData.Values
                .Where(h => h.Total > 0)
                .OrderByDescending(h => h.Total)
                .Take(16),
            total,
            h => (h.Name, h.Total, (long)(h.Total / h.ParsedSeconds)));
    }

    private ObservableCollection<OverlayPlayerRow> BuildRows<T>(
        IEnumerable<T> source, long total,
        Func<T, (string Name, long Amount, long Rate)> selector)
    {
        return new ObservableCollection<OverlayPlayerRow>(
            source.Select(item =>
            {
                var (name, amount, rate) = selector(item);
                var pct = amount * 100.0 / total;
                var cls = _fm.GetPlayerClass(name);
                var display = cls is not null ? $"[{cls}] {name}" : name;
                return new OverlayPlayerRow(
                    Name: display,
                    DamageText: FormatDamage(amount),
                    Dps: rate,
                    Percent: Math.Round(pct, 1));
            }));
    }

    /// <summary>Resets the auto-hide countdown when the user interacts with the overlay.</summary>
    public void ResetAutoHideTimer()
    {
        if (_autoHideTimer.IsEnabled)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        }
    }

    public void SaveBounds(double left, double top, double width, double height)
    {
        var s = _settingsStore.Load();
        s.OverlayLeft = left;
        s.OverlayTop = top;
        s.OverlayWidth = width;
        s.OverlayHeight = height;
        s.OverlayLocked = IsLocked;
        _settingsStore.Save(s);
    }

    private string ElapsedStr()
    {
        var secs = (long)(DateTime.Now - _fightStartWallClock).TotalSeconds;
        return FormatDuration(secs);
    }

    private static string FormatDuration(long seconds)
    {
        var m = seconds / 60;
        var s = seconds % 60;
        return $"{m}:{s:D2}";
    }

    private static string FormatDamage(long d) => d switch
    {
        >= 1_000_000 => $"{d / 1_000_000.0:F1}M",
        _            => $"{d:N0}"
    };
}
