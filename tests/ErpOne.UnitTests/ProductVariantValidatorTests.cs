using ErpOne.Application.Products;
using Xunit;

namespace ErpOne.UnitTests;

public class ProductVariantValidatorTests
{
    private static VariantInput Input(decimal? pct) =>
        new(null, 100m, 90m, 40m, null, null, 0, true, [], pct);

    [Fact]
    public void Accepts_null_and_in_range_percent()
    {
        var v = new VariantInputValidator();
        Assert.True(v.Validate(Input(null)).IsValid);
        Assert.True(v.Validate(Input(0m)).IsValid);
        Assert.True(v.Validate(Input(100m)).IsValid);
    }

    [Fact]
    public void Rejects_out_of_range_percent()
    {
        var v = new VariantInputValidator();
        Assert.False(v.Validate(Input(-1m)).IsValid);
        Assert.False(v.Validate(Input(101m)).IsValid);
    }
}
