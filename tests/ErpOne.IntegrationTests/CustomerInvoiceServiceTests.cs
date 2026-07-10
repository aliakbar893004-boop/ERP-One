using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CustomerInvoiceServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CustomerInvoiceServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Customer (credit 100k) + product + Confirmed SO (10 × 1000); returns (customerId, soId).
    private static async Task<(int customerId, int soId)> SeedConfirmedSoAsync(IServiceProvider sp, decimal creditLimit = 100000m)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var customer = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", creditLimit, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var so = sp.GetRequiredService<ISalesOrderService>();
        var soDto = await so.CreateAsync(new CreateSalesOrderRequest(customer.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new SalesOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        await so.SubmitAsync(soDto.Id);   // empty chain in tests → auto-confirmed

        return (customer.Id, soDto.Id);
    }

    [Fact]
    public async Task Create_from_one_so_computes_totals_and_number()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, soId) = await SeedConfirmedSoAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();

        var inv = await svc.CreateAsync(new CreateCustomerInvoiceRequest(
            customerId, new DateTime(2026, 7, 3), null, "PO-CUST-1", null, [soId]));

        Assert.StartsWith("ARV-202607-", inv.InvoiceNumber);
        Assert.Equal("Open", inv.Status);
        Assert.Equal(10000m, inv.GrandTotal);
        Assert.Equal(10000m, inv.Outstanding);
        Assert.Single(inv.Lines);
        Assert.Equal(new DateTime(2026, 7, 3).AddDays(30), inv.DueDate);
    }

    [Fact]
    public async Task Invoiced_so_is_excluded_then_freed_on_cancel()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, soId) = await SeedConfirmedSoAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();

        Assert.Contains(await svc.GetUninvoicedSalesOrdersAsync(customerId), s => s.SalesOrderId == soId);
        var inv = await svc.CreateAsync(new CreateCustomerInvoiceRequest(customerId, new DateTime(2026, 7, 3), null, null, null, [soId]));
        Assert.DoesNotContain(await svc.GetUninvoicedSalesOrdersAsync(customerId), s => s.SalesOrderId == soId);

        await svc.CancelAsync(inv.Id);
        Assert.Contains(await svc.GetUninvoicedSalesOrdersAsync(customerId), s => s.SalesOrderId == soId);
    }

    [Fact]
    public async Task Create_with_empty_list_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, _) = await SeedConfirmedSoAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new CreateCustomerInvoiceRequest(customerId, new DateTime(2026, 7, 3), null, null, null, [])));
    }

    [Fact]
    public async Task Credit_reflects_outstanding()
    {
        using var scope = _factory.Services.CreateScope();
        var (customerId, soId) = await SeedConfirmedSoAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerInvoiceService>();

        var before = await svc.GetCustomerCreditAsync(customerId);
        Assert.Equal(100000m, before.Available);   // limit 100k, no invoice yet

        await svc.CreateAsync(new CreateCustomerInvoiceRequest(customerId, new DateTime(2026, 7, 3), null, null, null, [soId]));
        var after = await svc.GetCustomerCreditAsync(customerId);
        Assert.Equal(10000m, after.Outstanding);
        Assert.Equal(90000m, after.Available);
    }
}
