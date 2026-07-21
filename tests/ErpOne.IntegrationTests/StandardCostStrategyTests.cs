using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StandardCostStrategyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StandardCostStrategyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetStandardAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.StandardCost);   // bypass lock: direct entity mutation for test setup
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Standard_inbound_is_noop_and_outbound_returns_standard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetStandardAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 1000m, null, null, true); // standard = 1000
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductStocks.Add(new ProductStock(variant.Id, wh.Id, 10));
        await db.SaveChangesAsync();

        // Inbound at a different actual cost must NOT change the standard.
        await db.UpsertStockAsync(variant.Id, wh.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, wh.Id, 5, 1300m, default);
        await db.SaveChangesAsync();

        var costPrice = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id)
            .Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1000m, costPrice); // unchanged

        var outbound = await costing.GetOutboundUnitCostAsync(variant.Id, wh.Id, 3, default);
        Assert.Equal(1000m, outbound);
    }
}
