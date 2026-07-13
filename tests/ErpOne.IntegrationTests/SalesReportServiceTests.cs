using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // Seeds masters + opening stock (100 @ 1000) and an open shift; returns ids for creating POS sales.
    private static async Task<(string user, int wh, int variant, int pmCash, int shift)> SeedAsync(IServiceProvider sp)
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
        return (user, wh.Id, variant.Id, pmCash.Id, shift.Id);
    }

    [Fact]
    public async Task Pos_sale_appears_with_revenue_and_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);

        await sp.GetRequiredService<IPosSaleService>().CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var summary = await svc.GetSalesSummaryAsync(new SalesFilter(null, null, "POS", wh, null, null, null));

        Assert.Equal(4000m, summary.Revenue);   // 2 * 2000
        Assert.Equal(2000m, summary.Cogs);       // 2 * 1000
        Assert.Equal(2000m, summary.GrossProfit);
        Assert.Equal(50m, summary.MarginPercent);
        Assert.True(summary.Qty >= 2);
    }

    [Fact]
    public async Task Channel_filter_excludes_other_channel()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);

        await sp.GetRequiredService<IPosSaleService>().CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var b2b = await svc.GetSalesSummaryAsync(new SalesFilter(null, null, "B2B", wh, null, null, null));

        Assert.Equal(0, b2b.Lines);  // only a POS sale exists
    }

    [Fact]
    public async Task Paged_returns_pos_row_fields()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);

        await sp.GetRequiredService<IPosSaleService>().CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var page = await svc.GetSalesPagedAsync(new SalesFilter(null, null, null, wh, null, null, null), 1, 50);

        var row = Assert.Single(page.Items);
        Assert.Equal("POS", row.Channel);
        Assert.Equal(2000m, row.Revenue);
        Assert.Equal(1000m, row.Cogs);
        Assert.Equal(1000m, row.GrossProfit);
    }
}
