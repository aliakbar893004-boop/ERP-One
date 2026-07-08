using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ProductVariantDiscountTests
{
    private static ProductVariant Make(decimal? pct) =>
        new("SKU-1", null, price: 100_000m, discountPrice: 90_000m, costPrice: 40_000m,
            weight: null, dimensions: null, isActive: true, discountPercent: pct);

    [Fact]
    public void Stores_discount_percent_when_valid()
    {
        Assert.Equal(10m, Make(10m).DiscountPercent);
        Assert.Null(Make(null).DiscountPercent);
    }

    [Fact]
    public void Rejects_out_of_range_percent()
    {
        Assert.Throws<ArgumentException>(() => Make(-1m));
        Assert.Throws<ArgumentException>(() => Make(101m));
    }

    [Fact]
    public void Update_sets_percent()
    {
        var v = Make(null);
        v.Update(null, 100_000m, 80_000m, 40_000m, null, null, true, 20m);
        Assert.Equal(20m, v.DiscountPercent);
        Assert.Equal(80_000m, v.DiscountPrice);
    }
}
