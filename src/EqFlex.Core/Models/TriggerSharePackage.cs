namespace EqFlex.Core.Models;

/// <summary>
/// Portable trigger share format — no local IDs, overlay referenced by name.
/// Serialized as JSON and stored in the EQ Flex share service (Cloudflare KV).
/// </summary>
public sealed class TriggerSharePackage
{
    public string Version     { get; set; } = "1";
    public string Author      { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Top-level folders in this share. Nested children preserve the full hierarchy.</summary>
    public List<ShareFolder> Folders { get; set; } = [];
}

public sealed class ShareFolder
{
    public string Name       { get; set; } = string.Empty;
    public bool IsEnabled    { get; set; } = true;
    public List<ShareFolder> Children { get; set; } = [];
    public List<ShareTrigger> Triggers { get; set; } = [];
}

public sealed class ShareTrigger
{
    public string Name       { get; set; } = string.Empty;
    public bool IsEnabled    { get; set; } = true;
    public double CooldownSec { get; set; }
    public List<SharePhrase>  Phrases { get; set; } = [];
    public List<ShareAction>  Actions { get; set; } = [];
}

public sealed class SharePhrase
{
    public string Pattern  { get; set; } = string.Empty;
    public bool   UseRegex { get; set; }
}

public sealed class ShareAction
{
    public string ActionType     { get; set; } = string.Empty;
    public string Text           { get; set; } = string.Empty;
    public double DurationSec    { get; set; }
    public string TextColor      { get; set; } = string.Empty;
    public double FontSize       { get; set; }
    public bool   IsBold         { get; set; }
    public string TimerBarColor  { get; set; } = string.Empty;
    public string AudioPath      { get; set; } = string.Empty;
    public bool   SpeakInterrupt { get; set; }
    /// <summary>Overlay window name — resolved to a local ID on import (0 if not found).</summary>
    public string OverlayName    { get; set; } = string.Empty;
}
