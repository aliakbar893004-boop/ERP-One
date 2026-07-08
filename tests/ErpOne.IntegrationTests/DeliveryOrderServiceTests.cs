using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.DeliveryOrders;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class DeliveryOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DeliveryOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Customer ctor: (code, name, contactPerson, phone, email, address, taxId, paymentTermDays, defaultCurrency, creditLimit, isActive)
    // ProductVariant ctor via product.AddVariant: (sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive)
    private static async Task<(int cust, int wh, int variant)> SeedMastersAsync(IServiceProvider sp, int openingQty = 100)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT DO {id}", null, null, null, null, null, 30, "IDR", 0m, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true); // CostPrice 800
        await db.SaveChangesAsync();

        // Opening stock so a later Post has on-hand to draw down (mutasi masuk → CostPrice becomes 800 via MA on 0 base).
        if (openingQty > 0)
            await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, openingQty, 800m);

        return (cust.Id, wh.Id, variant.Id);
    }

    // Creates a Confirmed SO (empty approval chain) with one line: qty 10 @ 1000, no discount/tax.
    private static async Task<SalesOrderDto> ConfirmedSoAsync(IServiceProvider sp, int cust, int wh, int variant)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(
            cust, wh, new DateTime(2026, 7, 1), null, "so",
            [new SalesOrderLineRequest(variant, 10, 1000m, 0m, null)]));
        await soSvc.SubmitAsync(so.Id);
        return (await soSvc.GetByIdAsync(so.Id))!;
    }

    [Fact]
    public async Task GetSoForDelivery_returns_remaining()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();

        var dto = await svc.GetSoForDeliveryAsync(so.Id);
        Assert.NotNull(dto);
        var line = Assert.Single(dto!.Lines);
        Assert.Equal(10, line.OrderedQuantity);
        Assert.Equal(0, line.AlreadyDeliveredQuantity);
        Assert.Equal(10, line.RemainingQuantity);
    }

    [Fact]
    public async Task CreateDraft_generates_number_and_does_not_move_stock()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soLineId = so.Lines[0].Id;

        var doc = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), "kirim sebagian",
            [new DeliveryOrderLineRequest(soLineId, 4)]));

        Assert.StartsWith("DO-202607-", doc.DoNumber);
        Assert.Equal("Draft", doc.Status);
        Assert.Single(doc.Lines);
        Assert.Equal(0m, doc.Lines[0].UnitCost); // COGS belum di-set sebelum Post

        var stockSvc = sp.GetRequiredService<IStockService>();
        Assert.Equal(100, await stockSvc.GetOnHandAsync(variant, wh)); // draft belum mengurangi stok
    }

    [Fact]
    public async Task CreateDraft_rejects_over_delivery_strict()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soLineId = so.Lines[0].Id;

        // qty 10, STRICT → 11 must fail (no tolerance)
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                so.Id, new DateTime(2026, 7, 1), null,
                [new DeliveryOrderLineRequest(soLineId, 11)])));
    }

    [Fact]
    public async Task DeleteDraft_removes_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var doc = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));

        Assert.True(await svc.DeleteDraftAsync(doc.Id));
        Assert.Null(await svc.GetByIdAsync(doc.Id));
    }

    [Fact]
    public async Task Post_partial_then_full_moves_stock_out_and_updates_so_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp); // 100 on hand @ CostPrice 800
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // qty 10
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var soLineId = so.Lines[0].Id;

        // Deliver 4 → stock 100 - 4 = 96; SO PartiallyDelivered
        var d1 = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(soLineId, 4)]));
        Assert.True(await svc.PostAsync(d1.Id));

        Assert.Equal("Posted", (await svc.GetByIdAsync(d1.Id))!.Status);
        Assert.Equal(96, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("PartiallyDelivered", (await soSvc.GetByIdAsync(so.Id))!.Status);

        // Deliver remaining 6 → stock 90; SO Delivered
        var d2 = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(soLineId, 6)]));
        Assert.True(await svc.PostAsync(d2.Id));

        Assert.Equal(90, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Delivered", (await soSvc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task Post_writes_out_stock_movement_and_snapshots_cogs_without_touching_ma()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp); // CostPrice 800
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 5)]));
        await svc.PostAsync(d.Id);

        // Movement: Out, RefType "DO", qty is stored signed negative on the ledger, unit cost = CostPrice 800.
        var movements = await stockSvc.GetMovementsByVariantAsync(variant);
        Assert.Contains(movements, m => m.RefType == "DO" && m.Type == MovementType.Out && m.Quantity == -5 && m.UnitCost == 800m);

        // COGS snapshot on the DO line = CostPrice at Post.
        var line = (await svc.GetByIdAsync(d.Id))!.Lines[0];
        Assert.Equal(800m, line.UnitCost);
        Assert.Equal(4000m, line.LineCost); // 5 * 800

        // MA is untouched by a stock-out: variant CostPrice stays 800.
        var v = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(800m, v!.CostPrice);
    }

    [Fact]
    public async Task Post_rejected_when_stock_insufficient_no_mutation()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, openingQty: 3); // only 3 on hand
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // wants to deliver 5
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 5)]));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.PostAsync(d.Id));

        // No mutation: stock unchanged, DO still Draft.
        Assert.Equal(3, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Draft", (await svc.GetByIdAsync(d.Id))!.Status);
    }

    [Fact]
    public async Task Post_twice_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(d.Id));
    }

    [Fact]
    public async Task CloseSo_locks_a_partially_delivered_so()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id); // SO now PartiallyDelivered

        Assert.True(await soSvc.CloseAsync(so.Id));
        Assert.Equal("Closed", (await soSvc.GetByIdAsync(so.Id))!.Status);

        // After close, SO is no longer deliverable.
        Assert.Null(await svc.GetSoForDeliveryAsync(so.Id));
    }

    [Fact]
    public async Task GetCreditInfo_counts_partially_delivered_so_as_outstanding()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // GrandTotal 10000
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id); // SO → PartiallyDelivered

        var info = await soSvc.GetCreditInfoAsync(cust, thisOrderTotal: 0m, excludeSoId: null, default);
        Assert.Equal(10000m, info.EstimatedOutstanding); // PartiallyDelivered still counts
    }
}
