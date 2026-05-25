using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Interfaces;
using EqFlex.Core.Models;
using EqFlex.Core.Parsing;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Logging;
using EqFlex.Infrastructure.Storage;
using Microsoft.Win32;

namespace EqFlex.App.ViewModels;

public sealed record ParseTimeRangeOption(string Label, TimeSpan? Span);

public sealed partial class LogViewModel : ObservableObject, IDisposable
{
    private readonly ProfileStore _profileStore;
    private readonly FightManager _fightManager;
    private readonly ISpellDataService _spells;
    private readonly SettingsStore _settingsStore;
    private readonly TriggerEngine _triggerEngine;
    private readonly FctOverlayViewModel _fctVm;

    private string _currentPlayerName = string.Empty;

    private LogTailer? _tailer;
    private LogProcessor? _processor;
    private CancellationTokenSource? _replayCts;

    [ObservableProperty] private CharacterProfile? _activeProfile;
    [ObservableProperty] private string _statusText = "No log loaded.";
    [ObservableProperty] private string _logPath = string.Empty;
    [ObservableProperty] private bool _isTailing;
    [ObservableProperty] private bool _isReplaying;
    [ObservableProperty] private long _linesProcessed;
    [ObservableProperty] private string _lastEventTime = "—";
    [ObservableProperty] private double _replayProgress;      // 0–100
    [ObservableProperty] private string _replayProgressText = string.Empty;
    [ObservableProperty] private ParseTimeRangeOption _selectedTimeRange;
    public IReadOnlyList<ParseTimeRangeOption> TimeRangeOptions { get; } =
    [
        new("Last hour",    TimeSpan.FromHours(1)),
        new("Last 8 hours", TimeSpan.FromHours(8)),
        new("Last 24 hours",TimeSpan.FromHours(24)),
        new("Last 2 days",  TimeSpan.FromDays(2)),
        new("Last 7 days",  TimeSpan.FromDays(7)),
        new("All",          null),
    ];

    // Parse Log and Browse are enabled whenever we're not actively replaying and have a path.
    // Clicking Parse Log while tailing is fine — ParseLog() calls StopAll() first.
    public bool CanStart => !IsReplaying && !string.IsNullOrWhiteSpace(LogPath);

    // Start Tail requires we're not already tailing (it doesn't stop-and-restart by itself).
    private bool CanStartTail => !IsTailing && !IsReplaying && !string.IsNullOrWhiteSpace(LogPath);

    private void NotifyCanStartChanged()
    {
        OnPropertyChanged(nameof(CanStart));
        ParseLogCommand.NotifyCanExecuteChanged();
        StartTailingCommand.NotifyCanExecuteChanged();
        BrowseLogCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTailingChanged(bool value) => NotifyCanStartChanged();
    partial void OnIsReplayingChanged(bool value) => NotifyCanStartChanged();
    partial void OnLogPathChanged(string value) => NotifyCanStartChanged();

    public LogViewModel(ProfileStore profileStore, FightManager fightManager,
        ISpellDataService spells, SettingsStore settingsStore, TriggerEngine triggerEngine,
        FctOverlayViewModel fctVm)
    {
        _profileStore = profileStore;
        _fightManager = fightManager;
        _spells = spells;
        _settingsStore = settingsStore;
        _triggerEngine = triggerEngine;
        _fctVm = fctVm;
        _selectedTimeRange = TimeRangeOptions[^1]; // default: All

        var lastProfile = profileStore.GetLastUsed();
        if (lastProfile is not null)
            LoadProfile(lastProfile);
    }

    public void LoadProfile(CharacterProfile profile)
    {
        StopAll();
        ActiveProfile = profile;
        LogPath = profile.LogPath;
        StatusText = $"Profile: {profile.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void BrowseLog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select EverQuest Log File",
            Filter = "Log files (eqlog_*.txt)|eqlog_*.txt|All files (*.*)|*.*",
            InitialDirectory = LogPath.Length > 0 ? Path.GetDirectoryName(LogPath) : null
        };
        if (dlg.ShowDialog() == true)
            LogPath = dlg.FileName;
    }

    // ── Parse existing log (from beginning) then transition to live tail ──

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ParseLog()
    {
        if (!ValidateLogPath()) return;

        StopAll();
        LinesProcessed = 0;
        ReplayProgress = 0;
        ReplayProgressText = string.Empty;

        var (damage, healing, cast, registry) = BuildParsers();
        // Replay processor never runs triggers — historical lines should not fire alerts/TTS/audio.
        // The live-tail processor built after EndReplay() uses the real trigger engine.
        _processor = BuildProcessor(damage, healing, cast, registry, includeTriggers: false);
        _processor.Start();

        // Suppress UI events during replay — per-record FightUpdated callbacks would flood the
        // WPF dispatcher queue and bury progress bar updates. EndReplay fires a single batch.
        _fightManager.BeginReplay();

        _replayCts = new CancellationTokenSource();
        IsReplaying = true;
        StatusText = $"Parsing: {Path.GetFileName(LogPath)}";

        var progress = new Progress<(long linesRead, long bytesRead, long totalBytes)>(p =>
        {
            LinesProcessed = p.linesRead;
            ReplayProgress = p.totalBytes > 0
                ? Math.Min(100.0, p.bytesRead * 100.0 / p.totalBytes)
                : 0;
            ReplayProgressText = $"{p.linesRead:N0} lines  ({ReplayProgress:F0}%)";
        });

        var cutoff = ComputeCutoffTimestamp(SelectedTimeRange);

        try
        {
            // Run the replay loop on a thread-pool thread so the UI stays responsive.
            await Task.Run(async () =>
            {
                await foreach (var line in LogTailer.ReplayAsync(LogPath, progress, cutoff, _replayCts.Token))
                    _processor.Enqueue(line);
            }, _replayCts.Token);

            if (!_replayCts.IsCancellationRequested)
            {
                // Drain: complete the channel writer and wait for the consumer to finish ALL
                // queued lines (no token cancellation — unlike Stop(), this doesn't abandon items).
                // Uses a 30 s ceiling so even very large logs (500k+ lines) drain completely.
                StatusText = "Finalizing…";
                await Task.Run(() => _processor!.Drain(), _replayCts.Token);

                // All records processed — fire a single batch of UI updates
                _fightManager.EndReplay();

                // Expire any fights still "active" at log-end so the overlay doesn't treat
                // them as live. They show in the fight list as completed fights.
                _fightManager.ExpireAllActive();

                var fightCount = _fightManager.GetCompletedFights().Count;

                // Build a fresh processor for live tailing — this one includes triggers.
                _processor = BuildProcessor(damage, healing, cast, registry);
                _processor.Start();

                StatusText = $"Parse complete — {fightCount} fight{(fightCount == 1 ? "" : "s")}. Tailing: {Path.GetFileName(LogPath)}";
                ReplayProgress = 100;
                ReplayProgressText = $"{LinesProcessed:N0} lines (100%)";

                _tailer = BuildTailer();
                _tailer.Start(fromEnd: true);
                IsTailing = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Parse cancelled.";
        }
        finally
        {
            // Always re-enable events (EndReplay is idempotent if already called above)
            _fightManager.EndReplay();
            IsReplaying = false;
            _replayCts.Dispose();
            _replayCts = null;
        }
    }

    [RelayCommand]
    private void CancelReplay()
    {
        _replayCts?.Cancel();
    }

    // ── Live tail only (from end of file) ──

    [RelayCommand(CanExecute = nameof(CanStartTail))]
    private void StartTailing()
    {
        if (!ValidateLogPath()) return;

        StopAll();
        LinesProcessed = 0;

        var (damage, healing, cast, registry) = BuildParsers();
        _processor = BuildProcessor(damage, healing, cast, registry);

        _tailer = BuildTailer();

        _processor.Start();
        _tailer.Start(fromEnd: true);

        IsTailing = true;
        StatusText = $"Tailing: {Path.GetFileName(LogPath)}";
    }

    [RelayCommand]
    private void StopAll()
    {
        _replayCts?.Cancel();
        _tailer?.Stop();
        _processor?.Stop();
        _tailer?.Dispose();
        _tailer = null;
        _processor = null;
        IsTailing = false;
        IsReplaying = false;
        ReplayProgress = 0;
        ReplayProgressText = string.Empty;
        if (LogPath.Length > 0)
            StatusText = "Stopped.";
    }

    // ── Shared helpers ──

    private (DamageParser damage, HealingParser healing, CastParser cast, PlayerRegistry registry) BuildParsers()
    {
        var playerName = ResolvePlayerName();
        _currentPlayerName = playerName;
        _triggerEngine.PlayerName = playerName;

        // Create a fresh registry for this session and hand it to FightManager
        var registry = new PlayerRegistry(playerName, _spells);
        _fightManager.BeginSession(registry);

        var damage = new DamageParser(playerName, _spells, registry);
        damage.DamageProcessed += r =>
        {
            _fightManager.RecordDamage(r);
            // Skip the dispatcher post during replay — 200k+ InvokeAsync calls would still
            // flood the queue even though FightUpdated is suppressed.
            if (IsReplaying) return;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsReplaying)
                {
                    LinesProcessed++;
                    LastEventTime = DateTime.Now.ToString("HH:mm:ss");
                    if (_fctVm.IsEnabled) SpawnFctDamage(r);
                }
            });
        };
        damage.EntitySlain += (slain, _) => _fightManager.RemoveFight(slain);

        var healing = new HealingParser(playerName);
        healing.HealProcessed += r =>
        {
            _fightManager.RecordHealing(r);
            if (IsReplaying) return;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsReplaying && _fctVm.IsEnabled)
                    SpawnFctHeal(r);
            });
        };

        var cast = new CastParser(playerName, registry, _spells);

        return (damage, healing, cast, registry);
    }

    // ── FCT helpers ──

    private static readonly HashSet<string> _meleeAutoAttacks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Slashes", "Crushes", "Pierces", "Punches", "Hits"
    };

    private bool IsAbilityHit(DamageRecord r)
        => r.Type == DamageType.Melee && !string.IsNullOrEmpty(r.Ability) && !_meleeAutoAttacks.Contains(r.Ability);

    private bool IsMyPetDamage(DamageRecord r)
        => r.AttackerOwner != null &&
           r.AttackerOwner.Equals(_currentPlayerName, StringComparison.OrdinalIgnoreCase);

    private void SpawnFctDamage(DamageRecord r)
    {
        if (r.Total == 0) return;
        bool isMyPet = IsMyPetDamage(r);
        if (r.Perspective == DamagePerspective.Observed && !isMyPet) return;
        if (r.Perspective == DamagePerspective.Incoming && !_fctVm.ShowIncoming) return;
        if (isMyPet && !_fctVm.ShowPet) return;

        if (r.Perspective != DamagePerspective.Incoming && !isMyPet)
        {
            bool isAbility = IsAbilityHit(r);
            var typeVisible = isAbility ? _fctVm.ShowAbility : r.Type switch
            {
                DamageType.Melee                    => _fctVm.ShowMelee,
                DamageType.Spell or DamageType.Proc => _fctVm.ShowSpell,
                DamageType.Dot                      => _fctVm.ShowDot,
                _                                   => true
            };
            if (!typeVisible) return;
        }

        var color    = GetFctDamageColor(r);
        var fontSize = r.IsCritical
            ? _fctVm.BaseFontSize * _fctVm.CritScale
            : _fctVm.BaseFontSize;
        _fctVm.SpawnText(r.Total.ToString("N0"), color, fontSize, r.IsCritical);
    }

    private void SpawnFctHeal(HealingRecord r)
    {
        if (r.Amount <= 0 && r.OverHeal <= 0) return;
        var isForPlayer  = r.Target.Equals(_currentPlayerName, StringComparison.OrdinalIgnoreCase);
        var isFromPlayer = r.Healer.Equals(_currentPlayerName, StringComparison.OrdinalIgnoreCase);

        bool isReceived = isForPlayer;
        bool isDone     = isFromPlayer && !isForPlayer;

        if (!isReceived && !isDone) return;
        if (isReceived && !_fctVm.ShowHealReceived) return;
        if (isDone     && !_fctVm.ShowHealDone)     return;

        Color color;
        if (isReceived)
            color = r.IsCritical ? ParseFctColor(_fctVm.HealReceivedCritColor, Color.FromRgb(0x00, 0xFF, 0x7F))
                                 : ParseFctColor(_fctVm.HealReceivedColor,     Color.FromRgb(0x22, 0xDD, 0x44));
        else
            color = r.IsCritical ? ParseFctColor(_fctVm.HealDoneCritColor, Color.FromRgb(0x44, 0xFF, 0x88))
                                 : ParseFctColor(_fctVm.HealDoneColor,     Color.FromRgb(0x22, 0xCC, 0x55));

        var text = r.Amount <= 0
            ? $"({r.OverHeal:N0} oh)"
            : r.OverHeal > 0
                ? $"+{r.Amount:N0} ({r.OverHeal:N0} oh)"
                : $"+{r.Amount:N0}";

        if (isReceived && !isFromPlayer && _fctVm.ShowHealReceivedCaster)
            text = $"{r.Healer}: {text}";
        var fontSize = r.IsCritical
            ? _fctVm.BaseFontSize * _fctVm.CritScale
            : _fctVm.BaseFontSize;
        _fctVm.SpawnText(text, color, fontSize, r.IsCritical);
    }

    private Color GetFctDamageColor(DamageRecord r)
    {
        if (r.Perspective == DamagePerspective.Incoming)
            return r.IsCritical ? ParseFctColor(_fctVm.IncomingCritColor, Color.FromRgb(0xFF, 0x40, 0x40))
                                : ParseFctColor(_fctVm.IncomingColor,     Color.FromRgb(0xCC, 0x55, 0x55));

        if (IsMyPetDamage(r))
            return r.IsCritical ? ParseFctColor(_fctVm.PetCritColor, Color.FromRgb(0xFF, 0xCC, 0x44))
                                : ParseFctColor(_fctVm.PetColor,     Color.FromRgb(0xFF, 0x99, 0x00));

        if (IsAbilityHit(r))
            return r.IsCritical ? ParseFctColor(_fctVm.AbilityCritColor, Color.FromRgb(0xFF, 0xCC, 0x00))
                                : ParseFctColor(_fctVm.AbilityColor,     Color.FromRgb(0xFF, 0xFF, 0x44));

        return r.Type switch
        {
            DamageType.Melee when r.IsCritical => ParseFctColor(_fctVm.MeleeCritColor, Color.FromRgb(0xFF, 0x8C, 0x00)),
            DamageType.Melee                   => ParseFctColor(_fctVm.MeleeColor,     Colors.White),
            DamageType.Spell when r.IsCritical => ParseFctColor(_fctVm.SpellCritColor, Color.FromRgb(0x00, 0xCC, 0xFF)),
            DamageType.Spell                   => ParseFctColor(_fctVm.SpellColor,     Color.FromRgb(0x44, 0x88, 0xFF)),
            DamageType.Dot   when r.IsCritical => ParseFctColor(_fctVm.DotCritColor,   Color.FromRgb(0xDD, 0x44, 0xFF)),
            DamageType.Dot                     => ParseFctColor(_fctVm.DotColor,       Color.FromRgb(0x99, 0x44, 0xDD)),
            DamageType.Proc  when r.IsCritical => ParseFctColor(_fctVm.SpellCritColor, Color.FromRgb(0x00, 0xCC, 0xFF)),
            DamageType.Proc                    => ParseFctColor(_fctVm.SpellColor,     Color.FromRgb(0x44, 0x88, 0xFF)),
            _                                  => Color.FromRgb(0xCC, 0xCC, 0xCC),
        };
    }

    private static Color ParseFctColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    /// <summary>Raised when a {FLEX:share/CODE} is detected in a live log line.</summary>
    public event Action<string>? ShareCodeDetected;

    private LogProcessor BuildProcessor(DamageParser damage, HealingParser healing, CastParser cast,
        PlayerRegistry registry, bool includeTriggers = true)
    {
        var profile = ActiveProfile;
        var processor = new LogProcessor(
            profile?.ParseDamage != false ? damage : null,
            profile?.ParseHealing != false ? healing : null,
            profile?.ParseCasting != false ? cast : null,
            registry,
            _spells,
            onPetCharmed: petName => _fightManager.RemoveFight(petName),
            triggerEngine: includeTriggers ? _triggerEngine : null);

        if (includeTriggers)
            processor.ShareCodeDetected += code => ShareCodeDetected?.Invoke(code);

        return processor;
    }

    private LogTailer BuildTailer()
    {
        var tailer = new LogTailer(LogPath);
        tailer.LineRead += line => _processor!.Enqueue(line);
        tailer.Error += ex => Application.Current.Dispatcher.InvokeAsync(() =>
            StatusText = $"Error: {ex.Message}");
        var profile = ActiveProfile;
        if (profile?.LogArchiveEnabled == true && profile.LogArchiveSizeMb > 0)
        {
            tailer.MaxSizeBytes = (long)profile.LogArchiveSizeMb * 1024 * 1024;
            tailer.ArchiveNeeded += OnArchiveNeeded;
        }
        return tailer;
    }

    // Called on the LogTailer background thread — dispatch to UI before touching state.
    private void OnArchiveNeeded()
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (!IsTailing) return;

            // The tailer task has already exited (it returned after firing ArchiveNeeded).
            // Stop() will return quickly since _task is already done.
            _tailer?.Stop();
            _tailer?.Dispose();
            _tailer = null;

            TryRenameLog(LogPath);

            // Create a fresh empty log so EQ can continue writing immediately.
            try { await File.WriteAllTextAsync(LogPath, string.Empty); } catch { }

            StatusText = $"Log archived. Restarting tail: {Path.GetFileName(LogPath)}";

            _tailer = BuildTailer();
            _tailer.Start(fromEnd: true);
        });
    }

    private string ResolvePlayerName()
    {
        if (!string.IsNullOrWhiteSpace(ActiveProfile?.PlayerName))
            return ActiveProfile.PlayerName;

        // Fall back to extracting from filename: eqlog_PlayerName_ServerName.txt
        var stem = Path.GetFileNameWithoutExtension(LogPath);
        var parts = stem.Replace("eqlog_", string.Empty, StringComparison.OrdinalIgnoreCase).Split('_');
        return parts.Length > 0 ? parts[0] : "Unknown";
    }

    // EQ log timestamps are local time with no timezone, so we compare against DateTime.Now
    // using the same Unspecified-kind epoch as LogProcessor.
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static long ComputeCutoffTimestamp(ParseTimeRangeOption option)
    {
        if (option.Span is null) return 0; // All — no cutoff
        var cutoffDt = DateTime.Now - option.Span.Value;
        return (long)(cutoffDt - UnixEpoch).TotalSeconds;
    }

    private bool ValidateLogPath()
    {
        if (!string.IsNullOrWhiteSpace(LogPath) && File.Exists(LogPath)) return true;
        StatusText = "Log file not found.";
        return false;
    }

    private static void TryRenameLog(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(dir, $"{name}_{stamp}.txt");
            File.Move(path, dest, overwrite: false);
        }
        catch { /* non-critical */ }
    }

    public void Dispose()
    {
        StopAll();
        _processor?.Dispose();
    }
}
