using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EqFlex.Infrastructure.Storage;

namespace EqFlex.App.ViewModels;

public sealed partial class FctOverlayViewModel : ObservableObject
{
    private readonly SettingsStore _store;

    /// <summary>
    /// Fired on the UI thread by SpawnText(). Code-behind subscribes and animates a TextBlock
    /// on the FCT canvas. (text, color, fontSize, isBold)
    /// </summary>
    public event Action<string, Color, double, bool>? SpawnRequested;

    // ── Visibility toggles ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isLocked = true;
    [ObservableProperty] private bool _showIncoming;
    [ObservableProperty] private bool _showMelee = true;
    [ObservableProperty] private bool _showAbility = true;
    [ObservableProperty] private bool _showSpell = true;
    [ObservableProperty] private bool _showDot = true;
    [ObservableProperty] private bool _showHealDone = true;
    [ObservableProperty] private bool _showHealReceived = true;
    [ObservableProperty] private bool _showHealReceivedCaster;
    [ObservableProperty] private bool _showPet = true;

    // ── Animation settings ────────────────────────────────────────────────────
    [ObservableProperty] private int _baseFontSize = 16;
    [ObservableProperty] private double _critScale = 1.4;
    [ObservableProperty] private int _floatDistance = 120;
    [ObservableProperty] private double _floatDuration = 2.5;
    [ObservableProperty] private int _laneCount = 5;

    // ── Colors (#RRGGBB or #AARRGGBB) ────────────────────────────────────────
    [ObservableProperty] private string _meleeColor      = "#FFFFFF";
    [ObservableProperty] private string _meleeCritColor  = "#FF8C00";
    [ObservableProperty] private string _abilityColor     = "#FFFF44";
    [ObservableProperty] private string _abilityCritColor = "#FFCC00";
    [ObservableProperty] private string _spellColor      = "#4488FF";
    [ObservableProperty] private string _spellCritColor  = "#00CCFF";
    [ObservableProperty] private string _dotColor        = "#9944DD";
    [ObservableProperty] private string _dotCritColor    = "#DD44FF";
    [ObservableProperty] private string _healDoneColor         = "#22CC55";
    [ObservableProperty] private string _healDoneCritColor     = "#44FF88";
    [ObservableProperty] private string _healReceivedColor     = "#22DD44";
    [ObservableProperty] private string _healReceivedCritColor = "#00FF7F";
    [ObservableProperty] private string _incomingColor      = "#CC5555";
    [ObservableProperty] private string _incomingCritColor  = "#FF4040";
    [ObservableProperty] private string _petColor           = "#FF9900";
    [ObservableProperty] private string _petCritColor       = "#FFCC44";

    // ── Initial window bounds (read-only, set in constructor) ─────────────────
    public double InitialLeft   { get; }
    public double InitialTop    { get; }
    public double InitialWidth  { get; }
    public double InitialHeight { get; }

    public FctOverlayViewModel(SettingsStore store)
    {
        _store = store;
        var s = store.Load();

        _isEnabled     = s.FctEnabled;
        _isLocked      = s.FctLocked;
        _showIncoming  = s.FctShowIncoming;
        _showMelee     = s.FctShowMelee;
        _showAbility   = s.FctShowAbility;
        _showSpell     = s.FctShowSpell;
        _showDot       = s.FctShowDot;
        _showHealDone             = s.FctShowHealDone;
        _showHealReceived         = s.FctShowHealReceived;
        _showHealReceivedCaster   = s.FctShowHealReceivedCaster;
        _showPet          = s.FctShowPet;

        _baseFontSize  = s.FctBaseFontSize  > 0 ? s.FctBaseFontSize  : 16;
        _critScale     = s.FctCritScale     > 0 ? s.FctCritScale     : 1.4;
        _floatDistance = s.FctFloatDistance > 0 ? s.FctFloatDistance : 120;
        _floatDuration = s.FctFloatDuration > 0 ? s.FctFloatDuration : 2.5;
        _laneCount     = s.FctLaneCount     > 0 ? s.FctLaneCount     : 5;

        _meleeColor        = NonEmpty(s.FctMeleeColor,       "#FFFFFF");
        _meleeCritColor    = NonEmpty(s.FctMeleeCritColor,   "#FF8C00");
        _abilityColor      = NonEmpty(s.FctAbilityColor,     "#FFFF44");
        _abilityCritColor  = NonEmpty(s.FctAbilityCritColor, "#FFCC00");
        _spellColor        = NonEmpty(s.FctSpellColor,       "#4488FF");
        _spellCritColor    = NonEmpty(s.FctSpellCritColor,   "#00CCFF");
        _dotColor          = NonEmpty(s.FctDotColor,         "#9944DD");
        _dotCritColor      = NonEmpty(s.FctDotCritColor,     "#DD44FF");
        _healDoneColor         = NonEmpty(s.FctHealDoneColor,        "#22CC55");
        _healDoneCritColor     = NonEmpty(s.FctHealDoneCritColor,    "#44FF88");
        _healReceivedColor     = NonEmpty(s.FctHealReceivedColor,    "#22DD44");
        _healReceivedCritColor = NonEmpty(s.FctHealReceivedCritColor,"#00FF7F");
        _incomingColor     = NonEmpty(s.FctIncomingColor,    "#CC5555");
        _incomingCritColor = NonEmpty(s.FctIncomingCritColor,"#FF4040");
        _petColor          = NonEmpty(s.FctPetColor,         "#FF9900");
        _petCritColor      = NonEmpty(s.FctPetCritColor,     "#FFCC44");

        InitialLeft    = s.FctLeft;
        InitialTop     = s.FctTop;
        InitialWidth   = s.FctWidth  > 0 ? s.FctWidth  : 400;
        InitialHeight  = s.FctHeight > 0 ? s.FctHeight : 300;
    }

    private static string NonEmpty(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>Must be called on the UI thread. Raises SpawnRequested for the window code-behind.</summary>
    public void SpawnText(string text, Color color, double fontSize = 16, bool isBold = false)
        => SpawnRequested?.Invoke(text, color, fontSize, isBold);

    public void SaveBounds(double l, double t, double w, double h)
    {
        var s = _store.Load();
        s.FctLeft = l; s.FctTop = t; s.FctWidth = w; s.FctHeight = h;
        _store.Save(s);
    }

    // All observable properties call SaveSettings on change.
    partial void OnIsEnabledChanged(bool value)       => SaveSettings();
    partial void OnIsLockedChanged(bool value)        => SaveSettings();
    partial void OnShowIncomingChanged(bool value)    => SaveSettings();
    partial void OnShowMeleeChanged(bool value)       => SaveSettings();
    partial void OnShowAbilityChanged(bool value)     => SaveSettings();
    partial void OnShowSpellChanged(bool value)       => SaveSettings();
    partial void OnShowDotChanged(bool value)         => SaveSettings();
    partial void OnShowHealDoneChanged(bool value)           => SaveSettings();
    partial void OnShowHealReceivedChanged(bool value)       => SaveSettings();
    partial void OnShowHealReceivedCasterChanged(bool value) => SaveSettings();
    partial void OnShowPetChanged(bool value)         => SaveSettings();
    partial void OnBaseFontSizeChanged(int value)     => SaveSettings();
    partial void OnCritScaleChanged(double value)     => SaveSettings();
    partial void OnFloatDistanceChanged(int value)    => SaveSettings();
    partial void OnFloatDurationChanged(double value) => SaveSettings();
    partial void OnLaneCountChanged(int value)        => SaveSettings();
    partial void OnMeleeColorChanged(string value)       => SaveSettings();
    partial void OnMeleeCritColorChanged(string value)   => SaveSettings();
    partial void OnAbilityColorChanged(string value)     => SaveSettings();
    partial void OnAbilityCritColorChanged(string value) => SaveSettings();
    partial void OnSpellColorChanged(string value)       => SaveSettings();
    partial void OnSpellCritColorChanged(string value)   => SaveSettings();
    partial void OnDotColorChanged(string value)         => SaveSettings();
    partial void OnDotCritColorChanged(string value)     => SaveSettings();
    partial void OnHealDoneColorChanged(string value)         => SaveSettings();
    partial void OnHealDoneCritColorChanged(string value)     => SaveSettings();
    partial void OnHealReceivedColorChanged(string value)     => SaveSettings();
    partial void OnHealReceivedCritColorChanged(string value) => SaveSettings();
    partial void OnIncomingColorChanged(string value)    => SaveSettings();
    partial void OnIncomingCritColorChanged(string value)=> SaveSettings();
    partial void OnPetColorChanged(string value)         => SaveSettings();
    partial void OnPetCritColorChanged(string value)     => SaveSettings();

    private void SaveSettings()
    {
        var s = _store.Load();
        s.FctEnabled      = IsEnabled;
        s.FctLocked       = IsLocked;
        s.FctShowIncoming = ShowIncoming;
        s.FctShowMelee    = ShowMelee;
        s.FctShowAbility  = ShowAbility;
        s.FctShowSpell    = ShowSpell;
        s.FctShowDot      = ShowDot;
        s.FctShowHealDone           = ShowHealDone;
        s.FctShowHealReceived       = ShowHealReceived;
        s.FctShowHealReceivedCaster = ShowHealReceivedCaster;
        s.FctShowPet      = ShowPet;
        s.FctBaseFontSize  = BaseFontSize;
        s.FctCritScale     = CritScale;
        s.FctFloatDistance = FloatDistance;
        s.FctFloatDuration = FloatDuration;
        s.FctLaneCount     = LaneCount;
        s.FctMeleeColor        = MeleeColor;
        s.FctMeleeCritColor    = MeleeCritColor;
        s.FctAbilityColor      = AbilityColor;
        s.FctAbilityCritColor  = AbilityCritColor;
        s.FctSpellColor        = SpellColor;
        s.FctSpellCritColor    = SpellCritColor;
        s.FctDotColor          = DotColor;
        s.FctDotCritColor      = DotCritColor;
        s.FctHealDoneColor         = HealDoneColor;
        s.FctHealDoneCritColor     = HealDoneCritColor;
        s.FctHealReceivedColor     = HealReceivedColor;
        s.FctHealReceivedCritColor = HealReceivedCritColor;
        s.FctIncomingColor     = IncomingColor;
        s.FctIncomingCritColor = IncomingCritColor;
        s.FctPetColor          = PetColor;
        s.FctPetCritColor      = PetCritColor;
        _store.Save(s);
    }
}
