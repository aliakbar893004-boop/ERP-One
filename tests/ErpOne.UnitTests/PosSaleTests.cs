using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PosSaleTests
{
    private static PosSale Sale(bool cash = true, int? taxId = 1, decimal rate = 11m) =>
        new("POS-20260702-0001", cashierShiftId: 5, warehouseId: 3,
            saleDate: new DateTime(2026, 7, 2, 10, 0, 0), paymentMethodId: 1,
            isCashPayment: cash, taxId: taxId, taxRateSnapshot: rate,
            cashierUserId: "u-op1", cashierName: "Budi");

    [Fact]
    public void Line_computes_subtotal_discount_total()
    {
        var l = new PosSaleLine(5, "SKU-1", "Kopi", quantity: 2, unitPrice: 62_000m, discountPercent: 10m, unitCost: 40_000m);
        Assert.Equal(124_000m, l.LineSubtotal);
        Assert.Equal(12_400m, l.LineDiscount);
        Assert.Equal(111_600m, l.LineTotal);
    }

    [Fact]
    public void Settle_computes_tax_exclusive_grand_total_and_change()
    {
        var s = Sale();
        s.AddLine(5, "SKU-1", "Kopi", 2, 50_000m, 0m, 40_000m);   // 100_000
        s.AddLine(6, "SKU-2", "Gula", 1, 20_000m, 0m, 12_000m);   // 20_000
        s.Settle(transactionDiscount: 10_000m, amountTendered: 150_000m);

        Assert.Equal(120_000m, s.Subtotal);
        Assert.Equal(12_100m, s.TaxTotal);       // (120k-10k)*11%
        Assert.Equal(122_100m, s.GrandTotal);
        Assert.Equal(27_900m, s.ChangeGiven);
        Assert.Equal(92_000m, s.CogsTotal);      // 2*40k + 1*12k
        Assert.Equal(2, s.Lines.Count);
    }

    [Fact]
    public void Settle_no_tax_when_taxId_null()
    {
        var s = Sale(taxId: null, rate: 0m);
        s.AddLine(5, "SKU-1", "Kopi", 1, 100_000m, 0m, 40_000m);
        s.Settle(0m, 100_000m);
        Assert.Equal(0m, s.TaxTotal);
        Assert.Equal(100_000m, s.GrandTotal);
        Assert.Equal(0m, s.ChangeGiven);
    }

    [Fact]
    public void Settle_noncash_sets_tendered_to_grand_and_zero_change()
    {
        var s = Sale(cash: false, taxId: null, rate: 0m);
        s.AddLine(5, "SKU-1", "Kopi", 1, 100_000m, 0m, 40_000m);
        s.Settle(0m, amountTendered: 0m);
        Assert.Equal(100_000m, s.AmountTendered);
        Assert.Equal(0m, s.ChangeGiven);
    }

    [Fact]
    public void Settle_rejects_bad_state()
    {
        var empty = Sale();
        Assert.Throws<InvalidOperationException>(() => empty.Settle(0m, 0m));

        var s = Sale(taxId: null, rate: 0m);
        s.AddLine(5, "SKU-1", "Kopi", 1, 100_000m, 0m, 40_000m);
        Assert.Throws<ArgumentException>(() => s.Settle(-1m, 100_000m));
        Assert.Throws<ArgumentException>(() => s.Settle(200_000m, 100_000m));
        Assert.Throws<InvalidOperationException>(() => s.Settle(0m, 50_000m));
    }

    [Fact]
    public void AddLine_rejects_bad_args()
    {
        var s = Sale();
        Assert.Throws<ArgumentException>(() => s.AddLine(0, "S", "P", 1, 1m, 0m, 0m));
        Assert.Throws<ArgumentException>(() => s.AddLine(5, "S", "P", 0, 1m, 0m, 0m));
        Assert.Throws<ArgumentException>(() => s.AddLine(5, "S", "P", 1, -1m, 0m, 0m));
        Assert.Throws<ArgumentException>(() => s.AddLine(5, "S", "P", 1, 1m, 101m, 0m));
        Assert.Throws<ArgumentException>(() => s.AddLine(5, "S", "P", 1, 1m, 0m, -1m));
    }

    [Fact]
    public void Ctor_stores_operating_cashier()
    {
        var s = Sale();
        Assert.Equal("u-op1", s.CashierUserId);
        Assert.Equal("Budi", s.CashierName);
    }

    [Fact]
    public void Ctor_rejects_blank_cashier()
    {
        Assert.Throws<ArgumentException>(() => new PosSale(
            "POS-20260702-0002", 5, 3, new DateTime(2026, 7, 2, 10, 0, 0),
            1, true, null, 0m, cashierUserId: "  ", cashierName: "Budi"));
        Assert.Throws<ArgumentException>(() => new PosSale(
            "POS-20260702-0003", 5, 3, new DateTime(2026, 7, 2, 10, 0, 0),
            1, true, null, 0m, cashierUserId: "u-op1", cashierName: ""));
    }
}
