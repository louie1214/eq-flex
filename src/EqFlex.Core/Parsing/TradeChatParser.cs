using System.Text.RegularExpressions;
using EqFlex.Core.Models;

namespace EqFlex.Core.Parsing;

/// <summary>
/// Parses EverQuest auction channel lines into TradeRecord objects.
/// Handles in-zone auction broadcasts and Auction/Auction1/Auction2 channel tells.
/// </summary>
public sealed class TradeChatParser
{
    // "PlayerName auctions, 'MESSAGE'"
    private static readonly Regex AuctionBroadcastRegex = new(
        @"^(\w+) auctions, '(.+)'$",
        RegexOptions.Compiled);

    // "PlayerName tells Auction[N]:[N], 'MESSAGE'" — covers Auction, Auction1, Auction2, etc.
    private static readonly Regex AuctionTellRegex = new(
        @"^(\w+) tells Auction\d*:\d+, '(.+)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Splits message into typed segments at keyword boundaries
    private static readonly Regex KeywordRegex = new(
        @"\b(WTS|WTB|WTSell|WTBuy|Selling|Buying)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Unit-suffixed prices — Krono matched before 'k' to prevent false matches.
    // Matches: 1kr, 2 KR, 1krono, 1kronos, 12k, 10.5k, 500pp, 5kpp, 6000p
    private static readonly Regex PriceRegex = new(
        @"\b(\d+(?:\.\d+)?)\s*(kr(?:ono)?s?|kpp?|pp?|k)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Bare number prices (no unit suffix) — assumed platinum.
    // Only matches at end-of-item positions: before a comma/semicolon, end of string,
    // or trailing keywords like OBO/PST. Negative lookbehind excludes +5/−5 stat modifiers.
    private static readonly Regex BareNumberPriceRegex = new(
        @"(?<![+\-])\b(\d+(?:\.\d+)?)()(?=\s*(?:,|;|$|(?:obo|pst|or\s+best|or\s+bo|tell\s+me)\b))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _server;

    public TradeChatParser(string server) => _server = server;

    /// <summary>
    /// Parses a user-entered max-price string into its platinum and Krono components.
    /// Accepts the same shorthand as auction lines: "10k", "1kr", "1kr 5kpp", "5000", etc.
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
            seller = m.Groups[1].Value;
            message = m.Groups[2].Value;
        }
        else
        {
            m = AuctionTellRegex.Match(action);
            if (!m.Success) return null;
            seller = m.Groups[1].Value;
            message = m.Groups[2].Value;
        }

        var keywords = KeywordRegex.Matches(message);
        if (keywords.Count == 0) return null;

        var items = new List<TradeItem>();
        var primaryType = ClassifyKeyword(keywords[0].Value);

        for (int i = 0; i < keywords.Count; i++)
        {
            var kw = keywords[i];
            var type = ClassifyKeyword(kw.Value);
            int contentStart = kw.Index + kw.Length;
            int contentEnd   = i + 1 < keywords.Count ? keywords[i + 1].Index : message.Length;
            var segment = message[contentStart..contentEnd].Trim().TrimStart(':', ' ');
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

    private static readonly char[] _nameTrimChars = [',', ' ', ':', ';'];

    private static void ExtractItems(string segment, TradeType type, List<TradeItem> items)
    {
        if (string.IsNullOrWhiteSpace(segment)) return;

        var priceMatches = MergePriceMatches(segment);
        if (priceMatches.Count == 0)
        {
            // No price tokens — treat the whole segment as one item name
            var name = segment.Trim(_nameTrimChars);
            if (!string.IsNullOrWhiteSpace(name))
                items.Add(new TradeItem { Name = name, Type = type });
            return;
        }

        int pos = 0;
        foreach (var pm in priceMatches)
        {
            // Trim both ends to strip leading ", " from items after the first
            var name = segment[pos..pm.Index].Trim(_nameTrimChars);
            if (string.IsNullOrWhiteSpace(name))
            {
                if (items.Count > 0)
                {
                    var prev = items[^1];
                    if (prev.Price is null)
                    {
                        // First price for this item
                        prev.Price = ParsePriceValue(pm);
                        prev.Unit  = ParsePriceUnit(pm);
                    }
                    else if (prev.Price2 is null)
                    {
                        // Second component of a combined price (e.g., "1kr 5kpp")
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

        // Capture any item after the last price (listed without a price, e.g. "WTS Sword 500k, Shield")
        var trailing = segment[pos..].Trim(_nameTrimChars);
        if (!string.IsNullOrWhiteSpace(trailing) && !IsJunkSuffix(trailing))
            items.Add(new TradeItem { Name = trailing, Type = type });
    }

    // Combines unit-suffixed and bare-number price matches, ordered by position.
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

    // Common suffixes that follow a price but aren't item names
    private static bool IsJunkSuffix(string s) =>
        s.Equals("pst", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("obo", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("or best offer", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("or bo", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("tell me", StringComparison.OrdinalIgnoreCase);

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
