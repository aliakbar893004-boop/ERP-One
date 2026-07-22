using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class FifoCostingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FifoCostingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetFifoAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.Fifo);
        await db.SaveChangesAsync();
    }

    private static async Task InboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty, decimal cost)
    {
        await db.UpsertStockAsync(variantId, whId, qty, default);
        await costing.OnInboundAsync(variantId, whId, qty, cost, default);
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> OutboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty)
    {
        var unit = await costing.GetOutboundUnitCostAsync(variantId, whId, qty, default);
        await db.UpsertStockAsync(variantId, whId, -qty, default);
        await db.SaveChangesAsync();
        return unit;
    }

    private static async Task<decimal> RowCostAsync(AppDbContext db, int variantId, int whId) =>
        await db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId && s.WarehouseId == whId)
            .Select(s => s.CostPrice).SingleAsync();

    private static (Warehouse whA, Warehouse whB, Product product, ProductVariant variant) NewFixtures(string id)
    {
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        return (whA, whB, product, variant);
    }

    [Fact]
    public async Task Outbound_consumes_oldest_layers_first_weighted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, _, product, variant) = NewFixtures(id);
        db.Warehouses.Add(whA); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1200m);

        // Consume 15: 10@1000 + 5@1200 = 16000 / 15 = 1066.666... -> 1066.67
        var unit = await OutboundAsync(db, costing, variant.Id, whA.Id, 15);
        Assert.Equal(1066.67m, unit);

        // Remaining: 5 @ 1200 -> display row cost 1200
        Assert.Equal(1200m, await RowCostAsync(db, variant.Id, whA.Id));

        // Next outbound of 5 -> exactly 1200; then remaining 0
        var unit2 = await OutboundAsync(db, costing, variant.Id, whA.Id, 5);
        Assert.Equal(1200m, unit2);
        Assert.Equal(0m, await RowCostAsync(db, variant.Id, whA.Id));
    }

    [Fact]
    public async Task Layers_are_independent_per_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        Assert.Equal(1400m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
    }

    [Fact]
    public async Task Transfer_moves_a_fifo_layer_into_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m); // A: layer 10@1000, B empty

        // Same call sequence StockTransferService.PostAsync uses: outbound source, then inbound dest.
        var cost = await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 5, default); // consumes A -> 1000
        await db.UpsertStockAsync(variant.Id, whA.Id, -5, default);
        await db.UpsertStockAsync(variant.Id, whB.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, whB.Id, 5, cost, default); // new layer 5@1000 in B
        await db.SaveChangesAsync();

        Assert.Equal(1000m, cost);
        Assert.Equal(1000m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id)); // A still 5@1000
    }

    [Fact]
    public async Task StockLevels_show_per_warehouse_cost_under_fifo()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var stock = scope.ServiceProvider.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        var levels = await stock.GetLevelsByVariantAsync(variant.Id);
        Assert.Equal(1000m, levels.Single(l => l.WarehouseId == whA.Id).CostPrice);
        Assert.Equal(1400m, levels.Single(l => l.WarehouseId == whB.Id).CostPrice);
    }

    [Fact]
    public async Task Full_transfer_service_moves_fifo_cost_to_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var transfers = scope.ServiceProvider.GetRequiredService<ErpOne.Application.StockTransfers.IStockTransferService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        // Two layers in source A: 10@1000 then 10@1200.
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1200m);

        // No approval chain seeded (CustomWebApplicationFactory skips BootstrapSeeder) -> Submit auto-posts.
        var created = await transfers.CreateAsync(new ErpOne.Application.StockTransfers.CreateStockTransferRequest(
            DateTime.Today, whA.Id, whB.Id, null,
            [new ErpOne.Application.StockTransfers.StockTransferLineInput(variant.Id, 15)]));
        await transfers.SubmitAsync(created.Id);

        // Source consumed 10@1000 + 5@1200 = 16000/15 = 1066.67; that weighted cost seeds ONE dest layer of 15.
        Assert.Equal(1066.67m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
        // Source A remaining: 5 @ 1200.
        Assert.Equal(1200m, await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 1, default));
    }
}
