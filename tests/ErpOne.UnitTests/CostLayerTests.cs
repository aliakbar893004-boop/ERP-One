using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CostLayerTests
{
    [Fact]
    public void Ctor_sets_remaining_equal_to_original()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Equal(10, l.OriginalQty);
        Assert.Equal(10, l.RemainingQty);
        Assert.Equal(1000m, l.UnitCost);
    }

    [Fact]
    public void Ctor_rejects_non_positive_quantity()
    {
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, 1000m, 0));
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, 1000m, -1));
    }

    [Fact]
    public void Ctor_rejects_negative_unit_cost()
    {
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, -1m, 10));
    }

    [Fact]
    public void Consume_takes_min_of_request_and_remaining()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Equal(4, l.Consume(4));   // took 4
        Assert.Equal(6, l.RemainingQty);
        Assert.Equal(6, l.Consume(9));   // only 6 left, took 6
        Assert.Equal(0, l.RemainingQty);
        Assert.Equal(0, l.Consume(3));   // nothing left
    }

    [Fact]
    public void Consume_rejects_non_positive()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Throws<ArgumentException>(() => l.Consume(0));
        Assert.Throws<ArgumentException>(() => l.Consume(-2));
    }
}
