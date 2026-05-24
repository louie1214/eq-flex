using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Infrastructure.Storage;

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
}
