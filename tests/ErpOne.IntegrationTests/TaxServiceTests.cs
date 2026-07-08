using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Taxes;
using Xunit;

namespace ErpOne.IntegrationTests;

public class TaxServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public TaxServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_PersistsRate()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxService>();
        var created = await svc.CreateAsync(new CreateTaxRequest("PPN", "PPN 11%", 11m, false, null));
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(11m, fetched!.Rate);
        Assert.False(fetched.IsInclusive);
    }
}
