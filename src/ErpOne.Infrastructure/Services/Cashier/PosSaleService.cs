using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.PosSales;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PosSaleService(
    AppDbContext db,
    IValidator<CreatePosSaleRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IPosSaleService
{
    public async Task<IReadOnlyList<PosProductOptionDto>> SearchProductsAsync(int warehouseId, string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, v.Barcode, ProductName = p.Name, v.Price, v.DiscountPrice, v.DiscountPercent };

        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Barcode == term || x.Sku.Contains(term) || x.ProductName.Contains(term));

        var rows = await q.OrderBy(x => x.ProductName).Take(20)
            .Select(x => new { x.Id, x.Sku, x.Barcode, x.ProductName, x.Price, x.DiscountPrice, x.DiscountPercent })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var stock = await db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductVariantId))
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        return rows.Select(r => new PosProductOptionDto(
            r.Id, r.Sku, r.ProductName, r.Barcode,
            r.DiscountPrice ?? r.Price,
            stock.FirstOrDefault(s => s.VariantId == r.Id)?.Qty ?? 0,
            r.Price, r.DiscountPercent)).ToList();
    }

    public async Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
            ?? throw Fail("Shift tidak ditemukan.");
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup.");

        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == request.PaymentMethodId, ct);
        if (method is null || !method.IsActive) throw Fail("Metode pembayaran tidak valid.");
        var isCash = method.Type == PaymentType.Tunai;

        decimal taxRate = 0m;
        if (request.TaxId is { } taxId)
        {
            var tax = await db.Taxes.FirstOrDefaultAsync(t => t.Id == taxId, ct) ?? throw Fail("Pajak tidak ditemukan.");
            taxRate = tax.Rate;
        }

        var whId = shift.WarehouseId;
        var now = DateTime.Now;

        // Fase-1: cek stok semua baris (akumulasi per varian) sebelum mutasi apa pun.
        var takenPerVariant = new Dictionary<int, int>();
        foreach (var line in request.Lines)
        {
            var onHand = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.ProductVariantId && s.WarehouseId == whId)
                .SumAsync(s => (int?)s.Quantity, ct) ?? 0;
            var already = takenPerVariant.TryGetValue(line.ProductVariantId, out var t) ? t : 0;
            var available = onHand - already;
            if (line.Quantity > available)
            {
                var sku = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                    .Select(v => v.Sku).FirstOrDefaultAsync(ct) ?? line.ProductVariantId.ToString();
                throw Fail($"Stok {sku} tidak cukup (tersedia {available}).");
            }
            takenPerVariant[line.ProductVariantId] = already + line.Quantity;
        }

        var sale = new PosSale(await docNumbers.NextAsync(DocumentTypes.PosSale, now, ct), shift.Id, whId, now,
            request.PaymentMethodId, isCash, request.TaxId, taxRate, userId, userName);

        foreach (var line in request.Lines)
        {
            var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Varian {line.ProductVariantId} tidak ditemukan.");
            var name = await db.Products.Where(p => p.Id == v.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "—";

            sale.AddLine(v.Id, v.Sku, name, line.Quantity, line.UnitPrice, line.DiscountPercent, v.CostPrice);

            db.StockMovements.Add(new StockMovement(v.Id, whId, MovementType.Out,
                -line.Quantity, v.CostPrice, now, refType: "POS", refId: null, note: sale.SaleNumber));
            await db.UpsertStockAsync(v.Id, whId, -line.Quantity, ct);
        }

        sale.Settle(request.TransactionDiscount, request.AmountTendered);

        var trackedShift = await db.CashierShifts.FirstAsync(s => s.Id == shift.Id, ct);
        trackedShift.RecordSale(request.PaymentMethodId, isCash, sale.GrandTotal);

        db.PosSales.Add(sale);
        await db.SaveChangesAsync(ct);

        await journalPoster.PostPosSaleAsync(sale, ct);

        await tx.CommitAsync(ct);
        return (await GetByIdAsync(sale.Id, ct))!;
    }

    public async Task<PosSaleDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var sale = await db.PosSales.AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sale is null) return null;

        var whName = await db.Warehouses.Where(w => w.Id == sale.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var cashierName = sale.CashierName;
        var pmName = await db.PaymentMethods.Where(m => m.Id == sale.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(ct) ?? "—";

        var lines = sale.Lines.OrderBy(l => l.Id).Select(l => new PosSaleLineDto(
            l.Id, l.ProductVariantId, l.VariantSku, l.ProductName,
            l.Quantity, l.UnitPrice, l.DiscountPercent, l.LineTotal)).ToList();

        return new PosSaleDto(sale.Id, sale.SaleNumber, sale.CashierShiftId, sale.WarehouseId, whName, cashierName,
            sale.SaleDate, sale.PaymentMethodId, pmName, sale.IsCashPayment,
            sale.TaxId, sale.TaxRateSnapshot, sale.TransactionDiscount,
            sale.Subtotal, sale.TaxTotal, sale.GrandTotal, sale.AmountTendered, sale.ChangeGiven, lines);
    }

    public async Task<PagedResult<PosSaleListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, int? shiftId, int? paymentMethodId = null, string? cashierUserId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.PosSales.AsNoTracking().AsQueryable();
        if (shiftId is { } sid) query = query.Where(s => s.CashierShiftId == sid);
        if (paymentMethodId is { } pmid) query = query.Where(s => s.PaymentMethodId == pmid);
        if (!string.IsNullOrWhiteSpace(cashierUserId)) query = query.Where(s => s.CashierUserId == cashierUserId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => s.SaleNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new { s.Id, s.SaleNumber, s.SaleDate, s.CashierName, s.PaymentMethodId, s.GrandTotal })
            .ToListAsync(ct);

        var pmIds = rows.Select(r => r.PaymentMethodId).Distinct().ToList();
        var pms = await db.PaymentMethods.AsNoTracking().Where(m => pmIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var items = rows.Select(r => new PosSaleListItemDto(
            r.Id, r.SaleNumber, r.SaleDate,
            r.CashierName,
            pms.FirstOrDefault(p => p.Id == r.PaymentMethodId)?.Name ?? "—",
            r.GrandTotal)).ToList();

        return new PagedResult<PosSaleListItemDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<PosCashierDto>> GetCashiersAsync(CancellationToken ct = default) =>
        await db.PosSales.AsNoTracking()
            .GroupBy(s => new { s.CashierUserId, s.CashierName })
            .OrderBy(g => g.Key.CashierName)
            .Select(g => new PosCashierDto(g.Key.CashierUserId, g.Key.CashierName))
            .ToListAsync(ct);


    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PosSale", message)]);
}
