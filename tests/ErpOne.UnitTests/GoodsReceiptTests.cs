using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class GoodsReceiptTests
{
    private static GoodsReceipt Make() =>
        new("GRN-202606-0001", purchaseOrderId: 1, receiptDate: new DateTime(2026, 6, 29), notes: "  catatan  ");

    private static GoodsReceiptLine Line() => new(purchaseOrderLineId: 7, productVariantId: 5, quantityReceived: 3, unitCost: 900m);

    [Fact]
    public void New_grn_is_draft_and_trims_notes()
    {
        var grn = Make();
        Assert.Equal(GoodsReceiptStatus.Draft, grn.Status);
        Assert.Equal("catatan", grn.Notes);
        Assert.Equal("GRN-202606-0001", grn.GrnNumber);
    }

    [Fact]
    public void Post_requires_lines()
    {
        var grn = Make();
        Assert.Throws<InvalidOperationException>(() => grn.Post());
        grn.SetLines([Line()]);
        grn.Post();
        Assert.Equal(GoodsReceiptStatus.Posted, grn.Status);
    }

    [Fact]
    public void Cannot_modify_after_post()
    {
        var grn = Make();
        grn.SetLines([Line()]);
        grn.Post();
        Assert.Throws<InvalidOperationException>(() => grn.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() => grn.UpdateHeader(DateTime.Today, null));
        Assert.Throws<InvalidOperationException>(() => grn.Post());
    }

    [Fact]
    public void Line_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(0, 5, 3, 900m));
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(7, 5, 0, 900m));
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(7, 5, 3, -1m));
    }
}
