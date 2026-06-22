using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqFlex.App.ViewModels;
using EqFlex.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EqFlex.App.Views;

public partial class TunnelView : UserControl
{
    private ScrollViewer? _tradesScrollViewer;
    private double        _detailPaneWidth = 300;

    public TunnelView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Layout persistence + scroll-lock setup ────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            RestoreLayout();
            _tradesScrollViewer = FindScrollViewer(TradesGrid);
            if (_tradesScrollViewer is not null)
                _tradesScrollViewer.ScrollChanged += OnTradesScrollChanged;
            if (DataContext is TunnelViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        }, DispatcherPriority.Loaded);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SaveLayout();
        if (_tradesScrollViewer is not null)
            _tradesScrollViewer.ScrollChanged -= OnTradesScrollChanged;
        if (DataContext is TunnelViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    // Only react to user-initiated scrolls — ignore events fired because content was added/removed
    // (ExtentHeightChange != 0). Without this guard, inserting items at index 0 while the user
    // is at the top triggers a momentary non-zero offset and accidentally pauses the feed.
    private void OnTradesScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0) return;
        if (DataContext is not TunnelViewModel vm) return;
        vm.SetScrollPaused(_tradesScrollViewer!.VerticalOffset >= 1.0);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TunnelViewModel.HasSelectedTrade)) return;
        var show = (DataContext as TunnelViewModel)?.HasSelectedTrade == true;

        if (show)
        {
            TradesDetailPane.Visibility = Visibility.Visible;
            TradesSplitter.Visibility   = Visibility.Visible;
            TradesContentGrid.ColumnDefinitions[1].Width    = new GridLength(4);
            TradesContentGrid.ColumnDefinitions[2].MinWidth = 180;
            TradesContentGrid.ColumnDefinitions[2].Width    = new GridLength(_detailPaneWidth);
        }
        else
        {
            var w = TradesContentGrid.ColumnDefinitions[2].ActualWidth;
            if (w > 0) _detailPaneWidth = w;
            TradesContentGrid.ColumnDefinitions[2].MinWidth = 0;
            TradesDetailPane.Visibility = Visibility.Collapsed;
            TradesSplitter.Visibility   = Visibility.Collapsed;
            TradesContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            TradesContentGrid.ColumnDefinitions[2].Width = new GridLength(0);
        }

        (DataContext as TunnelViewModel)?.SetSelectionPaused(show);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject o)
    {
        if (o is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(o, i));
            if (result is not null) return result;
        }
        return null;
    }

    private void RestoreLayout()
    {
        var settings = App.Services.GetRequiredService<SettingsStore>().Load();
        if (settings.LayoutWidths.Count == 0) return;

        RestoreColumns(settings, "tunnel.trades",     TradesGrid);
        RestoreColumns(settings, "tunnel.krono",      KronoGrid);
        RestoreColumns(settings, "tunnel.alerts",     AlertsGrid);
        RestoreColumns(settings, "tunnel.alert.hits", AlertHitsGrid);
        RestoreColumns(settings, "tunnel.prices",     PricesGrid);
    }

    private void SaveLayout()
    {
        var store    = App.Services.GetRequiredService<SettingsStore>();
        var settings = store.Load();

        SaveColumns(settings, "tunnel.trades",     TradesGrid);
        SaveColumns(settings, "tunnel.krono",      KronoGrid);
        SaveColumns(settings, "tunnel.alerts",     AlertsGrid);
        SaveColumns(settings, "tunnel.alert.hits", AlertHitsGrid);
        SaveColumns(settings, "tunnel.prices",     PricesGrid);

        store.Save(settings);
    }

    private static void SaveColumns(AppSettings settings, string key, DataGrid grid)
    {
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

    // Flush buffered trades directly — bypasses the scroll/pause logic so it works even when the
    // detail pane is open (which would re-pause immediately if we only called ScrollToTop).
    private void OnNewBadgeClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is TunnelViewModel vm)
            vm.FlushBuffer();
        _tradesScrollViewer?.ScrollToTop();
    }

    // ── Item tooltip — Trades tab ──────────────────────────────────────────────

    private void OnTradeItemTooltipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not TextBlock tb || DataContext is not TunnelViewModel vm) return;
        var row = tb.DataContext as TradeRowVm;
        if (row is null || string.IsNullOrWhiteSpace(row.ItemName)) return;
        LoadTooltip(tb, row.ItemName, null, vm);
    }

    // ── Item tooltip — Prices tab ──────────────────────────────────────────────

    private void OnPriceItemTooltipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not TextBlock tb || DataContext is not TunnelViewModel vm) return;
        var row = tb.DataContext as SaleRowVm;
        if (row is null || string.IsNullOrWhiteSpace(row.ItemName)) return;
        LoadTooltip(tb, row.ItemName, row.ItemId, vm);
    }

    // ── Shared async tooltip loader ────────────────────────────────────────────

    private static void LoadTooltip(TextBlock tb, string itemName, int? itemId, TunnelViewModel vm)
    {
        if (tb.ToolTip is not ToolTip tooltip) return;
        if (tooltip.Content is not TextBlock content) return;

        // Already loaded for this item
        if (tooltip.Tag is string loaded && loaded == itemName) return;

        content.Text = "Loading…";
        tooltip.Tag  = itemName;

        _ = LoadAsync(content, itemName, itemId, vm);
    }

    private static async Task LoadAsync(TextBlock content, string itemName, int? itemId, TunnelViewModel vm)
    {
        var stats = await vm.GetItemStatsAsync(itemName, itemId);

        // Marshal back to UI thread
        content.Dispatcher.Invoke(() =>
        {
            if (stats is null)
                content.Text = string.IsNullOrEmpty(itemName) ? string.Empty : "(no stats found)";
            else
                content.Text = stats.FormatTooltip();
        });
    }
}
