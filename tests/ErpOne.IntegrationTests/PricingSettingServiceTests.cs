using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Pricing;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingSettingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingSettingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Update_then_read_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPricingSettingService>();

        await svc.UpdateAsync(12.5m);
        Assert.Equal(12.5m, (await svc.GetAsync()).DefaultMaxDiscountPercent);

        // Kembalikan ke 100 agar test lain (fixture berbagi) tidak terpengaruh.
        await svc.UpdateAsync(100m);
    }

    [Fact]
    public async Task Boundary_values_are_accepted()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPricingSettingService>();

        await svc.UpdateAsync(0m);
        Assert.Equal(0m, (await svc.GetAsync()).DefaultMaxDiscountPercent);

        await svc.UpdateAsync(100m);
        Assert.Equal(100m, (await svc.GetAsync()).DefaultMaxDiscountPercent);
    }

    [Fact]
    public async Task Percent_outside_range_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPricingSettingService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(-1m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(101m));
    }
}
