using LiteDB;

namespace EqFlex.Infrastructure.Storage;

public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public int? LastActiveProfileId { get; set; }
    public double MainWindowLeft { get; set; }
    public double MainWindowTop { get; set; }
    public double MainWindowWidth { get; set; } = 900;
    public double MainWindowHeight { get; set; } = 600;
    public bool MainWindowMaximized { get; set; }

    // Keyed layout widths: splitter positions and DataGrid column widths.
    public Dictionary<string, double> LayoutWidths { get; set; } = [];

    // Log archiving
    public bool LogArchiveEnabled { get; set; }
    public int LogArchiveSizeMb { get; set; } = 500;

    // Overlay
    public double OverlayLeft { get; set; } = -1;
    public double OverlayTop { get; set; } = -1;
    public bool OverlayLocked { get; set; } = true;
    public bool OverlayAutoShow { get; set; }
    public int OverlayAutoHideDelay { get; set; } = 5;
    public double OverlayWidth { get; set; } = 280;
    public double OverlayHeight { get; set; } = 260;

    // Floating Combat Text — visibility
    public bool FctEnabled { get; set; }
    public bool FctShowIncoming { get; set; }
    public bool FctShowMelee { get; set; } = true;
    public bool FctShowAbility { get; set; } = true;
    public bool FctShowSpell { get; set; } = true;
    public bool FctShowDot { get; set; } = true;
    public bool FctShowHealDone { get; set; } = true;
    public bool FctShowHealReceived { get; set; } = true;
    public bool FctShowHealReceivedCaster { get; set; }
    public bool FctShowPet { get; set; } = true;
    public bool FctLocked { get; set; } = true;
    // Floating Combat Text — colors (#RRGGBB or #AARRGGBB)
    public string FctMeleeColor { get; set; } = "#FFFFFF";
    public string FctMeleeCritColor { get; set; } = "#FF8C00";
    public string FctAbilityColor { get; set; } = "#FFFF44";
    public string FctAbilityCritColor { get; set; } = "#FFCC00";
    public string FctSpellColor { get; set; } = "#4488FF";
    public string FctSpellCritColor { get; set; } = "#00CCFF";
    public string FctDotColor { get; set; } = "#9944DD";
    public string FctDotCritColor { get; set; } = "#DD44FF";
    public string FctHealDoneColor { get; set; } = "#22CC55";
    public string FctHealDoneCritColor { get; set; } = "#44FF88";
    public string FctHealReceivedColor { get; set; } = "#22DD44";
    public string FctHealReceivedCritColor { get; set; } = "#00FF7F";
    public string FctIncomingColor { get; set; } = "#CC5555";
    public string FctIncomingCritColor { get; set; } = "#FF4040";
    public string FctPetColor { get; set; } = "#FF9900";
    public string FctPetCritColor { get; set; } = "#FFCC44";
    // Floating Combat Text — position/size
    public double FctLeft { get; set; } = -1;
    public double FctTop { get; set; } = -1;
    public double FctWidth { get; set; } = 400;
    public double FctHeight { get; set; } = 300;
    // Floating Combat Text — animation settings
    public int FctBaseFontSize { get; set; } = 16;
    public double FctCritScale { get; set; } = 1.4;
    public int FctFloatDistance { get; set; } = 120;
    public double FctFloatDuration { get; set; } = 2.5;
    public int FctLaneCount { get; set; } = 5;
}

public sealed class SettingsStore
{
    private const string Col = "settings";
    private readonly LiteDbContext _ctx;

    public SettingsStore(LiteDbContext ctx) => _ctx = ctx;

    public AppSettings Load()
    {
        var col = _ctx.Db.GetCollection<AppSettings>(Col);
        return col.FindById(1) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        settings.Id = 1;
        _ctx.Db.GetCollection<AppSettings>(Col).Upsert(settings);
    }
}
