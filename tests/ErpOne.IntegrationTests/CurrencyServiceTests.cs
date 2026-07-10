using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Currencies;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CurrencyServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CurrencyServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_normalizes_code_and_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        var created = await svc.CreateAsync(new CreateCurrencyRequest("usd", "US Dollar", "$", 2, false, true));
        Assert.Equal("USD", created.Code);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("US Dollar", fetched!.Name);
    }

    [Fact]
    public async Task Setting_new_base_demotes_previous_base()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        // IDR (Id=1) is seeded as base. Create a new base.
        var eur = await svc.CreateAsync(new CreateCurrencyRequest("eur", "Euro", "€", 2, true, true));
        Assert.True(eur.IsBase);

        var idr = await svc.GetByIdAsync(1);
        Assert.False(idr!.IsBase); // previous base demoted
    }

    [Fact]
    public async Task Delete_base_currency_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        // Create our own base currency (order-independent — the shared DB fixture means
        // the seeded IDR may already have been demoted by another test).
        var baseCur = await svc.CreateAsync(new CreateCurrencyRequest("bhd", "Bahraini Dinar", "BD", 3, true, true));
        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(baseCur.Id));
    }
}
