namespace EqFlex.Core.Models;

public sealed class TriggerFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Folder";
    public bool IsEnabled { get; set; } = true;
    /// <summary>0 = root level. Any other value = parent folder Id.</summary>
    public int ParentFolderId { get; set; }
}

public enum TriggerActionType { DisplayText, Speak, PlayAudio, Timer, ShowFct }

public sealed class CapturePhrase
{
    public string Pattern { get; set; } = string.Empty;
    public bool UseRegex { get; set; }
}

public sealed class TriggerAction
{
    public TriggerActionType ActionType { get; set; }

    // DisplayText / Speak / Timer — the text shown or spoken ({S0}, {GroupName}, {C} substituted)
    public string Text { get; set; } = string.Empty;

    // How long the effect lasts: display text visibility duration OR timer countdown length.
    // Displayed in the "Duration" column for all action types.
    public double DurationSec { get; set; } = 5;

    // DisplayText color (#RRGGBB or #AARRGGBB)
    public string TextColor { get; set; } = "#FFD4D4D4";
    public double FontSize { get; set; } = 13;
    public bool IsBold { get; set; }

    // Timer — progress bar fill colour. Empty string = overlay default (#007ACC).
    public string TimerBarColor { get; set; } = string.Empty;

    // PlayAudio
    public string AudioPath { get; set; } = string.Empty;

    // Speak
    public bool SpeakInterrupt { get; set; }

    /// <summary>
    /// Id of the OverlayWindow that receives DisplayText/Timer actions.
    /// 0 = route to whichever overlay is first/default.
    /// </summary>
    public int OverlayId { get; set; }
}

public sealed class Trigger
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int FolderId { get; set; }
    public List<CapturePhrase> Phrases { get; set; } = [];
    public double CooldownSec { get; set; }
    public List<TriggerAction> Actions { get; set; } = [];
}
