using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PurchaseOrderTests
{
    private static PurchaseOrder Make() =>
        new("PO-202606-0001", supplierId: 1, warehouseId: 2,
            orderDate: new DateTime(2026, 6, 24), expectedDate: null, currency: "idr", notes: null);

    private static PurchaseOrderLine Line() => new(5, 10, 1000m, 0m, null, 0m);

    [Fact]
    public void New_po_is_draft_and_normalizes_currency()
    {
        var po = Make();
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("IDR", po.Currency);
    }

    [Fact]
    public void SetLines_recomputes_totals()
    {
        var po = Make();
        po.SetLines([Line(), Line()]);
        Assert.Equal(20000m, po.Subtotal);
        Assert.Equal(20000m, po.GrandTotal);
        Assert.Equal(2, po.Lines.Count);
    }

    [Fact]
    public void Submit_requires_lines()
    {
        var po = Make();
        Assert.Throws<InvalidOperationException>(() => po.Submit());
        po.SetLines([Line()]);
        po.Submit();
        Assert.Equal(PurchaseOrderStatus.PendingApproval, po.Status);
    }

    [Fact]
    public void Cannot_edit_lines_unless_draft()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        Assert.Throws<InvalidOperationException>(() => po.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() =>
            po.UpdateHeader(1, 2, DateTime.Today, null, "IDR", null));
    }

    [Fact]
    public void Confirm_only_from_pending()
    {
        var po = Make();
        Assert.Throws<InvalidOperationException>(() => po.MarkConfirmed());
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        Assert.Equal(PurchaseOrderStatus.Confirmed, po.Status);
    }

    [Fact]
    public void ReturnToDraft_stores_reason()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.ReturnToDraft("revisi harga");
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("revisi harga", po.RejectionNote);
    }

    [Fact]
    public void Cancel_allowed_from_draft_and_pending_only()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        Assert.Throws<InvalidOperationException>(() => po.Cancel()); // confirmed tak bisa cancel di B1

        var po2 = Make();
        po2.Cancel();
        Assert.Equal(PurchaseOrderStatus.Cancelled, po2.Status);
    }

    [Fact]
    public void ExpectedDate_must_be_on_or_after_order_date() =>
        Assert.Throws<ArgumentException>(() =>
            new PurchaseOrder("PO-1", 1, 2, new DateTime(2026, 6, 24),
                expectedDate: new DateTime(2026, 6, 1), currency: "IDR", notes: null));

    private static PurchaseOrder Confirmed()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        return po;
    }

    [Fact]
    public void CanReceive_only_when_confirmed_or_partially_received()
    {
        Assert.False(Make().CanReceive);
        var po = Confirmed();
        Assert.True(po.CanReceive);
        po.MarkPartiallyReceived();
        Assert.True(po.CanReceive);
    }

    [Fact]
    public void MarkReceived_and_partial_require_receivable_status()
    {
        Assert.Throws<InvalidOperationException>(() => Make().MarkReceived());
        var po = Confirmed();
        po.MarkReceived();
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);
    }

    [Fact]
    public void Close_only_from_partially_received()
    {
        var po = Confirmed();
        Assert.Throws<InvalidOperationException>(() => po.Close()); // Confirmed cannot close
        po.MarkPartiallyReceived();
        po.Close();
        Assert.Equal(PurchaseOrderStatus.Closed, po.Status);
    }
}
