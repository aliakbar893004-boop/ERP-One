using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class GoodsReceiptServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public GoodsReceiptServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var sup = new Supplier($"SP{id}", $"PT GRN {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(sup); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    // Creates a Confirmed PO (empty approval chain) with one line: qty 10 @ 1000, no discount/tax.
    private static async Task<PurchaseOrderDto> ConfirmedPoAsync(IServiceProvider sp, int sup, int wh, int variant)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var po = await poSvc.CreateAsync(new CreatePurchaseOrderRequest(
            sup, wh, new DateTime(2026, 6, 29), null, "po",
            [new PurchaseOrderLineRequest(variant, 10, 1000m, 0m, null)]));
        await poSvc.SubmitAsync(po.Id);
        return (await poSvc.GetByIdAsync(po.Id))!;
    }

    [Fact]
    public async Task GetPoForReceipt_returns_remaining_and_default_cost()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();

        var dto = await svc.GetPoForReceiptAsync(po.Id);
        Assert.NotNull(dto);
        var line = Assert.Single(dto!.Lines);
        Assert.Equal(10, line.OrderedQuantity);
        Assert.Equal(10, line.RemainingQuantity);
        Assert.Equal(1000m, line.DefaultUnitCost);
    }

    [Fact]
    public async Task CreateDraft_generates_number_and_does_not_move_stock()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poLineId = po.Lines[0].Id;

        var grn = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), "terima sebagian",
            [new GoodsReceiptLineRequest(poLineId, 4, 1000m)]));

        Assert.StartsWith("GRN-202606-", grn.GrnNumber);
        Assert.Equal("Draft", grn.Status);
        Assert.Single(grn.Lines);

        var stockSvc = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        Assert.Equal(0, await stockSvc.GetOnHandAsync(variant, wh)); // draft hasn't posted
    }

    [Fact]
    public async Task CreateDraft_rejects_over_tolerance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poLineId = po.Lines[0].Id;

        // qty 10, tol 10% -> max 11; 12 must fail
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
                po.Id, new DateTime(2026, 6, 29), null,
                [new GoodsReceiptLineRequest(poLineId, 12, 1000m)])));
    }

    [Fact]
    public async Task Dashboard_counts_by_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();

        var before = await svc.GetDashboardAsync();
        await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));
        var after = await svc.GetDashboardAsync();

        Assert.Equal(before.TotalCount + 1, after.TotalCount);
        Assert.Equal(before.DraftCount + 1, after.DraftCount);
    }

    [Fact]
    public async Task DeleteDraft_removes_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));

        Assert.True(await svc.DeleteDraftAsync(grn.Id));
        Assert.Null(await svc.GetByIdAsync(grn.Id));
    }

    [Fact]
    public async Task Post_partial_then_full_moves_stock_updates_hpp_and_po_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant); // qty 10
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var stockSvc = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var poLineId = po.Lines[0].Id;

        // Receive 4 @ 1000 (variant starts with CostPrice 800, 0 on hand → MA = 1000)
        var g1 = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(poLineId, 4, 1000m)]));
        Assert.True(await svc.PostAsync(g1.Id));

        Assert.Equal("Posted", (await svc.GetByIdAsync(g1.Id))!.Status);
        Assert.Equal(4, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("PartiallyReceived", (await poSvc.GetByIdAsync(po.Id))!.Status);
        var v1 = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(1000m, v1!.CostPrice); // (0*800 + 4*1000)/4

        // Receive remaining 6 @ 1000 → fully received
        var g2 = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(poLineId, 6, 1000m)]));
        Assert.True(await svc.PostAsync(g2.Id));

        Assert.Equal(10, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Received", (await poSvc.GetByIdAsync(po.Id))!.Status);
    }

    [Fact]
    public async Task Post_writes_grn_stock_movement()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var stockSvc = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 5, 950m)]));
        await svc.PostAsync(g.Id);

        var movements = await stockSvc.GetMovementsByVariantAsync(variant);
        Assert.Contains(movements, m => m.RefType == "GRN" && m.Quantity == 5);

        // RefId is not exposed on StockMovementDto — verify it directly against the persisted ledger.
        var mv = await db.StockMovements.FirstOrDefaultAsync(m => m.RefType == "GRN" && m.RefId == g.Id);
        Assert.NotNull(mv);
        Assert.Equal(MovementType.In, mv!.Type);
        Assert.Equal(5, mv.Quantity);
    }

    [Fact]
    public async Task Post_two_lines_same_variant_computes_global_moving_average()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);

        // PO with TWO lines for the SAME variant: 2 @ 1000 and 3 @ 1500.
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var po = await poSvc.CreateAsync(new CreatePurchaseOrderRequest(
            sup, wh, new DateTime(2026, 6, 29), null, "po-2lines",
            [new PurchaseOrderLineRequest(variant, 2, 1000m, 0m, null),
             new PurchaseOrderLineRequest(variant, 3, 1500m, 0m, null)]));
        await poSvc.SubmitAsync(po.Id);
        po = (await poSvc.GetByIdAsync(po.Id))!;

        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var db = sp.GetRequiredService<AppDbContext>();

        // One GRN receiving both lines fully, at their respective unit costs.
        var grn = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null,
            [new GoodsReceiptLineRequest(po.Lines[0].Id, 2, 1000m),
             new GoodsReceiptLineRequest(po.Lines[1].Id, 3, 1500m)]));
        Assert.True(await svc.PostAsync(grn.Id));

        // Global moving average across both lines: (2*1000 + 3*1500) / 5 = 1300.
        var v = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(1300m, v!.CostPrice);
    }

    [Fact]
    public async Task Post_uses_overridden_unit_cost_for_moving_average()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant); // PO line UnitPrice 1000
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var db = sp.GetRequiredService<AppDbContext>();

        // Override cost to 1200; 0 on hand → MA becomes 1200
        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 5, 1200m)]));
        await svc.PostAsync(g.Id);

        var v = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(1200m, v!.CostPrice);
    }

    [Fact]
    public async Task Post_twice_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));
        await svc.PostAsync(g.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(g.Id));
    }

    [Fact]
    public async Task ClosePo_locks_a_partially_received_po()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();

        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));
        await svc.PostAsync(g.Id); // PO now PartiallyReceived

        Assert.True(await poSvc.CloseAsync(po.Id));
        Assert.Equal("Closed", (await poSvc.GetByIdAsync(po.Id))!.Status);

        // After close, PO is no longer receivable
        Assert.Null(await svc.GetPoForReceiptAsync(po.Id));
    }
}
