using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;
using Microsoft.Win32;

namespace EqFlex.App.ViewModels;

public sealed partial class CharacterViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private ShellViewModel? _shell;

    [ObservableProperty] private ObservableCollection<CharacterProfile> _profiles = [];
    [ObservableProperty] private CharacterProfile? _selectedProfile;
    [ObservableProperty] private bool _isEditing;

    // Edit form fields
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editPlayerName = string.Empty;
    [ObservableProperty] private string _editLogPath = string.Empty;
    [ObservableProperty] private bool _editLogArchiveEnabled;
    [ObservableProperty] private int _editLogArchiveSizeMb = 500;
    [ObservableProperty] private bool _editParseDamage = true;
    [ObservableProperty] private bool _editParseHealing = true;
    [ObservableProperty] private bool _editParseCasting = true;
    [ObservableProperty] private bool _editParseTrade;

    public CharacterViewModel(ProfileStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>
    /// Called by ShellViewModel after construction to avoid a circular DI dependency.
    /// ShellViewModel can't be injected in the constructor because CharacterViewModel
    /// is resolved during ShellViewModel's own construction.
    /// </summary>
    public void Initialize(ShellViewModel shell)
    {
        _shell = shell;
    }

    partial void OnSelectedProfileChanged(CharacterProfile? value) =>
        ActivateProfileCommand.NotifyCanExecuteChanged();

    private void Reload()
    {
        Profiles = new ObservableCollection<CharacterProfile>(_store.GetAll()
            .OrderByDescending(p => p.LastUsed));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void ActivateProfile()
    {
        if (SelectedProfile is null || _shell is null) return;
        _shell.ActivateProfile(SelectedProfile);
    }

    private bool HasSelectedProfile => SelectedProfile is not null;

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ClearEditForm();
        IsEditing = true;
    }

    [RelayCommand]
    private void EditProfile()
    {
        if (SelectedProfile is null) return;
        PopulateEditForm(SelectedProfile);
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(EditName)) return;

        var profile = SelectedProfile ?? new CharacterProfile();
        profile.Name = EditName.Trim();
        profile.PlayerName = EditPlayerName.Trim();
        profile.LogPath = EditLogPath.Trim();
        profile.LogArchiveEnabled = EditLogArchiveEnabled;
        profile.LogArchiveSizeMb = Math.Max(1, EditLogArchiveSizeMb);
        profile.ParseDamage = EditParseDamage;
        profile.ParseHealing = EditParseHealing;
        profile.ParseCasting = EditParseCasting;
        profile.ParseTrade = EditParseTrade;
        profile.LastUsed = DateTime.UtcNow;

        _store.Upsert(profile);
        IsEditing = false;
        Reload();
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        if (MessageBox.Show($"Delete profile '{SelectedProfile.Name}'?",
            "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        _store.Delete(SelectedProfile.Id);
        Reload();
        SelectedProfile = null;
    }

    [RelayCommand]
    private void BrowseLogPath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select EverQuest Log File",
            Filter = "Log files (eqlog_*.txt)|eqlog_*.txt|All files (*.*)|*.*",
            InitialDirectory = EditLogPath.Length > 0 ? Path.GetDirectoryName(EditLogPath) : null
        };
        if (dlg.ShowDialog() == true)
            EditLogPath = dlg.FileName;
    }

    private void PopulateEditForm(CharacterProfile p)
    {
        EditName = p.Name;
        EditPlayerName = p.PlayerName;
        EditLogPath = p.LogPath;
        EditLogArchiveEnabled = p.LogArchiveEnabled;
        EditLogArchiveSizeMb = p.LogArchiveSizeMb > 0 ? p.LogArchiveSizeMb : 500;
        EditParseDamage = p.ParseDamage;
        EditParseHealing = p.ParseHealing;
        EditParseCasting = p.ParseCasting;
        EditParseTrade = p.ParseTrade;
    }

    private void ClearEditForm()
    {
        EditName = EditPlayerName = EditLogPath = string.Empty;
        EditLogArchiveEnabled = false;
        EditLogArchiveSizeMb = 500;
        EditParseDamage = EditParseHealing = EditParseCasting = true;
        EditParseTrade = false;
    }
}
