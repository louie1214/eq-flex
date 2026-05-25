using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EqFlex.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private AppSettings _settings;

    [ObservableProperty] private bool _parseDamage = true;
    [ObservableProperty] private bool _parseHealing = true;
    [ObservableProperty] private bool _parseCasting = true;
    [ObservableProperty] private bool _parseChat;
    [ObservableProperty] private bool _parseTrade;

    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();
    }

    [RelayCommand]
    private void Save()
    {
        _store.Save(_settings);
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
