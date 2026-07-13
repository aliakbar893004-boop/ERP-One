using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PurchaseReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var sup = new Supplier($"SP{id}", $"PT GRN {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(sup); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    private static async Task PostGrnAsync(IServiceProvider sp, int sup, int wh, int variant, int qty, decimal cost)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var po = await poSvc.CreateAsync(new CreatePurchaseOrderRequest(
            sup, wh, new DateTime(2026, 7, 1), null, "po",
            [new PurchaseOrderLineRequest(variant, qty, cost, 0m, null)]));
        await poSvc.SubmitAsync(po.Id);
        po = (await poSvc.GetByIdAsync(po.Id))!;
        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 7, 1), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, qty, cost)]));
        await grnSvc.PostAsync(grn.Id);
    }

    [Fact]
    public async Task Posted_grn_appears_in_report_with_value()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await PostGrnAsync(sp, sup, wh, variant, 5, 1000m);

        var svc = sp.GetRequiredService<IPurchaseReportService>();
        var summary = await svc.GetPurchaseSummaryAsync(new PurchaseFilter(null, null, sup, null, null));

        Assert.Equal(5, summary.Qty);
        Assert.Equal(5000m, summary.TotalCost);
        Assert.Equal(1, summary.Receipts);

        var page = await svc.GetPurchasesPagedAsync(new PurchaseFilter(null, null, sup, null, null), 1, 50);
        var row = Assert.Single(page.Items);
        Assert.Equal(5000m, row.Value);
        Assert.Equal(sup, row.SupplierId);
    }

    [Fact]
    public async Task Supplier_filter_excludes_other_suppliers()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (supA, whA, varA) = await SeedMastersAsync(sp);
        var (supB, whB, varB) = await SeedMastersAsync(sp);
        await PostGrnAsync(sp, supA, whA, varA, 2, 500m);
        await PostGrnAsync(sp, supB, whB, varB, 3, 700m);

        var svc = sp.GetRequiredService<IPurchaseReportService>();
        var onlyA = await svc.GetPurchasesPagedAsync(new PurchaseFilter(null, null, supA, null, null), 1, 50);

        Assert.All(onlyA.Items, r => Assert.Equal(supA, r.SupplierId));
        Assert.NotEmpty(onlyA.Items);
    }
}
