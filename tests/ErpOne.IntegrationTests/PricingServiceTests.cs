using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Pricing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static readonly DateOnly Today = new(2026, 7, 27);

    /// <summary>Varian baru dengan harga master tertentu. Kode unik per pemanggil agar test independen.</summary>
    private static async Task<int> NewVariantAsync(AppDbContext db, string suffix, decimal price,
        decimal? discountPrice = null)
    {
        var product = new Product($"PRC-P-{suffix}", $"Pricing Probe {suffix}", null, null, null, null, null,
            ProductStatus.Aktif);
        var variant = product.AddVariant($"PRC-SKU-{suffix}", null, price, discountPrice, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private static async Task<int> NewPriceListAsync(AppDbContext db, string code, bool isActive,
        int variantId, params (int MinQty, decimal Price)[] tiers)
    {
        var list = new PriceList(code, code, null, isActive);
        list.SetLines(tiers.Select(t => new PriceListLine(variantId, t.MinQty, t.Price)));
        db.PriceLists.Add(list);
        await db.SaveChangesAsync();
        return list.Id;
    }

    [Fact]
    public async Task Resolves_tier_by_quantity()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "TIER", 100_000m);
        var listId = await NewPriceListAsync(db, "PRC-TIER", true, variantId,
            (1, 90_000m), (10, 85_000m), (50, 78_000m));

        var wh = new Warehouse("PRC-WH-TIER", "Tier WH", null, true, false, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var nine = await pricing.ResolveAsync(new PriceRequest(variantId, 9, null, wh.Id, Today));
        var ten = await pricing.ResolveAsync(new PriceRequest(variantId, 10, null, wh.Id, Today));
        var sixty = await pricing.ResolveAsync(new PriceRequest(variantId, 60, null, wh.Id, Today));

        Assert.Equal(90_000m, nine.UnitPrice);
        Assert.Equal(1, nine.MatchedMinQty);
        Assert.Equal(85_000m, ten.UnitPrice);
        Assert.Equal(78_000m, sixty.UnitPrice);
        Assert.Equal(PriceSource.PriceList, sixty.Source);
        Assert.Equal(100_000m, sixty.ListPrice); // harga master tetap dilaporkan
        Assert.Equal("PRC-TIER", sixty.PriceListName);
    }

    [Fact]
    public async Task Falls_back_to_variant_discount_price_then_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var withDiscount = await NewVariantAsync(db, "FB1", 100_000m, 95_000m);
        var plain = await NewVariantAsync(db, "FB2", 70_000m);

        var a = await pricing.ResolveAsync(new PriceRequest(withDiscount, 1, null, null, Today));
        var b = await pricing.ResolveAsync(new PriceRequest(plain, 1, null, null, Today));

        Assert.Equal(95_000m, a.UnitPrice);
        Assert.Equal(PriceSource.VariantDiscountPrice, a.Source);
        Assert.Equal(70_000m, b.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, b.Source);
    }

    [Fact]
    public async Task Variant_absent_from_price_list_falls_back_to_master_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var listed = await NewVariantAsync(db, "ABS1", 100_000m);
        var unlisted = await NewVariantAsync(db, "ABS2", 60_000m);
        var listId = await NewPriceListAsync(db, "PRC-ABSENT", true, listed, (1, 90_000m));

        var wh = new Warehouse("PRC-WH-ABS", "Absent WH", null, true, false, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(unlisted, 5, null, wh.Id, Today));

        Assert.Equal(60_000m, result.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, result.Source);
        Assert.Null(result.PriceListId);
    }

    [Fact]
    public async Task Customer_price_list_wins_over_warehouse_default()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "PRIO", 100_000m);
        var retailId = await NewPriceListAsync(db, "PRC-RETAIL", true, variantId, (1, 95_000m));
        var grosirId = await NewPriceListAsync(db, "PRC-GROSIR", true, variantId, (1, 80_000m));

        var wh = new Warehouse("PRC-WH-PRIO", "Prio WH", null, true, false, retailId);
        db.Warehouses.Add(wh);
        var cust = new Customer("PRC-C-PRIO", "Prio Customer", null, null, null, null, null, 0, "IDR", 0m, true, grosirId);
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(variantId, 1, cust.Id, wh.Id, Today));

        Assert.Equal(80_000m, result.UnitPrice);
        Assert.Equal(grosirId, result.PriceListId);
    }

    [Fact]
    public async Task Inactive_price_list_is_ignored_without_error()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "INACT", 100_000m);
        var listId = await NewPriceListAsync(db, "PRC-INACTIVE", false, variantId, (1, 50_000m));

        var wh = new Warehouse("PRC-WH-INACT", "Inactive WH", null, true, false, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(variantId, 1, null, wh.Id, Today));

        Assert.Equal(100_000m, result.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, result.Source);
    }

    [Fact]
    public async Task ResolveMany_returns_results_in_request_order()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var a = await NewVariantAsync(db, "MANY-A", 10_000m);
        var b = await NewVariantAsync(db, "MANY-B", 20_000m);
        var c = await NewVariantAsync(db, "MANY-C", 30_000m);

        var results = await pricing.ResolveManyAsync(
        [
            new PriceRequest(c, 1, null, null, Today),
            new PriceRequest(a, 1, null, null, Today),
            new PriceRequest(b, 1, null, null, Today),
        ]);

        Assert.Equal([30_000m, 10_000m, 20_000m], results.Select(r => r.UnitPrice));
    }

    [Fact]
    public async Task Max_discount_falls_back_to_global_default_when_no_roles()
    {
        using var scope = _factory.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync(null));
        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync([]));
    }

    [Fact]
    public async Task Max_discount_takes_largest_across_roles()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        db.Roles.Add(new ApplicationRole("PRC-Cashier")
        { NormalizedName = "PRC-CASHIER", MaxDiscountPercent = 5m });
        db.Roles.Add(new ApplicationRole("PRC-Supervisor")
        { NormalizedName = "PRC-SUPERVISOR", MaxDiscountPercent = 20m });
        db.Roles.Add(new ApplicationRole("PRC-Unset")
        { NormalizedName = "PRC-UNSET" });
        await db.SaveChangesAsync();

        Assert.Equal(20m, await pricing.GetMaxDiscountPercentAsync(["PRC-Cashier", "PRC-Supervisor"]));
        Assert.Equal(5m, await pricing.GetMaxDiscountPercentAsync(["PRC-Cashier", "PRC-Unset"]));
        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync(["PRC-Unset"]));
    }
}
