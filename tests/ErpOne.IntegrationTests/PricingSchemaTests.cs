using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingSchemaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingSchemaTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public void Pricing_tables_use_master_prefix()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("M_PriceLists", db.Model.FindEntityType(typeof(PriceList))!.GetTableName());
        Assert.Equal("M_PriceListLines", db.Model.FindEntityType(typeof(PriceListLine))!.GetTableName());
        Assert.Equal("M_PricingSettings", db.Model.FindEntityType(typeof(PricingSetting))!.GetTableName());
    }

    [Fact]
    public async Task PricingSetting_seed_row_exists_with_hundred_percent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.PricingSettings.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Id);
        Assert.Equal(100m, row.DefaultMaxDiscountPercent);
    }

    [Fact]
    public async Task Duplicate_tier_for_same_variant_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product("SCHEMA-P1", "Schema Probe", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant("SCHEMA-SKU-1", null, 100_000m, null, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var list = new PriceList("SCHEMA-DUP", "Schema Dup", null, true);
        list.SetLines([
            new PriceListLine(variant.Id, 1, 90_000m),
            new PriceListLine(variant.Id, 1, 80_000m),
        ]);
        db.PriceLists.Add(list);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
