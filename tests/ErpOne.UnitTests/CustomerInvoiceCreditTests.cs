using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CustomerInvoiceCreditTests
{
    private static CustomerInvoice InvoiceOf(decimal grand)
    {
        var inv = new CustomerInvoice("CINV-1", 1, "IDR", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), null, null);
        inv.SetLines([new CustomerInvoiceLine(1, 1, 1, 1, grand, 0m, 0m)]); // qty1 × price=grand, no disc/tax
        return inv;
    }

    [Fact]
    public void ApplyCredit_reduces_outstanding_and_sets_status()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(400m);
        Assert.Equal(400m, inv.CreditedAmount);
        Assert.Equal(600m, inv.Outstanding);
        Assert.Equal(CustomerInvoiceStatus.PartiallyPaid, inv.Status);
    }

    [Fact]
    public void ApplyCredit_full_marks_paid()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(1000m);
        Assert.Equal(0m, inv.Outstanding);
        Assert.Equal(CustomerInvoiceStatus.Paid, inv.Status);
    }

    [Fact]
    public void ApplyCredit_rejects_over_outstanding()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyPayment(600m);
        Assert.Throws<InvalidOperationException>(() => inv.ApplyCredit(500m)); // 600 paid + 500 credit > 1000
    }

    [Fact]
    public void ApplyPayment_guard_accounts_for_credit()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(700m);
        Assert.Throws<InvalidOperationException>(() => inv.ApplyPayment(400m)); // 700 credit + 400 pay > 1000
        inv.ApplyPayment(300m); // exactly fills
        Assert.Equal(CustomerInvoiceStatus.Paid, inv.Status);
        Assert.Equal(0m, inv.Outstanding);
    }

    [Fact]
    public void ReverseCredit_restores_outstanding()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(400m);
        inv.ReverseCredit(400m);
        Assert.Equal(0m, inv.CreditedAmount);
        Assert.Equal(1000m, inv.Outstanding);
        Assert.Equal(CustomerInvoiceStatus.Open, inv.Status);
    }
}
