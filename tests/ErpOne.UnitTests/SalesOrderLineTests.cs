using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class SalesOrderLineTests
{
    [Fact]
    public void Computes_amounts_with_discount_and_tax()
    {
        // 10 x 1000 = 10000; diskon 10% = 1000; setelah diskon 9000; pajak 11% = 990; total 9990
        var l = new SalesOrderLine(5, 10, 1000m, 10m, taxId: 1, taxRateSnapshot: 11m);
        Assert.Equal(10000m, l.LineSubtotal);
        Assert.Equal(1000m, l.LineDiscount);
        Assert.Equal(990m, l.LineTax);
        Assert.Equal(9990m, l.LineTotal);
    }

    [Fact]
    public void No_tax_when_taxId_null_even_if_rate_passed()
    {
        var l = new SalesOrderLine(5, 2, 500m, 0m, taxId: null, taxRateSnapshot: 11m);
        Assert.Equal(0m, l.TaxRateSnapshot);
        Assert.Equal(0m, l.LineTax);
        Assert.Equal(1000m, l.LineTotal);
    }

    [Fact]
    public void Rejects_non_positive_quantity() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 0, 100m, 0m, null, 0m));

    [Fact]
    public void Rejects_negative_price() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 1, -1m, 0m, null, 0m));

    [Fact]
    public void Rejects_discount_out_of_range() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 1, 100m, 150m, null, 0m));
}
