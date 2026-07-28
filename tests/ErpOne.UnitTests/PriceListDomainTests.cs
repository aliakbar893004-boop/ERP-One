using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PriceListDomainTests
{
    [Fact]
    public void Code_is_normalized_to_uppercase_and_trimmed()
    {
        var list = new PriceList(" grosir ", "Grosir", null, true);
        Assert.Equal("GROSIR", list.Code);
    }

    [Fact]
    public void Empty_code_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new PriceList("  ", "Grosir", null, true));
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new PriceList("GROSIR", " ", null, true));
    }

    [Fact]
    public void Line_rejects_min_qty_below_one()
    {
        Assert.Throws<ArgumentException>(() => new PriceListLine(1, 0, 90_000m));
    }

    [Fact]
    public void Line_rejects_negative_price()
    {
        Assert.Throws<ArgumentException>(() => new PriceListLine(1, 1, -1m));
    }

    [Fact]
    public void Line_accepts_valid_tier()
    {
        var line = new PriceListLine(7, 10, 85_000m);
        Assert.Equal(7, line.ProductVariantId);
        Assert.Equal(10, line.MinQty);
        Assert.Equal(85_000m, line.UnitPrice);
    }

    [Fact]
    public void SetLines_replaces_previous_lines()
    {
        var list = new PriceList("GROSIR", "Grosir", null, true);
        list.SetLines([new PriceListLine(1, 1, 90_000m)]);
        list.SetLines([new PriceListLine(2, 1, 80_000m), new PriceListLine(2, 10, 75_000m)]);

        Assert.Equal(2, list.Lines.Count);
        Assert.All(list.Lines, l => Assert.Equal(2, l.ProductVariantId));
    }

    [Fact]
    public void PricingSetting_rejects_percent_outside_zero_hundred()
    {
        var setting = new PricingSetting();
        Assert.Throws<ArgumentException>(() => setting.SetDefaultMaxDiscountPercent(-1m));
        Assert.Throws<ArgumentException>(() => setting.SetDefaultMaxDiscountPercent(100.01m));
    }

    [Fact]
    public void PricingSetting_accepts_boundary_values()
    {
        var setting = new PricingSetting();
        setting.SetDefaultMaxDiscountPercent(0m);
        Assert.Equal(0m, setting.DefaultMaxDiscountPercent);
        setting.SetDefaultMaxDiscountPercent(100m);
        Assert.Equal(100m, setting.DefaultMaxDiscountPercent);
    }
}
