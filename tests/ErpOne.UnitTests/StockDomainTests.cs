using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class StockDomainTests
{
    [Fact]
    public void ProductStock_ApplyDelta_increases_and_decreases()
    {
        var s = new ProductStock(1, 1, 10);
        s.ApplyDelta(5);
        Assert.Equal(15, s.Quantity);
        s.ApplyDelta(-7);
        Assert.Equal(8, s.Quantity);
    }

    [Fact]
    public void ProductStock_ApplyDelta_rejects_negative_result()
    {
        var s = new ProductStock(1, 1, 3);
        Assert.Throws<InvalidOperationException>(() => s.ApplyDelta(-4));
    }

    [Fact]
    public void StockMovement_rejects_zero_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            new StockMovement(1, 1, MovementType.Adjustment, 0, 0m, new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void ApplyMovingAverage_computes_weighted_average()
    {
        // 10 @ 1000 already on hand (CostPrice 1000), add 10 @ 2000 -> avg 1500
        var v = MakeVariant(costPrice: 1000m);
        v.ApplyMovingAverage(totalQtyBefore: 10, inQty: 10, inUnitCost: 2000m);
        Assert.Equal(1500m, v.CostPrice);
    }

    [Fact]
    public void ApplyMovingAverage_from_zero_onhand_takes_incoming_cost()
    {
        var v = MakeVariant(costPrice: 0m);
        v.ApplyMovingAverage(totalQtyBefore: 0, inQty: 5, inUnitCost: 1234m);
        Assert.Equal(1234m, v.CostPrice);
    }

    [Fact]
    public void ApplyMovingAverage_ignores_non_positive_inQty()
    {
        var v = MakeVariant(costPrice: 500m);
        v.ApplyMovingAverage(totalQtyBefore: 10, inQty: 0, inUnitCost: 9999m);
        Assert.Equal(500m, v.CostPrice);
    }

    private static ProductVariant MakeVariant(decimal costPrice) =>
        new("SKU-1", null, price: 100m, discountPrice: null, costPrice: costPrice,
            weight: null, dimensions: null, isActive: true);
}
