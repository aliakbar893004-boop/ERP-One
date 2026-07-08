using FluentValidation.TestHelper;
using ErpOne.Application.DeliveryOrders;
using Xunit;

namespace ErpOne.UnitTests;

public class DeliveryOrderValidatorTests
{
    private static DeliveryOrderLineRequest Line() => new(SalesOrderLineId: 7, QuantityDelivered: 3);

    [Fact]
    public void Create_requires_so_date_and_lines()
    {
        var v = new CreateDeliveryOrderValidator();
        var bad = new CreateDeliveryOrderRequest(0, default, null, []);
        var r = v.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.SalesOrderId);
        r.ShouldHaveValidationErrorFor(x => x.DeliveryDate);
        r.ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Create_valid_passes()
    {
        var v = new CreateDeliveryOrderValidator();
        var ok = new CreateDeliveryOrderRequest(1, new DateTime(2026, 7, 1), "ok", [Line()]);
        v.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Line_rejects_bad_values()
    {
        var v = new DeliveryOrderLineRequestValidator();
        v.TestValidate(new DeliveryOrderLineRequest(0, 3)).ShouldHaveValidationErrorFor(x => x.SalesOrderLineId);
        v.TestValidate(new DeliveryOrderLineRequest(7, 0)).ShouldHaveValidationErrorFor(x => x.QuantityDelivered);
    }
}
