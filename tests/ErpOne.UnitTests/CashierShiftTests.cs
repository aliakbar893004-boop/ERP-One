using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CashierShiftTests
{
    private static CashierShift Open(decimal openingFloat = 100_000m) =>
        new("SHIFT-20260702-0001", warehouseId: 3, cashierUserId: "u-1",
            cashierName: "Rani", openingFloat: openingFloat, openedAt: new DateTime(2026, 7, 2, 8, 0, 0));

    [Fact]
    public void New_shift_is_open_with_zero_sales()
    {
        var s = Open();
        Assert.Equal(CashierShiftStatus.Open, s.Status);
        Assert.Equal(100_000m, s.OpeningFloat);
        Assert.Equal(0m, s.CashSalesTotal);
        Assert.Equal(100_000m, s.ExpectedCash);
        Assert.Equal(0m, s.TotalSalesAmount);
        Assert.Equal(0, s.TransactionCount);
    }

    [Fact]
    public void Ctor_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new CashierShift("", 3, "u-1", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 0, "u-1", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "u-1", "", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "u-1", "Rani", -1m, DateTime.Now));
    }

    [Fact]
    public void RecordSale_accumulates_per_method_and_cash_only_into_cash_total()
    {
        var s = Open();
        s.RecordSale(paymentMethodId: 1, isCash: true, amount: 50_000m);
        s.RecordSale(paymentMethodId: 1, isCash: true, amount: 30_000m);
        s.RecordSale(paymentMethodId: 2, isCash: false, amount: 70_000m); // kartu

        Assert.Equal(80_000m, s.CashSalesTotal);               // hanya tunai (50k+30k)
        Assert.Equal(150_000m, s.TotalSalesAmount);            // semua metode (50k+30k+70k)
        Assert.Equal(3, s.TransactionCount);
        Assert.Equal(100_000m + 80_000m, s.ExpectedCash);      // float + tunai

        var cashTotal = Assert.Single(s.Totals, t => t.PaymentMethodId == 1);
        Assert.Equal(80_000m, cashTotal.TotalAmount);
        Assert.Equal(2, cashTotal.TransactionCount);
        var cardTotal = Assert.Single(s.Totals, t => t.PaymentMethodId == 2);
        Assert.Equal(70_000m, cardTotal.TotalAmount);
        Assert.Equal(1, cardTotal.TransactionCount);
    }

    [Fact]
    public void RecordSale_rejects_bad_args_and_when_closed()
    {
        var s = Open();
        Assert.Throws<ArgumentException>(() => s.RecordSale(0, true, 10m));
        Assert.Throws<ArgumentException>(() => s.RecordSale(1, true, 0m));
        Assert.Throws<ArgumentException>(() => s.RecordSale(1, true, -5m));
        s.Close(countedCash: 100_000m, note: null, closedAt: DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => s.RecordSale(1, true, 10m));
    }

    [Fact]
    public void Close_computes_variance_short_over_and_exact()
    {
        var s = Open(100_000m);
        s.RecordSale(1, true, 50_000m);           // ExpectedCash = 150_000
        s.Close(countedCash: 148_000m, note: "  kurang 2rb  ", closedAt: new DateTime(2026, 7, 2, 16, 0, 0));

        Assert.Equal(CashierShiftStatus.Closed, s.Status);
        Assert.Equal(148_000m, s.CountedCash);
        Assert.Equal(-2_000m, s.CashVariance);    // kurang
        Assert.Equal("kurang 2rb", s.ClosingNote); // di-trim
        Assert.Equal(new DateTime(2026, 7, 2, 16, 0, 0), s.ClosedAt);
    }

    [Fact]
    public void Close_rejects_negative_and_double_close()
    {
        var s = Open();
        Assert.Throws<ArgumentException>(() => s.Close(-1m, null, DateTime.Now));
        s.Close(100_000m, null, DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => s.Close(100_000m, null, DateTime.Now));
    }
}
