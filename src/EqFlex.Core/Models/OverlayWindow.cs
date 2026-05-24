namespace EqFlex.Core.Models;

public enum OverlayTextAlign { Left, Center, Right }

public sealed class OverlayWindow
{
    public int Id { get; set; }
    public string Name { get; set; } = "Overlay";
    public double Left { get; set; } = -1;   // -1 = unset, place at default
    public double Top { get; set; } = -1;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 300;
    /// <summary>Opacity of the dark background panel only. Text/timers remain fully opaque.</summary>
    public double BackgroundOpacity { get; set; } = 0.85;
    /// <summary>Show the border outline and header bar. False = content only, no chrome.</summary>
    public bool ShowChrome { get; set; } = false;
    public bool IsEnabled { get; set; } = true;
    public OverlayTextAlign TextAlign { get; set; } = OverlayTextAlign.Left;
    public int TimerRowHeight { get; set; } = 22;
}
