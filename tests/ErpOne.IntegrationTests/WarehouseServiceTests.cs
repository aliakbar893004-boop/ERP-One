using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Warehouses;
using Xunit;

namespace ErpOne.IntegrationTests;

public class WarehouseServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public WarehouseServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task SettingSecondDefault_UnsetsFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IWarehouseService>();

        var a = await svc.CreateAsync(new CreateWarehouseRequest("WH-A", "Gudang A", null, true, true));
        var b = await svc.CreateAsync(new CreateWarehouseRequest("WH-B", "Gudang B", null, true, true));

        var def = await svc.GetDefaultAsync();
        Assert.NotNull(def);
        Assert.Equal("WH-B", def!.Code);          // default terbaru menang

        var reloadedA = await svc.GetByIdAsync(a.Id);
        Assert.False(reloadedA!.IsDefault);        // A tidak lagi default
    }
}
