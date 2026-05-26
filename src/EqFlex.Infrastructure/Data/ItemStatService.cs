using System.Net.Http;
using System.Net;
using System.Text.RegularExpressions;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;
using LiteDB;

namespace EqFlex.Infrastructure.Data;

// ── LiteDB document ───────────────────────────────────────────────────────────

internal sealed class CachedItemStat
{
    [BsonId] public int      Id          { get; set; }
    public int               CacheVersion { get; set; }
    public bool              NotFound    { get; set; }
    public DateTime          FetchedAt   { get; set; }
    public string            Name        { get; set; } = string.Empty;
    public string            Slot        { get; set; } = string.Empty;
    public string            Skill       { get; set; } = string.Empty;
    public int?              AC          { get; set; }
    public int?              HP          { get; set; }
    public int?              Mana        { get; set; }
    public int?              End         { get; set; }
    public int?              STR         { get; set; }
    public int?              STA         { get; set; }
    public int?              AGI         { get; set; }
    public int?              DEX         { get; set; }
    public int?              WIS         { get; set; }
    public int?              INT         { get; set; }
    public int?              CHA         { get; set; }
    public int?              SvMagic     { get; set; }
    public int?              SvFire      { get; set; }
    public int?              SvCold      { get; set; }
    public int?              SvPoison    { get; set; }
    public int?              SvDisease   { get; set; }
    public int?              DMG         { get; set; }
    public int?              DmgBonus    { get; set; }
    public int?              Delay       { get; set; }
    public double?           Weight      { get; set; }
    public string            Classes     { get; set; } = string.Empty;
    public string            Races       { get; set; } = string.Empty;
    public string            LucyUrl     { get; set; } = string.Empty;
    public string            RawText     { get; set; } = string.Empty;

    public ItemStatDto ToDto() => new()
    {
        ItemId    = Id,
        Name      = Name,
        Slot      = Slot,
        Skill     = Skill,
        AC        = AC,
        HP        = HP,
        Mana      = Mana,
        End       = End,
        STR       = STR,
        STA       = STA,
        AGI       = AGI,
        DEX       = DEX,
        WIS       = WIS,
        INT       = INT,
        CHA       = CHA,
        SvMagic   = SvMagic,
        SvFire    = SvFire,
        SvCold    = SvCold,
        SvPoison  = SvPoison,
        SvDisease = SvDisease,
        DMG       = DMG,
        DmgBonus  = DmgBonus,
        Delay     = Delay,
        Weight    = Weight,
        Classes   = Classes,
        Races     = Races,
        LucyUrl   = LucyUrl,
        RawText   = RawText,
    };
}

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class ItemStatService
{
    private const string Col = "item_stats";
    // Bump this whenever the parser adds new fields — forces a re-fetch of stale cache entries.
    private const int CurrentCacheVersion = 4;
    // Only retry NotFound entries after this interval (handles transient failures)
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromHours(24);

    private readonly LiteDbContext _ctx;
    private readonly ItemNameIndex _nameIndex;
    private readonly HttpClient    _http;

    private readonly Dictionary<int, Task<ItemStatDto?>> _inflight = [];
    private readonly object _lock = new();

    public ItemStatService(LiteDbContext ctx, ItemNameIndex nameIndex, HttpClient http)
    {
        _ctx       = ctx;
        _nameIndex = nameIndex;
        _http      = http;
    }

    /// <summary>
    /// Returns stats for the given item. Checks LiteDB cache first; fetches Lucy on miss.
    /// Returns null if the item is unknown or not found on Lucy.
    /// </summary>
    public Task<ItemStatDto?> GetStatsAsync(string itemName, int? itemId = null, CancellationToken ct = default)
    {
        int id;
        if (itemId.HasValue)
            id = itemId.Value;
        else if (!_nameIndex.TryGetId(itemName, out id))
            return Task.FromResult<ItemStatDto?>(null);

        Task<ItemStatDto?> task;
        lock (_lock)
        {
            if (_inflight.TryGetValue(id, out task!)) return task;
            task = FetchAsync(id, ct);
            _inflight[id] = task;
            _ = task.ContinueWith(_ => { lock (_lock) _inflight.Remove(id); }, TaskScheduler.Default);
        }
        return task;
    }

    private async Task<ItemStatDto?> FetchAsync(int id, CancellationToken ct)
    {
        var col    = _ctx.Db.GetCollection<CachedItemStat>(Col);
        var cached = col.FindById(id);
        if (cached is not null)
        {
            // Valid hit: correct version and not a stale NotFound
            bool versionOk  = cached.CacheVersion == CurrentCacheVersion;
            bool notFoundOk = !cached.NotFound || (DateTime.UtcNow - cached.FetchedAt) < NotFoundTtl;
            if (versionOk && notFoundOk)
                return cached.NotFound ? null : cached.ToDto();
            // Fall through to re-fetch (stale version or stale NotFound)
        }

        var url  = $"https://lucy.allakhazam.com/item.html?id={id}";
        var html = await FetchHtmlAsync(url, ct);
        if (html is null) return null; // network failure — don't cache

        var stat = ParseLucy(html, id, url);
        stat.CacheVersion = CurrentCacheVersion;
        stat.FetchedAt    = DateTime.UtcNow;
        col.Upsert(stat);
        return stat.NotFound ? null : stat.ToDto();
    }

    // ── HTTP fetch with meta-refresh follow ───────────────────────────────────

    // Lucy sends an HTML meta-refresh on first visit to set a cookie.
    // HttpClient only follows HTTP 301/302 redirects, not HTML meta-refresh.
    private static readonly Regex _metaRefresh = new(
        @"<meta\s[^>]*HTTP-EQUIV\s*=\s*[""']?Refresh[""']?[^>]*CONTENT\s*=\s*[""'][^;]*;\s*URL=([^""'>\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task<string?> FetchHtmlAsync(string url, CancellationToken ct)
    {
        string html;
        try { html = await _http.GetStringAsync(url, ct); }
        catch { return null; }

        var m = _metaRefresh.Match(html);
        if (m.Success)
        {
            var path     = m.Groups[1].Value.Trim().TrimEnd('"', '\'');
            var redirect = path.StartsWith('/') ? "https://lucy.allakhazam.com" + path : path;
            try { html = await _http.GetStringAsync(redirect, ct); }
            catch { return null; }
        }

        return html;
    }

    // ── HTML parsing — scoped to the shotdata cell ────────────────────────────

    private static readonly Regex _shotdata  = new(@"class=""shotdata""[^>]*>(.*?)</td>",                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex _h1        = new(@"<h1[^>]*>([^<]+)</h1>",                             RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _titleTag  = new(@"<title[^>]*>([^<]+)</title>",                       RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _rSlot     = new(@"Slot:\s*([A-Z][A-Z/ ]+?)(?:\s*<|\s*$|\s*\n)",      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSkill    = new(@"Skill:\s*([^\n<]+?)(?:\s+Atk|\s*<|\s*$|\s*\n)",    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rAC       = new(@"\bAC:\s*(\d+)",                                     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rHP       = new(@"\bHP:\s*\+?(-?\d+)",                                RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rMana     = new(@"\bMana:\s*\+?(-?\d+)",                              RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rEnd      = new(@"\bEnd(?:urance)?:\s*\+?(-?\d+)",                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSTR      = new(@"\bSTR:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSTA      = new(@"\bSTA:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rAGI      = new(@"\bAGI:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rDEX      = new(@"\bDEX:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rWIS      = new(@"\bWIS:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rINT      = new(@"\bINT:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rCHA      = new(@"\bCHA:\s*\+?(-?\d+)",                               RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSvMagic  = new(@"\bSV\s+MAGIC:\s*\+?(-?\d+)",                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSvFire   = new(@"\bSV\s+FIRE:\s*\+?(-?\d+)",                         RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSvCold   = new(@"\bSV\s+COLD:\s*\+?(-?\d+)",                         RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSvPoison = new(@"\bSV\s+POISON:\s*\+?(-?\d+)",                       RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rSvDisease= new(@"\bSV\s+DISEASE:\s*\+?(-?\d+)",                      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rDMG      = new(@"\bDMG:\s*(\d+)",                                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rBonus    = new(@"\bDmg\s+Bonus:\s*(\d+)",                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rDelay    = new(@"\bAtk\s+Delay:\s*(\d+)",                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rWT       = new(@"\bWT:\s*([\d.]+)",                                   RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rClass    = new(@"\bClass:\s*([A-Z][A-Z/ ]+?)(?:\s*<|\s*$|\s*\n)",   RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _rRace     = new(@"\bRace:\s*([A-Z][A-Z/ ]+?)(?:\s*<|\s*$|\s*\n)",    RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static CachedItemStat ParseLucy(string html, int id, string url)
    {
        // Guard against receiving a meta-refresh page (shouldn't happen post-follow, but defensive)
        if (html.Length < 500 &&
            html.Contains("Refresh", StringComparison.OrdinalIgnoreCase) &&
            html.Contains("HTTP-EQUIV", StringComparison.OrdinalIgnoreCase))
            return new CachedItemStat { Id = id, NotFound = true };

        if (html.Contains("No item found",  StringComparison.OrdinalIgnoreCase) ||
            html.Contains("Item not found", StringComparison.OrdinalIgnoreCase))
            return new CachedItemStat { Id = id, NotFound = true };

        // Scope parsing to the shotdata cell to avoid false matches in page chrome
        var shotMatch = _shotdata.Match(html);
        var block = shotMatch.Success ? shotMatch.Groups[1].Value : html;

        // Item name — prefer <h1>, fall back to <title>
        string name = string.Empty;
        var h1m = _h1.Match(html);
        if (h1m.Success)
        {
            name = WebUtility.HtmlDecode(h1m.Groups[1].Value.Trim());
        }
        else
        {
            var tm = _titleTag.Match(html);
            if (tm.Success)
            {
                var t = WebUtility.HtmlDecode(tm.Groups[1].Value);
                name = t.Contains("Item Details for ", StringComparison.OrdinalIgnoreCase)
                    ? t.Replace("Item Details for ", "", StringComparison.OrdinalIgnoreCase).Trim()
                    : t.Split("::")[0].Split('|')[0].Trim();
            }
        }

        if (name.Length == 0) return new CachedItemStat { Id = id, NotFound = true };

        return new CachedItemStat
        {
            Id        = id,
            LucyUrl   = url,
            Name      = name,
            RawText   = CleanShotdata(block),
            Slot      = ExtractText(_rSlot,     block),
            Skill     = ExtractText(_rSkill,    block),
            AC        = ExtractInt(_rAC,        block),
            HP        = ExtractInt(_rHP,        block),
            Mana      = ExtractInt(_rMana,      block),
            End       = ExtractInt(_rEnd,       block),
            STR       = ExtractInt(_rSTR,       block),
            STA       = ExtractInt(_rSTA,       block),
            AGI       = ExtractInt(_rAGI,       block),
            DEX       = ExtractInt(_rDEX,       block),
            WIS       = ExtractInt(_rWIS,       block),
            INT       = ExtractInt(_rINT,       block),
            CHA       = ExtractInt(_rCHA,       block),
            SvMagic   = ExtractInt(_rSvMagic,   block),
            SvFire    = ExtractInt(_rSvFire,    block),
            SvCold    = ExtractInt(_rSvCold,    block),
            SvPoison  = ExtractInt(_rSvPoison,  block),
            SvDisease = ExtractInt(_rSvDisease, block),
            DMG       = ExtractInt(_rDMG,       block),
            DmgBonus  = ExtractInt(_rBonus,     block),
            Delay     = ExtractInt(_rDelay,     block),
            Weight    = ExtractDouble(_rWT,     block),
            Classes   = ExtractText(_rClass,    block),
            Races     = ExtractText(_rRace,     block),
        };
    }

    private static string CleanShotdata(string block)
    {
        var s = Regex.Replace(block, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<a\s[^>]+>([^<]*)</a>", "$1", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "", RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(s);
        var lines = s.Split('\n')
                     .Select(l => l.Trim())
                     .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private static int? ExtractInt(Regex rx, string text)
    {
        var m = rx.Match(text);
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    private static double? ExtractDouble(Regex rx, string text)
    {
        var m = rx.Match(text);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string ExtractText(Regex rx, string text)
    {
        var m = rx.Match(text);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : string.Empty;
    }
}
