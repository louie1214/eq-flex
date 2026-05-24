using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EqFlex.App.Controls;

public partial class ColorWheelPicker : UserControl
{
    public static readonly DependencyProperty SelectedHexProperty =
        DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorWheelPicker),
            new FrameworkPropertyMetadata("#FFD4D4D4",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedHexChanged));

    public string SelectedHex
    {
        get => (string)GetValue(SelectedHexProperty);
        set => SetValue(SelectedHexProperty, value);
    }

    private static void OnSelectedHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorWheelPicker p && e.NewValue is string hex)
            p.ApplyHexExternal(hex);
    }

    private const int D = 168;
    private double _hue, _sat, _val = 1.0;
    private bool _isDragging, _suppressCallback;

    public ColorWheelPicker()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-sync slider to current val (handles case where Value_ValueChanged
        // fired before IsLoaded was true and was skipped).
        _suppressCallback = true;
        ValueSlider.Value = _val;
        _suppressCallback = false;
        RenderWheel();
        UpdateDotPosition();
    }

    // ── Wheel rendering ──────────────────────────────────────────────────────

    private void RenderWheel()
    {
        if (WheelImage is null) return;

        var bmp = new WriteableBitmap(D, D, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[D * D * 4];
        double cx = D / 2.0, cy = D / 2.0, radius = D / 2.0 - 1;

        for (int y = 0; y < D; y++)
        for (int x = 0; x < D; x++)
        {
            double dx = x - cx, dy = y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            int i = (y * D + x) * 4;

            if (r > radius + 0.5) { pixels[i + 3] = 0; continue; }

            double hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;
            double sat = Math.Min(r / radius, 1.0);
            var (R, G, B) = HsvToRgb(hue, sat, _val);
            pixels[i] = B; pixels[i + 1] = G; pixels[i + 2] = R; pixels[i + 3] = 255;
        }

        bmp.WritePixels(new Int32Rect(0, 0, D, D), pixels, D * 4, 0);
        WheelImage.Source = bmp;
    }

    // ── Mouse input ──────────────────────────────────────────────────────────

    private void Wheel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        ((IInputElement)sender).CaptureMouse();
        PickFromPoint(e.GetPosition(WheelImage));
        e.Handled = true;
    }

    private void Wheel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        PickFromPoint(e.GetPosition(WheelImage));
    }

    private void Wheel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((IInputElement)sender).ReleaseMouseCapture();
    }

    private void PickFromPoint(Point pos)
    {
        double cx = D / 2.0, cy = D / 2.0, radius = D / 2.0 - 1;
        double dx = pos.X - cx, dy = pos.Y - cy;
        double r = Math.Sqrt(dx * dx + dy * dy);
        _hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;
        _sat = Math.Min(r / radius, 1.0);
        UpdateDotPosition();
        PushSelectedHex();
    }

    private void Value_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressCallback || !IsLoaded) return;
        _val = e.NewValue;
        RenderWheel();
        UpdateDotPosition();
        PushSelectedHex();
    }

    // ── Selection dot ────────────────────────────────────────────────────────

    private void UpdateDotPosition()
    {
        if (SelectionDot is null || SelectionDotShadow is null) return;

        double cx = D / 2.0, cy = D / 2.0, radius = D / 2.0 - 1;
        double angle = _hue * Math.PI / 180.0;
        double dotX = cx + Math.Cos(angle) * _sat * radius - 5;
        double dotY = cy + Math.Sin(angle) * _sat * radius - 5;
        Canvas.SetLeft(SelectionDot, dotX);
        Canvas.SetTop(SelectionDot, dotY);
        Canvas.SetLeft(SelectionDotShadow, dotX);
        Canvas.SetTop(SelectionDotShadow, dotY);

        SelectionDot.Stroke = _sat < 0.15 && _val > 0.8
            ? new SolidColorBrush(Color.FromRgb(50, 50, 50))
            : Brushes.White;
    }

    // ── Hex sync ─────────────────────────────────────────────────────────────

    private void PushSelectedHex()
    {
        var (R, G, B) = HsvToRgb(_hue, _sat, _val);
        _suppressCallback = true;
        SelectedHex = $"#FF{R:X2}{G:X2}{B:X2}";
        _suppressCallback = false;
    }

    private void ApplyHexExternal(string hex)
    {
        if (_suppressCallback) return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            (_hue, _sat, _val) = RgbToHsv(color.R, color.G, color.B);
        }
        catch { return; }

        // If not yet loaded, state is updated but rendering is deferred to OnLoaded.
        if (!IsLoaded) return;

        _suppressCallback = true;
        ValueSlider.Value = _val;
        _suppressCallback = false;
        RenderWheel();
        UpdateDotPosition();
    }

    // ── Color math ───────────────────────────────────────────────────────────

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        if (s == 0) { var g = (byte)(v * 255); return (g, g, g); }
        double hh = h / 60.0;
        int sector = (int)hh % 6;
        double f = hh - Math.Floor(hh);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (double r, double gr, double b) = sector switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q)
        };
        return ((byte)(r * 255), (byte)(gr * 255), (byte)(b * 255));
    }

    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
        double delta = max - min;
        double V = max, S = max == 0 ? 0 : delta / max;
        double H = 0;
        if (delta > 0)
        {
            if (max == R) H = 60 * (((G - B) / delta) % 6);
            else if (max == G) H = 60 * ((B - R) / delta + 2);
            else H = 60 * ((R - G) / delta + 4);
            if (H < 0) H += 360;
        }
        return (H, S, V);
    }
}
