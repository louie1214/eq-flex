using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;
using EqFlex.Core.Models;
using EqFlex.Core.Parsing;
using EqFlex.Infrastructure.Data;
using EqFlex.Infrastructure.Storage;
using Microsoft.Win32;

// Alias to avoid ambiguity with System.Windows.Controls.Button, etc.
using SoundFile = EqFlex.App.Services.SoundFile;
using SoundLibrary = EqFlex.App.Services.SoundLibrary;

namespace EqFlex.App.ViewModels;

public sealed class TradeRowVm
{
    public long Timestamp { get; init; }
    public string TimeDisplay { get; init; } = string.Empty;
    public string TypeDisplay { get; init; } = string.Empty;
    public string Seller { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string Price { get; init; } = string.Empty;
    public TradeRecord Source { get; init; } = null!;
}

public sealed class KronoRowVm
{
    public string TimeDisplay { get; init; } = string.Empty;
    public string TypeDisplay { get; init; } = string.Empty;
    public string Seller      { get; init; } = string.Empty;
    public string ItemName    { get; init; } = string.Empty;
    public string Price       { get; init; } = string.Empty;
    public string RawLine     { get; init; } = string.Empty;
}

public sealed class SaleRowVm
{
    public string TimeDisplay { get; init; } = string.Empty;
    public string TypeDisplay { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string Seller { get; init; } = string.Empty;
    public string Price { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    /// <summary>Magelo item ID — present for API results, null for local log data.</summary>
    public int? ItemId { get; init; }
}

public sealed class AlertHitRowVm
{
    public string TimeDisplay { get; init; } = string.Empty;
    public string ItemName    { get; init; } = string.Empty;
    public string Seller      { get; init; } = string.Empty;
    public string Price       { get; init; } = string.Empty;
    public string RawLine     { get; init; } = string.Empty;
}

public sealed partial class TunnelViewModel : ObservableObject
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly TradeStore       _store;
    private readonly AraduneApiClient _api;
    private readonly ItemStatService  _itemStats;
    private readonly OverlayManager   _overlayManager;
    private readonly SoundLibrary     _soundLibrary;
    private readonly SettingsStore    _settings;
    private readonly List<TradeRowVm> _allTrades  = [];
    private readonly List<TradeRowVm> _feedBuffer = [];
    private readonly Dictionary<string, ItemStatDto?> _statCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _feedPaused;
    private bool _scrollPaused;
    private bool _selectionPaused;
    private bool   _loaded;
    private string _loadedServer = string.Empty;
    private CancellationTokenSource? _enrichCts;

    // ── Trades tab ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _hasSearchText;
    [ObservableProperty] private string _selectedTypeFilter = "All";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int  _bufferedTradeCount;
    [ObservableProperty] private bool _hasBufferedTrades;

    // ── Item stat filters ────────────────────────────────────────────────────
    [ObservableProperty] private string _filterSlot  = string.Empty;
    [ObservableProperty] private string _filterClass = string.Empty;
    [ObservableProperty] private string _filterRace  = string.Empty;
    [ObservableProperty] private bool   _hasItemFilter;

    partial void OnFilterSlotChanged(string value)  { SyncHasItemFilter(); ApplyFilter(); StartEnrichment(); }
    partial void OnFilterClassChanged(string value) { SyncHasItemFilter(); ApplyFilter(); StartEnrichment(); }
    partial void OnFilterRaceChanged(string value)  { SyncHasItemFilter(); ApplyFilter(); StartEnrichment(); }

    private void SyncHasItemFilter() =>
        HasItemFilter = FilterSlot.Length > 0 || FilterClass.Length > 0 || FilterRace.Length > 0;

    [RelayCommand]
    private void ClearItemFilter()
    {
        FilterSlot = FilterClass = FilterRace = string.Empty;
    }

    private void StartEnrichment()
    {
        _enrichCts?.Cancel();
        if (!HasItemFilter) return;
        _enrichCts = new CancellationTokenSource();
        var names = _allTrades
            .Select(r => r.ItemName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !_statCache.ContainsKey(n))
            .ToList();
        if (names.Count > 0)
            _ = EnrichAsync(names, _enrichCts.Token);
    }

    private async Task EnrichAsync(List<string> names, CancellationToken ct)
    {
        try
        {
            int i = 0;
            foreach (var name in names)
            {
                if (ct.IsCancellationRequested) return;

                var stat = _itemStats.TryGetCachedStats(name)
                           ?? await _itemStats.GetStatsAsync(name, ct: ct);

                if (ct.IsCancellationRequested) return;

                i++;
                int captured = i;
                int total    = names.Count;
                // Don't pass ct to InvokeAsync — it throws OperationCanceledException
                // when the token fires mid-queue, causing unobserved task exceptions.
                // Instead check ct inside the callback so cancelled runs are no-ops.
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    _statCache[name] = stat;
                    if (captured % 20 == 0 || captured == total) PruneByStatFilter();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Removes items that no longer match from FilteredTrades and _feedBuffer without
    // clearing and rebuilding the entire list (preserves scroll position / selection).
    private void PruneByStatFilter()
    {
        for (int i = FilteredTrades.Count - 1; i >= 0; i--)
            if (!MatchesFilter(FilteredTrades[i])) FilteredTrades.RemoveAt(i);

        bool bufferChanged = false;
        for (int i = _feedBuffer.Count - 1; i >= 0; i--)
        {
            if (!MatchesFilter(_feedBuffer[i]))
            {
                _feedBuffer.RemoveAt(i);
                bufferChanged = true;
            }
        }
        if (bufferChanged) BufferedTradeCount = _feedBuffer.Count;
        TotalCount = FilteredTrades.Count;
    }

    partial void OnBufferedTradeCountChanged(int value) => HasBufferedTrades = value > 0;
    public ObservableCollection<TradeRowVm> FilteredTrades { get; } = [];
    public string[] TypeFilters { get; } = ["All", "WTS", "WTB"];
    public string[] SlotOptions  { get; } = [
        "", "AMMO", "ARMS", "BACK", "CHARM", "CHEST", "EAR", "FACE",
        "FEET", "FINGER", "HANDS", "HEAD", "LEGS", "NECK",
        "POWER SOURCE", "PRIMARY", "RANGE", "SECONDARY", "SHOULDER", "WAIST", "WRIST",
    ];
    public string[] ClassOptions { get; } = [
        "", "BER", "BRD", "BST", "CLR", "DRU", "ENC",
        "MAG", "MNK", "NEC", "PAL", "RNG", "ROG", "SHD", "SHM", "WAR", "WIZ",
    ];
    public string[] RaceOptions  { get; } = [
        "", "BAR", "DEF", "DRK", "DWF", "ELF", "ERU", "FRG", "GNM",
        "HEF", "HFL", "HIE", "HUM", "IKS", "OGR", "TRL", "VAH",
    ];

    // ── Trade detail pane ─────────────────────────────────────────────────────
    [ObservableProperty] private TradeRowVm? _selectedTradeRow;
    [ObservableProperty] private bool        _hasSelectedTrade;
    [ObservableProperty] private string      _detailStatsText     = string.Empty;
    [ObservableProperty] private bool        _isLoadingDetailStats;
    private int _detailLoadVersion;
    private int _detailPriceVersion;
    private CancellationTokenSource? _detailCts;

    // ── Detail pane price history ─────────────────────────────────────────────
    public ObservableCollection<SaleRowVm> DetailPriceHistory { get; } = [];
    [ObservableProperty] private bool   _detailPriceLoaded;
    [ObservableProperty] private bool   _isLoadingDetailPrices;
    [ObservableProperty] private string _detailPriceStatus    = string.Empty;
    [ObservableProperty] private string _detailPriceSummary   = string.Empty;
    [ObservableProperty] private bool   _hasDetailPriceSummary;

    partial void OnSelectedTradeRowChanged(TradeRowVm? value)
    {
        HasSelectedTrade      = value is not null;
        DetailStatsText       = string.Empty;
        IsLoadingDetailStats  = false;
        DetailPriceHistory.Clear();
        DetailPriceLoaded     = false;
        IsLoadingDetailPrices = false;
        DetailPriceStatus     = string.Empty;
        DetailPriceSummary    = string.Empty;
        HasDetailPriceSummary = false;

        _detailCts?.Cancel();
        if (value is null) return;

        _detailCts = new CancellationTokenSource();
        _ = LoadDetailStatsAsync(value, ++_detailLoadVersion);
    }

    [RelayCommand]
    private void ShowDetailPriceHistory()
    {
        if (SelectedTradeRow is null || DetailPriceLoaded) return;
        DetailPriceLoaded = true;
        _detailCts ??= new CancellationTokenSource();
        _ = LoadDetailPriceHistoryAsync(SelectedTradeRow.ItemName, ++_detailPriceVersion, _detailCts.Token);
    }

    [RelayCommand]
    private void ClearSelection() => SelectedTradeRow = null;

    private async Task LoadDetailStatsAsync(TradeRowVm row, int version)
    {
        IsLoadingDetailStats = true;
        var allStats = await _itemStats.GetAllStatsAsync(row.ItemName);
        if (version != _detailLoadVersion) return;
        IsLoadingDetailStats = false;

        if (allStats.Count == 0)
        {
            DetailStatsText = "(no stats found)";
        }
        else if (allStats.Count == 1)
        {
            DetailStatsText = allStats[0].FormatTooltip();
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < allStats.Count; i++)
            {
                if (i > 0) sb.Append("\n\n");
                sb.Append($"── Version {i + 1} of {allStats.Count} (ID {allStats[i].ItemId}) ──\n");
                sb.Append(allStats[i].FormatTooltip());
            }
            DetailStatsText = sb.ToString();
        }
    }

    private async Task LoadDetailPriceHistoryAsync(string itemName, int version, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemName) || _loadedServer.Length == 0) return;

        IsLoadingDetailPrices = true;

        try
        {
            // Phase 1: local store (fast — show immediately)
            var local = await Task.Run(() => _store.Search(_loadedServer, itemName, null, 30), ct);
            if (version != _detailPriceVersion || ct.IsCancellationRequested) return;

            foreach (var r in local)
            {
                var dt = UnixEpoch.AddSeconds(r.Timestamp);
                if (r.Items.Count > 0)
                {
                    var matches = r.Items
                        .Where(i => i.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var item in (matches.Count > 0 ? matches : r.Items))
                    {
                        if (!item.Price.HasValue) continue;
                        DetailPriceHistory.Add(new SaleRowVm
                        {
                            TimeDisplay = dt.ToString("MM/dd HH:mm"),
                            TypeDisplay = r.Type == TradeType.Unknown ? "—" : r.Type.ToString(),
                            Seller      = r.Seller,
                            Price       = item.PriceDisplay,
                            Source      = "Local",
                        });
                    }
                }
            }

            DetailPriceStatus     = DetailPriceHistory.Count > 0
                ? $"{DetailPriceHistory.Count} local · fetching API…"
                : "Fetching from API…";
            IsLoadingDetailPrices = false;

            // Phase 2: API (slower — append when ready)
            var summaryTask = _api.GetItemPriceAsync(itemName, _loadedServer, ct);
            var salesTask   = _api.GetRecentSalesAsync(itemName, _loadedServer, null, 30, ct);
            await Task.WhenAll(summaryTask, salesTask);
            if (version != _detailPriceVersion || ct.IsCancellationRequested) return;

            var summary  = summaryTask.Result;
            var (sales, _) = salesTask.Result;

            if (summary is not null)
            {
                var parts = new List<string>();
                if (summary.AveragePlatPrice.HasValue)  parts.Add($"Avg {summary.AveragePlatPrice.Value:N0} pp");
                if (summary.AverageKronoPrice.HasValue) parts.Add($"Avg {summary.AverageKronoPrice.Value:N1} kr");
                if (parts.Count > 0)
                {
                    DetailPriceSummary    = string.Join("  ·  ", parts) + $"  ({summary.SampleSize:N0} sales)";
                    HasDetailPriceSummary = true;
                }
            }

            int apiAdded = 0;
            foreach (var s in sales)
            {
                if (!s.PlatPrice.HasValue && !s.KronoPrice.HasValue) continue;
                DetailPriceHistory.Add(new SaleRowVm
                {
                    TimeDisplay = s.Datetime.ToString("MM/dd HH:mm"),
                    TypeDisplay = s.TransactionType ? "WTB" : "WTS",
                    Seller      = s.Auctioneer,
                    Price       = FormatApiPrice(s),
                    Source      = "API",
                    ItemId      = s.ItemId,
                });
                apiAdded++;
            }

            DetailPriceStatus = DetailPriceHistory.Count > 0
                ? $"{DetailPriceHistory.Count} result(s)" +
                  (local.Count > 0 && apiAdded > 0 ? " (local + API)" : local.Count > 0 ? " local" : " from API")
                : "No results found.";
        }
        catch (OperationCanceledException) { IsLoadingDetailPrices = false; }
    }

    // ── Krono tab ──────────────────────────────────────────────────────────────
    [ObservableProperty] private double _kronoRatePp;
    [ObservableProperty] private string _kronoApiPrice = "—";
    [ObservableProperty] private string _kronoApiInfo = string.Empty;
    [ObservableProperty] private bool _hasKronoApiInfo;
    [ObservableProperty] private bool _isLoadingKrono;
    [ObservableProperty] private string _kronoError = string.Empty;
    [ObservableProperty] private bool _hasKronoError;
    public ObservableCollection<KronoRowVm> LocalKronoHistory { get; } = [];

    // ── Prices tab ─────────────────────────────────────────────────────────────

    // ── Alerts tab ─────────────────────────────────────────────────────────────
    public ObservableCollection<ItemAlert>    Alerts    { get; } = [];
    public ObservableCollection<AlertHitRowVm> AlertHits { get; } = [];
    public string[] AlertTypeOptions { get; } = ["WTS", "WTB", "Any"];
    public IReadOnlyCollection<TriggerOverlayViewModel> AvailableOverlays => _overlayManager.Overlays;
    public IReadOnlyList<SoundFile> AvailableSounds => _soundLibrary.Sounds;

    // ── Shared alert display settings (apply to all alerts) ───────────────────
    [ObservableProperty] private bool   _showAlertSettings;
    [ObservableProperty] private string _alertTextColor       = "#FFD4D4D4";
    [ObservableProperty] private double _alertFontSize        = 13;
    [ObservableProperty] private bool   _alertIsBold;
    [ObservableProperty] private string _alertStrokeColor     = string.Empty;
    [ObservableProperty] private double _alertStrokeThickness;
    [ObservableProperty] private string _alertSoundPath       = string.Empty;
    [ObservableProperty] private int    _alertOverlayId;

    partial void OnAlertTextColorChanged(string value)       => SaveAlertDisplaySettings();
    partial void OnAlertFontSizeChanged(double value)        => SaveAlertDisplaySettings();
    partial void OnAlertIsBoldChanged(bool value)            => SaveAlertDisplaySettings();
    partial void OnAlertStrokeColorChanged(string value)     => SaveAlertDisplaySettings();
    partial void OnAlertStrokeThicknessChanged(double value) => SaveAlertDisplaySettings();
    partial void OnAlertSoundPathChanged(string value)       => SaveAlertDisplaySettings();
    partial void OnAlertOverlayIdChanged(int value)          => SaveAlertDisplaySettings();

    private void SaveAlertDisplaySettings()
    {
        var s = _settings.Load();
        s.AlertTextColor       = AlertTextColor;
        s.AlertFontSize        = AlertFontSize;
        s.AlertIsBold          = AlertIsBold;
        s.AlertStrokeColor     = AlertStrokeColor;
        s.AlertStrokeThickness = AlertStrokeThickness;
        s.AlertSoundPath       = AlertSoundPath;
        s.AlertOverlayId       = AlertOverlayId;
        _settings.Save(s);
    }

    // ── Per-alert edit form ────────────────────────────────────────────────────
    [ObservableProperty] private ItemAlert? _selectedAlert;
    [ObservableProperty] private bool       _hasSelectedAlert;
    [ObservableProperty] private string     _editItemName  = string.Empty;
    [ObservableProperty] private string     _editAlertType = "WTS";
    [ObservableProperty] private string     _editMaxPrice  = string.Empty;
    [ObservableProperty] private bool       _editIsEnabled = true;

    [RelayCommand]
    private void ToggleAlertSettings() => ShowAlertSettings = !ShowAlertSettings;

    partial void OnSelectedAlertChanged(ItemAlert? value)
    {
        HasSelectedAlert = value is not null;
        if (value is null) return;
        EditItemName  = value.ItemName;
        EditAlertType = value.AlertType == TradeType.Unknown ? "Any" : value.AlertType.ToString();
        var priceParts = new List<string>(2);
        if (value.MaxPriceKrono.HasValue) priceParts.Add($"{value.MaxPriceKrono:0.##}kr");
        if (value.MaxPricePp.HasValue)    priceParts.Add($"{value.MaxPricePp:0.##}");
        EditMaxPrice  = string.Join(" ", priceParts);
        EditIsEnabled = value.IsEnabled;
    }

    partial void OnKronoRatePpChanged(double value)
    {
        var s = _settings.Load();
        s.KronoPpRate = value;
        _settings.Save(s);
    }

    private int RetentionDays()
    {
        var days = _settings.Load().TradeRetentionDays;
        return days > 0 ? days : 14;
    }

    public TunnelViewModel(TradeStore store, AraduneApiClient api, ItemStatService itemStats,
        OverlayManager overlayManager, SoundLibrary soundLibrary, SettingsStore settings)
    {
        _store          = store;
        _api            = api;
        _itemStats      = itemStats;
        _overlayManager = overlayManager;
        _soundLibrary   = soundLibrary;
        _settings       = settings;
        var cfg = settings.Load();
        _kronoRatePp    = cfg.KronoPpRate;
        _alertTextColor       = cfg.AlertTextColor.Length > 0 ? cfg.AlertTextColor : "#FFD4D4D4";
        _alertFontSize        = cfg.AlertFontSize  > 0 ? cfg.AlertFontSize  : 13;
        _alertIsBold          = cfg.AlertIsBold;
        _alertStrokeColor     = cfg.AlertStrokeColor;
        _alertStrokeThickness = cfg.AlertStrokeThickness;
        _alertSoundPath       = cfg.AlertSoundPath;
        _alertOverlayId       = cfg.AlertOverlayId;
    }

    /// <summary>Fetches item stats for a tooltip; returns null if unknown or not found on Lucy.</summary>
    public Task<ItemStatDto?> GetItemStatsAsync(string itemName, int? itemId = null)
        => _itemStats.GetStatsAsync(itemName, itemId);

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public void EnsureLoaded(string server)
    {
        if (_loaded && _loadedServer == server) return;
        _loadedServer = server;
        _loaded = true;

        var days = RetentionDays();
        _store.PurgeOld(days);
        _store.PurgeOldHits(days);

        _allTrades.Clear();
        foreach (var r in _store.GetRecent(server, days))
            _allTrades.AddRange(ToRows(r));
        ApplyFilter();

        LoadLocalKronoHistory();
        LoadAlerts();
        _ = RefreshKronoAsync();
    }

    // ── Trades ─────────────────────────────────────────────────────────────────

    // Called from TunnelView.xaml.cs — two independent pause reasons that are OR'd together.
    internal void SetScrollPaused(bool paused)
    {
        _scrollPaused = paused;
        SyncPauseState();
    }

    internal void SetSelectionPaused(bool paused)
    {
        _selectionPaused = paused;
        SyncPauseState();
    }

    private void SyncPauseState()
    {
        var shouldPause = _scrollPaused || _selectionPaused;
        if (_feedPaused == shouldPause) return;
        _feedPaused = shouldPause;
        if (!shouldPause) FlushBuffer();
    }

    internal void FlushBuffer()
    {
        if (_feedBuffer.Count == 0) return;
        // Buffer is oldest-first (same reverse-insert order as live path); flush newest-first.
        for (int i = _feedBuffer.Count - 1; i >= 0; i--)
            FilteredTrades.Insert(0, _feedBuffer[i]);
        _feedBuffer.Clear();
        BufferedTradeCount = 0;
        TotalCount = FilteredTrades.Count;
    }

    public void AddLiveTrade(TradeRecord record)
    {
        var rows = ToRows(record).ToList();
        // Always track in _allTrades so filter rebuilds are correct.
        for (int i = rows.Count - 1; i >= 0; i--)
            _allTrades.Insert(0, rows[i]);

        if (_feedPaused)
        {
            // Buffer filtered rows; flush into FilteredTrades when user returns to top.
            for (int i = rows.Count - 1; i >= 0; i--)
                if (MatchesFilter(rows[i]))
                    _feedBuffer.Add(rows[i]);
            BufferedTradeCount = _feedBuffer.Count;
        }
        else
        {
            for (int i = rows.Count - 1; i >= 0; i--)
                if (MatchesFilter(rows[i]))
                    FilteredTrades.Insert(0, rows[i]);
            TotalCount = FilteredTrades.Count;
        }

        if (record.Type == TradeType.WTS)
        {
            var dt = UnixEpoch.AddSeconds(record.Timestamp);
            foreach (var item in record.Items.Where(i => i.Unit == PriceUnit.PP &&
                         i.Name.Contains("Krono", StringComparison.OrdinalIgnoreCase)))
            {
                LocalKronoHistory.Insert(0, new KronoRowVm
                {
                    TimeDisplay = dt.ToString("MM/dd HH:mm:ss"),
                    TypeDisplay = "WTS",
                    Seller      = record.Seller,
                    ItemName    = item.Name,
                    Price       = item.PriceDisplay,
                    RawLine     = record.RawLine
                });
            }
        }

        // Enrich any new item names when a stat filter is active.
        if (HasItemFilter)
        {
            var uncached = rows.Select(r => r.ItemName)
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .Where(n => !_statCache.ContainsKey(n))
                               .ToList();
            if (uncached.Count > 0)
                _ = EnrichAsync(uncached, CancellationToken.None);
        }

        CheckAlerts(record);
    }

    partial void OnSearchTextChanged(string value) { HasSearchText = value.Length > 0; ApplyFilter(); }
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void ApplyFilter()
    {
        // Buffer is discarded — _allTrades already contains those rows, so they'll appear here.
        _feedBuffer.Clear();
        BufferedTradeCount = 0;
        FilteredTrades.Clear();
        foreach (var row in _allTrades)
        {
            if (MatchesFilter(row))
                FilteredTrades.Add(row);
        }
        TotalCount = FilteredTrades.Count;
    }

    private bool MatchesFilter(TradeRowVm row)
    {
        if (SelectedTypeFilter != "All" && row.TypeDisplay != SelectedTypeFilter) return false;
        var q = SearchText.Trim();
        if (q.Length > 0 && !row.Seller.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                             !row.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase))
            return false;

        if (HasItemFilter)
        {
            if (!_statCache.TryGetValue(row.ItemName, out var stat))
                return true; // not yet enriched — include optimistically until stats arrive
            if (stat is null)
                return false; // confirmed not on Lucy (unparsed line or unknown item) — exclude
            if (FilterSlot.Length  > 0 && !stat.Slot.Contains(FilterSlot, StringComparison.OrdinalIgnoreCase)) return false;
            if (FilterClass.Length > 0 && !stat.Classes.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
                                          !stat.Classes.Contains(FilterClass, StringComparison.OrdinalIgnoreCase)) return false;
            if (FilterRace.Length  > 0 && !stat.Races.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
                                          !stat.Races.Contains(FilterRace, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static IEnumerable<TradeRowVm> ToRows(TradeRecord r)
    {
        var dt = UnixEpoch.AddSeconds(r.Timestamp);
        var timeDisplay = dt.ToString("MM/dd HH:mm:ss");

        if (r.Items.Count > 0)
        {
            foreach (var item in r.Items)
            {
                yield return new TradeRowVm
                {
                    Timestamp   = r.Timestamp,
                    TimeDisplay = timeDisplay,
                    TypeDisplay = item.Type == TradeType.Unknown ? "—" : item.Type.ToString(),
                    Seller      = r.Seller,
                    ItemName    = item.Name,
                    Price       = item.Price.HasValue ? item.PriceDisplay : string.Empty,
                    Source      = r
                };
            }
        }
        else
        {
            yield return new TradeRowVm
            {
                Timestamp   = r.Timestamp,
                TimeDisplay = timeDisplay,
                TypeDisplay = r.Type == TradeType.Unknown ? "—" : r.Type.ToString(),
                Seller      = r.Seller,
                ItemName    = r.RawLine,
                Price       = string.Empty,
                Source      = r
            };
        }
    }

    // ── Krono ──────────────────────────────────────────────────────────────────

    private void LoadLocalKronoHistory()
    {
        LocalKronoHistory.Clear();
        foreach (var r in _store.GetKronoTrades(_loadedServer, 14))
        {
            var dt = UnixEpoch.AddSeconds(r.Timestamp);
            foreach (var item in r.Items.Where(i => i.Unit == PriceUnit.PP &&
                         i.Name.Contains("Krono", StringComparison.OrdinalIgnoreCase)))
            {
                LocalKronoHistory.Add(new KronoRowVm
                {
                    TimeDisplay = dt.ToString("MM/dd HH:mm:ss"),
                    TypeDisplay = r.Type == TradeType.Unknown ? "—" : r.Type.ToString(),
                    Seller      = r.Seller,
                    ItemName    = item.Name,
                    Price       = item.PriceDisplay,
                    RawLine     = r.RawLine
                });
            }
        }
    }

    [RelayCommand]
    private async Task RefreshKronoAsync()
    {
        if (_loadedServer.Length == 0) return;
        IsLoadingKrono = true;
        KronoError     = string.Empty;
        HasKronoError  = false;

        var result = await _api.GetKronoPriceAsync(_loadedServer);
        if (result is not null)
        {
            KronoApiPrice   = $"{result.AveragePrice:N0} pp";
            KronoApiInfo    = $"{result.SampleSize:N0} sales · updated {result.LastUpdated:MM/dd HH:mm}";
            HasKronoApiInfo = true;
            KronoRatePp     = result.AveragePrice;   // persisted via OnKronoRatePpChanged
        }
        else
        {
            KronoApiInfo    = string.Empty;
            HasKronoApiInfo = false;
            KronoError      = "Unable to reach araduneauctions.net";
            HasKronoError   = true;
        }

        IsLoadingKrono = false;
    }

    // ── Alerts ─────────────────────────────────────────────────────────────────

    private void LoadAlerts()
    {
        Alerts.Clear();
        foreach (var a in _store.GetAllAlerts())
            Alerts.Add(a);

        AlertHits.Clear();
        var dt0 = UnixEpoch;
        foreach (var h in _store.GetRecentHits(RetentionDays()))
        {
            AlertHits.Add(new AlertHitRowVm
            {
                TimeDisplay = dt0.AddSeconds(h.Timestamp).ToString("MM/dd HH:mm:ss"),
                ItemName    = h.ItemName,
                Seller      = h.Seller,
                Price       = h.Price,
                RawLine     = h.RawLine
            });
        }
    }

    private void CheckAlerts(TradeRecord record)
    {
        var enabled = _store.GetEnabledAlerts();
        if (enabled.Count == 0) return;

        foreach (var alert in enabled)
        {
            foreach (var item in record.Items)
            {
                if (string.IsNullOrEmpty(alert.ItemName)) continue;
                if (!item.Name.Contains(alert.ItemName, StringComparison.OrdinalIgnoreCase)) continue;
                if (alert.AlertType != TradeType.Unknown && item.Type != alert.AlertType) continue;
                var hasCap = alert.MaxPricePp.HasValue || alert.MaxPriceKrono.HasValue;
                if (hasCap)
                {
                    if (KronoRatePp > 0)
                    {
                        // Normalise everything to total PP equivalent for comparison
                        var itemPp = (item.PricePp ?? 0) + (item.PriceKrono ?? 0) * KronoRatePp;
                        var maxPp  = (alert.MaxPricePp ?? 0) + (alert.MaxPriceKrono ?? 0) * KronoRatePp;
                        if (itemPp > maxPp) continue;
                    }
                    else
                    {
                        // No rate known — check each component independently
                        if (alert.MaxPriceKrono.HasValue) { var kr = item.PriceKrono; if (kr.HasValue && kr.Value > alert.MaxPriceKrono.Value) continue; }
                        if (alert.MaxPricePp.HasValue)    { var pp = item.PricePp;    if (pp.HasValue && pp.Value > alert.MaxPricePp.Value) continue; }
                    }
                }

                FireAlert(alert, record, item);
                break;
            }
        }
    }

    private void FireAlert(ItemAlert alert, TradeRecord record, TradeItem item)
    {
        var text = $"{item.Name}  ·  {record.Seller}  ·  {item.PriceDisplay}";
        _overlayManager.ShowAlertText(text, AlertOverlayId, color: AlertTextColor,
            fontSize: AlertFontSize, isBold: AlertIsBold,
            strokeColor: AlertStrokeColor, strokeThickness: AlertStrokeThickness);
        if (!string.IsNullOrEmpty(AlertSoundPath))
            _overlayManager.PlayAudio(AlertSoundPath);

        var hit = new ItemAlertHit
        {
            AlertId   = alert.Id,
            Timestamp = record.Timestamp,
            ItemName  = item.Name,
            Seller    = record.Seller,
            Price     = item.PriceDisplay,
            RawLine   = record.RawLine
        };
        _store.SaveHit(hit);

        var dt = UnixEpoch.AddSeconds(record.Timestamp);
        AlertHits.Insert(0, new AlertHitRowVm
        {
            TimeDisplay = dt.ToString("MM/dd HH:mm:ss"),
            ItemName    = item.Name,
            Seller      = record.Seller,
            Price       = item.PriceDisplay,
            RawLine     = record.RawLine
        });
    }

    [RelayCommand]
    private void AddAlert()
    {
        var alert = new ItemAlert { IsEnabled = true, AlertType = TradeType.WTS };
        _store.SaveAlert(alert);
        var newId = alert.Id;
        LoadAlerts();
        SelectedAlert = Alerts.FirstOrDefault(a => a.Id == newId);
    }

    [RelayCommand]
    private void SaveAlertEnabled(ItemAlert? alert)
    {
        if (alert is null) return;
        _store.SaveAlert(alert);
        if (SelectedAlert == alert) EditIsEnabled = alert.IsEnabled;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedAlert))]
    private void DeleteAlert()
    {
        if (SelectedAlert is null) return;
        _store.DeleteAlert(SelectedAlert.Id);
        Alerts.Remove(SelectedAlert);
        SelectedAlert = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedAlert))]
    private void SaveAlert()
    {
        if (SelectedAlert is null) return;
        SelectedAlert.ItemName   = EditItemName?.Trim() ?? string.Empty;
        SelectedAlert.AlertType  = EditAlertType == "WTS" ? TradeType.WTS
                                 : EditAlertType == "WTB" ? TradeType.WTB
                                 : TradeType.Unknown;
        (SelectedAlert.MaxPricePp, SelectedAlert.MaxPriceKrono) =
            TradeChatParser.ParseMaxPrice(EditMaxPrice);
        SelectedAlert.IsEnabled  = EditIsEnabled;
        var savedId = SelectedAlert.Id;
        _store.SaveAlert(SelectedAlert);
        LoadAlerts();
        SelectedAlert = Alerts.FirstOrDefault(a => a.Id == savedId);
    }

    [RelayCommand]
    private void ImportSound()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import audio file",
            Filter = "Audio files|*.wav;*.mp3;*.ogg;*.flac|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var imported = _soundLibrary.Import(dlg.FileName);
        OnPropertyChanged(nameof(AvailableSounds));
        if (imported is not null)
        {
            AlertSoundPath = imported.FileName;
            _overlayManager.PlayAudio(imported.FileName);
        }
    }

    [RelayCommand]
    private void PreviewAudio()
    {
        if (!string.IsNullOrWhiteSpace(AlertSoundPath))
            _overlayManager.PlayAudio(AlertSoundPath);
    }

    partial void OnHasSelectedAlertChanged(bool value)
    {
        DeleteAlertCommand.NotifyCanExecuteChanged();
        SaveAlertCommand.NotifyCanExecuteChanged();
    }

    private static string FormatApiPrice(SalesLogDto s)
    {
        if (s.PlatPrice.HasValue && s.KronoPrice.HasValue)
            return $"{s.PlatPrice.Value:N0} pp + {s.KronoPrice.Value:N0} kr";
        if (s.PlatPrice.HasValue)  return $"{s.PlatPrice.Value:N0} pp";
        if (s.KronoPrice.HasValue) return $"{s.KronoPrice.Value:N0} kr";
        return "—";
    }
}
