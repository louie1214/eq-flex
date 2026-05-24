using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EqFlex.Core.Models;
using EqFlex.Core.Services;
using WpfTextAlignment = System.Windows.TextAlignment;

namespace EqFlex.App.ViewModels;

// ── Display items ─────────────────────────────────────────────────────────────

public sealed partial class TriggerAlertItem : ObservableObject
{
    public string Text { get; }
    public string TextColor { get; }
    public double FontSize { get; }
    public bool IsBold { get; }
    public bool IsPreview { get; }
    [ObservableProperty] private double _secondsRemaining;

    public TriggerAlertItem(string text, string color, double durationSec,
        double fontSize = 13, bool isBold = false, bool isPreview = false)
    {
        Text = text;
        TextColor = color;
        FontSize = fontSize > 0 ? fontSize : 13;
        IsBold = isBold;
        IsPreview = isPreview;
        SecondsRemaining = durationSec;
    }

    public bool IsExpired => !IsPreview && SecondsRemaining <= 0;
}

public sealed partial class TriggerTimerItem : ObservableObject
{
    private const string DefaultBarColor = "#FF007ACC";

    public string Name { get; }
    public double TotalSec { get; }
    public bool IsPreview { get; }
    /// <summary>Progress bar fill color. Falls back to the default blue when empty.</summary>
    public string BarColor { get; }
    [ObservableProperty] private double _remainingSec;

    partial void OnRemainingSecChanged(double value)
    {
        OnPropertyChanged(nameof(BarPercent));
        OnPropertyChanged(nameof(TimeDisplay));
    }

    public double BarPercent => TotalSec > 0 ? Math.Max(0, Math.Min(100, RemainingSec / TotalSec * 100.0)) : 0;
    public string TimeDisplay => RemainingSec > 0 ? $"{(int)RemainingSec}s" : "Done";

    public TriggerTimerItem(string name, double durationSec, string? barColor = null, bool isPreview = false)
    {
        Name = name;
        TotalSec = durationSec;
        RemainingSec = durationSec;
        IsPreview = isPreview;
        BarColor = string.IsNullOrWhiteSpace(barColor) ? DefaultBarColor : barColor;
    }

    public bool IsExpired => !IsPreview && RemainingSec <= 0;
}

// ── ViewModel ────────────────────────────────────────────────────────────────

public sealed partial class TriggerOverlayViewModel : ObservableObject
{
    public OverlayWindow Config { get; }
    public int OverlayId => Config.Id;
    public string OverlayName => Config.Name;

    private readonly DispatcherTimer _ticker;
    private TriggerAlertItem? _previewAlert;
    private TriggerTimerItem? _previewTimer;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLocked = true;
    [ObservableProperty] private bool _isPreview;
    [ObservableProperty] private bool _showChrome;
    [ObservableProperty] private double _backgroundOpacity;
    [ObservableProperty] private OverlayTextAlign _textAlign;
    [ObservableProperty] private int _timerRowHeight;

    /// <summary>Chrome (border + header) is visible when explicitly enabled OR while in preview mode.</summary>
    public bool ChromeVisible => ShowChrome || IsPreview;

    public WpfTextAlignment WpfTextAlignment => TextAlign switch
    {
        OverlayTextAlign.Center => WpfTextAlignment.Center,
        OverlayTextAlign.Right  => WpfTextAlignment.Right,
        _                       => WpfTextAlignment.Left
    };
    [ObservableProperty] private ObservableCollection<TriggerAlertItem> _alerts = [];
    [ObservableProperty] private ObservableCollection<TriggerTimerItem> _timers = [];

    /// <summary>Raised when the overlay's position/size should be persisted.</summary>
    public event Action<OverlayWindow>? SaveRequested;

    public TriggerOverlayViewModel(OverlayWindow config)
    {
        Config = config;
        BackgroundOpacity = config.BackgroundOpacity;
        ShowChrome = config.ShowChrome;
        TextAlign = config.TextAlign;
        TimerRowHeight = config.TimerRowHeight;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    partial void OnBackgroundOpacityChanged(double value)
    {
        Config.BackgroundOpacity = Math.Clamp(value, 0.0, 1.0);
        SaveRequested?.Invoke(Config);
    }

    partial void OnShowChromeChanged(bool value)
    {
        Config.ShowChrome = value;
        SaveRequested?.Invoke(Config);
        OnPropertyChanged(nameof(ChromeVisible));
    }

    partial void OnTextAlignChanged(OverlayTextAlign value)
    {
        Config.TextAlign = value;
        SaveRequested?.Invoke(Config);
        OnPropertyChanged(nameof(WpfTextAlignment));
    }

    partial void OnTimerRowHeightChanged(int value)
    {
        Config.TimerRowHeight = Math.Clamp(value, 14, 60);
        SaveRequested?.Invoke(Config);
    }

    partial void OnIsPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(ChromeVisible));
        if (value)
        {
            _previewAlert = new TriggerAlertItem(
                "Preview: Trigger text appears here in the configured color",
                "#FFFF9900", 99999, isPreview: true);
            _previewTimer = new TriggerTimerItem("Preview Timer", 60, isPreview: true)
            {
                RemainingSec = 38
            };
            if (_previewAlert is not null) Alerts.Insert(0, _previewAlert);
            if (_previewTimer is not null) Timers.Insert(0, _previewTimer);
            IsLocked = false;   // unlock so user can drag while previewing
            IsOpen = true;
        }
        else
        {
            if (_previewAlert is not null) { Alerts.Remove(_previewAlert); _previewAlert = null; }
            if (_previewTimer is not null) { Timers.Remove(_previewTimer); _previewTimer = null; }
            IsLocked = true;
            if (Alerts.Count == 0 && Timers.Count == 0) IsOpen = false;
        }
    }

    // Called by OverlayManager to deliver a display/timer action to this window.
    public void Dispatch(TriggerFiredArgs args, string text)
    {
        if (Application.Current.Dispatcher.CheckAccess()) DoDispatch(args, text);
        else Application.Current.Dispatcher.InvokeAsync(() => DoDispatch(args, text));
    }

    private void DoDispatch(TriggerFiredArgs args, string text)
    {
        switch (args.Action.ActionType)
        {
            case TriggerActionType.DisplayText:
                Alerts.Insert(0, new TriggerAlertItem(
                    text, args.Action.TextColor, args.Action.DurationSec,
                    args.Action.FontSize, args.Action.IsBold));
                if (!IsOpen) IsOpen = true;
                break;

            case TriggerActionType.Timer:
                var name = string.IsNullOrWhiteSpace(text) ? args.Trigger.Name : text;
                var existing = Timers.FirstOrDefault(t =>
                    !t.IsPreview && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null) Timers.Remove(existing);
                Timers.Insert(0, new TriggerTimerItem(name, args.Action.DurationSec,
                    barColor: args.Action.TimerBarColor));
                if (!IsOpen) IsOpen = true;
                break;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        foreach (var a in Alerts.Where(a => !a.IsPreview).ToList())
        {
            a.SecondsRemaining--;
            if (a.IsExpired) Alerts.Remove(a);
        }
        foreach (var t in Timers.Where(t => !t.IsPreview).ToList())
        {
            t.RemainingSec--;
            if (t.IsExpired) Timers.Remove(t);
        }
        if (!IsPreview && Alerts.Count == 0 && Timers.Count == 0)
            IsOpen = false;
    }

    public void SavePosition(double left, double top, double width, double height)
    {
        Config.Left = left; Config.Top = top; Config.Width = width; Config.Height = height;
        SaveRequested?.Invoke(Config);
    }
}
