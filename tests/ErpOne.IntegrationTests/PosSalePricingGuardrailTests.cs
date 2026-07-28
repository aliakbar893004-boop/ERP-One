using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.PriceLists;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosSalePricingGuardrailTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosSalePricingGuardrailTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const string CashierRole = "POSG-Cashier"; // 5%

    private sealed record Seeded(string User, int Warehouse, int Variant, int PmCash, int Shift, string Sku);

    /// <summary>Gudang dengan price list default (harga dasar 90.000), stok 100, shift terbuka,
    /// varian berharga master 100.000.</summary>
    private static async Task<Seeded> SeedAsync(IServiceProvider sp, string suffix)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var priceLists = sp.GetRequiredService<IPriceListService>();

        var sku = $"POSG-SKU-{suffix}";
        var product = new Product($"POSG-P-{suffix}", $"POSG Probe {suffix}", null, null, null, null, null,
            ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"POSG-CSH-{suffix}", "Tunai", PaymentType.Tunai, true);
        db.Products.Add(product);
        db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();

        var variant = product.AddVariant(sku, null, 100_000m, null, 40_000m, null, null, true);
        await db.SaveChangesAsync();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest($"POSG-PL-{suffix}", "POSG List", null, true,
            [new PriceListLineRequest(variant.Id, 1, 90_000m)]));

        var wh = new Warehouse($"POSG-WH-{suffix}", $"POSG WH {suffix}", null, true, false, list.Id);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 40_000m);

        var user = "posg-" + Guid.NewGuid().ToString("N")[..8];
        var shift = await sp.GetRequiredService<ICashierShiftService>()
            .OpenAsync(user, $"POSG User {suffix}", new OpenShiftRequest(wh.Id, 0m));

        if (!await db.Roles.AnyAsync(r => r.Name == CashierRole))
        {
            db.Roles.Add(new ApplicationRole(CashierRole)
            { NormalizedName = CashierRole.ToUpperInvariant(), MaxDiscountPercent = 5m });
            await db.SaveChangesAsync();
        }

        return new Seeded(user, wh.Id, variant.Id, pmCash.Id, shift.Id, sku);
    }

    [Fact]
    public async Task Search_returns_price_list_price_not_master_price()
    {
        using var scope = _factory.Services.CreateScope();
        var s = await SeedAsync(scope.ServiceProvider, "SEARCH");
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();

        var options = await pos.SearchProductsAsync(s.Warehouse, s.Sku);

        var option = Assert.Single(options);
        Assert.Equal(90_000m, option.UnitPrice);   // dari price list gudang
        Assert.Equal(100_000m, option.Price);      // harga master tetap, untuk harga coret
    }

    [Fact]
    public async Task Client_supplied_price_is_ignored_in_favour_of_engine_price()
    {
        using var scope = _factory.Services.CreateScope();
        var s = await SeedAsync(scope.ServiceProvider, "FAKE");
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();

        // Client "mengarang" harga 1 rupiah. Server harus memakai 90.000 dari price list.
        var sale = await pos.CreateSaleAsync(s.User, "POSG Fake", s.Shift,
            new CreatePosSaleRequest(s.PmCash, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(s.Variant, 1, 1m, 0m)]));

        Assert.Equal(90_000m, sale.Lines[0].UnitPrice);
        Assert.Equal(90_000m, sale.GrandTotal);
    }

    [Fact]
    public async Task Discount_above_role_limit_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var s = await SeedAsync(scope.ServiceProvider, "REJECT");
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => pos.CreateSaleAsync(
            s.User, "POSG Reject", s.Shift,
            new CreatePosSaleRequest(s.PmCash, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(s.Variant, 1, 90_000m, 30m)]),
            [CashierRole]));

        Assert.Contains(s.Sku, ex.Message);
    }

    [Fact]
    public async Task Discount_within_role_limit_is_accepted()
    {
        using var scope = _factory.Services.CreateScope();
        var s = await SeedAsync(scope.ServiceProvider, "OK");
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();

        var sale = await pos.CreateSaleAsync(s.User, "POSG Ok", s.Shift,
            new CreatePosSaleRequest(s.PmCash, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(s.Variant, 1, 90_000m, 4m)]),
            [CashierRole]);

        Assert.Equal(4m, sale.Lines[0].DiscountPercent);
        Assert.Equal(90_000m, sale.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Stock_still_moves_when_price_comes_from_engine()
    {
        using var scope = _factory.Services.CreateScope();
        var s = await SeedAsync(scope.ServiceProvider, "STOCK");
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await pos.CreateSaleAsync(s.User, "POSG Stock", s.Shift,
            new CreatePosSaleRequest(s.PmCash, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(s.Variant, 5, 90_000m, 0m)]));

        var onHand = await db.ProductStocks
            .Where(x => x.ProductVariantId == s.Variant && x.WarehouseId == s.Warehouse)
            .SumAsync(x => x.Quantity);
        Assert.Equal(95, onHand);
    }
}
