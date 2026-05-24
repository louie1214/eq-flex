using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;

namespace EqFlex.App.ViewModels;

public sealed partial class OverlaysViewModel : ObservableObject
{
    private readonly OverlayManager _overlayManager;

    // DPS overlay — settings surfaced here
    public OverlayViewModel DpsOverlay { get; }

    // FCT overlay
    public FctOverlayViewModel FctVm { get; }

    // Trigger overlays
    [ObservableProperty] private ObservableCollection<TriggerOverlayViewModel> _triggerOverlays = [];
    [ObservableProperty] private TriggerOverlayViewModel? _selectedTriggerOverlay;
    [ObservableProperty] private bool _hasTriggerSelection;
    [ObservableProperty] private string _newOverlayName = "New Overlay";

    public OverlaysViewModel(OverlayManager overlayManager, OverlayViewModel dpsOverlay, FctOverlayViewModel fctVm)
    {
        _overlayManager = overlayManager;
        DpsOverlay = dpsOverlay;
        FctVm = fctVm;
        Refresh();
    }

    partial void OnSelectedTriggerOverlayChanged(TriggerOverlayViewModel? value)
    {
        HasTriggerSelection = value is not null;
        DeleteTriggerOverlayCommand.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        TriggerOverlays = new ObservableCollection<TriggerOverlayViewModel>(_overlayManager.Overlays);
    }

    // ── Trigger overlay CRUD ──────────────────────────────────────────────────

    [RelayCommand]
    private void AddTriggerOverlay()
    {
        if (string.IsNullOrWhiteSpace(NewOverlayName)) return;
        _overlayManager.CreateOverlay(NewOverlayName.Trim());
        NewOverlayName = "New Overlay";
        Refresh();
        SelectedTriggerOverlay = TriggerOverlays.LastOrDefault();
    }

    [RelayCommand(CanExecute = nameof(HasTriggerSelection))]
    private void DeleteTriggerOverlay()
    {
        if (SelectedTriggerOverlay is null) return;
        _overlayManager.DeleteOverlay(SelectedTriggerOverlay.OverlayId);
        Refresh();
        SelectedTriggerOverlay = TriggerOverlays.FirstOrDefault();
    }

    // ── Preview / move ────────────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePreview(TriggerOverlayViewModel? vm)
    {
        if (vm is null) return;
        vm.IsPreview = !vm.IsPreview;
    }

    [RelayCommand]
    private void ToggleDpsVisible() => DpsOverlay.IsOpen = !DpsOverlay.IsOpen;

    [RelayCommand]
    private void ToggleDpsMove()
    {
        DpsOverlay.IsLocked = !DpsOverlay.IsLocked;
        if (!DpsOverlay.IsLocked && !DpsOverlay.IsOpen)
            DpsOverlay.IsOpen = true;
        OnPropertyChanged(nameof(DpsMoveLabel));
    }

    public string DpsMoveLabel => DpsOverlay.IsLocked ? "Move DPS Overlay" : "Lock DPS Overlay";

    [RelayCommand]
    private void ToggleFctMove()
    {
        FctVm.IsLocked = !FctVm.IsLocked;
        OnPropertyChanged(nameof(FctMoveLabel));
    }

    public string FctMoveLabel => FctVm.IsLocked ? "Move FCT Overlay" : "Lock FCT Overlay";
}
