using FluentValidation.TestHelper;
using ErpOne.Application.GoodsReceipts;
using Xunit;

namespace ErpOne.UnitTests;

public class GoodsReceiptValidatorTests
{
    private static GoodsReceiptLineRequest Line() => new(PurchaseOrderLineId: 7, QuantityReceived: 3, UnitCost: 900m);

    [Fact]
    public void Create_requires_po_date_and_lines()
    {
        var v = new CreateGoodsReceiptValidator();
        var bad = new CreateGoodsReceiptRequest(0, default, null, []);
        var r = v.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.PurchaseOrderId);
        r.ShouldHaveValidationErrorFor(x => x.ReceiptDate);
        r.ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Create_valid_passes()
    {
        var v = new CreateGoodsReceiptValidator();
        var ok = new CreateGoodsReceiptRequest(1, new DateTime(2026, 6, 29), "ok", [Line()]);
        v.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_requires_date_and_lines()
    {
        var v = new UpdateGoodsReceiptValidator();
        var bad = new UpdateGoodsReceiptRequest(default, null, []);
        var r = v.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.ReceiptDate);
        r.ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Update_valid_passes()
    {
        var v = new UpdateGoodsReceiptValidator();
        var ok = new UpdateGoodsReceiptRequest(new DateTime(2026, 6, 29), null, [Line()]);
        v.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Line_rejects_bad_values()
    {
        var v = new GoodsReceiptLineRequestValidator();
        v.TestValidate(new GoodsReceiptLineRequest(0, 0, -1m)).ShouldHaveValidationErrorFor(x => x.PurchaseOrderLineId);
        v.TestValidate(new GoodsReceiptLineRequest(7, 0, 900m)).ShouldHaveValidationErrorFor(x => x.QuantityReceived);
        v.TestValidate(new GoodsReceiptLineRequest(7, 3, -1m)).ShouldHaveValidationErrorFor(x => x.UnitCost);
    }
}
