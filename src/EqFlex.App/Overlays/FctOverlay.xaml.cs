using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using EqFlex.App.ViewModels;

namespace EqFlex.App.Overlays;

public partial class FctOverlay : Window
{
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _boundsTimer;
    private readonly Random _rng = new();

    // Round-robin counter for lane/tier slot assignment.
    // slot = _spawnIndex % (laneCount × 3)
    // lane = slot % laneCount   → horizontal column
    // tier = slot / laneCount   → vertical offset within column (0=bottom, 1=mid, 2=upper)
    private int _spawnIndex;

    public FctOverlay()
    {
        InitializeComponent();

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => EnforceTopmost();

        _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _boundsTimer.Tick += (_, _) =>
        {
            _boundsTimer.Stop();
            Vm?.SaveBounds(Left, Top, Width, Height);
        };

        DataContextChanged += OnDataContextChanged;
        Loaded += OnFirstLoaded;
    }

    private FctOverlayViewModel? Vm => DataContext as FctOverlayViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FctOverlayViewModel old)
        {
            old.SpawnRequested -= OnSpawnRequested;
            old.PropertyChanged -= OnVmPropertyChanged;
        }
        if (e.NewValue is FctOverlayViewModel vm)
        {
            vm.SpawnRequested += OnSpawnRequested;
            vm.PropertyChanged += OnVmPropertyChanged;
            Width  = vm.InitialWidth;
            Height = vm.InitialHeight;
        }
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        UpdateLockVisual();
        ApplyClickThrough();
        _topmostTimer.Start();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FctOverlayViewModel.IsLocked))
        {
            ApplyClickThrough();
            UpdateLockVisual();
        }
    }

    // Called on the UI thread via SpawnText → SpawnRequested event.
    private void OnSpawnRequested(string text, Color color, double fontSize, bool isBold)
    {
        var vm        = Vm!;
        var laneCount = Math.Max(1, vm.LaneCount);
        var distance  = Math.Max(20, vm.FloatDistance);
        var duration  = Math.Max(0.5, vm.FloatDuration);
        var fadeDur   = Math.Min(1.0, duration * 0.4);
        var fadeDelay = duration - fadeDur;

        // Lane/tier slot — round-robin through laneCount × 3 positions.
        var slots = laneCount * 3;
        var slot  = _spawnIndex % slots;
        _spawnIndex = (_spawnIndex + 1) % (slots * 1000); // prevent int overflow drift
        var lane = slot % laneCount;
        var tier = slot / laneCount;    // 0=bottom, 1=middle, 2=upper

        var cw = FctCanvas.ActualWidth  > 0 ? FctCanvas.ActualWidth  : Width;
        var ch = FctCanvas.ActualHeight > 0 ? FctCanvas.ActualHeight : Height;

        // X: centre of this lane's column ± small random jitter.
        var laneWidth = cw / laneCount;
        var laneCenter = laneWidth * lane + laneWidth / 2.0 - fontSize;
        var x = laneCenter + _rng.Next(-6, 7);

        // Y: bottom baseline shifted up by tier × (fontSize + 6).
        var tierStep = fontSize + 6;
        var startY   = ch - fontSize - 10 - tier * tierStep;
        startY = Math.Max(10, startY); // never above top edge

        var tb = new TextBlock
        {
            Text       = text,
            Foreground = new SolidColorBrush(color),
            FontSize   = fontSize,
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
            Effect     = new DropShadowEffect { Color = Colors.Black, Opacity = 0.9, BlurRadius = 3, ShadowDepth = 1 }
        };

        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        x = Math.Max(0, Math.Min(cw - tb.DesiredSize.Width, x));

        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, startY);
        FctCanvas.Children.Add(tb);

        var sb = new Storyboard();

        var moveAnim = new DoubleAnimation(startY, startY - distance, TimeSpan.FromSeconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(moveAnim, tb);
        Storyboard.SetTargetProperty(moveAnim, new PropertyPath(Canvas.TopProperty));

        var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(fadeDur))
        {
            BeginTime = TimeSpan.FromSeconds(fadeDelay)
        };
        Storyboard.SetTarget(fadeAnim, tb);
        Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(OpacityProperty));

        sb.Children.Add(moveAnim);
        sb.Children.Add(fadeAnim);
        sb.Completed += (_, _) =>
        {
            FctCanvas.Children.Remove(tb);
            tb.Effect = null;
        };
        sb.Begin();
    }

    private void UpdateLockVisual()
    {
        if (Vm is null) return;
        var unlocked = !Vm.IsLocked;
        UnlockBg.Visibility   = unlocked ? Visibility.Visible : Visibility.Collapsed;
        DragHeader.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        ResizeGrip.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        LockBtn.Text          = unlocked ? "🔓" : "🔒";
    }

    private void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if (Vm?.IsLocked == true)
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
        else
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style & ~NativeMethods.WS_EX_TRANSPARENT);
    }

    private void EnforceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Lock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null) Vm.IsLocked = !Vm.IsLocked;
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
        Hide();
    }
}
