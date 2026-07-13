using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class InventoryValuationReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public InventoryValuationReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int variant, int wh)> SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1500m, null, 0m, null, null, true);
        await db.SaveChangesAsync();

        var stock = sp.GetRequiredService<IStockService>();
        await stock.RecordOpeningAsync(variant.Id, wh.Id, 10, 1000m); // qty 10, value 10000, MA 1000
        return (variant.Id, wh.Id);
    }

    [Fact]
    public async Task Valuation_today_matches_productstock_value()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var result = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Category, null, null, false);

        // Compare against ProductStock.Qty * variant.CostPrice for the seeded variant.
        var expectedValue = await (
            from s in db.ProductStocks
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            where s.ProductVariantId == variant
            select s.Quantity * v.CostPrice).SumAsync();

        var line = result.Groups.SelectMany(g => g.Items).Single(i => i.VariantId == variant);
        Assert.Equal(10, line.Qty);
        Assert.Equal(expectedValue, line.Value);
        Assert.Equal(1000m, line.AvgCost);
    }

    [Fact]
    public async Task Valuation_asof_past_excludes_later_movements()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, _) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();

        // Opening was recorded "now"; as-of yesterday there should be no qty for this variant.
        var result = await svc.GetValuationAsync(DateTime.Today.AddDays(-1), ValuationGroupBy.Category, null, null, false);

        Assert.DoesNotContain(result.Groups.SelectMany(g => g.Items), i => i.VariantId == variant);
    }

    [Fact]
    public async Task GroupBy_category_and_warehouse_have_same_grand_total()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();

        var byCat = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Category, null, null, false);
        var byWh = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Warehouse, null, null, false);

        Assert.Equal(byCat.GrandTotalValue, byWh.GrandTotalValue);
        Assert.Equal(byCat.GrandTotalQty, byWh.GrandTotalQty);
    }
}
