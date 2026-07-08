using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class SupplierDomainTests
{
    private static Supplier Make(string code = "sup-1", int term = 30, string? currency = "idr") =>
        new(code, "PT Sumber Makmur", "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", term, currency, "BCA", "123", "PT SM", true);

    [Fact]
    public void Ctor_normalizes_code_and_currency()
    {
        var s = Make(code: "sup-1", currency: "idr");
        Assert.Equal("SUP-1", s.Code);
        Assert.Equal("IDR", s.DefaultCurrency);
    }

    [Fact]
    public void Ctor_blank_currency_defaults_to_IDR()
    {
        var s = Make(currency: "  ");
        Assert.Equal("IDR", s.DefaultCurrency);
    }

    [Fact]
    public void Ctor_requires_code()
    {
        Assert.Throws<ArgumentException>(() => Make(code: "  "));
    }

    [Fact]
    public void Ctor_rejects_negative_payment_term()
    {
        Assert.Throws<ArgumentException>(() => Make(term: -1));
    }

    [Fact]
    public void Update_changes_fields()
    {
        var s = Make();
        s.Update("SUP-2", "PT Baru", null, null, null, null, null, 0, "USD", null, null, null, false);
        Assert.Equal("SUP-2", s.Code);
        Assert.Equal("PT Baru", s.Name);
        Assert.Null(s.ContactPerson);
        Assert.Equal(0, s.PaymentTermDays);
        Assert.Equal("USD", s.DefaultCurrency);
        Assert.False(s.IsActive);
    }
}
