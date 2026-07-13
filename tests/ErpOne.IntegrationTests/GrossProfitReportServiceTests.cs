using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class GrossProfitReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public GrossProfitReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // Seeds masters + opening stock (100 @ 1000) and records one POS sale of 3 @ 2000.
    private static async Task SeedPosSaleAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);

        var user = NewUser();
        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(user, "Rani", new OpenShiftRequest(wh.Id, 0m));
        await sp.GetRequiredService<IPosSaleService>().CreateSaleAsync(user, "Rani", shift.Id,
            new CreatePosSaleRequest(pmCash.Id, null, 0m, 6000m, [new PosSaleLineRequest(variant.Id, 3, 2000m, 0m)]));
    }

    [Fact]
    public async Task GrossProfit_by_product_matches_revenue_minus_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await SeedPosSaleAsync(sp);

        var svc = sp.GetRequiredService<IGrossProfitReportService>();
        var result = await svc.GetGrossProfitAsync(
            new GrossProfitFilter(null, null, "POS", GrossProfitGroupBy.Product));

        // 3 @ 2000 revenue = 6000; cogs 3 @ 1000 = 3000; GP = 3000; margin 50%.
        Assert.Equal(6000m, result.TotalRevenue);
        Assert.Equal(3000m, result.TotalCogs);
        Assert.Equal(3000m, result.TotalGrossProfit);
        Assert.Equal(50m, result.TotalMarginPercent);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task GrossProfit_total_matches_sales_summary()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await SeedPosSaleAsync(sp);

        var gp = await sp.GetRequiredService<IGrossProfitReportService>()
            .GetGrossProfitAsync(new GrossProfitFilter(null, null, "POS", GrossProfitGroupBy.Category));
        var sales = await sp.GetRequiredService<ISalesReportService>()
            .GetSalesSummaryAsync(new SalesFilter(null, null, "POS", null, null, null, null));

        Assert.Equal(sales.GrossProfit, gp.TotalGrossProfit);
        Assert.Equal(sales.Revenue, gp.TotalRevenue);
    }
}
