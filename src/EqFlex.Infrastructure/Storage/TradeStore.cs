using EqFlex.Core.Models;

namespace EqFlex.Infrastructure.Storage;

public sealed class TradeStore
{
    private const string TradeCol = "trades";
    private const string AlertCol = "item_alerts";
    private const string HitCol   = "item_alert_hits";

    // Timestamps use local-time-since-1970-01-01 epoch (same as LogProcessor)
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static long NowTs() => (long)(DateTime.Now - UnixEpoch).TotalSeconds;
    private static long CutoffTs(int days) => (long)(DateTime.Now.AddDays(-days) - UnixEpoch).TotalSeconds;

    private readonly LiteDbContext _ctx;

    public TradeStore(LiteDbContext ctx) => _ctx = ctx;

    // ── Trades ────────────────────────────────────────────────────────────────

    public void Save(TradeRecord record)
        => _ctx.Db.GetCollection<TradeRecord>(TradeCol).Upsert(record);

    /// <summary>Returns trades for the given server from the last <paramref name="days"/> days, newest first.</summary>
    public IReadOnlyList<TradeRecord> GetRecent(string server, int days = 14)
    {
        var cutoff = CutoffTs(days);
        var col = _ctx.Db.GetCollection<TradeRecord>(TradeCol);
        col.EnsureIndex(x => x.Timestamp);
        return col.Find(r => r.Server == server && r.Timestamp >= cutoff)
                  .OrderByDescending(r => r.Timestamp)
                  .ToList();
    }

    /// <summary>Filters trades by search text and optional type.</summary>
    public IReadOnlyList<TradeRecord> Search(string server, string query, TradeType? type = null, int days = 14)
    {
        var cutoff = CutoffTs(days);
        var col = _ctx.Db.GetCollection<TradeRecord>(TradeCol);
        col.EnsureIndex(x => x.Timestamp);

        var results = col.Find(r => r.Server == server && r.Timestamp >= cutoff)
                         .AsEnumerable();

        if (type.HasValue)
            results = results.Where(r => r.Type == type.Value);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            results = results.Where(r =>
                r.Seller.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Items.Any(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return results.OrderByDescending(r => r.Timestamp).ToList();
    }

    /// <summary>Returns WTS Krono records with a PP price, oldest first, for price history.</summary>
    public IReadOnlyList<TradeRecord> GetKronoTrades(string server, int days = 14)
    {
        var cutoff = CutoffTs(days);
        var col = _ctx.Db.GetCollection<TradeRecord>(TradeCol);
        col.EnsureIndex(x => x.Timestamp);
        return col.Find(r => r.Server == server && r.Timestamp >= cutoff)
                  .Where(r => r.Type == TradeType.WTS &&
                               r.Items.Any(i => i.Unit == PriceUnit.PP &&
                                                i.Name.Contains("Krono", StringComparison.OrdinalIgnoreCase)))
                  .OrderBy(r => r.Timestamp)
                  .ToList();
    }

    /// <summary>Deletes trade records older than <paramref name="days"/> days.</summary>
    public void PurgeOld(int days = 14)
    {
        var cutoff = CutoffTs(days);
        _ctx.Db.GetCollection<TradeRecord>(TradeCol).DeleteMany(r => r.Timestamp < cutoff);
    }

    // ── Alerts ────────────────────────────────────────────────────────────────

    public IReadOnlyList<ItemAlert> GetAllAlerts()
        => _ctx.Db.GetCollection<ItemAlert>(AlertCol).FindAll().OrderBy(a => a.ItemName).ToList();

    public IReadOnlyList<ItemAlert> GetEnabledAlerts()
        => _ctx.Db.GetCollection<ItemAlert>(AlertCol).Find(a => a.IsEnabled).ToList();

    public void SaveAlert(ItemAlert alert)
        => _ctx.Db.GetCollection<ItemAlert>(AlertCol).Upsert(alert);

    public void DeleteAlert(int id)
        => _ctx.Db.GetCollection<ItemAlert>(AlertCol).Delete(id);

    // ── Alert hits ────────────────────────────────────────────────────────────

    public void SaveHit(ItemAlertHit hit)
        => _ctx.Db.GetCollection<ItemAlertHit>(HitCol).Insert(hit);

    public IReadOnlyList<ItemAlertHit> GetRecentHits(int days = 14)
    {
        var cutoff = CutoffTs(days);
        var col = _ctx.Db.GetCollection<ItemAlertHit>(HitCol);
        col.EnsureIndex(x => x.Timestamp);
        return col.Find(h => h.Timestamp >= cutoff)
                  .OrderByDescending(h => h.Timestamp)
                  .ToList();
    }

    public void PurgeOldHits(int days = 14)
    {
        var cutoff = CutoffTs(days);
        _ctx.Db.GetCollection<ItemAlertHit>(HitCol).DeleteMany(h => h.Timestamp < cutoff);
    }
}
