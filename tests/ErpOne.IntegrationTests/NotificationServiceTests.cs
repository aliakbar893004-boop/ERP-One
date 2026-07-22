using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.Notifications;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class NotificationServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public NotificationServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static Func<string, bool> AllPerms => _ => true;
    private static Func<string, bool> NoPerms => _ => false;

    private static int NewDocId() => Math.Abs(Guid.NewGuid().GetHashCode()) % 1_000_000 + 1;

    [Fact]
    public async Task Approval_group_shows_for_matching_role_only()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();

        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.PurchaseOrder, NewDocId(), 1, "Managers"));
        await db.SaveChangesAsync();

        var forManager = await svc.GetForUserAsync("u1", ["Managers"], AllPerms, DateTime.Today);
        Assert.Contains(forManager.Groups, g => g.Key == "approval:PurchaseOrder" && g.Count >= 1);

        var forOther = await svc.GetForUserAsync("u1", ["Cashiers"], AllPerms, DateTime.Today);
        Assert.DoesNotContain(forOther.Groups, g => g.Key == "approval:PurchaseOrder");
    }

    [Fact]
    public async Task Approval_group_gated_by_approve_permission()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.PurchaseOrder, NewDocId(), 1, "Managers"));
        await db.SaveChangesAsync();

        var gated = await svc.GetForUserAsync("u1", ["Managers"], NoPerms, DateTime.Today);
        Assert.DoesNotContain(gated.Groups, g => g.Key == "approval:PurchaseOrder");
    }

    [Fact]
    public async Task Non_approval_groups_gated_by_permission()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var none = await svc.GetForUserAsync("u1", [], NoPerms, DateTime.Today);
        Assert.DoesNotContain(none.Groups, g => g.Key is "low-stock" or "ar-due" or "ap-due");
    }

    [Fact]
    public async Task Ar_due_counts_only_within_window_and_unpaid()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var (custId, soId, soLineId, variantId) = await SeedConfirmedSoAsync(scope.ServiceProvider);
        db.CustomerInvoices.Add(NewCustomerInvoice(custId, DateTime.Today.AddDays(3), 1000m, soId, soLineId, variantId));   // due soon -> counts
        db.CustomerInvoices.Add(NewCustomerInvoice(custId, DateTime.Today.AddDays(60), 1000m, soId, soLineId, variantId));  // far -> no
        var paid = NewCustomerInvoice(custId, DateTime.Today.AddDays(2), 1000m, soId, soLineId, variantId); paid.ApplyPayment(1000m); // paid -> no
        db.CustomerInvoices.Add(paid);
        await db.SaveChangesAsync();

        var res = await svc.GetForUserAsync("u1", [], p => p == "reports.ar-aging.index", DateTime.Today);
        var arGroup = res.Groups.Single(g => g.Key == "ar-due");
        Assert.Equal(1, arGroup.Count);
    }

    [Fact]
    public async Task TotalCount_is_sum_of_groups()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.SalesOrder, NewDocId(), 1, "Managers"));
        await db.SaveChangesAsync();
        var res = await svc.GetForUserAsync("u1", ["Managers"], p => p.EndsWith(".approve"), DateTime.Today);
        Assert.Equal(res.Groups.Sum(g => g.Count), res.TotalCount);
    }

    // Customer + product/variant + a confirmed SalesOrder (empty chain) so invoice lines satisfy FKs.
    private static async Task<(int customerId, int soId, int soLineId, int variantId)> SeedConfirmedSoAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 0m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(cust.Id, wh.Id, new DateTime(2026, 7, 1), null, "so",
            [new SalesOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        await soSvc.SubmitAsync(so.Id);
        so = (await soSvc.GetByIdAsync(so.Id))!;
        return (cust.Id, so.Id, so.Lines[0].Id, variant.Id);
    }

    private static CustomerInvoice NewCustomerInvoice(int customerId, DateTime dueDate, decimal amount,
        int soId, int soLineId, int variantId)
    {
        var num = $"CINV-{Guid.NewGuid():N}"[..14];
        var inv = new CustomerInvoice(num, customerId, "IDR", DateTime.Today, dueDate, null, null);
        inv.SetLines([new CustomerInvoiceLine(soId, soLineId, variantId, 1, amount, 0m, 0m)]);
        return inv;
    }
}
