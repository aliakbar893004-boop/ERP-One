using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CostingSettingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CostingSettingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static ICostingSettingService Svc(IServiceScope s) =>
        s.ServiceProvider.GetRequiredService<ICostingSettingService>();

    [Fact]
    public async Task GetMethodAsync_defaults_to_moving_average()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(CostingMethod.MovingAverage, await Svc(scope).GetMethodAsync());
    }

    [Fact]
    public async Task UpdateMethodAsync_rejects_unsupported_method()
    {
        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Svc(scope).UpdateMethodAsync(CostingMethod.Fifo));
        Assert.Contains("belum didukung", ex.Message);
    }

    [Fact]
    public async Task UpdateMethodAsync_rejected_once_stock_movement_exists()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.StockMovements.Add(new StockMovement(variant.Id, wh.Id, MovementType.In, 1, 800m, DateTime.UtcNow, refType: "Test"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Svc(scope).UpdateMethodAsync(CostingMethod.MovingAverage));
        Assert.Contains("terkunci", ex.Message);
    }

    [Fact]
    public async Task UpdateMethodAsync_accepts_standard_cost_when_unlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Order-independent: ensure unlocked (another test in this class may have added movements).
        db.StockMovements.RemoveRange(db.StockMovements);
        await db.SaveChangesAsync();

        await Svc(scope).UpdateMethodAsync(CostingMethod.StandardCost);
        Assert.Equal(CostingMethod.StandardCost, await Svc(scope).GetMethodAsync());

        // Restore default so sibling tests in this shared-DB class stay order-independent.
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.MovingAverage);
        await db.SaveChangesAsync();
    }
}
