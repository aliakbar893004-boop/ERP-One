using ErpOne.Application.CashierShifts;
using Xunit;

namespace ErpOne.UnitTests;

public class CashierShiftValidatorTests
{
    [Fact]
    public void Open_requires_warehouse_and_nonnegative_float()
    {
        var v = new OpenShiftRequestValidator();
        Assert.False(v.Validate(new OpenShiftRequest(0, 100m)).IsValid);
        Assert.False(v.Validate(new OpenShiftRequest(3, -1m)).IsValid);
        Assert.True(v.Validate(new OpenShiftRequest(3, 0m)).IsValid);
    }

    [Fact]
    public void Close_requires_nonnegative_cash_and_note_length()
    {
        var v = new CloseShiftRequestValidator();
        Assert.False(v.Validate(new CloseShiftRequest(-1m, null)).IsValid);
        Assert.False(v.Validate(new CloseShiftRequest(0m, new string('x', 501))).IsValid);
        Assert.True(v.Validate(new CloseShiftRequest(100m, "ok")).IsValid);
    }
}
