using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ProductVariantMigrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ProductVariantMigrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Schema_PersistsProductWithVariantAndAttributeLinks()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new ProductCategory("MIG", "Migration Cat", null);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product("MIG/0001", "Shirt", null, category.Id, null, null, null, ProductStatus.Aktif);
        product.AddVariant("MIG/0001-M", null, 100m, null, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var loaded = await db.Products
            .Include(p => p.Variants).ThenInclude(v => v.Attributes)
            .AsNoTracking()
            .FirstAsync(p => p.Id == product.Id);

        Assert.Single(loaded.Variants);
        Assert.Equal("MIG/0001-M", loaded.Variants[0].Sku);
        Assert.Equal("MIG/0001", loaded.Code);
    }
}
