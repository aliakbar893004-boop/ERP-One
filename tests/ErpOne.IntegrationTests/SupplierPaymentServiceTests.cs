using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Application.SupplierPayments;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SupplierPaymentServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierPaymentServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int supplierId, int invoiceId, int accountId, decimal grand)> SeedInvoiceAndAccountAsync(IServiceProvider sp)
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
        var poDto = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new PurchaseOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        await po.SubmitAsync(poDto.Id);

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(poDto.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(poDto.Lines[0].Id, 10, 1000m)]));
        await grnSvc.PostAsync(grn.Id);

        var invSvc = sp.GetRequiredService<ISupplierInvoiceService>();
        var inv = await invSvc.CreateAsync(new CreateSupplierInvoiceRequest(supplier.Id, new DateTime(2026, 7, 3), null, null, null, [grn.Id]));

        var acc = sp.GetRequiredService<ICashBankAccountService>();
        var account = await acc.CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));

        return (supplier.Id, inv.Id, account.Id, inv.GrandTotal);
    }

    [Fact]
    public async Task Submit_with_empty_chain_posts_and_updates_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var draft = await pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null, [new PaymentAllocationInput(invoiceId, grand)]));
        Assert.StartsWith("APP-202607-", draft.PaymentNumber);

        await pay.SubmitAsync(draft.Id);   // no chain seeded in tests → auto-posted

        var posted = await pay.GetByIdAsync(draft.Id);
        Assert.Equal("Posted", posted!.Status);

        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Paid", invoice!.Status);
        Assert.Equal(0m, invoice.Outstanding);

        Assert.Equal(-grand, await acc.GetBalanceAsync(accountId));  // opening 0 − payment out
    }

    [Fact]
    public async Task Allocation_over_outstanding_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();

        await Assert.ThrowsAsync<ValidationException>(() => pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null, [new PaymentAllocationInput(invoiceId, grand + 1m)])));
    }

    [Fact]
    public async Task Mismatched_account_currency_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, _, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var usd = await acc.CreateAsync(new CreateCashBankAccountRequest($"USD{Guid.NewGuid().ToString("N")[..4]}", "USD Acc", "Bank", "USD", 0m, "X", "1", "Y", true));
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();

        await Assert.ThrowsAsync<ValidationException>(() => pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, usd.Id, new DateTime(2026, 7, 5), null, [new PaymentAllocationInput(invoiceId, grand)])));
    }

    [Fact]
    public async Task Void_reverses_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var draft = await pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null, [new PaymentAllocationInput(invoiceId, grand)]));
        await pay.SubmitAsync(draft.Id);
        await pay.VoidAsync(draft.Id, "tester");

        var voided = await pay.GetByIdAsync(draft.Id);
        Assert.Equal("Voided", voided!.Status);
        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Open", invoice!.Status);
        Assert.Equal(grand, invoice.Outstanding);
        Assert.Equal(0m, await acc.GetBalanceAsync(accountId));   // out then in → back to opening
    }
}
