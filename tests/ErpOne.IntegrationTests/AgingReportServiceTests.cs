using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.CustomerReceipts;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.Reports;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AgingReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AgingReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Id() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // customer + warehouse + product/variant + IDR cash account; returns ids reused across invoices.
    private static async Task<(int customerId, int accountId, int wh, int variant)> SeedCustomerAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id();
        var customer = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 100_000_000m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        var acc = await sp.GetRequiredService<ICashBankAccountService>()
            .CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));
        return (customer.Id, acc.Id, wh.Id, variant.Id);
    }

    // One SO (auto-confirmed) → one Open customer invoice with explicit dueDate; GrandTotal = qty*price.
    private static async Task<(int invoiceId, decimal grand)> SeedArInvoiceAsync(
        IServiceProvider sp, int customerId, int wh, int variant, DateTime invoiceDate, DateTime dueDate, int qty, decimal price)
    {
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(customerId, wh, invoiceDate, null, null,
            [new SalesOrderLineRequest(variant, qty, price, 0m, null)]));
        await soSvc.SubmitAsync(so.Id); // empty approval chain → auto-confirms
        var inv = await sp.GetRequiredService<ICustomerInvoiceService>()
            .CreateAsync(new CreateCustomerInvoiceRequest(customerId, invoiceDate, dueDate, null, null, [so.Id]));
        return (inv.Id, inv.GrandTotal);
    }

    private static async Task AddReceiptAsync(IServiceProvider sp, int customerId, int accountId, int invoiceId, DateTime date, decimal amount) =>
        await sp.GetRequiredService<ICustomerReceiptService>().CreateAsync(
            new CreateCustomerReceiptRequest(customerId, accountId, date, null, [new ReceiptAllocationInput(invoiceId, amount)]));

    [Fact]
    public async Task Ar_point_in_time_buckets_exclude_post_asof_receipts()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (customerId, accountId, wh, variant) = await SeedCustomerAsync(sp);

        // A: due 15 days ago → bucket 1–30. Two receipts: 4000 before asOf (counted), 6000 after (ignored).
        var (invA, _) = await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-40), asOf.AddDays(-15), 10, 1000m); // grand 10000
        await AddReceiptAsync(sp, customerId, accountId, invA, asOf.AddDays(-5), 4000m);
        await AddReceiptAsync(sp, customerId, accountId, invA, asOf.AddDays(5), 6000m);

        // B: due 10 days in the future → Not Due, no receipts. grand 10000.
        await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-30), asOf.AddDays(10), 10, 1000m);

        // C: due 100 days ago → 90+, fully paid BEFORE asOf → excluded (outstanding 0 point-in-time).
        var (invC, _) = await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-110), asOf.AddDays(-100), 10, 1000m);
        await AddReceiptAsync(sp, customerId, accountId, invC, asOf.AddDays(-90), 10000m);

        var r = await sp.GetRequiredService<IAgingReportService>().GetArAgingAsync(asOf, customerId);

        Assert.Equal(AgingSide.Receivable, r.Side);
        Assert.Equal(2, r.InvoiceCount);                 // A (6000) + B (10000); C excluded
        Assert.Single(r.Parties);                        // one customer
        Assert.Equal(10_000m, r.GrandTotals.NotDue);     // B
        Assert.Equal(6_000m, r.GrandTotals.D1_30);       // A after the pre-asOf receipt only
        Assert.Equal(0m, r.GrandTotals.D90Plus);         // C gone
        Assert.Equal(16_000m, r.GrandTotals.Total);
    }

    // supplier + warehouse + product/variant + IDR account + one Open supplier invoice (via PO→GRN); grand = qty*price.
    private static async Task<(int supplierId, int accountId, int invoiceId, decimal grand)> SeedApInvoiceAsync(
        IServiceProvider sp, DateTime invoiceDate, DateTime dueDate, int qty, decimal price)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id();
        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, invoiceDate, null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, price, 0m, null)]));
        await po.SubmitAsync(created.Id); // empty chain → auto-confirms

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(created.Id, invoiceDate, null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, qty, price)]));
        await grnSvc.PostAsync(grn.Id);

        var inv = await sp.GetRequiredService<ISupplierInvoiceService>()
            .CreateAsync(new CreateSupplierInvoiceRequest(supplier.Id, invoiceDate, dueDate, $"SUP-{id}", null, [grn.Id]));

        var acc = await sp.GetRequiredService<ICashBankAccountService>()
            .CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));

        return (supplier.Id, acc.Id, inv.Id, inv.GrandTotal);
    }

    // Seed a Posted SupplierPayment directly (bypass approval) allocated to one invoice, dated `date`.
    private static async Task AddPaymentAsync(IServiceProvider sp, int supplierId, int accountId, int invoiceId, DateTime date, decimal amount)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var pay = new SupplierPayment($"APP-{Id()}", supplierId, accountId, "IDR", date, null);
        pay.SetAllocations([new SupplierPaymentAllocation(invoiceId, amount)]);
        pay.Submit();     // Draft → PendingApproval
        pay.MarkPosted(); // PendingApproval → Posted
        db.SupplierPayments.Add(pay);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ap_point_in_time_buckets()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Invoice 1: due 45 days ago → 31–60. Partial payment 3000 before asOf → outstanding 7000.
        var (sup1, acc1, inv1, _) = await SeedApInvoiceAsync(sp, asOf.AddDays(-75), asOf.AddDays(-45), 10, 1000m); // grand 10000
        await AddPaymentAsync(sp, sup1, acc1, inv1, asOf.AddDays(-10), 3000m);

        // Invoice 2 (different supplier): due 5 days in future → Not Due, grand 5000.
        await SeedApInvoiceAsync(sp, asOf.AddDays(-20), asOf.AddDays(5), 5, 1000m);

        var r = await sp.GetRequiredService<IAgingReportService>().GetApAgingAsync(asOf, null);

        Assert.Equal(AgingSide.Payable, r.Side);
        Assert.Equal(2, r.PartyCount);
        Assert.Equal(7_000m, r.GrandTotals.D31_60);   // inv1 after pre-asOf payment
        Assert.Equal(5_000m, r.GrandTotals.NotDue);   // inv2
        Assert.Equal(12_000m, r.GrandTotals.Total);
    }

    [Fact]
    public async Task Ar_customer_filter_narrows_results()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var (custA, _, whA, varA) = await SeedCustomerAsync(sp);
        await SeedArInvoiceAsync(sp, custA, whA, varA, asOf.AddDays(-40), asOf.AddDays(-15), 10, 1000m); // grand 10000
        var (custB, _, whB, varB) = await SeedCustomerAsync(sp);
        await SeedArInvoiceAsync(sp, custB, whB, varB, asOf.AddDays(-40), asOf.AddDays(-15), 7, 1000m);  // grand 7000

        var onlyA = await sp.GetRequiredService<IAgingReportService>().GetArAgingAsync(asOf, custA);

        Assert.Single(onlyA.Parties);
        Assert.Equal(custA, onlyA.Parties[0].PartyId);
        Assert.Equal(10_000m, onlyA.GrandTotals.Total);
    }
}
