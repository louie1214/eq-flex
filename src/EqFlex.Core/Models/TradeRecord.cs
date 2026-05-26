namespace EqFlex.Core.Models;

public enum TradeType { Unknown, WTS, WTB }
public enum PriceUnit { Unknown, PP, Krono }

public sealed class TradeItem
{
    public string   Name  { get; set; } = string.Empty;
    public double?  Price { get; set; }
    public PriceUnit Unit { get; set; } = PriceUnit.Unknown;
    public TradeType Type { get; set; } = TradeType.Unknown;

    // Second price component for combined prices like "1kr 5kpp"
    public double?  Price2 { get; set; }
    public PriceUnit Unit2  { get; set; } = PriceUnit.Unknown;

    // The platinum component (from either Price or Price2) used for alert filtering
    public double? PricePp =>
        Unit  == PriceUnit.PP ? Price  :
        Unit2 == PriceUnit.PP ? Price2 : null;

    // The Krono component (from either Price or Price2) used for alert filtering
    public double? PriceKrono =>
        Unit  == PriceUnit.Krono ? Price  :
        Unit2 == PriceUnit.Krono ? Price2 : null;

    public string PriceDisplay
    {
        get
        {
            var p1 = FormatComponent(Price,  Unit);
            var p2 = FormatComponent(Price2, Unit2);
            return (p1.Length > 0, p2.Length > 0) switch
            {
                (true, true)  => $"{p1} + {p2}",
                (true, false) => p1,
                (false, true) => p2,
                _             => string.Empty
            };
        }
    }

    private static string FormatComponent(double? price, PriceUnit unit) => unit switch
    {
        PriceUnit.Krono => price.HasValue ? $"{price:N0}kr" : string.Empty,
        PriceUnit.PP    => price.HasValue ? $"{price:N0}pp" : string.Empty,
        _               => price.HasValue ? $"{price:N0}pp" : string.Empty
    };
}

public sealed class TradeRecord
{
    public int Id { get; set; }
    public long Timestamp { get; set; }
    public string Seller { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public TradeType Type { get; set; }
    public List<TradeItem> Items { get; set; } = [];
    public string RawLine { get; set; } = string.Empty;
}

public sealed class ItemAlert
{
    public int Id { get; set; }
    public string ItemName    { get; set; } = string.Empty;
    public TradeType AlertType { get; set; } = TradeType.WTS;
    public double? MaxPricePp    { get; set; }
    public double? MaxPriceKrono { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string MaxPriceDisplay
    {
        get
        {
            var parts = new List<string>(2);
            if (MaxPriceKrono.HasValue) parts.Add($"{MaxPriceKrono:N0}kr");
            if (MaxPricePp.HasValue)    parts.Add($"{MaxPricePp:N0}pp");
            return string.Join(" + ", parts);
        }
    }
}

public sealed class ItemAlertHit
{
    public int    Id        { get; set; }
    public int    AlertId   { get; set; }
    public long   Timestamp { get; set; }
    public string ItemName  { get; set; } = string.Empty;
    public string Seller    { get; set; } = string.Empty;
    public string Price     { get; set; } = string.Empty;
    public string RawLine   { get; set; } = string.Empty;
}
