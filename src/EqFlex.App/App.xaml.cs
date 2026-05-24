using System.IO;
using System.Windows;
using System.Windows.Media;
using EqFlex.App.Overlays;
using EqFlex.App.Services;
using EqFlex.App.ViewModels;
using EqFlex.Core.Interfaces;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Data;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EqFlex.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    internal static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqFlex", "eqflex.db");

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton(new LiteDbContext(dbPath));
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<SettingsStore>();

        // Data
        var spellData = new SpellDataService();
        spellData.Load(dataDir);
        services.AddSingleton<ISpellDataService>(spellData);

        // Domain
        services.AddSingleton<FightManager>();
        services.AddSingleton<TriggerEngine>();

        // Trigger + overlay storage
        services.AddSingleton<TriggerStore>();
        services.AddSingleton<OverlayWindowStore>();

        // Overlay manager (multi-window trigger overlays)
        services.AddSingleton<OverlayManager>();

        // ViewModels
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<FctOverlayViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<CharacterViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<DamageViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<TriggersViewModel>();
        services.AddSingleton<OverlaysViewModel>();

        Services = services.BuildServiceProvider();

        // Load triggers into engine (respects folder enabled state)
        var triggerStore = Services.GetRequiredService<TriggerStore>();
        var triggerEngine = Services.GetRequiredService<TriggerEngine>();
        triggerEngine.LoadTriggers(triggerStore.GetEnabledForEngine());

        // Initialize OverlayManager — creates all persisted trigger overlay windows
        var overlayManager = Services.GetRequiredService<OverlayManager>();
        overlayManager.Initialize();

        // Eagerly subscribe DamageViewModel to FightManager events before any parsing runs.
        // Without this, the singleton is lazy and misses EndReplay() if the user parses before
        // navigating to the Damage page.
        Services.GetRequiredService<DamageViewModel>();

        // Create the DPS overlay window (hidden until user clicks Overlay)
        var overlayVm = Services.GetRequiredService<OverlayViewModel>();
        var overlay = new DamageMeterOverlay { DataContext = overlayVm };

        // Restore saved position, otherwise place in the top-right of the primary screen
        if (overlayVm.InitialLeft >= 0 && overlayVm.InitialTop >= 0)
        {
            overlay.Left = overlayVm.InitialLeft;
            overlay.Top = overlayVm.InitialTop;
        }
        else
        {
            overlay.Left = SystemParameters.PrimaryScreenWidth - 300;
            overlay.Top = 40;
        }

        // Create the FCT overlay window (always open, click-through when locked, transparent when empty)
        var fctVm = Services.GetRequiredService<FctOverlayViewModel>();
        var fctOverlay = new FctOverlay { DataContext = fctVm };
        if (fctVm.InitialLeft >= 0 && fctVm.InitialTop >= 0)
        {
            fctOverlay.Left = fctVm.InitialLeft;
            fctOverlay.Top  = fctVm.InitialTop;
        }
        else
        {
            fctOverlay.Left = SystemParameters.PrimaryScreenWidth  / 2 - 200;
            fctOverlay.Top  = SystemParameters.PrimaryScreenHeight / 2 - 150;
        }
        fctOverlay.Show();

        var shell = new MainWindow
        {
            DataContext = Services.GetRequiredService<ShellViewModel>()
        };

        // Restore window bounds
        var winSettings = Services.GetRequiredService<SettingsStore>().Load();
        if (winSettings.MainWindowWidth >= 400)  shell.Width  = winSettings.MainWindowWidth;
        if (winSettings.MainWindowHeight >= 300) shell.Height = winSettings.MainWindowHeight;
        if (IsOnScreen(winSettings.MainWindowLeft, winSettings.MainWindowTop))
        {
            shell.Left = winSettings.MainWindowLeft;
            shell.Top  = winSettings.MainWindowTop;
        }
        else
        {
            shell.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Save window bounds on close, then mark shutting down so overlay OnClosing doesn't cancel.
        shell.Closing += (s, _) =>
        {
            var w = (Window)s!;
            var store = Services.GetRequiredService<SettingsStore>();
            var cfg = store.Load();
            cfg.MainWindowMaximized = w.WindowState == WindowState.Maximized;
            var bounds = w.WindowState == WindowState.Normal
                ? new Rect(w.Left, w.Top, w.Width, w.Height)
                : w.RestoreBounds;
            if (!bounds.IsEmpty && bounds.Width >= 400)
            {
                cfg.MainWindowLeft   = bounds.Left;
                cfg.MainWindowTop    = bounds.Top;
                cfg.MainWindowWidth  = bounds.Width;
                cfg.MainWindowHeight = bounds.Height;
            }
            store.Save(cfg);
            IsShuttingDown = true;
        };

        if (winSettings.MainWindowMaximized)
            shell.WindowState = WindowState.Maximized;

        shell.Show();
        MainWindow = shell;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Returns true if the point lands on any connected monitor (so the window will be visible).</summary>
    private static bool IsOnScreen(double left, double top)
    {
        if (left == 0 && top == 0) return false; // treat default/unset as "centre instead"
        var vsLeft   = SystemParameters.VirtualScreenLeft;
        var vsTop    = SystemParameters.VirtualScreenTop;
        var vsRight  = vsLeft + SystemParameters.VirtualScreenWidth;
        var vsBottom = vsTop  + SystemParameters.VirtualScreenHeight;
        return left >= vsLeft && left < vsRight && top >= vsTop && top < vsBottom;
    }
}
