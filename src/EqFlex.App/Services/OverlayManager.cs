using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Media;
using EqFlex.App.Overlays;
using EqFlex.App.ViewModels;
using EqFlex.Core.Models;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Storage;

namespace EqFlex.App.Services;

/// <summary>
/// Owns all TriggerOverlay window instances. Routes trigger actions to the correct window,
/// and handles shared resources (TTS, audio) that are independent of any specific overlay.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class OverlayManager
{
    private readonly TriggerEngine _engine;
    private readonly OverlayWindowStore _store;
    private readonly FctOverlayViewModel _fctVm;
    private readonly SpeechSynthesizer _tts = new();
    private MediaPlayer? _audioPlayer;

    private readonly Dictionary<int, (OverlayWindow Config, TriggerOverlayViewModel Vm, TriggerOverlay Window)> _overlays = [];

    public IReadOnlyCollection<TriggerOverlayViewModel> Overlays => _overlays.Values.Select(e => e.Vm).ToList();

    public OverlayManager(TriggerEngine engine, OverlayWindowStore store, FctOverlayViewModel fctVm)
    {
        _engine = engine;
        _store = store;
        _fctVm = fctVm;
        _engine.TriggerFired += OnTriggerFired;
    }

    /// <summary>Load persisted overlays. Call once from App.OnStartup on the UI thread.</summary>
    public void Initialize()
    {
        var configs = _store.GetAll();
        foreach (var config in configs)
            CreateWindow(config);

        // Always ensure at least one default overlay exists.
        if (_overlays.Count == 0)
        {
            var def = new OverlayWindow { Name = "Default Overlay" };
            _store.Save(def);
            CreateWindow(def);
        }
    }

    public TriggerOverlayViewModel CreateOverlay(string name)
    {
        var config = new OverlayWindow { Name = name };
        _store.Save(config);
        return CreateWindow(config);
    }

    public void DeleteOverlay(int overlayId)
    {
        if (!_overlays.TryGetValue(overlayId, out var entry)) return;
        entry.Window.ForceClose();
        _overlays.Remove(overlayId);
        _store.Delete(overlayId);
    }

    private TriggerOverlayViewModel CreateWindow(OverlayWindow config)
    {
        var vm = new TriggerOverlayViewModel(config);
        vm.SaveRequested += c => _store.Save(c);

        var win = new TriggerOverlay { DataContext = vm };

        // Restore saved position/size
        if (config.Left >= 0) win.Left = config.Left;
        else win.Left = SystemParameters.PrimaryScreenWidth - 360;
        if (config.Top >= 0) win.Top = config.Top;
        else win.Top = 40;
        win.Width = config.Width;
        win.Height = config.Height;

        _overlays[config.Id] = (config, vm, win);
        return vm;
    }

    private void OnTriggerFired(TriggerFiredArgs args)
    {
        var text = TriggerEngine.Substitute(args.Action.Text, args.Captures);

        switch (args.Action.ActionType)
        {
            case TriggerActionType.Speak:
                if (args.Action.SpeakInterrupt) _tts.SpeakAsyncCancelAll();
                _tts.SpeakAsync(text);
                break;

            case TriggerActionType.PlayAudio when !string.IsNullOrWhiteSpace(args.Action.AudioPath):
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _audioPlayer ??= new MediaPlayer();
                    _audioPlayer.Open(new Uri(args.Action.AudioPath, UriKind.Absolute));
                    _audioPlayer.Play();
                });
                break;

            case TriggerActionType.DisplayText:
            case TriggerActionType.Timer:
                var vm = ResolveOverlay(args.Action.OverlayId);
                vm?.Dispatch(args, text);
                break;

            case TriggerActionType.ShowFct when _fctVm.IsEnabled && !string.IsNullOrWhiteSpace(text):
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var color = ParseColor(args.Action.TextColor);
                    _fctVm.SpawnText(text, color, args.Action.FontSize, args.Action.IsBold);
                });
                break;
        }
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.White; }
    }

    private TriggerOverlayViewModel? ResolveOverlay(int overlayId)
    {
        if (overlayId > 0 && _overlays.TryGetValue(overlayId, out var entry))
            return entry.Vm;
        // Fall back to first enabled overlay
        return _overlays.Values.FirstOrDefault(e => e.Config.IsEnabled).Vm;
    }
}
