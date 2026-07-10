using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CompanySettings;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CompanySettingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CompanySettingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Get_returns_seeded_single_row()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompanySettingService>();

        var s = await svc.GetAsync();
        Assert.Equal(1, s.Id);
    }

    [Fact]
    public async Task Update_then_Get_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompanySettingService>();

        await svc.UpdateAsync(new UpdateCompanySettingRequest(
            "Toko Maju", "Jl. Merdeka 1", "021-555", "hi@maju.co", "01.234.567.8-000",
            null, "Selamat datang", "Terima kasih"));

        var s = await svc.GetAsync();
        Assert.Equal("Toko Maju", s.CompanyName);
        Assert.Equal("Terima kasih", s.ReceiptFooter);
    }
}
