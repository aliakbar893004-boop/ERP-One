using ErpOne.Application.PosSales;
using Xunit;

namespace ErpOne.UnitTests;

public class PosSaleValidatorTests
{
    [Fact]
    public void Rejects_empty_cart_and_bad_payment()
    {
        var v = new CreatePosSaleValidator();
        Assert.False(v.Validate(new CreatePosSaleRequest(0, null, 0m, 0m, [new PosSaleLineRequest(5, 1, 100m, 0m)])).IsValid);
        Assert.False(v.Validate(new CreatePosSaleRequest(1, null, 0m, 0m, [])).IsValid);
        Assert.True(v.Validate(new CreatePosSaleRequest(1, null, 0m, 0m, [new PosSaleLineRequest(5, 1, 100m, 0m)])).IsValid);
    }

    [Fact]
    public void Rejects_bad_line()
    {
        var v = new CreatePosSaleValidator();
        Assert.False(v.Validate(new CreatePosSaleRequest(1, null, 0m, 0m, [new PosSaleLineRequest(5, 0, 100m, 0m)])).IsValid);
        Assert.False(v.Validate(new CreatePosSaleRequest(1, null, 0m, 0m, [new PosSaleLineRequest(5, 1, 100m, 150m)])).IsValid);
    }
}
