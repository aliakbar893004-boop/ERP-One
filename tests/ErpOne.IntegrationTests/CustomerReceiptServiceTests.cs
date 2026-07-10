using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.CustomerReceipts;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CustomerReceiptServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CustomerReceiptServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // customer + Confirmed SO + Open Customer Invoice (10000) + IDR cash account; returns (customerId, invoiceId, accountId, grand).
    private static async Task<(int customerId, int invoiceId, int accountId, decimal grand)> SeedInvoiceAndAccountAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var customer = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 100000m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(customer.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new SalesOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        await soSvc.SubmitAsync(so.Id);

        var invSvc = sp.GetRequiredService<ICustomerInvoiceService>();
        var inv = await invSvc.CreateAsync(new CreateCustomerInvoiceRequest(customer.Id, new DateTime(2026, 7, 3), null, null, null, [so.Id]));

        var acc = sp.GetRequiredService<ICashBankAccountService>();
        var account = await acc.CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));

        return (customer.Id, inv.Id, account.Id, inv.GrandTotal);
    }

    [Fact]
    public async Task Create_posts_and_updates_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var rec = scope.ServiceProvider.GetRequiredService<ICustomerReceiptService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();

        var receipt = await rec.CreateAsync(new CreateCustomerReceiptRequest(
            customerId, accountId, new DateTime(2026, 7, 6), null, [new ReceiptAllocationInput(invoiceId, grand)]));

        Assert.StartsWith("ARR-202607-", receipt.ReceiptNumber);
        Assert.Equal("Posted", receipt.Status);

        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Paid", invoice!.Status);
        Assert.Equal(0m, invoice.Outstanding);
        Assert.Equal(grand, await acc.GetBalanceAsync(accountId));   // opening 0 + receipt in
    }

    [Fact]
    public async Task Allocation_over_outstanding_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var rec = scope.ServiceProvider.GetRequiredService<ICustomerReceiptService>();
        await Assert.ThrowsAsync<ValidationException>(() => rec.CreateAsync(new CreateCustomerReceiptRequest(
            customerId, accountId, new DateTime(2026, 7, 6), null, [new ReceiptAllocationInput(invoiceId, grand + 1m)])));
    }

    [Fact]
    public async Task Mismatched_account_currency_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, invoiceId, _, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var usd = await acc.CreateAsync(new CreateCashBankAccountRequest($"USD{Guid.NewGuid().ToString("N")[..4]}", "USD", "Bank", "USD", 0m, "B", "1", "H", true));
        var rec = scope.ServiceProvider.GetRequiredService<ICustomerReceiptService>();
        await Assert.ThrowsAsync<ValidationException>(() => rec.CreateAsync(new CreateCustomerReceiptRequest(
            customerId, usd.Id, new DateTime(2026, 7, 6), null, [new ReceiptAllocationInput(invoiceId, grand)])));
    }

    [Fact]
    public async Task Void_reverses_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var rec = scope.ServiceProvider.GetRequiredService<ICustomerReceiptService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();

        var receipt = await rec.CreateAsync(new CreateCustomerReceiptRequest(
            customerId, accountId, new DateTime(2026, 7, 6), null, [new ReceiptAllocationInput(invoiceId, grand)]));
        await rec.VoidAsync(receipt.Id, "tester");

        var voided = await rec.GetByIdAsync(receipt.Id);
        Assert.Equal("Voided", voided!.Status);
        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Open", invoice!.Status);
        Assert.Equal(grand, invoice.Outstanding);
        Assert.Equal(0m, await acc.GetBalanceAsync(accountId));   // in then out → back to opening
    }
}
