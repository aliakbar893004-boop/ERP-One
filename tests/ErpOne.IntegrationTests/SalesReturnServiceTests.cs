using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.DeliveryOrders;
using ErpOne.Application.Sales.SalesReturns;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesReturnServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesReturnServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SeedChainAsync(AppDbContext db)
    {
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SalesReturn))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SalesReturn, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    // Customer + product (CostPrice=unitCost) + opening stock qty@unitCost + confirmed SO + posted DO of qty.
    private static async Task<(int customerId, int doId, int doLineId, int variantId, int warehouseId)>
        SeedPostedDoAsync(IServiceProvider sp, int qty, decimal unitCost)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 0m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, unitCost, null, null, true);
        await db.SaveChangesAsync();

        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, qty, unitCost);

        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(cust.Id, wh.Id, new DateTime(2026, 7, 1), null, "so",
            [new SalesOrderLineRequest(variant.Id, qty, 1000m, 0m, null)]));
        await soSvc.SubmitAsync(so.Id);
        so = (await soSvc.GetByIdAsync(so.Id))!;

        var doSvc = sp.GetRequiredService<IDeliveryOrderService>();
        var doc = await doSvc.CreateDraftAsync(new CreateDeliveryOrderRequest(so.Id, new DateTime(2026, 7, 2), null,
            [new DeliveryOrderLineRequest(so.Lines[0].Id, qty)]));
        await doSvc.PostAsync(doc.Id);

        var doLineId = await db.DeliveryOrderLines.AsNoTracking().Where(l => l.DeliveryOrderId == doc.Id).Select(l => l.Id).FirstAsync();
        return (cust.Id, doc.Id, doLineId, variant.Id, wh.Id);
    }

    private static async Task<(int invoiceId, int invLineId, int doLineId, int variantId, int whId, decimal grandTotal)>
        SeedCustomerInvoiceAsync(IServiceProvider sp, int qty, decimal unitCost)
    {
        var (customerId, doId, doLineId, variantId, whId) = await SeedPostedDoAsync(sp, qty, unitCost);
        var db = sp.GetRequiredService<AppDbContext>();
        var soId = await db.DeliveryOrders.AsNoTracking().Where(d => d.Id == doId).Select(d => d.SalesOrderId).FirstAsync();
        var invSvc = sp.GetRequiredService<ICustomerInvoiceService>();
        var inv = await invSvc.CreateAsync(new CreateCustomerInvoiceRequest(customerId, new DateTime(2026, 7, 3), null, null, null, [soId]));
        var invLineId = await db.CustomerInvoiceLines.AsNoTracking().Where(l => l.CustomerInvoiceId == inv.Id).Select(l => l.Id).FirstAsync();
        return (inv.Id, invLineId, doLineId, variantId, whId, inv.GrandTotal);
    }

    [Fact]
    public async Task Do_path_full_return_increases_stock_and_posts_cogs_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<ISalesReturnService>();
        var stock = sp.GetRequiredService<IStockService>();
        await SeedChainAsync(db);

        var (_, doId, doLineId, variantId, whId) = await SeedPostedDoAsync(sp, 10, 100m); // on-hand 0 after DO

        var created = await svc.CreateAsync(new CreateSalesReturnRequest(
            "DeliveryOrder", doId, null, DateTime.Today, null,
            [new SalesReturnLineInput(doLineId, null, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);
        Assert.Equal(10, await stock.GetOnHandAsync(variantId, whId)); // 0 + 10 back

        var je = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(x => x.SourceType == "SalesReturn" && x.SourceId == created.Id);
        Assert.Equal(1000m, je.Lines.Sum(l => l.Debit)); // Dr Inventory 1000
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit)); // balanced
    }

    [Fact]
    public async Task Partial_returns_track_remaining_and_reject_over_return()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<ISalesReturnService>();
        await SeedChainAsync(db);
        var (_, doId, doLineId, _, _) = await SeedPostedDoAsync(sp, 10, 100m);

        var first = await svc.CreateAsync(new CreateSalesReturnRequest("DeliveryOrder", doId, null, DateTime.Today, null,
            [new SalesReturnLineInput(doLineId, null, 6)]));
        await svc.SubmitAsync(first.Id); await svc.ApproveAsync(first.Id, "admin", _ => true);

        var src = await svc.GetReturnableSourceAsync("DeliveryOrder", doId);
        Assert.Equal(4, src!.Lines.Single(l => l.DeliveryOrderLineId == doLineId).RemainingQty);

        var second = await svc.CreateAsync(new CreateSalesReturnRequest("DeliveryOrder", doId, null, DateTime.Today, null,
            [new SalesReturnLineInput(doLineId, null, 4)]));
        await svc.SubmitAsync(second.Id); await svc.ApproveAsync(second.Id, "admin", _ => true);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreateSalesReturnRequest("DeliveryOrder", doId, null, DateTime.Today, null,
                [new SalesReturnLineInput(doLineId, null, 1)])));
    }

    [Fact]
    public async Task Invoice_path_return_credits_outstanding_and_posts_balanced_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<ISalesReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, doLineId, _, _, grandTotal) = await SeedCustomerInvoiceAsync(sp, 10, 100m);

        var created = await svc.CreateAsync(new CreateSalesReturnRequest("CustomerInvoice", null, invoiceId, DateTime.Today, null,
            [new SalesReturnLineInput(doLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var inv = await db.CustomerInvoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(grandTotal, inv.CreditedAmount);
        Assert.Equal(0m, inv.Outstanding);

        var je = await db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.SourceType == "SalesReturn" && x.SourceId == created.Id);
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit)); // balanced
        Assert.True(je.Lines.Sum(l => l.Debit) > 0);
    }

    [Fact]
    public async Task Return_over_invoice_outstanding_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<ISalesReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, doLineId, _, _, grandTotal) = await SeedCustomerInvoiceAsync(sp, 10, 100m);

        // Receive the invoice down so Outstanding < a full return.
        var inv = await db.CustomerInvoices.FirstAsync(i => i.Id == invoiceId);
        inv.ApplyPayment(grandTotal - 100m);
        await db.SaveChangesAsync();

        var created = await svc.CreateAsync(new CreateSalesReturnRequest("CustomerInvoice", null, invoiceId, DateTime.Today, null,
            [new SalesReturnLineInput(doLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));
    }
}
