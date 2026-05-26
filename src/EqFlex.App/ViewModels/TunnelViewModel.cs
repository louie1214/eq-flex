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
    private readonly List<TradeRowVm> _allTrades = [];
    private bool   _loaded;
    private string _loadedServer = string.Empty;
    private CancellationTokenSource? _priceCts;

    // ── Trades tab ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _hasSearchText;
    [ObservableProperty] private string _selectedTypeFilter = "All";
    [ObservableProperty] private int _totalCount;
    public ObservableCollection<TradeRowVm> FilteredTrades { get; } = [];
    public string[] TypeFilters { get; } = ["All", "WTS", "WTB"];

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
    [ObservableProperty] private string _priceSearchText = string.Empty;
    [ObservableProperty] private bool _isSearchingPrices;
    [ObservableProperty] private bool _includeApiResults;
    [ObservableProperty] private string _priceStatusText = string.Empty;
    [ObservableProperty] private string _apiPriceSummary = string.Empty;
    [ObservableProperty] private bool _hasApiPriceSummary;
    public ObservableCollection<SaleRowVm> PriceResults { get; } = [];

    // ── Alerts tab ─────────────────────────────────────────────────────────────
    public ObservableCollection<ItemAlert>    Alerts    { get; } = [];
    public ObservableCollection<AlertHitRowVm> AlertHits { get; } = [];
    public string[] AlertTypeOptions { get; } = ["WTS", "WTB", "Any"];
    public IReadOnlyCollection<TriggerOverlayViewModel> AvailableOverlays => _overlayManager.Overlays;
    public IReadOnlyList<SoundFile> AvailableSounds => _soundLibrary.Sounds;

    // ── Shared alert display settings (apply to all alerts) ───────────────────
    [ObservableProperty] private bool   _showAlertSettings;
    [ObservableProperty] private string _alertTextColor = "#FFD4D4D4";
    [ObservableProperty] private double _alertFontSize  = 13;
    [ObservableProperty] private bool   _alertIsBold;
    [ObservableProperty] private string _alertSoundPath = string.Empty;
    [ObservableProperty] private int    _alertOverlayId;

    partial void OnAlertTextColorChanged(string value)  => SaveAlertDisplaySettings();
    partial void OnAlertFontSizeChanged(double value)   => SaveAlertDisplaySettings();
    partial void OnAlertIsBoldChanged(bool value)       => SaveAlertDisplaySettings();
    partial void OnAlertSoundPathChanged(string value)  => SaveAlertDisplaySettings();
    partial void OnAlertOverlayIdChanged(int value)     => SaveAlertDisplaySettings();

    private void SaveAlertDisplaySettings()
    {
        var s = _settings.Load();
        s.AlertTextColor = AlertTextColor;
        s.AlertFontSize  = AlertFontSize;
        s.AlertIsBold    = AlertIsBold;
        s.AlertSoundPath = AlertSoundPath;
        s.AlertOverlayId = AlertOverlayId;
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
        _alertTextColor = cfg.AlertTextColor.Length > 0 ? cfg.AlertTextColor : "#FFD4D4D4";
        _alertFontSize  = cfg.AlertFontSize  > 0 ? cfg.AlertFontSize  : 13;
        _alertIsBold    = cfg.AlertIsBold;
        _alertSoundPath = cfg.AlertSoundPath;
        _alertOverlayId = cfg.AlertOverlayId;
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

        _store.PurgeOld(14);
        _store.PurgeOldHits(14);

        _allTrades.Clear();
        foreach (var r in _store.GetRecent(server, 14))
            _allTrades.AddRange(ToRows(r));
        ApplyFilter();

        LoadLocalKronoHistory();
        LoadAlerts();
        _ = RefreshKronoAsync();
    }

    // ── Trades ─────────────────────────────────────────────────────────────────

    public void AddLiveTrade(TradeRecord record)
    {
        var rows = ToRows(record).ToList();
        // Insert in reverse so the first item ends up at index 0 (newest first)
        for (int i = rows.Count - 1; i >= 0; i--)
            _allTrades.Insert(0, rows[i]);
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (MatchesFilter(rows[i]))
                FilteredTrades.Insert(0, rows[i]);
        }
        TotalCount = FilteredTrades.Count;

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

        CheckAlerts(record);
    }

    partial void OnSearchTextChanged(string value) { HasSearchText = value.Length > 0; ApplyFilter(); }
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void ApplyFilter()
    {
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
        if (q.Length == 0) return true;
        return row.Seller.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               row.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase);
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
        foreach (var h in _store.GetRecentHits(14))
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
            fontSize: AlertFontSize, isBold: AlertIsBold);
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
        Alerts.Add(alert);
        SelectedAlert = alert;
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
        _store.SaveAlert(SelectedAlert);

        // Replace (not Remove+Insert) to refresh the grid row without crashing DataGridCellsPanel
        var idx = Alerts.IndexOf(SelectedAlert);
        if (idx >= 0) Alerts[idx] = SelectedAlert;
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

    // ── Prices ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchPricesAsync()
    {
        var term = PriceSearchText.Trim();
        if (term.Length == 0) return;

        _priceCts?.Cancel();
        _priceCts = new CancellationTokenSource();
        var ct = _priceCts.Token;

        IsSearchingPrices  = true;
        HasApiPriceSummary = false;
        ApiPriceSummary    = string.Empty;
        PriceResults.Clear();
        PriceStatusText    = "Searching local data...";

        var local = _store.Search(_loadedServer, term, null, 14);

        if (ct.IsCancellationRequested) { IsSearchingPrices = false; return; }

        if (local.Count > 0)
        {
            foreach (var r in local)
            {
                var dt = UnixEpoch.AddSeconds(r.Timestamp);
                if (r.Items.Count > 0)
                {
                    var matches = r.Items
                        .Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var items = matches.Count > 0 ? matches : r.Items;
                    foreach (var item in items)
                    {
                        PriceResults.Add(new SaleRowVm
                        {
                            TimeDisplay = dt.ToString("MM/dd HH:mm:ss"),
                            TypeDisplay = r.Type == TradeType.Unknown ? "—" : r.Type.ToString(),
                            ItemName    = item.Name,
                            Seller      = r.Seller,
                            Price       = item.Price.HasValue ? item.PriceDisplay : "—",
                            Source      = "Local"
                        });
                    }
                }
                else
                {
                    PriceResults.Add(new SaleRowVm
                    {
                        TimeDisplay = dt.ToString("MM/dd HH:mm:ss"),
                        TypeDisplay = r.Type == TradeType.Unknown ? "—" : r.Type.ToString(),
                        ItemName    = string.Empty,
                        Seller      = r.Seller,
                        Price       = "—",
                        Source      = "Local"
                    });
                }
            }

            if (!IncludeApiResults)
            {
                PriceStatusText   = $"{PriceResults.Count} local result(s)";
                IsSearchingPrices = false;
                return;
            }
        }

        // Fetch API (always when no local data, or when IncludeApiResults is checked)
        PriceStatusText = local.Count > 0
            ? $"{PriceResults.Count} local result(s) — fetching from API..."
            : "No local data — fetching from API...";

        var summaryTask = _api.GetItemPriceAsync(term, _loadedServer, ct);
        var salesTask   = _api.GetRecentSalesAsync(term, _loadedServer, null, 50, ct);
        await Task.WhenAll(summaryTask, salesTask);

        if (ct.IsCancellationRequested) { IsSearchingPrices = false; return; }

        var summary = summaryTask.Result;
        var (sales, _) = salesTask.Result;

        if (summary is not null)
        {
            var parts = new List<string>();
            if (summary.AveragePlatPrice.HasValue)
                parts.Add($"Avg {summary.AveragePlatPrice.Value:N0} pp");
            if (summary.AverageKronoPrice.HasValue)
                parts.Add($"Avg {summary.AverageKronoPrice.Value:N1} kr");
            if (parts.Count > 0)
            {
                ApiPriceSummary    = string.Join("  ·  ", parts) + $"  ({summary.SampleSize:N0} sales)";
                HasApiPriceSummary = true;
            }
        }

        foreach (var s in sales)
        {
            PriceResults.Add(new SaleRowVm
            {
                TimeDisplay = s.Datetime.ToString("MM/dd HH:mm:ss"),
                TypeDisplay = s.TransactionType ? "WTB" : "WTS",
                ItemName    = s.Item,
                Seller      = s.Auctioneer,
                Price       = FormatApiPrice(s),
                Source      = "API",
                ItemId      = s.ItemId
            });
        }

        PriceStatusText   = PriceResults.Count > 0
            ? $"{PriceResults.Count} result(s){(local.Count > 0 ? " (local + API)" : " from API")}"
            : "No results found.";
        IsSearchingPrices = false;
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
