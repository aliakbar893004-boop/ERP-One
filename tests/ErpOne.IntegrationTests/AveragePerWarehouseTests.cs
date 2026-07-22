using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AveragePerWarehouseTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AveragePerWarehouseTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetPerWarehouseAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.AveragePerWarehouse);
        await db.SaveChangesAsync();
    }

    private static async Task InboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty, decimal cost)
    {
        await db.UpsertStockAsync(variantId, whId, qty, default);
        await costing.OnInboundAsync(variantId, whId, qty, cost, default);
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> RowCostAsync(AppDbContext db, int variantId, int whId) =>
        await db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId && s.WarehouseId == whId)
            .Select(s => s.CostPrice).SingleAsync();

    [Fact]
    public async Task Per_warehouse_costs_are_independent_and_headline_is_weighted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id));
        Assert.Equal(1400m, await RowCostAsync(db, variant.Id, whB.Id));

        var headline = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id).Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1200m, headline); // (10*1000 + 10*1400)/20

        Assert.Equal(1000m, await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 1, default));
        Assert.Equal(1400m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
    }

    [Fact]
    public async Task Moving_average_within_a_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"W{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, wh.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, wh.Id, 10, 1200m);

        Assert.Equal(1100m, await RowCostAsync(db, variant.Id, wh.Id)); // (10*1000 + 10*1200)/20
    }

    [Fact]
    public async Task Transfer_moves_cost_into_destination_average()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m); // A: 10 @ 1000, B empty

        // Simulate a transfer of 5 A->B the way StockTransferService.PostAsync does it.
        var cost = await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 5, default); // = 1000
        await db.UpsertStockAsync(variant.Id, whA.Id, -5, default);
        await db.UpsertStockAsync(variant.Id, whB.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, whB.Id, 5, cost, default);
        await db.SaveChangesAsync();

        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id)); // source unchanged
        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whB.Id)); // dest now 1000
    }

    [Fact]
    public async Task StockLevels_show_per_warehouse_cost()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var stock = scope.ServiceProvider.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        var levels = await stock.GetLevelsByVariantAsync(variant.Id);
        Assert.Equal(1000m, levels.Single(l => l.WarehouseId == whA.Id).CostPrice);
        Assert.Equal(1400m, levels.Single(l => l.WarehouseId == whB.Id).CostPrice);
    }
}
