using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace EqFlex.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly SoundLibrary  _soundLibrary;
    private AppSettings _settings;

    public SettingsViewModel(SettingsStore store, SoundLibrary soundLibrary)
    {
        _store        = store;
        _soundLibrary = soundLibrary;
        _settings     = store.Load();
    }

    [RelayCommand]
    private void ImportAudio()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import audio file",
            Filter = "Audio files|*.wav;*.mp3;*.ogg;*.flac|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _soundLibrary.Import(dlg.FileName);
        MessageBox.Show($"'{Path.GetFileName(dlg.FileName)}' imported successfully.\n\nIt is now available in all sound pickers.",
            "Audio Imported", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqFlex", "logs");
        Directory.CreateDirectory(logDir);
        Process.Start("explorer.exe", logDir);
    }

    [RelayCommand]
    private void ClearUserData()
    {
        var result = MessageBox.Show(
            "This will permanently delete all character profiles, triggers, overlays, and settings.\n\n" +
            "The application will restart automatically.\n\n" +
            "Are you sure?",
            "Clear All User Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqFlex", "eqflex.db");

        // Dispose the DB connection before deleting the file.
        App.Services.GetRequiredService<LiteDbContext>().Dispose();

        try { File.Delete(dbPath); } catch { /* best-effort */ }

        // Restart into a clean state.
        Process.Start(Environment.ProcessPath!);
        Application.Current.Shutdown();
    }
}
