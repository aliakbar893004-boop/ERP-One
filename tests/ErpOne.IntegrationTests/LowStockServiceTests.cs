using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.LowStock;
using ErpOne.Application.Products;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class LowStockServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public LowStockServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // default warehouse + category; returns (warehouseId, categoryId).
    private static async Task<(int wh, int cat)> SeedBaseAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, true); // isDefault = true (opening stock lands here)
        var cat = new ProductCategory($"CT{id}", $"Kategori {id}", null);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();
        return (wh.Id, cat.Id);
    }

    [Fact]
    public async Task Low_stock_lists_variant_below_reorder_with_suggested_qty()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, cat) = await SeedBaseAsync(sp);

        // Single attribute-less variant via CreateAsync (also proves reorder persists):
        //  opening 8, reorder 10, reorderQty 50 → LOW (suggested 50).
        // (Two attribute-less variants in one product would collide on the auto-generated SKU.)
        var products = sp.GetRequiredService<IProductService>();
        var created = await products.CreateAsync(new CreateProductRequest(
            $"Prod {Sfx()}", null, cat, null, null, null, ProductStatus.Aktif,
            [
                new VariantInput(null, 2000m, null, 1000m, null, null, 8, true, [], null, 10, 50),
            ]));

        var svc = sp.GetRequiredService<ILowStockService>();
        // Query all warehouses (opening stock lands in whichever warehouse is IsDefault in the shared DB);
        // isolate by ProductId.
        var result = await svc.GetLowStockAsync(null);
        var mine = result.Rows.Where(r => r.ProductId == created.Id).ToList();
        var low = Assert.Single(mine);
        Assert.Equal(8, low.Quantity);
        Assert.Equal(10, low.ReorderLevel);
        Assert.Equal(50, low.SuggestedOrderQty);
        Assert.False(low.IsOutOfStock);
    }

    [Fact]
    public async Task Zero_reorder_not_tracked_and_zero_qty_flagged_out_and_warehouse_filter()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (wh, cat) = await SeedBaseAsync(sp);
        var other = new Warehouse($"WX{Sfx()}", "Lain", null, true, false);
        db.Warehouses.Add(other);
        await db.SaveChangesAsync();

        // Seed directly for full control (explicit SKUs → no auto-SKU collision):
        //  vOut:  reorder 5, ProductStock qty 0 → LOW + IsOutOfStock, suggested max(5-0,0)=5
        //  vUn:   reorder 0, qty 1  → NOT listed (not tracked)
        //  vSafe: reorder 10, qty 50 → NOT listed (above reorder)
        var id = Sfx();
        var product = new Product($"PR{id}", $"Produk {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vOut = product.AddVariant($"OUT{id}", null, 2000m, null, 1000m, null, null, true, null, 5, 0);
        var vUn = product.AddVariant($"UN{id}", null, 2000m, null, 1000m, null, null, true, null, 0, 0);
        var vSafe = product.AddVariant($"SF{id}", null, 2000m, null, 1000m, null, null, true, null, 10, 0);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(vOut.Id, wh, 0));
        db.ProductStocks.Add(new ProductStock(vUn.Id, wh, 1));
        db.ProductStocks.Add(new ProductStock(vSafe.Id, wh, 50));
        await db.SaveChangesAsync();

        var svc = sp.GetRequiredService<ILowStockService>();
        var result = await svc.GetLowStockAsync(wh);
        var mine = result.Rows.Where(r => r.ProductId == product.Id).ToList();

        var outRow = Assert.Single(mine);            // only vOut; vUn (reorder 0) & vSafe (above reorder) excluded
        Assert.Equal(vOut.Id, outRow.VariantId);
        Assert.True(outRow.IsOutOfStock);
        Assert.Equal(5, outRow.SuggestedOrderQty);

        // Warehouse filter: nothing in `other` warehouse.
        var none = await svc.GetLowStockAsync(other.Id);
        Assert.DoesNotContain(none.Rows, r => r.ProductId == product.Id);
    }

    [Fact]
    public async Task Dashboard_low_count_uses_reorder_level()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (wh, cat) = await SeedBaseAsync(sp);

        // Product LOW: variant reorder 10, stock 5. Product SAFE: reorder 10, stock 50.
        var id = Sfx();
        var pLow = new Product($"PL{id}", $"Low {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vLow = pLow.AddVariant($"L{id}", null, 2000m, null, 1000m, null, null, true, null, 10, 0);
        var pSafe = new Product($"PS{id}", $"Safe {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vSafe = pSafe.AddVariant($"S{id}", null, 2000m, null, 1000m, null, null, true, null, 10, 0);
        db.Products.AddRange(pLow, pSafe);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(vLow.Id, wh, 5));
        db.ProductStocks.Add(new ProductStock(vSafe.Id, wh, 50));
        await db.SaveChangesAsync();

        var dash = await sp.GetRequiredService<IProductService>().GetDashboardAsync();

        Assert.Contains(dash.LowStock, i => i.Id == pLow.Id);
        Assert.DoesNotContain(dash.LowStock, i => i.Id == pSafe.Id);
    }
}
