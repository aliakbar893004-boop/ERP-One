using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CashierShiftReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CashierShiftReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Uid() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // warehouse + product/variant (price 2000) + opening stock 100@1000 + cash & transfer payment methods.
    private static async Task<(int wh, int variant, int cashPm, int transferPm)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var cash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        var transfer = new PaymentMethod($"TRF{id}", "Transfer", PaymentType.Transfer, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(cash); db.PaymentMethods.Add(transfer);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 1000m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);
        return (wh.Id, variant.Id, cash.Id, transfer.Id);
    }

    [Fact]
    public async Task Groups_by_cashier_with_method_breakdown_and_variance()
    {
        // Shifts are stamped with the real clock at OpenAsync, so query a range around today.
        var from = DateTime.Today.AddDays(-1);
        var to = DateTime.Today.AddDays(1);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variant, cashPm, transferPm) = await SeedMastersAsync(sp);

        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var pos = sp.GetRequiredService<IPosSaleService>();

        // Cashier Rani: open (float 100000), 1 cash sale (2×2000=4000) + 1 transfer sale (1×2000=2000), close counted 103500 → var -500.
        var rani = Uid();
        var raniShift = await shifts.OpenAsync(rani, "Rani", new OpenShiftRequest(wh, 100_000m));
        await pos.CreateSaleAsync(rani, "Rani", raniShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await pos.CreateSaleAsync(rani, "Rani", raniShift.Id,
            new CreatePosSaleRequest(transferPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(raniShift.Id, rani, new CloseShiftRequest(103_500m, null)); // expected 104000 → var -500

        // Cashier Budi: open (float 50000), 1 cash sale (1×2000), close counted 52000 → var 0.
        var budi = Uid();
        var budiShift = await shifts.OpenAsync(budi, "Budi", new OpenShiftRequest(wh, 50_000m));
        await pos.CreateSaleAsync(budi, "Budi", budiShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(budiShift.Id, budi, new CloseShiftRequest(52_000m, null)); // expected 52000 → var 0

        // An OPEN shift (never closed) must be excluded.
        await shifts.OpenAsync(Uid(), "Ghost", new OpenShiftRequest(wh, 0m));

        var svc = sp.GetRequiredService<ICashierShiftReportService>();
        // Filter by this test's own warehouse to isolate from other tests sharing the DB.
        var r = await svc.GetShiftReportAsync(from, to, wh, null);

        Assert.Equal(2, r.CashierCount);
        Assert.Equal(2, r.ShiftCount);                      // 2 closed; open excluded
        Assert.Equal(8_000m, r.GrandTotalSales);            // 6000 + 2000
        Assert.Equal(3, r.GrandTransactionCount);           // 2 + 1
        Assert.Equal(-500m, r.GrandVariance);               // -500 + 0

        var raniC = Assert.Single(r.Cashiers, c => c.CashierName == "Rani");
        Assert.Equal(6_000m, raniC.TotalSales);
        Assert.Equal(-500m, raniC.TotalVariance);
        var raniS = Assert.Single(raniC.Shifts);
        Assert.Equal(104_000m, raniS.ExpectedCash);
        Assert.Equal(103_500m, raniS.CountedCash);
        Assert.Equal(-500m, raniS.CashVariance);
        Assert.Equal(4_000m, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Tunai").Amount);
        Assert.Equal(1, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Tunai").TransactionCount);
        Assert.Equal(2_000m, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Transfer").Amount);
        // Methods ordered by amount desc → Tunai first.
        Assert.Equal("Tunai", raniS.Methods[0].PaymentMethodName);
    }

    [Fact]
    public async Task Cashier_filter_and_get_cashiers()
    {
        var from = DateTime.Today.AddDays(-1);
        var to = DateTime.Today.AddDays(1);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variant, cashPm, _) = await SeedMastersAsync(sp);
        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var pos = sp.GetRequiredService<IPosSaleService>();

        var ani = Uid();
        var aniShift = await shifts.OpenAsync(ani, "Ani", new OpenShiftRequest(wh, 0m));
        await pos.CreateSaleAsync(ani, "Ani", aniShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(aniShift.Id, ani, new CloseShiftRequest(2000m, null));

        var edo = Uid();
        var edoShift = await shifts.OpenAsync(edo, "Edo", new OpenShiftRequest(wh, 0m));
        await pos.CreateSaleAsync(edo, "Edo", edoShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await shifts.CloseAsync(edoShift.Id, edo, new CloseShiftRequest(4000m, null));

        var svc = sp.GetRequiredService<ICashierShiftReportService>();

        var onlyEdo = await svc.GetShiftReportAsync(from, to, null, edo);
        Assert.Single(onlyEdo.Cashiers);
        Assert.Equal("Edo", onlyEdo.Cashiers[0].CashierName);
        Assert.Equal(4000m, onlyEdo.GrandTotalSales);

        var cashiers = await svc.GetCashiersAsync();
        Assert.Contains(cashiers, c => c.UserId == ani && c.Name == "Ani");
        Assert.Contains(cashiers, c => c.UserId == edo && c.Name == "Edo");
    }
}
