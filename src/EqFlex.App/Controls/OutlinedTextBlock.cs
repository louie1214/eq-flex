using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EqFlex.App.Controls;

/// <summary>
/// A text element that renders an optional colored outline (stroke) around each glyph.
/// When StrokeColor is empty or StrokeThickness is 0, renders identically to a plain TextBlock.
/// </summary>
public sealed class OutlinedTextBlock : FrameworkElement
{
    private static readonly FontFamily DefaultFont = new("Segoe UI");

    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeColorProperty =
        DependencyProperty.Register(nameof(StrokeColor), typeof(string), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(13.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(FontWeights.Normal,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(TextWrapping.Wrap,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(TextAlignment.Left,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text           { get => (string)GetValue(TextProperty);          set => SetValue(TextProperty, value); }
    public Brush  Fill           { get => (Brush)GetValue(FillProperty);           set => SetValue(FillProperty, value); }
    public string StrokeColor    { get => (string)GetValue(StrokeColorProperty);   set => SetValue(StrokeColorProperty, value); }
    public double StrokeThickness{ get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double FontSize       { get => (double)GetValue(FontSizeProperty);      set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = BuildFormattedText(double.IsInfinity(availableSize.Width) ? 10000 : availableSize.Width);
        return new Size(ft.Width, ft.Height);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var ft  = BuildFormattedText(ActualWidth > 0 ? ActualWidth : 10000);
        var geo = ft.BuildGeometry(new Point(0, 0));

        // Draw stroke first (double thickness; the pen is centered on the path edge,
        // so half falls inside the fill — drawing fill on top hides the inner half).
        if (!string.IsNullOrWhiteSpace(StrokeColor) && StrokeThickness > 0)
        {
            try
            {
                var color  = (Color)ColorConverter.ConvertFromString(StrokeColor);
                var brush  = new SolidColorBrush(color);
                brush.Freeze();
                var pen = new Pen(brush, StrokeThickness * 2) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
            catch { /* invalid color string — skip stroke */ }
        }

        dc.DrawGeometry(Fill, null, geo);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FormattedText BuildFormattedText(double maxWidth)
    {
        double dpi;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { dpi = 1.0; }

        var ft = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(DefaultFont, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize,
            Fill,
            dpi);

        ft.MaxTextWidth  = Math.Max(1, maxWidth);
        ft.TextAlignment = TextAlignment;
        if (TextWrapping == TextWrapping.NoWrap)
            ft.MaxLineCount = 1;

        return ft;
    }
}
