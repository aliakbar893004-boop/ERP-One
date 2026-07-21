using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CostingSettingTests
{
    [Fact]
    public void SetMethod_updates_the_method()
    {
        var setting = new CostingSetting();
        setting.SetMethod(CostingMethod.StandardCost);
        Assert.Equal(CostingMethod.StandardCost, setting.Method);
    }

    [Fact]
    public void Default_method_is_moving_average()
    {
        Assert.Equal(CostingMethod.MovingAverage, new CostingSetting().Method);
    }
}
