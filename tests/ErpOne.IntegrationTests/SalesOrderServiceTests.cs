using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seeds a customer (with credit limit), warehouse, and one active variant.
    // Customer ctor: (code, name, contactPerson, phone, email, address, taxId, paymentTermDays, defaultCurrency, creditLimit, isActive)
    private static async Task<(int cust, int wh, int variant)> SeedMastersAsync(IServiceProvider sp, decimal creditLimit = 0m)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT SO {id}", null, null, null, null, null, 30, "IDR", creditLimit, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // ProductVariant ctor: (sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive)
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (cust.Id, wh.Id, variant.Id);
    }

    private static CreateSalesOrderRequest New(int cust, int wh, int variant) =>
        new(cust, wh, new DateTime(2026, 7, 1), null, "test",
            [new SalesOrderLineRequest(variant, 10, 1000m, 0m, null)]);

    [Fact]
    public async Task Create_generates_number_and_totals()
    {
        using var scope = _factory.Services.CreateScope();
        var (cust, wh, variant) = await SeedMastersAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        Assert.StartsWith("SO-202607-", so.SoNumber);
        Assert.Equal(10000m, so.GrandTotal);
        Assert.Equal("Draft", so.Status);
        Assert.Single(so.Lines);
    }

    [Fact]
    public async Task Submit_with_empty_chain_confirms_immediately()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);

        Assert.Equal("Confirmed", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task Submit_approve_chain_confirms()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.SalesOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);
        Assert.Equal("PendingApproval", (await svc.GetByIdAsync(so.Id))!.Status);

        // CreatedBy null in test context (NullCurrentUser) → acting "approver" is not the creator
        await svc.ApproveAsync(so.Id, "approver", _ => true);
        Assert.Equal("Confirmed", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    // NOTE: segregation-of-duties (creator cannot approve own document) is enforced entirely
    // by the reused, document-agnostic approval engine and is already covered by
    // ApprovalServiceTests — which apply to SalesOrder verbatim. It is NOT re-tested here:
    // the integration test factory uses NullCurrentUser (so.CreatedBy is null), and the engine's
    // check `ApprovalService.EnsureCanAct` is guarded by `!string.IsNullOrEmpty(creatorUserName)`,
    // so with a null creator the rule is (correctly) inert. B1's PurchaseOrderServiceTests omits
    // this test for the same reason. Do not add a service-level creator-cannot-approve test unless
    // you first plumb a non-null ICurrentUser into the factory; if you do, assert ValidationException
    // (the engine throws ValidationException via Fail(...), NOT InvalidOperationException).

    [Fact]
    public async Task Reject_returns_to_draft_with_note()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.SalesOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);
        await svc.RejectAsync(so.Id, "approver", _ => true, "stok tidak cukup");

        var fetched = await svc.GetByIdAsync(so.Id);
        Assert.Equal("Draft", fetched!.Status);
        Assert.Equal("stok tidak cukup", fetched.RejectionNote);
        Assert.Empty(await svc.GetApprovalStepsAsync(so.Id));
    }

    [Fact]
    public async Task Cancel_marks_cancelled()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.CancelAsync(so.Id);
        Assert.Equal("Cancelled", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task So_numbers_are_unique_within_month()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var a = await svc.CreateAsync(New(cust, wh, variant));
        var b = await svc.CreateAsync(New(cust, wh, variant));
        Assert.NotEqual(a.SoNumber, b.SoNumber);
    }

    [Fact]
    public async Task GetCreditInfo_sums_confirmed_excludes_id_and_flags_over_limit()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, creditLimit: 15000m);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        // One Confirmed SO (empty chain → confirms on submit): GrandTotal 10000.
        var confirmed = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(confirmed.Id);

        // Outstanding counts the Confirmed SO (10000); a new 4000 order stays within 15000.
        var within = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 4000m, excludeSoId: null, default);
        Assert.Equal(15000m, within.CreditLimit);
        Assert.Equal(10000m, within.EstimatedOutstanding);
        Assert.False(within.ExceedsLimit); // 10000 + 4000 = 14000 <= 15000

        // Boundary: exactly at limit does NOT exceed (strictly greater-than).
        var atLimit = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5000m, excludeSoId: null, default);
        Assert.False(atLimit.ExceedsLimit); // 10000 + 5000 = 15000, not > 15000

        // Just over the limit exceeds.
        var over = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5001m, excludeSoId: null, default);
        Assert.True(over.ExceedsLimit); // 10000 + 5001 = 15001 > 15000

        // Excluding the confirmed SO drops outstanding to 0.
        var excluded = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5001m, excludeSoId: confirmed.Id, default);
        Assert.Equal(0m, excluded.EstimatedOutstanding);
        Assert.False(excluded.ExceedsLimit);
    }

    [Fact]
    public async Task GetCreditInfo_zero_limit_never_exceeds()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, creditLimit: 0m);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var confirmed = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(confirmed.Id);

        var info = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 999999m, excludeSoId: null, default);
        Assert.False(info.ExceedsLimit); // CreditLimit == 0 disables the check
    }
}
