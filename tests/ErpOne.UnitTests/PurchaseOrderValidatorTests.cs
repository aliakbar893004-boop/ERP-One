using FluentValidation.TestHelper;
using ErpOne.Application.PurchaseOrders;
using Xunit;

namespace ErpOne.UnitTests;

public class PurchaseOrderValidatorTests
{
    private readonly CreatePurchaseOrderValidator _v = new();

    private static CreatePurchaseOrderRequest Valid() =>
        new(SupplierId: 1, WarehouseId: 2, OrderDate: new DateTime(2026, 6, 24),
            ExpectedDate: null, Notes: null,
            Lines: [new PurchaseOrderLineRequest(5, 10, 1000m, 0m, null)]);

    [Fact]
    public void Valid_passes() => _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Requires_supplier() =>
        _v.TestValidate(Valid() with { SupplierId = 0 }).ShouldHaveValidationErrorFor(x => x.SupplierId);

    [Fact]
    public void Requires_at_least_one_line() =>
        _v.TestValidate(Valid() with { Lines = [] }).ShouldHaveValidationErrorFor(x => x.Lines);

    [Fact]
    public void Line_quantity_must_be_positive() =>
        _v.TestValidate(Valid() with { Lines = [new PurchaseOrderLineRequest(5, 0, 1000m, 0m, null)] })
          .ShouldHaveValidationErrorFor("Lines[0].Quantity");

    [Fact]
    public void Expected_before_order_fails() =>
        _v.TestValidate(Valid() with { ExpectedDate = new DateTime(2026, 6, 1) })
          .ShouldHaveValidationErrorFor(x => x.ExpectedDate);
}
