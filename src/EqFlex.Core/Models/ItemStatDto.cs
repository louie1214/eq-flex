namespace EqFlex.Core.Models;

public sealed class ItemStatDto
{
    public int     ItemId    { get; init; }
    public string  Name      { get; init; } = string.Empty;
    public string  RawText   { get; init; } = string.Empty;
    // Structured fields — retained for future use (alerts, filtering, search)
    public string  Slot      { get; init; } = string.Empty;
    public string  Skill     { get; init; } = string.Empty;
    public int?    AC        { get; init; }
    public int?    HP        { get; init; }
    public int?    Mana      { get; init; }
    public int?    End       { get; init; }
    public int?    STR       { get; init; }
    public int?    STA       { get; init; }
    public int?    AGI       { get; init; }
    public int?    DEX       { get; init; }
    public int?    WIS       { get; init; }
    public int?    INT       { get; init; }
    public int?    CHA       { get; init; }
    public int?    SvMagic   { get; init; }
    public int?    SvFire    { get; init; }
    public int?    SvCold    { get; init; }
    public int?    SvPoison  { get; init; }
    public int?    SvDisease { get; init; }
    public int?    DMG       { get; init; }
    public int?    DmgBonus  { get; init; }
    public int?    Delay     { get; init; }
    public double? Weight    { get; init; }
    public string  Classes   { get; init; } = string.Empty;
    public string  Races     { get; init; } = string.Empty;
    public string  LucyUrl   { get; init; } = string.Empty;

    /// <summary>Returns the cleaned shotdata block exactly as Lucy presents it.</summary>
    public string FormatTooltip() => RawText;
}
