using ErpOne.Application.Customers;
using Xunit;

namespace ErpOne.UnitTests;

public class CustomerValidatorTests
{
    private readonly CreateCustomerValidator _v = new();

    private static CreateCustomerRequest Valid() =>
        new("CUST-1", "Toko Jaya", "Sari", "0813", "c@d.com", "Jl. Melati",
            "02.345", 14, "IDR", 1000m, true);

    [Fact]
    public void Valid_request_passes()
    {
        var result = _v.Validate(Valid());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Blank_code_fails()
    {
        var result = _v.Validate(Valid() with { Code = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerRequest.Code));
    }

    [Fact]
    public void Negative_credit_limit_fails()
    {
        var result = _v.Validate(Valid() with { CreditLimit = -1m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerRequest.CreditLimit));
    }

    [Fact]
    public void Bad_email_fails()
    {
        var result = _v.Validate(Valid() with { Email = "nope" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerRequest.Email));
    }
}
