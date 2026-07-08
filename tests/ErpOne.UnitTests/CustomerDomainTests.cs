using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CustomerDomainTests
{
    private static Customer Make(string code = "cust-1", int term = 14, decimal limit = 1000m) =>
        new(code, "Toko Jaya", "Sari", "0813", "c@d.com", "Jl. Melati",
            "02.345", term, "idr", limit, true);

    [Fact]
    public void Ctor_normalizes_code_and_currency()
    {
        var c = Make();
        Assert.Equal("CUST-1", c.Code);
        Assert.Equal("IDR", c.DefaultCurrency);
    }

    [Fact]
    public void Ctor_rejects_negative_credit_limit()
    {
        Assert.Throws<ArgumentException>(() => Make(limit: -1m));
    }

    [Fact]
    public void Ctor_rejects_negative_payment_term()
    {
        Assert.Throws<ArgumentException>(() => Make(term: -5));
    }

    [Fact]
    public void Update_changes_fields()
    {
        var c = Make();
        c.Update("CUST-2", "Toko Baru", null, null, null, null, null, 30, "USD", 0m, false);
        Assert.Equal("CUST-2", c.Code);
        Assert.Equal(0m, c.CreditLimit);
        Assert.False(c.IsActive);
    }
}
