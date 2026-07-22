using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ProductStockCostTests
{
    [Fact]
    public void SetCost_updates_cost_price()
    {
        var s = new ProductStock(1, 1, 10);
        s.SetCost(1250.50m);
        Assert.Equal(1250.50m, s.CostPrice);
    }

    [Fact]
    public void SetCost_rejects_negative()
    {
        var s = new ProductStock(1, 1, 10);
        Assert.Throws<ArgumentException>(() => s.SetCost(-1m));
    }

    [Fact]
    public void New_stock_defaults_cost_to_zero()
    {
        Assert.Equal(0m, new ProductStock(1, 1, 5).CostPrice);
    }
}
