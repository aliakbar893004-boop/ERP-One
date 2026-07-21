using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StandardCostGrnPostingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StandardCostGrnPostingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Returns (grnId, ppvAccountId, inventoryAccountId, grIrAccountId).
    private static async Task<(int grnId, int ppv, int inv, int grir)> PostStandardGrnAsync(
        IServiceProvider sp, decimal standardCost, decimal actualPrice, int qty)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        // Force Standard method (bypass lock for setup).
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.StandardCost);
        await db.SaveChangesAsync();

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, standardCost, null, null, true); // standard
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, DateTime.Today, null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, actualPrice, 0m, null)]));
        await po.SubmitAsync(created.Id); // empty chain → auto-confirms

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(created.Id, DateTime.Today, null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, qty, actualPrice)]));
        await grnSvc.PostAsync(grn.Id);

        var cfg = await db.PostingConfigurations.FirstAsync();
        return (grn.Id, cfg.PurchasePriceVarianceAccountId!.Value, cfg.InventoryAccountId!.Value, cfg.GrIrAccountId!.Value);
    }

    private static async Task<(decimal debit, decimal credit)> LineAsync(AppDbContext db, int grnId, int accountId)
    {
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.SourceType == "GoodsReceipt" && j.SourceId == grnId);
        var line = je.Lines.SingleOrDefault(l => l.AccountId == accountId);
        return line is null ? (0m, 0m) : (line.Debit, line.Credit);
    }

    [Fact]
    public async Task Unfavorable_variance_debits_ppv()
    {
        using var scope = _factory.Services.CreateScope();
        var (grnId, ppv, inv, grir) = await PostStandardGrnAsync(scope.ServiceProvider, standardCost: 1000m, actualPrice: 1300m, qty: 10);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal((10_000m, 0m), await LineAsync(db, grnId, inv));   // Dr Inventory @ standard
        Assert.Equal((0m, 13_000m), await LineAsync(db, grnId, grir));  // Cr GR-IR @ actual
        Assert.Equal((3_000m, 0m), await LineAsync(db, grnId, ppv));    // Dr PPV (unfavorable)
    }

    [Fact]
    public async Task Favorable_variance_credits_ppv()
    {
        using var scope = _factory.Services.CreateScope();
        var (grnId, ppv, inv, grir) = await PostStandardGrnAsync(scope.ServiceProvider, standardCost: 1000m, actualPrice: 800m, qty: 10);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal((10_000m, 0m), await LineAsync(db, grnId, inv));   // Dr Inventory @ standard
        Assert.Equal((0m, 8_000m), await LineAsync(db, grnId, grir));   // Cr GR-IR @ actual
        Assert.Equal((0m, 2_000m), await LineAsync(db, grnId, ppv));    // Cr PPV (favorable)
    }
}
