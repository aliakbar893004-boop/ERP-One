using FluentValidation.TestHelper;
using ErpOne.Application.SalesOrders;
using Xunit;

namespace ErpOne.UnitTests;

public class SalesOrderValidatorTests
{
    private readonly CreateSalesOrderValidator _v = new();

    private static CreateSalesOrderRequest Valid() =>
        new(CustomerId: 1, WarehouseId: 2, OrderDate: new DateTime(2026, 7, 1),
            ExpectedDate: null, Notes: null,
            Lines: [new SalesOrderLineRequest(5, 10, 1000m, 0m, null)]);

    [Fact]
    public void Valid_passes() => _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Requires_customer() =>
        _v.TestValidate(Valid() with { CustomerId = 0 }).ShouldHaveValidationErrorFor(x => x.CustomerId);

    [Fact]
    public void Requires_warehouse() =>
        _v.TestValidate(Valid() with { WarehouseId = 0 }).ShouldHaveValidationErrorFor(x => x.WarehouseId);

    [Fact]
    public void Requires_at_least_one_line() =>
        _v.TestValidate(Valid() with { Lines = [] }).ShouldHaveValidationErrorFor(x => x.Lines);

    [Fact]
    public void Line_quantity_must_be_positive() =>
        _v.TestValidate(Valid() with { Lines = [new SalesOrderLineRequest(5, 0, 1000m, 0m, null)] })
          .ShouldHaveValidationErrorFor("Lines[0].Quantity");

    [Fact]
    public void Expected_before_order_fails() =>
        _v.TestValidate(Valid() with { ExpectedDate = new DateTime(2026, 6, 1) })
          .ShouldHaveValidationErrorFor(x => x.ExpectedDate);
}
