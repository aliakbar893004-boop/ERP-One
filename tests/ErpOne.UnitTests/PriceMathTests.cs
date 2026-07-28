using ErpOne.Application.Pricing;
using Xunit;

namespace ErpOne.UnitTests;

public class PriceMathTests
{
    private static readonly (int MinQty, decimal UnitPrice)[] Tiers =
    [
        (1, 90_000m),
        (10, 85_000m),
        (50, 78_000m),
    ];

    [Theory]
    [InlineData(1, 1, 90_000)]
    [InlineData(9, 1, 90_000)]
    [InlineData(10, 10, 85_000)]
    [InlineData(49, 10, 85_000)]
    [InlineData(50, 50, 78_000)]
    [InlineData(600, 50, 78_000)]
    public void PickTier_takes_largest_min_qty_not_exceeding_quantity(int qty, int expectedMinQty, decimal expectedPrice)
    {
        var tier = PriceMath.PickTier(Tiers, qty);

        Assert.NotNull(tier);
        Assert.Equal(expectedMinQty, tier!.Value.MinQty);
        Assert.Equal(expectedPrice, tier.Value.UnitPrice);
    }

    [Fact]
    public void PickTier_returns_null_when_no_tiers()
    {
        Assert.Null(PriceMath.PickTier([], 10));
    }

    [Fact]
    public void PickTier_returns_null_when_quantity_below_smallest_tier()
    {
        Assert.Null(PriceMath.PickTier([(5, 90_000m), (10, 85_000m)], 4));
    }

    [Fact]
    public void PickTier_is_independent_of_input_order()
    {
        var shuffled = new[] { (50, 78_000m), (1, 90_000m), (10, 85_000m) };
        var tier = PriceMath.PickTier(shuffled, 12);

        Assert.Equal(10, tier!.Value.MinQty);
    }

    [Fact]
    public void Deviation_from_discount_percent_only()
    {
        // harga engine 100.000, client kirim harga sama + diskon 10% -> menyimpang 10%
        Assert.Equal(10m, PriceMath.DeviationPercent(100_000m, 100_000m, 10m));
    }

    [Fact]
    public void Deviation_from_price_override_only()
    {
        // harga engine 100.000, client kirim 90.000 tanpa diskon -> menyimpang 10%
        Assert.Equal(10m, PriceMath.DeviationPercent(100_000m, 90_000m, 0m));
    }

    [Fact]
    public void Deviation_combines_price_override_and_discount()
    {
        // 90.000 * 0,9 = 81.000 dari 100.000 -> menyimpang 19%
        Assert.Equal(19m, PriceMath.DeviationPercent(100_000m, 90_000m, 10m));
    }

    [Fact]
    public void Deviation_is_negative_when_price_is_above_engine_price()
    {
        Assert.Equal(-20m, PriceMath.DeviationPercent(100_000m, 120_000m, 0m));
    }

    [Fact]
    public void Deviation_is_zero_when_resolved_price_is_zero()
    {
        // harga master belum diatur -> jangan bagi nol, anggap lolos
        Assert.Equal(0m, PriceMath.DeviationPercent(0m, 50_000m, 90m));
    }

    [Fact]
    public void Deviation_is_hundred_when_line_is_fully_discounted()
    {
        Assert.Equal(100m, PriceMath.DeviationPercent(100_000m, 100_000m, 100m));
    }

    [Fact]
    public void Effective_max_takes_largest_role_limit()
    {
        Assert.Equal(30m, PriceMath.EffectiveMaxDiscountPercent([5m, 30m, 15m], 100m));
    }

    [Fact]
    public void Effective_max_ignores_null_role_limits()
    {
        Assert.Equal(15m, PriceMath.EffectiveMaxDiscountPercent([null, 15m, null], 100m));
    }

    [Fact]
    public void Effective_max_falls_back_to_global_default_when_all_null()
    {
        Assert.Equal(7m, PriceMath.EffectiveMaxDiscountPercent([null, null], 7m));
    }

    [Fact]
    public void Effective_max_falls_back_to_global_default_when_no_roles()
    {
        Assert.Equal(42m, PriceMath.EffectiveMaxDiscountPercent([], 42m));
    }

    [Fact]
    public void Effective_max_of_zero_is_honoured_not_treated_as_unset()
    {
        // role dengan batas 0 = tidak boleh diskon; tidak boleh jatuh ke default global
        Assert.Equal(0m, PriceMath.EffectiveMaxDiscountPercent([0m], 100m));
    }
}
