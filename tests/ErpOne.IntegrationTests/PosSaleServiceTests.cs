using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosSaleServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosSaleServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // Returns (userId, warehouseId, variantId, cashPaymentMethodId, shiftId), opens a shift, seeds stock.
    private static async Task<(string user, int wh, int variant, int pmCash, int shift)> SeedAsync(IServiceProvider sp, int openingQty = 100)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", $"BC{id}", 100_000m, null, 40_000m, null, null, true); // price 100k, cost 40k
        await db.SaveChangesAsync();
        if (openingQty > 0)
            await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, openingQty, 40_000m);

        var user = NewUser();
        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(user, "Rani", new OpenShiftRequest(wh.Id, 0m));
        return (user, wh.Id, variant.Id, pmCash.Id, shift.Id);
    }

    [Fact]
    public async Task CreateSale_reduces_stock_writes_movement_and_snapshots_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var sale = await svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, TaxId: null, TransactionDiscount: 0m, AmountTendered: 500_000m,
            Lines: [new PosSaleLineRequest(variant, 5, 100_000m, 0m)]));

        Assert.StartsWith("POS-", sale.SaleNumber);
        Assert.Equal(500_000m, sale.GrandTotal);
        Assert.Equal("Rani", sale.CashierName);

        var onHand = await db.ProductStocks.Where(s => s.ProductVariantId == variant && s.WarehouseId == wh).SumAsync(s => s.Quantity);
        Assert.Equal(95, onHand);
        var mv = await db.StockMovements.Where(m => m.RefType == "POS" && m.ProductVariantId == variant).SingleAsync();
        Assert.Equal(-5, mv.Quantity);
        Assert.Equal(40_000m, mv.UnitCost);
        var cost = await db.ProductVariants.Where(v => v.Id == variant).Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(40_000m, cost); // MA tak berubah
    }

    [Fact]
    public async Task CreateSale_accumulates_cash_into_shift()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();

        await svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 200_000m, [new PosSaleLineRequest(variant, 2, 100_000m, 0m)]));

        var reloaded = await shiftSvc.GetOpenShiftByWarehouseAsync(wh);
        Assert.Equal(200_000m, reloaded!.CashSalesTotal);
        Assert.Equal(200_000m, reloaded.TotalSalesAmount);
        Assert.Equal(1, reloaded.TransactionCount);
    }

    [Fact]
    public async Task CreateSale_rejects_insufficient_stock_without_mutation()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp, openingQty: 3);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 500_000m, [new PosSaleLineRequest(variant, 5, 100_000m, 0m)])));

        var onHand = await db.ProductStocks.Where(s => s.ProductVariantId == variant && s.WarehouseId == wh).SumAsync(s => s.Quantity);
        Assert.Equal(3, onHand); // tak berubah
        // Shared DB (EnsureCreated, tanpa reset per test): scope ke warehouse unik test ini
        // agar hanya menguji rollback penjualan INI, bukan sale committed oleh test lain.
        Assert.False(await db.PosSales.AnyAsync(s => s.WarehouseId == wh));
    }

    [Fact]
    public async Task CreateSale_rejects_when_shift_not_open()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, _) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateSaleAsync(user, "Rani", 999999, new CreatePosSaleRequest(
            pmCash, null, 0m, 500_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)])));
    }

    [Fact]
    public async Task CreateSale_records_operating_cashier_for_each_user_on_one_shift()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (userA, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();

        var s1 = await svc.CreateSaleAsync(userA, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 100_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)]));
        // user kedua menjual ke SHIFT yang sama
        var s2 = await svc.CreateSaleAsync(NewUser(), "Sari", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 100_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)]));

        Assert.Equal("Rani", s1.CashierName);
        Assert.Equal("Sari", s2.CashierName);
        Assert.Equal(s1.CashierShiftId, s2.CashierShiftId);
    }

    [Fact]
    public async Task SearchProducts_matches_barcode_sku_name_with_onhand()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, _) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var sku = await db.ProductVariants.Where(v => v.Id == variant).Select(v => v.Sku).SingleAsync();

        var res = await svc.SearchProductsAsync(wh, sku);
        var opt = Assert.Single(res);
        Assert.Equal(variant, opt.VariantId);
        Assert.Equal(100_000m, opt.UnitPrice);
        Assert.Equal(100, opt.OnHand);
    }
}
