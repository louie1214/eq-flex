using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;
using Microsoft.Win32;

namespace EqFlex.App.ViewModels;

public sealed partial class SetupWizardViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;
    private readonly SettingsStore _settingsStore;

    [ObservableProperty] private int _step; // 0=Welcome 1=Character 2=Done
    [ObservableProperty] private string _profileName = string.Empty;
    [ObservableProperty] private string _playerName = string.Empty;
    [ObservableProperty] private string _server = string.Empty;
    [ObservableProperty] private string _logPath = string.Empty;

    public bool IsWelcomeStep   => Step == 0;
    public bool IsCharacterStep => Step == 1;
    public bool IsDoneStep      => Step == 2;

    public bool CanAdvance =>
        !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(LogPath);

    public CharacterProfile? CreatedProfile { get; private set; }

    public SetupWizardViewModel(ProfileStore profileStore, SettingsStore settingsStore)
    {
        _profileStore = profileStore;
        _settingsStore = settingsStore;
    }

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsCharacterStep));
        OnPropertyChanged(nameof(IsDoneStep));
    }

    partial void OnProfileNameChanged(string value) =>
        NextCommand.NotifyCanExecuteChanged();

    partial void OnLogPathChanged(string value)
    {
        NextCommand.NotifyCanExecuteChanged();

        // Auto-fill player name and server from log filename if not yet set.
        // eqlog_PlayerName_ServerName.txt → parts[0]=PlayerName, parts[1]=ServerName
        if (!string.IsNullOrWhiteSpace(value))
        {
            var stem = Path.GetFileNameWithoutExtension(value);
            var parts = stem.Replace("eqlog_", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Split('_');
            if (string.IsNullOrWhiteSpace(PlayerName) && parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                PlayerName = parts[0];
            if (string.IsNullOrWhiteSpace(Server) && parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                Server = char.ToUpper(parts[1][0]) + parts[1][1..];
        }
    }

    [RelayCommand]
    private void Start() => Step = 1;

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Next()
    {
        SaveProfile();
        Step = 2;
    }

    [RelayCommand]
    private void Back() => Step = 0;

    [RelayCommand]
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

    private void SaveProfile()
    {
        if (CreatedProfile is not null) return;
        var profile = new CharacterProfile
        {
            Name = ProfileName.Trim(),
            PlayerName = PlayerName.Trim(),
            Server = Server.Trim(),
            LogPath = LogPath.Trim(),
            ParseDamage = true,
            ParseHealing = true,
            ParseCasting = true,
            LastUsed = DateTime.UtcNow
        };
        _profileStore.Upsert(profile);
        CreatedProfile = profile;
    }

    public void MarkComplete()
    {
        var s = _settingsStore.Load();
        s.SetupComplete = true;
        _settingsStore.Save(s);
    }
}
