using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.Dashboard;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PosSales;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.Stock;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class DashboardServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DashboardServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];
    private static string Id6() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Masters + opening stock (100 @ 1000) + open shift; returns ids for POS sales.
    private static async Task<(string user, int wh, int variant, int pmCash, int shift)> SeedSalesAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id6();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);
        var user = NewUser();
        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(user, "Rani", new OpenShiftRequest(wh.Id, 0m));
        return (user, wh.Id, variant.Id, pmCash.Id, shift.Id);
    }

    // Customer (term 0) + confirmed SO (qty × 1000) → AR invoice with explicit dueDate/amount = qty × 1000.
    private static async Task SeedArInvoiceAsync(IServiceProvider sp, int qty, DateTime invoiceDate, DateTime dueDate)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id6();
        var customer = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 0, "IDR", 100_000_000m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var so = sp.GetRequiredService<ISalesOrderService>();
        var soDto = await so.CreateAsync(new CreateSalesOrderRequest(customer.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new SalesOrderLineRequest(variant.Id, qty, 1000m, 0m, null)]));
        await so.SubmitAsync(soDto.Id);   // empty chain → auto-confirmed

        var inv = sp.GetRequiredService<ICustomerInvoiceService>();
        await inv.CreateAsync(new CreateCustomerInvoiceRequest(customer.Id, invoiceDate, dueDate, null, null, [soDto.Id]));
    }

    // Supplier (term 0) + posted GRN (qty × 1000) → AP invoice with explicit dueDate/amount = qty × 1000.
    private static async Task SeedApInvoiceAsync(IServiceProvider sp, int qty, DateTime invoiceDate, DateTime dueDate)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id6();
        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 0, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var poDto = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, 1000m, 0m, null)]));
        await po.SubmitAsync(poDto.Id);   // auto-confirmed

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(poDto.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(poDto.Lines[0].Id, qty, 1000m)]));
        await grnSvc.PostAsync(grn.Id);

        var inv = sp.GetRequiredService<ISupplierInvoiceService>();
        await inv.CreateAsync(new CreateSupplierInvoiceRequest(supplier.Id, invoiceDate, dueDate, null, null, [grn.Id]));
    }

    [Fact]
    public async Task Today_revenue_and_txn_count_from_pos_sales()
    {
        var today = DateTime.Today;
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, _, variant, pmCash, shift) = await SeedSalesAsync(sp);

        var pos = sp.GetRequiredService<IPosSaleService>();
        await pos.CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await pos.CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(today);

        Assert.Equal(6000m, dash.Kpis.TodayRevenue);   // (2 + 1) × 2000
        Assert.Equal(2, dash.Kpis.TodayTxnCount);
        // 7-day trend plumbing: length 7, today at the end, no sales yesterday.
        Assert.Equal(7, dash.Kpis.RevenueTrend.Count);
        Assert.Equal(7, dash.Kpis.TxnTrend.Count);
        Assert.Equal(6000m, dash.Kpis.RevenueTrend[6]);
        Assert.Equal(2, dash.Kpis.TxnTrend[6]);
        Assert.Equal(0m, dash.Kpis.YesterdayRevenue);
        Assert.Equal(0, dash.Kpis.YesterdayTxnCount);
        // month-to-date includes today's two sales (both created "today").
        Assert.Equal(6000m, dash.Kpis.MonthRevenue);
        Assert.Equal(2, dash.Kpis.MonthTxnCount);
    }

    [Fact]
    public async Task Ar_ap_due_and_aging_buckets()
    {
        var asOf = new DateTime(2027, 1, 1);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // AR: three invoices landing in distinct buckets (age = asOf - dueDate).
        await SeedArInvoiceAsync(sp, 1, new DateTime(2026, 12, 15), new DateTime(2026, 12, 15)); // age ~17 → Current, 1000
        await SeedArInvoiceAsync(sp, 2, new DateTime(2026, 11, 15), new DateTime(2026, 11, 15)); // age ~47 → D31_60, 2000
        await SeedArInvoiceAsync(sp, 4, new DateTime(2026, 8, 15), new DateTime(2026, 8, 15));   // age ~139 → D90Plus, 4000
        // AP: one invoice in D90Plus.
        await SeedApInvoiceAsync(sp, 3, new DateTime(2026, 8, 15), new DateTime(2026, 8, 15));   // 3000

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(asOf);

        Assert.Equal(1000m, dash.ArAging.Current);
        Assert.Equal(2000m, dash.ArAging.D31_60);
        Assert.Equal(0m, dash.ArAging.D61_90);
        Assert.Equal(4000m, dash.ArAging.D90Plus);
        Assert.Equal(7000m, dash.ArAging.Total);
        Assert.Equal(7000m, dash.Kpis.ArDue);          // all due <= asOf + 7

        Assert.Equal(3000m, dash.ApAging.D90Plus);
        Assert.Equal(3000m, dash.ApAging.Total);
        Assert.Equal(3000m, dash.Kpis.ApDue);
    }

    [Fact]
    public async Task Pending_po_so_counted()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id6();

        var supplier = new Supplier($"SP{id}", $"PT S{id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var customer = new Customer($"CU{id}", $"PT C{id}", null, null, null, null, null, 30, "IDR", 100_000_000m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder($"PO-{id}", supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null, null);
        po.SetLines([new PurchaseOrderLine(variant.Id, 1, 1000m, 0m, null, 0m)]);
        po.Submit();   // → PendingApproval
        var so = new SalesOrder($"SO-{id}", customer.Id, wh.Id, new DateTime(2026, 7, 1), null, null, null);
        so.SetLines([new SalesOrderLine(variant.Id, 1, 1000m, 0m, null, 0m)]);
        so.Submit();   // → PendingApproval
        db.PurchaseOrders.Add(po); db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(DateTime.Today);

        Assert.Equal(1, dash.Pending.PoPendingCount);
        Assert.Equal(1, dash.Pending.SoPendingCount);
        Assert.Single(dash.Pending.PoPending);
        Assert.Single(dash.Pending.SoPending);
        Assert.Equal($"PO-{id}", dash.Pending.PoPending[0].Number);
        Assert.Equal($"PT S{id}", dash.Pending.PoPending[0].Party);
        Assert.Equal($"PT C{id}", dash.Pending.SoPending[0].Party);
    }
}
