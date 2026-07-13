using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockLedgerReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockLedgerReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seeds one product+variant+warehouse, then an opening IN (10 @ 1000) and an
    // adjustment OUT (-4) using the real stock service so StockMovement + ProductStock stay consistent.
    private static async Task<(int variant, int wh)> SeedLedgerAsync(IServiceProvider sp)
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
        await stock.RecordOpeningAsync(variant.Id, wh.Id, 10, 1000m);          // +10 @ 1000
        await stock.RecordAdjustmentAsync(new StockAdjustmentRequest(
            wh.Id, DateTime.UtcNow, "sell",
            [new StockAdjustmentLine(variant.Id, -4, 0m, "issue")]));           // -4 @ MA(1000)
        return (variant.Id, wh.Id);
    }

    [Fact]
    public async Task StockCard_opening_plus_movements_equals_closing_and_matches_onhand()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();
        var stock = sp.GetRequiredService<IStockService>();

        var card = await svc.GetStockCardAsync(variant, wh, DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date.AddDays(1));

        Assert.NotNull(card);
        Assert.Equal(card!.OpeningQty + card.Lines.Sum(l => l.Quantity), card.ClosingQty);
        Assert.Equal(6, card.ClosingQty); // 10 - 4
        Assert.Equal(await stock.GetOnHandAsync(variant, wh), card.ClosingQty);
        // Running qty of the last line equals closing qty.
        Assert.Equal(card.ClosingQty, card.Lines[^1].RunningQty);
    }

    [Fact]
    public async Task Summary_counts_in_out_and_net()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var summary = await svc.GetSummaryAsync(new StockLedgerFilter(null, wh, null, null, null));

        Assert.Equal(10, summary.TotalIn);
        Assert.Equal(4, summary.TotalOut);
        Assert.Equal(6, summary.NetChange);
        Assert.Equal(2, summary.Records);
    }

    [Fact]
    public async Task MovementsPaged_filters_by_type()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var all = await svc.GetMovementsPagedAsync(new StockLedgerFilter(null, wh, null, null, null), 1, 50);
        var adjustments = await svc.GetMovementsPagedAsync(
            new StockLedgerFilter(null, wh, MovementType.Adjustment, null, null), 1, 50);

        Assert.Equal(2, all.Total);
        Assert.All(adjustments.Items, m => Assert.Equal(MovementType.Adjustment, m.Type));
    }

    [Fact]
    public async Task BuildStockCardReport_returns_document_with_rows()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var doc = await svc.BuildStockCardReportAsync(variant, wh, DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date.AddDays(1));

        Assert.NotNull(doc);
        Assert.NotEmpty(doc!.Columns);
        Assert.NotEmpty(doc.Rows);
    }
}
