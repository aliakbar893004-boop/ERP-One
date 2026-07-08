using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PurchaseOrderLineTests
{
    [Fact]
    public void Computes_amounts_with_discount_and_tax()
    {
        // 10 x 1000 = 10000; diskon 10% = 1000; setelah diskon 9000; pajak 11% = 990; total 9990
        var l = new PurchaseOrderLine(5, 10, 1000m, 10m, taxId: 1, taxRateSnapshot: 11m);
        Assert.Equal(10000m, l.LineSubtotal);
        Assert.Equal(1000m, l.LineDiscount);
        Assert.Equal(990m, l.LineTax);
        Assert.Equal(9990m, l.LineTotal);
    }

    [Fact]
    public void No_tax_when_taxId_null_even_if_rate_passed()
    {
        var l = new PurchaseOrderLine(5, 2, 500m, 0m, taxId: null, taxRateSnapshot: 11m);
        Assert.Equal(0m, l.TaxRateSnapshot);
        Assert.Equal(0m, l.LineTax);
        Assert.Equal(1000m, l.LineTotal);
    }

    [Fact]
    public void Rejects_non_positive_quantity() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 0, 100m, 0m, null, 0m));

    [Fact]
    public void Rejects_negative_price() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 1, -1m, 0m, null, 0m));

    [Fact]
    public void Rejects_discount_out_of_range() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 1, 100m, 150m, null, 0m));

    [Fact]
    public void DefaultUnitCost_is_net_of_discount_rounded()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 10m, null, 0m); // 1000 * 0.9 = 900
        Assert.Equal(900m, line.DefaultUnitCost);
    }

    [Fact]
    public void ApplyReceipt_accumulates_and_tracks_full_receipt()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m);
        Assert.Equal(0, line.ReceivedQuantity);
        Assert.False(line.IsFullyReceived);

        line.ApplyReceipt(4, 0);
        Assert.Equal(4, line.ReceivedQuantity);
        Assert.False(line.IsFullyReceived);

        line.ApplyReceipt(6, 0);
        Assert.Equal(10, line.ReceivedQuantity);
        Assert.True(line.IsFullyReceived);
    }

    [Fact]
    public void ApplyReceipt_allows_up_to_tolerance()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m); // tol 10% -> max 11
        line.ApplyReceipt(11, 10);
        Assert.Equal(11, line.ReceivedQuantity);
    }

    [Fact]
    public void ApplyReceipt_rejects_over_tolerance()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m); // tol 10% -> max 11
        Assert.Throws<InvalidOperationException>(() => line.ApplyReceipt(12, 10));
    }

    [Fact]
    public void ApplyReceipt_rejects_non_positive()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m);
        Assert.Throws<ArgumentException>(() => line.ApplyReceipt(0, 10));
    }
}
