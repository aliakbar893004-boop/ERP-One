using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class SalesReturnTests
{
    private static SalesReturnLine DoLine(int qty, decimal cost) =>
        new(deliveryOrderLineId: 1, customerInvoiceLineId: null, productVariantId: 1, warehouseId: 1,
            variantSku: "SKU", productName: "P", quantity: qty, unitCost: cost,
            unitPrice: cost, discountPercent: 0m, taxRateSnapshot: 0m);

    private static SalesReturn NewDoReturn() =>
        new("CN-1", 1, SalesReturnSource.DeliveryOrder, deliveryOrderId: 10, customerInvoiceId: null,
            new DateTime(2026, 1, 5), null);

    [Fact]
    public void SetLines_recomputes_totals_including_inventory()
    {
        var r = NewDoReturn();
        r.SetLines([DoLine(10, 100m)]);
        Assert.Equal(1000m, r.Subtotal);
        Assert.Equal(0m, r.DiscountTotal);
        Assert.Equal(0m, r.TaxTotal);
        Assert.Equal(1000m, r.GrandTotal);
        Assert.Equal(1000m, r.InventoryTotal);
        Assert.Single(r.Lines);
    }

    [Fact]
    public void Invoice_line_recompute_applies_discount_and_tax()
    {
        var line = new SalesReturnLine(1, 5, 1, 1, "SKU", "P", quantity: 10,
            unitCost: 100m, unitPrice: 120m, discountPercent: 10m, taxRateSnapshot: 11m);
        Assert.Equal(1200m, line.LineSubtotal);       // 10 × 120
        Assert.Equal(120m, line.LineDiscount);        // 10%
        Assert.Equal(118.80m, line.LineTax);          // (1200-120) × 11%
        Assert.Equal(1198.80m, line.LineTotal);       // 1200 - 120 + 118.80
    }

    [Fact]
    public void RecomputeInventoryTotal_uses_updated_line_costs()
    {
        var r = NewDoReturn();
        r.SetLines([DoLine(10, 100m)]);
        foreach (var l in r.Lines) l.SetUnitCost(90m);
        r.RecomputeInventoryTotal();
        Assert.Equal(900m, r.InventoryTotal);
        Assert.Equal(1000m, r.GrandTotal); // billed total unchanged
    }

    [Fact]
    public void Submit_requires_lines_then_moves_to_pending()
    {
        var r = NewDoReturn();
        Assert.Throws<InvalidOperationException>(() => r.Submit());
        r.SetLines([DoLine(1, 100m)]);
        r.Submit();
        Assert.Equal(SalesReturnStatus.PendingApproval, r.Status);
    }

    [Fact]
    public void Lifecycle_post_and_return_to_draft()
    {
        var r = NewDoReturn();
        r.SetLines([DoLine(1, 100m)]);
        r.Submit();
        r.MarkPosted();
        Assert.Equal(SalesReturnStatus.Posted, r.Status);

        var r2 = NewDoReturn();
        r2.SetLines([DoLine(1, 100m)]);
        r2.Submit();
        r2.ReturnToDraft("wrong qty");
        Assert.Equal(SalesReturnStatus.Draft, r2.Status);
        Assert.Equal("wrong qty", r2.RejectionNote);
    }

    [Fact]
    public void SetLines_and_UpdateHeader_blocked_when_not_draft()
    {
        var r = NewDoReturn();
        r.SetLines([DoLine(1, 100m)]);
        r.Submit();
        Assert.Throws<InvalidOperationException>(() => r.SetLines([DoLine(1, 100m)]));
        Assert.Throws<InvalidOperationException>(() => r.UpdateHeader(new DateTime(2026, 2, 1), "x"));
    }
}
