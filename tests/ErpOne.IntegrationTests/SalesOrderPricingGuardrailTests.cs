using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PriceLists;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesOrderPricingGuardrailTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesOrderPricingGuardrailTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const string TightRole = "SOG-Tight";   // 5%
    private const string LooseRole = "SOG-Loose";   // 40%

    /// <summary>Customer dengan price list harga dasar 90.000 untuk varian berharga master 100.000.</summary>
    private static async Task<(int variantId, int customerId, int warehouseId, string sku)> SeedAsync(
        AppDbContext db, IPriceListService priceLists, string suffix)
    {
        var sku = $"SOG-SKU-{suffix}";
        var product = new Product($"SOG-P-{suffix}", $"SOG Probe {suffix}", null, null, null, null, null,
            ProductStatus.Aktif);
        var variant = product.AddVariant(sku, null, 100_000m, null, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest($"SOG-PL-{suffix}", "SOG List", null, true,
            [new PriceListLineRequest(variant.Id, 1, 90_000m)]));

        var customer = new Customer($"SOG-C-{suffix}", "SOG Customer", null, null, null, null, null, 30, "IDR",
            0m, true, list.Id);
        db.Customers.Add(customer);
        var warehouse = new Warehouse($"SOG-WH-{suffix}", "SOG WH", null, true, false);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        if (!await db.Roles.AnyAsync(r => r.Name == TightRole))
        {
            db.Roles.Add(new ApplicationRole(TightRole)
            { NormalizedName = TightRole.ToUpperInvariant(), MaxDiscountPercent = 5m });
            db.Roles.Add(new ApplicationRole(LooseRole)
            { NormalizedName = LooseRole.ToUpperInvariant(), MaxDiscountPercent = 40m });
            await db.SaveChangesAsync();
        }

        return (variant.Id, customer.Id, warehouse.Id, sku);
    }

    private static CreateSalesOrderRequest Request(int customerId, int warehouseId, int variantId,
        decimal unitPrice, decimal discountPercent) =>
        new(customerId, warehouseId, new DateTime(2026, 7, 27), null, null,
            [new SalesOrderLineRequest(variantId, 1, unitPrice, discountPercent, null)]);

    [Fact]
    public async Task Discount_within_role_limit_is_accepted_and_keeps_negotiated_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, _) = await SeedAsync(db, priceLists, "OK");

        // Harga engine 90.000; kirim 85.000 = menyimpang 5,56% -> di dalam batas 40%
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 85_000m, 0m), [LooseRole]);

        Assert.Equal(85_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Deviation_above_role_limit_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, sku) = await SeedAsync(db, priceLists, "REJECT");

        // Harga engine 90.000; kirim 90.000 dengan diskon 25% -> menyimpang 25% > batas 5%
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            so.CreateAsync(Request(customerId, warehouseId, variantId, 90_000m, 25m), [TightRole]));

        Assert.Contains(sku, ex.Message);
    }

    [Fact]
    public async Task Price_override_alone_can_breach_the_limit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, _) = await SeedAsync(db, priceLists, "OVERRIDE");

        // Tanpa diskon %, tapi harga diturunkan dari 90.000 ke 60.000 -> menyimpang 33,33% > 5%
        await Assert.ThrowsAsync<ValidationException>(() =>
            so.CreateAsync(Request(customerId, warehouseId, variantId, 60_000m, 0m), [TightRole]));
    }

    [Fact]
    public async Task Price_above_engine_price_is_always_allowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, _) = await SeedAsync(db, priceLists, "ABOVE");

        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 120_000m, 0m), [TightRole]);

        Assert.Equal(120_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task No_roles_falls_back_to_global_default_and_allows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, _) = await SeedAsync(db, priceLists, "NOROLE");

        // Default global 100% -> apa pun lolos; ini yang menjaga pemanggil lama tidak rusak.
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 10_000m, 0m));

        Assert.Equal(10_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Tier_price_is_the_baseline_for_the_limit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var sku = "SOG-SKU-TIER";
        var product = new Product("SOG-P-TIER", "SOG Tier", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant(sku, null, 100_000m, null, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Tier: qty >= 10 harganya 80.000
        var list = await priceLists.CreateAsync(new CreatePriceListRequest("SOG-PL-TIER", "SOG Tier List", null, true,
            [new PriceListLineRequest(variant.Id, 1, 90_000m), new PriceListLineRequest(variant.Id, 10, 80_000m)]));

        var customer = new Customer("SOG-C-TIER", "SOG Tier Customer", null, null, null, null, null, 30, "IDR",
            0m, true, list.Id);
        db.Customers.Add(customer);
        var warehouse = new Warehouse("SOG-WH-TIER", "SOG Tier WH", null, true, false);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        if (!await db.Roles.AnyAsync(r => r.Name == TightRole))
        {
            db.Roles.Add(new ApplicationRole(TightRole)
            { NormalizedName = TightRole.ToUpperInvariant(), MaxDiscountPercent = 5m });
            await db.SaveChangesAsync();
        }

        // Qty 10 -> baseline 80.000. Harga 80.000 lolos (menyimpang 0%)...
        var ok = await so.CreateAsync(new CreateSalesOrderRequest(customer.Id, warehouse.Id,
            new DateTime(2026, 7, 27), null, null,
            [new SalesOrderLineRequest(variant.Id, 10, 80_000m, 0m, null)]), [TightRole]);
        Assert.Equal(80_000m, ok.Lines[0].UnitPrice);

        // ...tapi qty 1 -> baseline 90.000, harga 80.000 menyimpang 11,11% > 5% -> ditolak.
        await Assert.ThrowsAsync<ValidationException>(() =>
            so.CreateAsync(new CreateSalesOrderRequest(customer.Id, warehouse.Id,
                new DateTime(2026, 7, 27), null, null,
                [new SalesOrderLineRequest(variant.Id, 1, 80_000m, 0m, null)]), [TightRole]));
    }

    [Fact]
    public async Task Update_is_guarded_too()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId, _) = await SeedAsync(db, priceLists, "UPDGUARD");
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 90_000m, 0m), [TightRole]);

        await Assert.ThrowsAsync<ValidationException>(() => so.UpdateAsync(created.Id,
            new UpdateSalesOrderRequest(warehouseId, new DateTime(2026, 7, 27), null, null,
                [new SalesOrderLineRequest(variantId, 1, 90_000m, 50m, null)]),
            [TightRole]));
    }
}
