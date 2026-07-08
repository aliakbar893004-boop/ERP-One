using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class DeliveryOrderTests
{
    private static DeliveryOrder Make() =>
        new("DO-202607-0001", salesOrderId: 1, deliveryDate: new DateTime(2026, 7, 1), notes: "  catatan  ");

    private static DeliveryOrderLine Line() => new(salesOrderLineId: 7, productVariantId: 5, quantityDelivered: 3);

    [Fact]
    public void New_do_is_draft_and_trims_notes()
    {
        var d = Make();
        Assert.Equal(DeliveryOrderStatus.Draft, d.Status);
        Assert.Equal("catatan", d.Notes);
        Assert.Equal("DO-202607-0001", d.DoNumber);
    }

    [Fact]
    public void New_line_defaults_unit_cost_to_zero()
    {
        var l = Line();
        Assert.Equal(0m, l.UnitCost);
        Assert.Equal(3, l.QuantityDelivered);
    }

    [Fact]
    public void SetUnitCost_sets_cogs_snapshot()
    {
        var l = Line();
        l.SetUnitCost(800m);
        Assert.Equal(800m, l.UnitCost);
        Assert.Throws<ArgumentException>(() => l.SetUnitCost(-1m));
    }

    [Fact]
    public void Post_requires_lines()
    {
        var d = Make();
        Assert.Throws<InvalidOperationException>(() => d.Post());
        d.SetLines([Line()]);
        d.Post();
        Assert.Equal(DeliveryOrderStatus.Posted, d.Status);
    }

    [Fact]
    public void Cannot_modify_after_post()
    {
        var d = Make();
        d.SetLines([Line()]);
        d.Post();
        Assert.Throws<InvalidOperationException>(() => d.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() => d.UpdateHeader(DateTime.Today, null));
        Assert.Throws<InvalidOperationException>(() => d.Post());
    }

    [Fact]
    public void Line_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(0, 5, 3));
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(7, 0, 3));
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(7, 5, 0));
    }
}
