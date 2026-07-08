using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class SalesOrderTests
{
    private static SalesOrder Make() =>
        new("SO-202607-0001", customerId: 1, warehouseId: 2,
            orderDate: new DateTime(2026, 7, 1), expectedDate: null, currency: "idr", notes: null);

    private static SalesOrderLine Line() => new(5, 10, 1000m, 0m, null, 0m);

    [Fact]
    public void New_so_is_draft_and_normalizes_currency()
    {
        var so = Make();
        Assert.Equal(SalesOrderStatus.Draft, so.Status);
        Assert.Equal("IDR", so.Currency);
    }

    [Fact]
    public void SetLines_recomputes_totals()
    {
        var so = Make();
        so.SetLines([Line(), Line()]);
        Assert.Equal(20000m, so.Subtotal);
        Assert.Equal(20000m, so.GrandTotal);
        Assert.Equal(2, so.Lines.Count);
    }

    [Fact]
    public void Submit_requires_lines()
    {
        var so = Make();
        Assert.Throws<InvalidOperationException>(() => so.Submit());
        so.SetLines([Line()]);
        so.Submit();
        Assert.Equal(SalesOrderStatus.PendingApproval, so.Status);
    }

    [Fact]
    public void Cannot_edit_lines_unless_draft()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        Assert.Throws<InvalidOperationException>(() => so.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() =>
            so.UpdateHeader(1, 2, DateTime.Today, null, "IDR", null));
    }

    [Fact]
    public void Confirm_only_from_pending()
    {
        var so = Make();
        Assert.Throws<InvalidOperationException>(() => so.MarkConfirmed());
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        Assert.Equal(SalesOrderStatus.Confirmed, so.Status);
    }

    [Fact]
    public void ReturnToDraft_stores_reason()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.ReturnToDraft("stok tidak cukup");
        Assert.Equal(SalesOrderStatus.Draft, so.Status);
        Assert.Equal("stok tidak cukup", so.RejectionNote);
    }

    [Fact]
    public void Cancel_allowed_from_draft_and_pending_only()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        Assert.Throws<InvalidOperationException>(() => so.Cancel()); // confirmed tak bisa cancel di C1

        var so2 = Make();
        so2.Cancel();
        Assert.Equal(SalesOrderStatus.Cancelled, so2.Status);
    }

    [Fact]
    public void ExpectedDate_must_be_on_or_after_order_date() =>
        Assert.Throws<ArgumentException>(() =>
            new SalesOrder("SO-1", 1, 2, new DateTime(2026, 7, 1),
                expectedDate: new DateTime(2026, 6, 1), currency: "IDR", notes: null));

    private static SalesOrder Confirmed()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        return so;
    }

    [Fact]
    public void ApplyDelivery_accumulates_and_tracks_full_delivery()
    {
        var line = Line(); // qty 10
        Assert.Equal(0, line.DeliveredQuantity);
        Assert.False(line.IsFullyDelivered);

        line.ApplyDelivery(4);
        Assert.Equal(4, line.DeliveredQuantity);
        Assert.False(line.IsFullyDelivered);

        line.ApplyDelivery(6);
        Assert.Equal(10, line.DeliveredQuantity);
        Assert.True(line.IsFullyDelivered);
    }

    [Fact]
    public void ApplyDelivery_allows_up_to_ordered_qty_exactly()
    {
        var line = Line(); // qty 10, strict cap = 10
        line.ApplyDelivery(10);
        Assert.Equal(10, line.DeliveredQuantity);
    }

    [Fact]
    public void ApplyDelivery_rejects_over_ordered_qty_strict()
    {
        var line = Line(); // qty 10 — no tolerance
        Assert.Throws<InvalidOperationException>(() => line.ApplyDelivery(11));
    }

    [Fact]
    public void ApplyDelivery_rejects_non_positive()
    {
        var line = Line();
        Assert.Throws<ArgumentException>(() => line.ApplyDelivery(0));
        Assert.Throws<ArgumentException>(() => line.ApplyDelivery(-1));
    }

    [Fact]
    public void CanDeliver_only_when_confirmed_or_partially_delivered()
    {
        Assert.False(Make().CanDeliver);
        var so = Confirmed();
        Assert.True(so.CanDeliver);
        so.MarkPartiallyDelivered();
        Assert.True(so.CanDeliver);
    }

    [Fact]
    public void MarkDelivered_and_partial_require_deliverable_status()
    {
        Assert.Throws<InvalidOperationException>(() => Make().MarkDelivered());
        Assert.Throws<InvalidOperationException>(() => Make().MarkPartiallyDelivered());
        var so = Confirmed();
        so.MarkDelivered();
        Assert.Equal(SalesOrderStatus.Delivered, so.Status);
    }

    [Fact]
    public void Close_only_from_partially_delivered()
    {
        var so = Confirmed();
        Assert.Throws<InvalidOperationException>(() => so.Close()); // Confirmed cannot close
        so.MarkPartiallyDelivered();
        so.Close();
        Assert.Equal(SalesOrderStatus.Closed, so.Status);
    }
}
