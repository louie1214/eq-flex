using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EqFlex.App.Services;

// ── Response DTOs ─────────────────────────────────────────────────────────────

public sealed class KronoPriceDto
{
    [JsonPropertyName("serverName")]  public string   ServerName   { get; init; } = string.Empty;
    [JsonPropertyName("averagePrice")] public double  AveragePrice { get; init; }
    [JsonPropertyName("sampleSize")]  public int      SampleSize   { get; init; }
    [JsonPropertyName("lastUpdated")] public DateTime LastUpdated  { get; init; }
}

public sealed class PriceCheckDto
{
    [JsonPropertyName("serverName")]       public string   ServerName        { get; init; } = string.Empty;
    [JsonPropertyName("item")]             public string   Item              { get; init; } = string.Empty;
    [JsonPropertyName("averagePlatPrice")] public double?  AveragePlatPrice  { get; init; }
    [JsonPropertyName("averageKronoPrice")]public double?  AverageKronoPrice { get; init; }
    [JsonPropertyName("sampleSize")]       public int      SampleSize        { get; init; }
    [JsonPropertyName("lastUpdated")]      public DateTime LastUpdated       { get; init; }
}

public sealed class SalesLogDto
{
    [JsonPropertyName("id")]              public int      Id              { get; init; }
    [JsonPropertyName("itemId")]          public int?     ItemId          { get; init; }
    [JsonPropertyName("item")]            public string   Item            { get; init; } = string.Empty;
    [JsonPropertyName("auctioneer")]      public string   Auctioneer      { get; init; } = string.Empty;
    /// <summary>true = WTB (buyer), false = WTS (seller)</summary>
    [JsonPropertyName("transactionType")] public bool     TransactionType { get; init; }
    [JsonPropertyName("platPrice")]       public double?  PlatPrice       { get; init; }
    [JsonPropertyName("kronoPrice")]      public double?  KronoPrice      { get; init; }
    [JsonPropertyName("datetime")]        public DateTime Datetime        { get; init; }
}

public sealed class PagedSalesResult
{
    [JsonPropertyName("items")]      public List<SalesLogDto> Items      { get; init; } = [];
    [JsonPropertyName("totalCount")] public int               TotalCount { get; init; }
}

// ── Client ────────────────────────────────────────────────────────────────────

public sealed class AraduneApiClient
{
    private const string Base = "https://www.araduneauctions.net/api";
    private readonly HttpClient _http;

    public AraduneApiClient(HttpClient http) => _http = http;

    /// <summary>Returns the current average Krono price for the given server, or null on failure.</summary>
    public async Task<KronoPriceDto?> GetKronoPriceAsync(string server, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<KronoPriceDto>(
                $"{Base}/krono-prices/{Uri.EscapeDataString(server)}", ct);
        }
        catch { return null; }
    }

    /// <summary>Returns average price summary for an item, or null on failure.</summary>
    public async Task<PriceCheckDto?> GetItemPriceAsync(string searchTerm, string server, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<PriceCheckDto>(
                $"{Base}/prices?searchTerm={Uri.EscapeDataString(searchTerm)}&serverName={Uri.EscapeDataString(server)}", ct);
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns up to <paramref name="pageSize"/> recent sales for an item.
    /// <paramref name="isBuy"/>: null = all, true = WTB only, false = WTS only.
    /// Returns empty on failure.
    /// </summary>
    public async Task<(IReadOnlyList<SalesLogDto> Items, int Total)> GetRecentSalesAsync(
        string searchTerm, string server, bool? isBuy = null,
        int pageSize = 100, CancellationToken ct = default)
    {
        try
        {
            var isBuyParam = isBuy.HasValue ? $"&isBuy={isBuy.Value.ToString().ToLowerInvariant()}" : string.Empty;
            var url = $"{Base}/sales?searchTerm={Uri.EscapeDataString(searchTerm)}" +
                      $"&serverName={Uri.EscapeDataString(server)}&pageSize={pageSize}{isBuyParam}";
            var result = await _http.GetFromJsonAsync<PagedSalesResult>(url, ct);
            return result is not null ? (result.Items, result.TotalCount) : ([], 0);
        }
        catch { return ([], 0); }
    }
}
