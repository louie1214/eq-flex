using System.Text.RegularExpressions;
using EqFlex.Core.Models;

namespace EqFlex.Core.Parsing;

public sealed class TradeChatParser
{
    // "PlayerName auctions, 'MESSAGE'"
    private static readonly Regex AuctionBroadcastRegex = new(
        @"^(\w+) auctions, '(.+)'$",
        RegexOptions.Compiled);

    // "PlayerName tells Auction[N]:[N], 'MESSAGE'"
    private static readonly Regex AuctionTellRegex = new(
        @"^(\w+) tells Auction\d*:\d+, '(.+)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Splits message at WTS/WTB keyword boundaries
    private static readonly Regex KeywordRegex = new(
        @"\b(WTS|WTB|WTSell|WTBuy|Selling|Buying)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Unit-suffixed prices — Krono matched before 'k' to prevent false matches.
    // Matches: 1kr, 2 KR, 1krono, 1kronos, 12k, 10.5k, 500pp, 5kpp, 6000p, 10,000pp
    // Comma-formatted alternative (e.g. 10,000pp) precedes the plain-digit alternative so it
    // consumes the full token and prevents the bare-number regex from matching just "10".
    private static readonly Regex PriceRegex = new(
        @"\b(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s*(kr(?:ono)?s?|kpp?|pp?|k)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Bare number prices (no unit suffix) — assumed platinum.
    // Matches before: comma/semicolon, end-of-string, the next item's first word, noise keywords,
    // or "@" location marker ("5000 @ EC").
    // Negative lookbehind excludes +5/−5 stat modifiers and x20-style quantity markers.
    // Negative lookahead excludes "N x Item" quantity notation (e.g. "3 x Krono").
    // Comma-formatted thousands (e.g. 10,000) listed first to prevent partial match on "10".
    private static readonly Regex BareNumberPriceRegex = new(
        @"(?<![+\-x])\b(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)()(?!\s+x\b)(?=\s*(?:,|;|$|(?:obo|pst|or\s+best|or\s+bo|tell\s+me)\b)|\s+@|\s+[A-Za-z]|\s+[-/|]\s)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "x20", "x 7", "x2" — quantity markers to strip from item names (suffix form)
    private static readonly Regex QuantityRegex = new(
        @"\s*\bx\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "5 x", "5x" — prefix quantity markers to strip from the start of cleaned names
    private static readonly Regex LeadingQuantityRegex = new(
        @"^\d+\s*x\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Leading noise words that appear after a price (e.g. "each, Item2" → "Item2")
    private static readonly Regex LeadingJunkRegex = new(
        @"^(?:each|ea)\b[\s,;]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Splits a no-price segment into individual items.
    // Comma/slash/plus with optional surrounding spaces, OR hyphen with mandatory spaces
    // (preserves intra-name hyphens like "Frost-Covered Tome").
    private static readonly Regex ItemSeparatorRegex = new(
        @"\s*[,/+|]\s*|\s+-\s+",
        RegexOptions.Compiled);

    // Trailing noise phrases to strip from item names
    private static readonly Regex TrailingJunkRegex = new(
        @"\s+(?:pst|obo|or\s+best\s+offer?|or\s+bo|tell\s+me|send\s+tells?|each|ea|wtb|wts)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Chars that delimit items but are not part of item names
    private static readonly char[] _nameTrimChars = [',', ' ', ':', ';', '-', '/', '+', '|'];

    private readonly string _server;

    public TradeChatParser(string server) => _server = server;

    /// <summary>
    /// Parses a user-entered max-price string into its platinum and Krono components.
    /// </summary>
    public static (double? Pp, double? Krono) ParseMaxPrice(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, null);
        double? pp = null, krono = null;
        foreach (var m in MergePriceMatches(input.Trim()))
        {
            var val  = ParsePriceValue(m);
            var unit = ParsePriceUnit(m);
            if (unit == PriceUnit.Krono) krono ??= val;
            else                         pp    ??= val;
        }
        return (pp, krono);
    }

    public TradeRecord? TryParse(string action, long timestamp)
    {
        string seller, message;

        var m = AuctionBroadcastRegex.Match(action);
        if (m.Success)
        {
            seller  = m.Groups[1].Value;
            message = m.Groups[2].Value;
        }
        else
        {
            m = AuctionTellRegex.Match(action);
            if (!m.Success) return null;
            seller  = m.Groups[1].Value;
            message = m.Groups[2].Value;
        }

        var keywords = KeywordRegex.Matches(message);
        if (keywords.Count == 0) return null;

        var items       = new List<TradeItem>();
        var primaryType = ClassifyKeyword(keywords[0].Value);

        for (int i = 0; i < keywords.Count; i++)
        {
            var kw           = keywords[i];
            var type         = ClassifyKeyword(kw.Value);
            int contentStart = kw.Index + kw.Length;
            int contentEnd   = i + 1 < keywords.Count ? keywords[i + 1].Index : message.Length;
            var segment      = message[contentStart..contentEnd].Trim().TrimStart(':', ' ');
            ExtractItems(segment, type, items);
        }

        return new TradeRecord
        {
            Timestamp = timestamp,
            Seller    = seller,
            Server    = _server,
            Type      = primaryType,
            Items     = items,
            RawLine   = action
        };
    }

    private static TradeType ClassifyKeyword(string kw) =>
        kw.StartsWith("WTB", StringComparison.OrdinalIgnoreCase) ||
        kw.StartsWith("Buy", StringComparison.OrdinalIgnoreCase)
            ? TradeType.WTB : TradeType.WTS;

    private static void ExtractItems(string segment, TradeType type, List<TradeItem> items)
    {
        if (string.IsNullOrWhiteSpace(segment)) return;

        var priceMatches = MergePriceMatches(segment);
        if (priceMatches.Count == 0)
        {
            // No price tokens — split on common item separators (, / - +) and add each part
            foreach (var part in ItemSeparatorRegex.Split(segment))
            {
                var name = CleanName(part);
                if (!string.IsNullOrWhiteSpace(name) && !IsJunkTrailing(name))
                    items.Add(new TradeItem { Name = name, Type = type });
            }
            return;
        }

        int pos = 0;
        foreach (var pm in priceMatches)
        {
            var name = CleanName(segment[pos..pm.Index]);
            if (string.IsNullOrWhiteSpace(name))
            {
                if (items.Count > 0)
                {
                    var prev = items[^1];
                    if (prev.Price is null)
                    {
                        prev.Price = ParsePriceValue(pm);
                        prev.Unit  = ParsePriceUnit(pm);
                    }
                    else if (prev.Price2 is null)
                    {
                        prev.Price2 = ParsePriceValue(pm);
                        prev.Unit2  = ParsePriceUnit(pm);
                    }
                }
                pos = pm.Index + pm.Length;
                continue;
            }

            items.Add(new TradeItem
            {
                Name  = name,
                Price = ParsePriceValue(pm),
                Unit  = ParsePriceUnit(pm),
                Type  = type
            });
            pos = pm.Index + pm.Length;
        }

        // Any text after the last price — add as a priceless item if it's not noise
        var trailing = CleanName(segment[pos..]);
        if (!string.IsNullOrWhiteSpace(trailing) && !IsJunkTrailing(trailing))
            items.Add(new TradeItem { Name = trailing, Type = type });
    }

    // Strips quantity markers, separator chars, leading/trailing noise from a raw name fragment.
    private static string CleanName(string raw)
    {
        var s = QuantityRegex.Replace(raw, "").Trim(_nameTrimChars);
        s = s.TrimEnd('@').TrimEnd();             // strip trailing "@" price connector ("Sword @ 5000")
        s = LeadingQuantityRegex.Replace(s, ""); // strip "5 x" / "5x" prefix quantities
        s = LeadingJunkRegex.Replace(s, "");      // strip "each"/"ea" left by prior price
        return TrailingJunkRegex.Replace(s, "").Trim();
    }

    // Returns true when a string is clearly not an item name (noise after a price or separator).
    private static bool IsJunkTrailing(string s) =>
        s.Equals("pst",            StringComparison.OrdinalIgnoreCase) ||
        s.Equals("obo",            StringComparison.OrdinalIgnoreCase) ||
        s.Equals("or best offer",  StringComparison.OrdinalIgnoreCase) ||
        s.Equals("or bo",          StringComparison.OrdinalIgnoreCase) ||
        s.Equals("tell me",        StringComparison.OrdinalIgnoreCase) ||
        s.Equals("send tell",      StringComparison.OrdinalIgnoreCase) ||
        s.Equals("each",           StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ea",             StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("or ",        StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("at ",        StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("@",          StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("all ",       StringComparison.OrdinalIgnoreCase);

    // Combines unit-suffixed and bare-number price matches ordered by position.
    // Bare-number matches that overlap a unit-suffixed match are excluded.
    private static List<Match> MergePriceMatches(string segment)
    {
        var primary = PriceRegex.Matches(segment).Cast<Match>().ToList();
        var covered = new HashSet<int>(primary.SelectMany(m => Enumerable.Range(m.Index, m.Length)));
        var bare    = BareNumberPriceRegex.Matches(segment)
            .Cast<Match>()
            .Where(m => !covered.Contains(m.Index));
        return primary.Concat(bare).OrderBy(m => m.Index).ToList();
    }

    private static double? ParsePriceValue(Match m)
    {
        if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var val))
            return null;
        var unit = m.Groups[2].Value.ToLowerInvariant();
        if (unit.StartsWith('k') && !unit.StartsWith("kr"))
            val *= 1000; // k or kpp
        return val;
    }

    private static PriceUnit ParsePriceUnit(Match m) =>
        m.Groups[2].Value.StartsWith("kr", StringComparison.OrdinalIgnoreCase)
            ? PriceUnit.Krono : PriceUnit.PP;
}
