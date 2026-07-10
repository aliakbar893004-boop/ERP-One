using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SupplierInvoiceServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierInvoiceServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Creates supplier + product + confirmed PO + posted GRN; returns (supplierId, grnId).
    private static async Task<(int supplierId, int grnId)> SeedPostedGrnAsync(IServiceProvider sp)
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
            [new PurchaseOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        // Empty approval chain in tests → submit auto-confirms.
        await po.SubmitAsync(created.Id);

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            created.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, 10, 1000m)]));
        await grnSvc.PostAsync(grn.Id);

        return (supplier.Id, grn.Id);
    }

    [Fact]
    public async Task Create_from_one_grn_computes_totals_and_number()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, "SUP-INV-1", null, [grnId]));

        Assert.StartsWith("APV-202607-", inv.InvoiceNumber);
        Assert.Equal("Open", inv.Status);
        Assert.Equal(10000m, inv.GrandTotal);      // 10 × 1000, no tax
        Assert.Equal(10000m, inv.Outstanding);
        Assert.Single(inv.Lines);
    }

    [Fact]
    public async Task Invoiced_grn_is_excluded_then_freed_on_cancel()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        Assert.Contains(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, null, null, [grnId]));
        Assert.DoesNotContain(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);

        await svc.CancelAsync(inv.Id);
        Assert.Contains(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);
    }

    [Fact]
    public async Task Create_with_empty_grn_list_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, _) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new CreateSupplierInvoiceRequest(supplierId, new DateTime(2026, 7, 3), null, null, null, [])));
    }

    [Fact]
    public async Task DueDate_defaults_from_supplier_payment_term()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, null, null, [grnId]));
        Assert.Equal(new DateTime(2026, 7, 3).AddDays(30), inv.DueDate);   // PaymentTermDays = 30
    }
}
