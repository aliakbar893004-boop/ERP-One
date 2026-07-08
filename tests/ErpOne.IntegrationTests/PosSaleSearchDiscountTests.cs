using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PosSales;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosSaleSearchDiscountTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosSaleSearchDiscountTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seed 1 varian; percentDiscount → DiscountPrice dihitung pemanggil (di sini kita set eksplisit).
    private static async Task<(int wh, string sku)> SeedVariantAsync(IServiceProvider sp,
        decimal price, decimal? discountPrice, decimal? discountPercent)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var v = product.AddVariant($"SK{id}", $"BC{id}", price, discountPrice, 40_000m, null, null, true, discountPercent);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(v.Id, wh.Id, 10, 40_000m);
        return (wh.Id, v.Sku);
    }

    [Fact]
    public async Task Search_returns_original_price_and_percent_for_percent_discount()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: 90_000m, discountPercent: 10m);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);       // harga asli
        Assert.Equal(90_000m, opt.UnitPrice);    // efektif = discount price
        Assert.Equal(10m, opt.DiscountPercent);
    }

    [Fact]
    public async Task Search_returns_null_percent_for_price_only_discount()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: 80_000m, discountPercent: null);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);
        Assert.Equal(80_000m, opt.UnitPrice);
        Assert.Null(opt.DiscountPercent);
    }

    [Fact]
    public async Task Search_no_discount_price_equals_unit()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: null, discountPercent: null);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);
        Assert.Equal(100_000m, opt.UnitPrice);
    }
}
