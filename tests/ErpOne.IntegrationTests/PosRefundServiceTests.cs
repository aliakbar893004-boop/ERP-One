using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.PosSales;
using ErpOne.Application.PosRefunds;
using ErpOne.Application.CashierShifts;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosRefundServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosRefundServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds warehouse, cash + card payment methods, product/variant with stock, an open shift.
    // Returns (shiftId, warehouseId, variantId, cashMethodId, cardMethodId).
    private static async Task<(int shiftId, int whId, int variantId, int cashPm, int cardPm)> SeedAsync(IServiceProvider sp, int opening)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Wh {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        var cash = new PaymentMethod($"CA{id}", $"Cash {id}", PaymentType.Tunai, true);
        var card = new PaymentMethod($"CD{id}", $"Card {id}", PaymentType.Kartu, true);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat); db.PaymentMethods.AddRange(cash, card);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, wh.Id, opening));
        await db.SaveChangesAsync();

        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shiftSvc.OpenAsync("cashier1", "Cashier One", new OpenShiftRequest(wh.Id, 0m));
        return (shift.Id, wh.Id, v.Id, cash.Id, card.Id);
    }

    private static async Task<PosSaleDto> SellAsync(IServiceProvider sp, int shiftId, int variantId, int pmId, int qty, decimal unitPrice)
    {
        var pos = sp.GetRequiredService<IPosSaleService>();
        return await pos.CreateSaleAsync("cashier1", "Cashier One", shiftId,
            new CreatePosSaleRequest(pmId, null, 0m, qty * unitPrice, [new PosSaleLineRequest(variantId, qty, unitPrice, 0m)]));
    }

    [Fact]
    public async Task Full_void_reverses_stock_shift_and_gl()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 3, 2000m); // sells 3 → stock 97
        Assert.Equal(97, await stock.GetOnHandAsync(variantId, wh));

        var refunds = sp.GetRequiredService<IPosRefundService>();
        var refundable = await refunds.GetRefundableAsync(sale.Id);
        var lines = refundable!.Lines.Select(l => new PosRefundLineInput(l.PosSaleLineId, l.RemainingQty)).ToList();
        var refund = await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("Customer changed mind", lines),
            "cashier1", "Cashier One", "supervisor");

        Assert.Equal(sale.GrandTotal, refund.GrandTotal);
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh)); // stock fully back

        var db = sp.GetRequiredService<AppDbContext>();
        var shift = await db.CashierShifts.Include(s => s.Totals).FirstAsync(s => s.Id == shiftId);
        Assert.Equal(0m, shift.Totals.First(t => t.PaymentMethodId == cashPm).TotalAmount);
        Assert.Equal(0m, shift.ExpectedCash); // OpeningFloat 0 + cash sales 0 after refund
        Assert.True(await db.JournalEntries.AnyAsync(j => j.SourceType == "PosRefund" && j.SourceId == refund.Id));
    }

    [Fact]
    public async Task Partial_refund_tracks_remaining_and_allows_second_refund()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 3, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();

        var r1 = await refunds.GetRefundableAsync(sale.Id);
        var lineId = r1!.Lines[0].PosSaleLineId;
        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("one back", [new PosRefundLineInput(lineId, 1)]),
            "cashier1", "Cashier One", "supervisor");

        var r2 = await refunds.GetRefundableAsync(sale.Id);
        Assert.Equal(2, r2!.Lines[0].RemainingQty);
        Assert.Equal("PartiallyRefunded", r2.RefundStatus);
        Assert.Equal(98, await stock.GetOnHandAsync(variantId, wh)); // 97 + 1

        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("rest back", [new PosRefundLineInput(lineId, 2)]),
            "cashier1", "Cashier One", "supervisor");
        var r3 = await refunds.GetRefundableAsync(sale.Id);
        Assert.Equal(0, r3!.Lines[0].RemainingQty);
        Assert.Equal("Refunded", r3.RefundStatus);
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
    }

    [Fact]
    public async Task Over_refund_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, _, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 2, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();
        var lineId = (await refunds.GetRefundableAsync(sale.Id))!.Lines[0].PosSaleLineId;

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("too much", [new PosRefundLineInput(lineId, 5)]),
                "cashier1", "Cashier One", "supervisor"));
    }

    [Fact]
    public async Task Refund_on_closed_shift_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 2, 2000m);
        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();
        await shiftSvc.CloseAsync(shiftId, "cashier1", new CloseShiftRequest(0m, null));

        var refunds = sp.GetRequiredService<IPosRefundService>();
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var before = await stock.GetOnHandAsync(variantId, wh);
        var db = sp.GetRequiredService<AppDbContext>();
        var lineId = await db.PosSaleLines.Where(l => l.PosSaleId == sale.Id).Select(l => l.Id).FirstAsync();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("late", [new PosRefundLineInput(lineId, 1)]),
                "cashier1", "Cashier One", "supervisor"));
        Assert.Equal(before, await stock.GetOnHandAsync(variantId, wh)); // unchanged
    }

    [Fact]
    public async Task Card_refund_does_not_touch_cash_drawer()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, _, variantId, _, cardPm) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cardPm, 2, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();
        var lineId = (await refunds.GetRefundableAsync(sale.Id))!.Lines[0].PosSaleLineId;

        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("card back", [new PosRefundLineInput(lineId, 2)]),
            "cashier1", "Cashier One", "supervisor");

        var db = sp.GetRequiredService<AppDbContext>();
        var shift = await db.CashierShifts.Include(s => s.Totals).FirstAsync(s => s.Id == shiftId);
        Assert.Equal(0m, shift.CashSalesTotal);      // never had cash
        Assert.Equal(0m, shift.ExpectedCash);        // OpeningFloat 0, untouched
        Assert.Equal(0m, shift.Totals.First(t => t.PaymentMethodId == cardPm).TotalAmount); // card total reversed
    }
}
