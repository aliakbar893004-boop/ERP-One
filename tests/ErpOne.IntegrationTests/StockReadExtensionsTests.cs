using Microsoft.Extensions.DependencyInjection;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockReadExtensionsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockReadExtensionsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Counts_unflushed_local_rows_without_double_counting_db_rows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Fresh product/variant + two warehouses so this test is isolated from other data.
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh1 = new Warehouse($"W1{id}", $"GD1 {id}", null, true, false);
        var wh2 = new Warehouse($"W2{id}", $"GD2 {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        db.Warehouses.AddRange(wh1, wh2);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Persisted (flushed): (variant, wh1) = 10.
        db.ProductStocks.Add(new ProductStock(variant.Id, wh1.Id, 10));
        await db.SaveChangesAsync();

        // Unflushed upserts: wh1 -> 15 (loads DB row into Local), wh2 -> new local row = 7.
        await db.UpsertStockAsync(variant.Id, wh1.Id, 5, default);
        await db.UpsertStockAsync(variant.Id, wh2.Id, 7, default);

        var total = await db.TotalOnHandLocalAwareAsync(variant.Id, default);

        // Correct = 15 (tracked wh1) + 7 (tracked wh2) = 22. Naive dbSum+localSum would be 10+15+7 = 32.
        Assert.Equal(22, total);
    }
}
