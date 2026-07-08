using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PurchaseOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Menyemai master minimal; mengembalikan (supplierId, warehouseId, variantId).
    // Menggunakan suffix unik agar setiap test tidak bentrok unique constraint di DB bersama.
    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var sup = new Supplier($"SP{id}", $"PT PO {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        // Real Warehouse ctor: (code, name, address, isActive, isDefault) — plan omitted isDefault
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        // Real Product ctor: (code, name, description, categoryId, brandId, baseUnitId, taxId, status) — plan omitted status
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(sup);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // ProductVariant.ProductId has private set; use product.AddVariant so EF tracks the FK correctly.
        // Plan incorrectly added variant directly to db.ProductVariants with product.Id as first ctor arg —
        // real ctor is (sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive).
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    private static CreatePurchaseOrderRequest New(int sup, int wh, int variant) =>
        new(sup, wh, new DateTime(2026, 6, 24), null, "test",
            [new PurchaseOrderLineRequest(variant, 10, 1000m, 0m, null)]);

    [Fact]
    public async Task Create_generates_number_and_totals()
    {
        using var scope = _factory.Services.CreateScope();
        var (sup, wh, variant) = await SeedMastersAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        Assert.StartsWith("PO-202606-", po.PoNumber);
        Assert.Equal(10000m, po.GrandTotal);
        Assert.Equal("Draft", po.Status);
        Assert.Single(po.Lines);
    }

    [Fact]
    public async Task Submit_with_empty_chain_confirms_immediately()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);

        var fetched = await svc.GetByIdAsync(po.Id);
        Assert.Equal("Confirmed", fetched!.Status);
    }

    [Fact]
    public async Task Submit_approve_chain_confirms()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);
        Assert.Equal("PendingApproval", (await svc.GetByIdAsync(po.Id))!.Status);

        // CreatedBy null pada konteks test (NullCurrentUser) → acting "approver" bukan creator
        await svc.ApproveAsync(po.Id, "approver", _ => true);
        Assert.Equal("Confirmed", (await svc.GetByIdAsync(po.Id))!.Status);
    }

    [Fact]
    public async Task Reject_returns_to_draft_with_note()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);
        await svc.RejectAsync(po.Id, "approver", _ => true, "harga ketinggian");

        var fetched = await svc.GetByIdAsync(po.Id);
        Assert.Equal("Draft", fetched!.Status);
        Assert.Equal("harga ketinggian", fetched.RejectionNote);
        Assert.Empty(await svc.GetApprovalStepsAsync(po.Id));
    }

    [Fact]
    public async Task Dashboard_counts_by_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var before = await svc.GetDashboardAsync();

        // two drafts
        await svc.CreateAsync(New(sup, wh, variant));
        await svc.CreateAsync(New(sup, wh, variant));
        // one confirmed (empty chain → confirms immediately on submit)
        var confirmed = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(confirmed.Id);

        var after = await svc.GetDashboardAsync();

        Assert.Equal(before.TotalCount + 3, after.TotalCount);
        Assert.True(after.DraftCount >= before.DraftCount + 2);
        Assert.True(after.ConfirmedCount >= before.ConfirmedCount + 1);
    }

    [Fact]
    public async Task Po_numbers_are_unique_within_month()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var a = await svc.CreateAsync(New(sup, wh, variant));
        var b = await svc.CreateAsync(New(sup, wh, variant));
        Assert.NotEqual(a.PoNumber, b.PoNumber);
    }
}
