using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.Purchasing.PurchaseReturns;
using ErpOne.Application.Stock;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PurchaseReturnServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseReturnServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seed a PurchaseReturn approval chain (one manager step) so Submit leaves the doc PendingApproval.
    private static async Task SeedChainAsync(AppDbContext db)
    {
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PurchaseReturn))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PurchaseReturn, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    // Supplier + product + confirmed PO + posted GRN of (qty @ unitCost). Returns anchor ids.
    private static async Task<(int supplierId, int grnId, int grnLineId, int variantId, int warehouseId)>
        SeedPostedGrnAsync(IServiceProvider sp, int qty, decimal unitCost)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(
            supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, unitCost, 0m, null)]));
        await po.SubmitAsync(created.Id); // empty PO chain in tests → auto-confirms

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            created.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, qty, unitCost)]));
        await grnSvc.PostAsync(grn.Id);

        var grnLineId = await db.GoodsReceiptLines.AsNoTracking()
            .Where(l => l.GoodsReceiptId == grn.Id).Select(l => l.Id).FirstAsync();
        return (supplier.Id, grn.Id, grnLineId, variant.Id, wh.Id);
    }

    // GRN → SupplierInvoice. Returns invoice anchors + grand total.
    private static async Task<(int invoiceId, int invLineId, int grnLineId, int variantId, int whId, decimal grandTotal)>
        SeedSupplierInvoiceAsync(IServiceProvider sp, int qty, decimal unitCost)
    {
        var (supplierId, grnId, grnLineId, variantId, whId) = await SeedPostedGrnAsync(sp, qty, unitCost);
        var invSvc = sp.GetRequiredService<ISupplierInvoiceService>();
        var inv = await invSvc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, "SUP-INV", null, [grnId]));
        var db = sp.GetRequiredService<AppDbContext>();
        var invLineId = await db.SupplierInvoiceLines.AsNoTracking()
            .Where(l => l.SupplierInvoiceId == inv.Id).Select(l => l.Id).FirstAsync();
        return (inv.Id, invLineId, grnLineId, variantId, whId, inv.GrandTotal);
    }

    [Fact]
    public async Task Grn_path_full_return_reduces_stock_and_posts_grir_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        var stock = sp.GetRequiredService<IStockService>();
        await SeedChainAsync(db);

        var (_, grnId, grnLineId, variantId, whId) = await SeedPostedGrnAsync(sp, 10, 100m);

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest(
            "GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);
        Assert.Equal(0, await stock.GetOnHandAsync(variantId, whId)); // 10 - 10

        var je = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(x => x.SourceType == "PurchaseReturn" && x.SourceId == created.Id);
        Assert.Equal(1000m, je.Lines.Sum(l => l.Debit)); // Dr GR-IR 1000
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit)); // balanced
    }

    [Fact]
    public async Task Partial_returns_track_remaining_and_reject_over_return()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (_, grnId, grnLineId, _, _) = await SeedPostedGrnAsync(sp, 10, 100m);

        var first = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 6)]));
        await svc.SubmitAsync(first.Id); await svc.ApproveAsync(first.Id, "admin", _ => true);

        var src = await svc.GetReturnableSourceAsync("GoodsReceipt", grnId);
        Assert.Equal(4, src!.Lines.Single(l => l.GoodsReceiptLineId == grnLineId).RemainingQty);

        var second = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 4)]));
        await svc.SubmitAsync(second.Id); await svc.ApproveAsync(second.Id, "admin", _ => true);

        // Third return over remaining -> rejected at create.
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
                [new PurchaseReturnLineInput(grnLineId, null, 1)])));
    }

    [Fact]
    public async Task Insufficient_on_hand_is_rejected_on_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        var stock = sp.GetRequiredService<IStockService>();
        await SeedChainAsync(db);
        var (_, grnId, grnLineId, variantId, whId) = await SeedPostedGrnAsync(sp, 10, 100m);

        // Draw stock down below the return qty via an adjustment out.
        await stock.RecordAdjustmentAsync(new StockAdjustmentRequest(
            whId, DateTime.Today, "draw", [new StockAdjustmentLine(variantId, -7, 0m, null)]));

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 10)]));
        await svc.SubmitAsync(created.Id);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));
    }

    [Fact]
    public async Task Invoice_path_return_credits_outstanding_and_posts_ap_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, grnLineId, _, _, grandTotal) = await SeedSupplierInvoiceAsync(sp, 10, 100m);

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("SupplierInvoice", null, invoiceId, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var inv = await db.SupplierInvoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(grandTotal, inv.CreditedAmount);
        Assert.Equal(0m, inv.Outstanding);

        var je = await db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.SourceType == "PurchaseReturn" && x.SourceId == created.Id);
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit)); // balanced
        Assert.True(je.Lines.Sum(l => l.Debit) > 0);
    }

    [Fact]
    public async Task Return_over_invoice_outstanding_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, grnLineId, _, _, grandTotal) = await SeedSupplierInvoiceAsync(sp, 10, 100m);

        // Pay the invoice down so Outstanding < a full return.
        var inv = await db.SupplierInvoices.FirstAsync(i => i.Id == invoiceId);
        inv.ApplyPayment(grandTotal - 100m);
        await db.SaveChangesAsync();

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("SupplierInvoice", null, invoiceId, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));
    }
}
