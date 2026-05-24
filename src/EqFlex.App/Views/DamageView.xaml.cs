using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqFlex.App.ViewModels;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EqFlex.App.Views;

public partial class DamageView : UserControl
{
    public DamageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void FightGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DamageViewModel vm)
            vm.OnFightSelectionChanged(FightGrid.SelectedItems.Cast<FightRow>().ToList());
    }

    // ── Layout persistence ────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Defer restore to after the first layout pass so ActualWidth values are valid.
        Dispatcher.InvokeAsync(RestoreLayout, DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => SaveLayout();

    private void RestoreLayout()
    {
        var settings = LoadSettings();
        if (settings.LayoutWidths.Count == 0) return;

        // Main left/right splitter
        if (settings.LayoutWidths.TryGetValue("splitter.left", out var lw) && lw > 50)
            LayoutGrid.ColumnDefinitions[0].Width = new GridLength(lw);

        RestoreColumns(settings, "fight",      FightGrid);
        RestoreColumns(settings, "players",    PlayersGrid);
        RestoreColumns(settings, "abilities",  AbilitiesGrid);
        RestoreColumns(settings, "tanking",    TankingGrid);
        RestoreColumns(settings, "healers",    HealersGrid);
        RestoreColumns(settings, "healspells", HealSpellsGrid);
    }

    private void SaveLayout()
    {
        var store = App.Services.GetRequiredService<SettingsStore>();
        var settings = store.Load();

        // Main splitter — use ActualWidth of the left column's content
        var leftWidth = LayoutGrid.ColumnDefinitions[0].ActualWidth;
        if (leftWidth > 0)
            settings.LayoutWidths["splitter.left"] = leftWidth;

        SaveColumns(settings, "fight",      FightGrid);
        SaveColumns(settings, "players",    PlayersGrid);
        SaveColumns(settings, "abilities",  AbilitiesGrid);
        SaveColumns(settings, "tanking",    TankingGrid);
        SaveColumns(settings, "healers",    HealersGrid);
        SaveColumns(settings, "healspells", HealSpellsGrid);

        store.Save(settings);
    }

    private static void SaveColumns(AppSettings settings, string key, DataGrid grid)
    {
        // Guard: if the grid has never been laid out its columns report MinWidth (≈20px).
        // Only persist when the grid has a real rendered width.
        if (grid.ActualWidth < 50) return;

        for (var i = 0; i < grid.Columns.Count; i++)
        {
            var w = grid.Columns[i].ActualWidth;
            if (w > 40)
                settings.LayoutWidths[$"{key}.col.{i}"] = w;
        }
    }

    private static void RestoreColumns(AppSettings settings, string key, DataGrid grid)
    {
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            if (settings.LayoutWidths.TryGetValue($"{key}.col.{i}", out var w) && w > 40)
                grid.Columns[i].Width = new DataGridLength(w);
        }
    }

    private static AppSettings LoadSettings() =>
        App.Services.GetRequiredService<SettingsStore>().Load();
}
