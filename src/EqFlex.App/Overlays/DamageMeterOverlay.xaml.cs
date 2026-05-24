using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EqFlex.App.ViewModels;

namespace EqFlex.App.Overlays;

public partial class DamageMeterOverlay : Window
{
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _boundsTimer;

    public DamageMeterOverlay()
    {
        InitializeComponent();

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => EnforceTopmost();

        // Debounced save: fires 500 ms after the last move or resize.
        _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _boundsTimer.Tick += (_, _) =>
        {
            _boundsTimer.Stop();
            Vm.SaveBounds(Left, Top, Width, Height);
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is OverlayViewModel old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is OverlayViewModel vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                // Restore saved size
                Width = vm.InitialWidth;
                Height = vm.InitialHeight;
            }
        };

        Loaded += OnFirstLoaded;
    }

    private OverlayViewModel Vm => (OverlayViewModel)DataContext;

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        ApplyClickThrough();
        UpdateLockVisual();
        _topmostTimer.Start();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsOpen))
        {
            if (Vm.IsOpen) Show();
            else Hide();
        }
        else if (e.PropertyName == nameof(OverlayViewModel.IsLocked))
        {
            ApplyClickThrough();
            UpdateLockVisual();
        }
    }

    private void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
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

    // Any click on the overlay resets the auto-hide countdown so the user can
    // review other views (Tank, Heal) without the overlay vanishing.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Vm.ResetAutoHideTimer();
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!Vm.IsLocked && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // Hand sizing to Windows via WM_NCLBUTTONDOWN so the transparent window resizes
    // correctly without any WPF chrome involvement.
    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN,
            new IntPtr(NativeMethods.HTBOTTOMRIGHT), IntPtr.Zero);
        e.Handled = true;
    }

    private void DpsMode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Vm.Mode = OverlayMode.Dps;
        e.Handled = true;
    }

    private void TankMode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Vm.Mode = OverlayMode.Tank;
        e.Handled = true;
    }

    private void HealMode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Vm.Mode = OverlayMode.Heal;
        e.Handled = true;
    }

    private void Lock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Vm.IsLocked = !Vm.IsLocked;
        e.Handled = true;
    }

    private void Close_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Vm.SaveBounds(Left, Top, Width, Height);
        Vm.IsOpen = false;
        e.Handled = true;
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.IsShuttingDown) return;
        e.Cancel = true;
        Vm.SaveBounds(Left, Top, Width, Height);
        Vm.IsOpen = false;
    }
}
