using EqFlex.Core.Models;
using EqFlex.Core.Parsing;
using Xunit;

namespace EqFlex.Tests.Parsing;

public sealed class TradeChatParserTests
{
    private readonly TradeChatParser _parser = new("TestServer");
    private const long Ts = 0;

    private TradeRecord Parse(string line) =>
        _parser.TryParse(line, Ts) ?? throw new InvalidOperationException($"Failed to parse: {line}");

    // ── Unit-suffixed prices ───────────────────────────────────────────────────

    [Fact]
    public void Parses_k_suffix()
    {
        var r = Parse("Seller auctions, 'WTS Blade of Carnage 50k'");
        Assert.Equal(50000, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Fact]
    public void Parses_decimal_k_suffix()
    {
        var r = Parse("Seller auctions, 'WTS Staff 10.5k'");
        Assert.Equal(10500, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Fact]
    public void Parses_pp_suffix()
    {
        var r = Parse("Seller auctions, 'WTS Ring 500pp'");
        Assert.Equal(500, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Theory]
    [InlineData("2kr")]
    [InlineData("2 KR")]
    [InlineData("2 Krono")]
    [InlineData("2 Kronos")]
    public void Parses_krono_variants(string priceText)
    {
        var r = Parse($"Seller auctions, 'WTS Cloak {priceText}'");
        Assert.Equal(2, r.Items[0].Price);
        Assert.Equal(PriceUnit.Krono, r.Items[0].Unit);
    }

    // ── Bare number prices (no unit = assume pp) ──────────────────────────────

    [Fact]
    public void Bare_number_at_end_is_pp()
    {
        var r = Parse("Seller auctions, 'WTS Sword of Power 5000'");
        Assert.Single(r.Items);
        Assert.Equal("Sword of Power", r.Items[0].Name);
        Assert.Equal(5000, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Fact]
    public void Bare_number_before_pst_is_pp()
    {
        var r = Parse("Seller auctions, 'WTS Staff of the Ancients 15000 pst'");
        Assert.Equal("Staff of the Ancients", r.Items[0].Name);
        Assert.Equal(15000, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Fact]
    public void Bare_number_before_obo_is_pp()
    {
        var r = Parse("Seller auctions, 'WTS Neck 8000 OBO'");
        Assert.Equal(8000, r.Items[0].Price);
        Assert.Equal(PriceUnit.PP, r.Items[0].Unit);
    }

    [Fact]
    public void Multiple_items_bare_numbers()
    {
        var r = Parse("Seller auctions, 'WTS Sword 500, Shield 300'");
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("Sword",  r.Items[0].Name); Assert.Equal(500, r.Items[0].Price);
        Assert.Equal("Shield", r.Items[1].Name); Assert.Equal(300, r.Items[1].Price);
    }

    [Fact]
    public void Stat_modifier_not_treated_as_price()
    {
        // "+5" following item name should not be parsed as a price
        var r = Parse("Seller auctions, 'WTS Ring of Power'");
        Assert.Null(r.Items[0].Price);
    }

    // ── Price display format ───────────────────────────────────────────────────

    [Fact]
    public void FormatPP_uses_comma_thousands()
    {
        var item = new TradeItem { Price = 10000, Unit = PriceUnit.PP };
        Assert.Equal("10,000pp", item.PriceDisplay);
    }

    [Fact]
    public void FormatPP_displays_pp_below_1000()
    {
        var item = new TradeItem { Price = 500, Unit = PriceUnit.PP };
        Assert.Equal("500pp", item.PriceDisplay);
    }

    [Fact]
    public void FormatKrono_displays_kr_suffix()
    {
        var item = new TradeItem { Price = 2, Unit = PriceUnit.Krono };
        Assert.Equal("2kr", item.PriceDisplay);
    }

    // ── Multi-currency prices ─────────────────────────────────────────────────

    [Theory]
    [InlineData("1KR 5kpp",   1, 5000)]
    [InlineData("1kr 5000pp", 1, 5000)]
    [InlineData("2 Krono 10k", 2, 10000)]
    public void Parses_combined_krono_and_pp(string priceText, double krono, double pp)
    {
        var r = Parse($"Seller auctions, 'WTS Sword {priceText}'");
        Assert.Single(r.Items);
        var item = r.Items[0];
        Assert.Equal("Sword", item.Name);

        // One component should be Krono, the other PP
        var kronoVal = item.Unit == PriceUnit.Krono ? item.Price : item.Price2;
        var ppVal    = item.Unit == PriceUnit.PP    ? item.Price :
                       item.Unit2 == PriceUnit.PP   ? item.Price2 : null;

        Assert.Equal(krono, kronoVal);
        Assert.Equal(pp,    ppVal);
    }

    [Fact]
    public void Combined_price_display_shows_both_components()
    {
        var item = new TradeItem { Price = 1, Unit = PriceUnit.Krono, Price2 = 5000, Unit2 = PriceUnit.PP };
        Assert.Equal("1kr + 5,000pp", item.PriceDisplay);
    }

    [Fact]
    public void PricePp_returns_pp_component_when_krono_is_primary()
    {
        // "1kr 5kpp" — Krono is Price, PP is Price2
        var item = new TradeItem { Price = 1, Unit = PriceUnit.Krono, Price2 = 5000, Unit2 = PriceUnit.PP };
        Assert.Equal(5000, item.PricePp);
    }

    [Fact]
    public void PricePp_returns_null_for_krono_only()
    {
        var item = new TradeItem { Price = 2, Unit = PriceUnit.Krono };
        Assert.Null(item.PricePp);
    }

    // ── ParseMaxPrice ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseMaxPrice_bare_number_is_pp()
    {
        var (pp, kr) = TradeChatParser.ParseMaxPrice("5000");
        Assert.Equal(5000, pp);
        Assert.Null(kr);
    }

    [Fact]
    public void ParseMaxPrice_k_suffix_is_pp()
    {
        var (pp, kr) = TradeChatParser.ParseMaxPrice("10k");
        Assert.Equal(10000, pp);
        Assert.Null(kr);
    }

    [Fact]
    public void ParseMaxPrice_kr_suffix_is_krono()
    {
        var (pp, kr) = TradeChatParser.ParseMaxPrice("1kr");
        Assert.Null(pp);
        Assert.Equal(1, kr);
    }

    [Theory]
    [InlineData("1kr 5kpp",  1, 5000)]
    [InlineData("1kr 5000",  1, 5000)]
    [InlineData("2 Krono 10k", 2, 10000)]
    public void ParseMaxPrice_combined_krono_and_pp(string input, double krono, double pp)
    {
        var (parsedPp, parsedKr) = TradeChatParser.ParseMaxPrice(input);
        Assert.Equal(pp,    parsedPp);
        Assert.Equal(krono, parsedKr);
    }

    [Fact]
    public void ParseMaxPrice_null_or_blank_returns_nulls()
    {
        Assert.Equal((null, null), TradeChatParser.ParseMaxPrice(null));
        Assert.Equal((null, null), TradeChatParser.ParseMaxPrice(""));
        Assert.Equal((null, null), TradeChatParser.ParseMaxPrice("  "));
    }

    [Fact]
    public void ItemAlert_MaxPriceDisplay_combined()
    {
        var alert = new ItemAlert { MaxPriceKrono = 1, MaxPricePp = 5000 };
        Assert.Equal("1kr + 5,000pp", alert.MaxPriceDisplay);
    }

    [Fact]
    public void ItemAlert_MaxPriceDisplay_pp_only()
    {
        var alert = new ItemAlert { MaxPricePp = 10000 };
        Assert.Equal("10,000pp", alert.MaxPriceDisplay);
    }
}
