using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EqFlex.App.ViewModels;

namespace EqFlex.App.Overlays;

public partial class TriggerOverlay : Window
{
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _boundsTimer;
    private bool _forceClose;

    public TriggerOverlay()
    {
        InitializeComponent();

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => EnforceTopmost();

        _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _boundsTimer.Tick += (_, _) =>
        {
            _boundsTimer.Stop();
            Vm?.SavePosition(Left, Top, Width, Height);
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TriggerOverlayViewModel old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is TriggerOverlayViewModel vm) vm.PropertyChanged += OnVmPropertyChanged;
        };

        Loaded += (_, _) =>
        {
            ApplyClickThrough();
            UpdateLockVisual();
            _topmostTimer.Start();
        };
    }

    private TriggerOverlayViewModel? Vm => DataContext as TriggerOverlayViewModel;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TriggerOverlayViewModel.IsOpen))
        {
            if (Vm?.IsOpen == true) Show(); else Hide();
        }
        else if (e.PropertyName == nameof(TriggerOverlayViewModel.IsLocked))
        {
            ApplyClickThrough();
            UpdateLockVisual();
        }
    }

    private void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || Vm is null) return;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if (Vm.IsLocked)
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
        else
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style & ~NativeMethods.WS_EX_TRANSPARENT);
    }

    private void UpdateLockVisual()
    {
        if (Vm is null) return;
        LockBtn.Text = Vm.IsLocked ? "🔒" : "🔓";
        DragArea.Cursor = Vm.IsLocked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void EnforceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) { }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.IsLocked == false && e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Lock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null) Vm.IsLocked = !Vm.IsLocked;
        e.Handled = true;
    }

    private void Close_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null) { Vm.SavePosition(Left, Top, Width, Height); Vm.IsOpen = false; }
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN,
            new IntPtr(NativeMethods.HTBOTTOMRIGHT), IntPtr.Zero);
        e.Handled = true;
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e); _boundsTimer.Stop(); _boundsTimer.Start();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo); _boundsTimer.Stop(); _boundsTimer.Start();
    }

    /// <summary>Called by OverlayManager to destroy the window cleanly.</summary>
    public void ForceClose() { _forceClose = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forceClose || App.IsShuttingDown) return;
        e.Cancel = true;
        if (Vm is not null) { Vm.SavePosition(Left, Top, Width, Height); Vm.IsOpen = false; }
    }
}
