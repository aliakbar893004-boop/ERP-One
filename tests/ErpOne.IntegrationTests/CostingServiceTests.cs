using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CostingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CostingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task OnInbound_two_receipts_same_variant_matches_manual_moving_average()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();

        // Fresh product/variant with opening cost basis 1000 + 100 on-hand in warehouse 1.
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 1000m, null, null, true);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductStocks.Add(new ProductStock(variant.Id, wh.Id, 100)); // basis: 100 @ 1000
        await db.SaveChangesAsync();

        // Two receipts of the same variant in one unit-of-work (no flush between).
        await db.UpsertStockAsync(variant.Id, wh.Id, 50, default);
        await costing.OnInboundAsync(variant.Id, wh.Id, 50, 1300m, default);  // (100*1000 + 50*1300)/150 = 1100.00
        await db.UpsertStockAsync(variant.Id, wh.Id, 60, default);
        await costing.OnInboundAsync(variant.Id, wh.Id, 60, 1250m, default);  // (150*1100 + 60*1250)/210 = 1142.857 -> 1142.86

        await db.SaveChangesAsync();

        var costPrice = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id)
            .Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1142.86m, costPrice);

        var outbound = await costing.GetOutboundUnitCostAsync(variant.Id, wh.Id, 10, default);
        Assert.Equal(1142.86m, outbound);
    }
}
