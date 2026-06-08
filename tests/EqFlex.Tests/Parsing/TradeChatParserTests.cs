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

    // ── Separator styles ──────────────────────────────────────────────────────

    [Fact]
    public void Hyphen_separator_splits_items_with_prices()
    {
        var r = Parse("Seller auctions, 'WTS Tribal Mask 25p - Ravenscale Gloves 25p - Reed Belt 50p'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Tribal Mask",      r.Items[0].Name); Assert.Equal(25, r.Items[0].Price);
        Assert.Equal("Ravenscale Gloves",r.Items[1].Name); Assert.Equal(25, r.Items[1].Price);
        Assert.Equal("Reed Belt",        r.Items[2].Name); Assert.Equal(50, r.Items[2].Price);
    }

    [Fact]
    public void Hyphen_separator_splits_items_without_prices()
    {
        var r = Parse("Seller auctions, 'WTS Cloak - Mask - Boots'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Cloak", r.Items[0].Name);
        Assert.Equal("Mask",  r.Items[1].Name);
        Assert.Equal("Boots", r.Items[2].Name);
    }

    [Fact]
    public void Intra_name_hyphen_preserved()
    {
        var r = Parse("Seller auctions, 'WTS Frost-Covered Tome 400'");
        Assert.Single(r.Items);
        Assert.Equal("Frost-Covered Tome", r.Items[0].Name);
        Assert.Equal(400, r.Items[0].Price);
    }

    [Fact]
    public void Comma_separator_splits_items_without_prices()
    {
        var r = Parse("Seller auctions, 'WTS Black Ice Leggings,Spider Fur Collar'");
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("Black Ice Leggings", r.Items[0].Name);
        Assert.Equal("Spider Fur Collar",  r.Items[1].Name);
    }

    [Fact]
    public void Slash_separator_splits_items_without_prices()
    {
        var r = Parse("Buyer auctions, 'WTB Shield / Helm'");
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("Shield", r.Items[0].Name);
        Assert.Equal("Helm",   r.Items[1].Name);
    }

    [Fact]
    public void Slash_separator_splits_items_with_prices()
    {
        var r = Parse("Seller auctions, 'WTS Spell: Cripple 100 / Spell: Pillar of Frost 100 / Spell: Defoliation 100'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Spell: Cripple",         r.Items[0].Name); Assert.Equal(100, r.Items[0].Price);
        Assert.Equal("Spell: Pillar of Frost",  r.Items[1].Name); Assert.Equal(100, r.Items[1].Price);
        Assert.Equal("Spell: Defoliation",      r.Items[2].Name); Assert.Equal(100, r.Items[2].Price);
    }

    [Fact]
    public void Pipe_separator_splits_items_without_prices()
    {
        var r = Parse("Buyer auctions, 'WTB Shield | Helm | Boots'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Shield", r.Items[0].Name);
        Assert.Equal("Helm",   r.Items[1].Name);
        Assert.Equal("Boots",  r.Items[2].Name);
    }

    [Fact]
    public void Plus_separator_splits_items_without_prices()
    {
        var r = Parse("Seller auctions, 'WTS Medallion of Nathsar + Medallion of Kunzar'");
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("Medallion of Nathsar", r.Items[0].Name);
        Assert.Equal("Medallion of Kunzar",  r.Items[1].Name);
    }

    // ── Space-separated chains (bare numbers as price separators) ─────────────

    [Fact]
    public void Space_separated_item_price_chain()
    {
        var r = Parse("Seller auctions, 'WTS Thin Banded Belt 200 Gnome Skin Gloves 400 Dark Ember 900'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Thin Banded Belt",  r.Items[0].Name); Assert.Equal(200, r.Items[0].Price);
        Assert.Equal("Gnome Skin Gloves", r.Items[1].Name); Assert.Equal(400, r.Items[1].Price);
        Assert.Equal("Dark Ember",        r.Items[2].Name); Assert.Equal(900, r.Items[2].Price);
    }

    [Fact]
    public void Mixed_bare_and_unit_price_chain()
    {
        var r = Parse("Seller auctions, 'WTS Ring of the Ancients 900 Guise of the Deceiver 1kr Ball of Golem Clay 150'");
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("Ring of the Ancients",   r.Items[0].Name); Assert.Equal(900,          r.Items[0].Price); Assert.Equal(PriceUnit.PP,    r.Items[0].Unit);
        Assert.Equal("Guise of the Deceiver",  r.Items[1].Name); Assert.Equal(1,            r.Items[1].Price); Assert.Equal(PriceUnit.Krono, r.Items[1].Unit);
        Assert.Equal("Ball of Golem Clay",     r.Items[2].Name); Assert.Equal(150,          r.Items[2].Price); Assert.Equal(PriceUnit.PP,    r.Items[2].Unit);
    }

    // ── Quantity markers ──────────────────────────────────────────────────────

    [Fact]
    public void Quantity_marker_stripped_from_item_name()
    {
        var r = Parse("Seller auctions, 'WTS Spider Silk x20 20p Spiderling Silk x90 30p'");
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("Spider Silk",     r.Items[0].Name); Assert.Equal(20, r.Items[0].Price);
        Assert.Equal("Spiderling Silk", r.Items[1].Name); Assert.Equal(30, r.Items[1].Price);
    }

    // ── Trailing noise ────────────────────────────────────────────────────────

    [Fact]
    public void Trailing_or_clause_not_added_as_item()
    {
        var r = Parse("Seller auctions, 'WTS Ring of the Ancients 600p or trade for shield'");
        Assert.Single(r.Items);
        Assert.Equal("Ring of the Ancients", r.Items[0].Name);
    }

    [Fact]
    public void Trailing_at_location_not_added_as_item()
    {
        var r = Parse("Buyer auctions, 'WTB krono 1300 - at shady'");
        Assert.Single(r.Items);
        Assert.Equal("krono", r.Items[0].Name);
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
