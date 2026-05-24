using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EqFlex.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly SettingsStore _settingsStore;

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string _activeSection = "Characters";
    [ObservableProperty] private CharacterProfile? _activeProfile;

    public OverlayViewModel OverlayVm { get; }

    public ShellViewModel(IServiceProvider services, SettingsStore settingsStore,
        OverlayViewModel overlayVm, ProfileStore profileStore)
    {
        _services = services;
        _settingsStore = settingsStore;
        OverlayVm = overlayVm;

        // Restore last active profile
        var settings = settingsStore.Load();
        if (settings.LastActiveProfileId.HasValue)
        {
            ActiveProfile = profileStore.GetAll()
                .FirstOrDefault(p => p.Id == settings.LastActiveProfileId.Value);
        }

        ShowCharacters();
    }

    /// <summary>
    /// Marks a profile as the active character for this session, updates the log VM,
    /// and starts live tailing if the profile has a log path configured.
    /// Persists the selection so it is restored on next launch.
    /// </summary>
    public void ActivateProfile(CharacterProfile profile)
    {
        ActiveProfile = profile;

        var settings = _settingsStore.Load();
        settings.LastActiveProfileId = profile.Id;
        _settingsStore.Save(settings);

        var logVm = _services.GetRequiredService<LogViewModel>();
        logVm.LoadProfile(profile);
        if (logVm.StartTailingCommand.CanExecute(null))
            logVm.StartTailingCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowDamage() { ActiveSection = "Damage"; CurrentPage = _services.GetRequiredService<DamageViewModel>(); }

    [RelayCommand]
    private void ShowCharacters()
    {
        ActiveSection = "Characters";
        var vm = _services.GetRequiredService<CharacterViewModel>();
        vm.Initialize(this);
        CurrentPage = vm;
    }

    [RelayCommand]
    private void ShowLog() { ActiveSection = "Log"; CurrentPage = _services.GetRequiredService<LogViewModel>(); }

    [RelayCommand]
    private void ShowSettings() { ActiveSection = "Settings"; CurrentPage = _services.GetRequiredService<SettingsViewModel>(); }

    [RelayCommand]
    private void ShowTriggers() { ActiveSection = "Triggers"; CurrentPage = _services.GetRequiredService<TriggersViewModel>(); }

    [RelayCommand]
    private void ShowOverlays() { ActiveSection = "Overlays"; CurrentPage = _services.GetRequiredService<OverlaysViewModel>(); }

    [RelayCommand]
    private void ToggleOverlay() => OverlayVm.IsOpen = !OverlayVm.IsOpen;
}
