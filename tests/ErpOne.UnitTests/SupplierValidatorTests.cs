using ErpOne.Application.Suppliers;
using Xunit;

namespace ErpOne.UnitTests;

public class SupplierValidatorTests
{
    private readonly CreateSupplierValidator _v = new();

    private static CreateSupplierRequest Valid() =>
        new("SUP-1", "PT Sumber", "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", 30, "IDR", "BCA", "123", "PT SM", true);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSupplierRequest.Code));
    }

    [Fact]
    public void Bad_code_chars_fail()
    {
        var result = _v.Validate(Valid() with { Code = "A B" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSupplierRequest.Code));
    }

    [Fact]
    public void Bad_email_fails()
    {
        var result = _v.Validate(Valid() with { Email = "not-an-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSupplierRequest.Email));
    }

    [Fact]
    public void Negative_term_fails()
    {
        var result = _v.Validate(Valid() with { PaymentTermDays = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSupplierRequest.PaymentTermDays));
    }
}
