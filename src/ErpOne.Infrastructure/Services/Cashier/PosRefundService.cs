using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.PosRefunds;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PosRefundService(
    AppDbContext db,
    IValidator<CreatePosRefundRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IPosRefundService
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public async Task<RefundableSaleDto?> GetRefundableAsync(int posSaleId, CancellationToken ct = default)
    {
        var sale = await db.PosSales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == posSaleId, ct);
        if (sale is null) return null;
        var shiftOpen = await db.CashierShifts.Where(s => s.Id == sale.CashierShiftId)
            .Select(s => s.Status).FirstOrDefaultAsync(ct) == CashierShiftStatus.Open;
        var refunded = await RefundedQtyByLineAsync(posSaleId, ct);

        var lines = new List<RefundableLineDto>();
        foreach (var l in sale.Lines.OrderBy(l => l.Id))
        {
            var already = refunded.TryGetValue(l.Id, out var q) ? q : 0;
            lines.Add(new RefundableLineDto(l.Id, l.ProductVariantId, l.VariantSku, l.ProductName,
                l.Quantity, already, l.Quantity - already, l.UnitPrice, l.DiscountPercent));
        }
        var remaining = lines.Sum(x => x.RemainingQty);
        var totalSold = lines.Sum(x => x.SoldQty);
        var status = remaining == totalSold ? "Completed" : remaining == 0 ? "Refunded" : "PartiallyRefunded";
        return new RefundableSaleDto(sale.Id, sale.SaleNumber, sale.CashierShiftId, shiftOpen, status, sale.GrandTotal, lines);
    }

    public async Task<PosRefundDto> RefundAsync(int posSaleId, CreatePosRefundRequest request,
        string cashierUserId, string cashierName, string authorizedBy, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sale = await db.PosSales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == posSaleId, ct)
            ?? throw Fail("Sale tidak ditemukan.");
        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == sale.CashierShiftId, ct)
            ?? throw Fail("Shift tidak ditemukan.");
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup, tidak bisa refund.");

        var refunded = await RefundedQtyByLineAsync(posSaleId, ct);
        var now = DateTime.Now;
        var number = await docNumbers.NextAsync(DocumentTypes.PosRefund, now, ct);
        var refund = new PosRefund(number, sale.Id, sale.CashierShiftId, now,
            sale.PaymentMethodId, sale.IsCashPayment, request.Reason, authorizedBy, cashierUserId, cashierName);

        decimal refundSubtotal = 0m, cogsTotal = 0m;
        foreach (var input in request.Lines)
        {
            var saleLine = sale.Lines.FirstOrDefault(l => l.Id == input.PosSaleLineId)
                ?? throw Fail($"Baris {input.PosSaleLineId} bukan bagian dari sale ini.");
            var already = refunded.TryGetValue(saleLine.Id, out var q) ? q : 0;
            var remaining = saleLine.Quantity - already;
            if (input.Quantity > remaining)
                throw Fail($"Qty refund {saleLine.VariantSku} melebihi sisa ({remaining}).");

            refund.AddLine(saleLine.Id, saleLine.ProductVariantId, saleLine.VariantSku, saleLine.ProductName,
                input.Quantity, saleLine.UnitPrice, saleLine.DiscountPercent, saleLine.UnitCost);

            db.StockMovements.Add(new StockMovement(saleLine.ProductVariantId, sale.WarehouseId, MovementType.In,
                input.Quantity, saleLine.UnitCost, now, refType: "PosRefund", refId: null, note: refund.RefundNumber));
            await db.UpsertStockAsync(saleLine.ProductVariantId, sale.WarehouseId, input.Quantity, ct);

            refundSubtotal += Round(Round(input.Quantity * saleLine.UnitPrice) * (100m - saleLine.DiscountPercent) / 100m);
            cogsTotal += Round(input.Quantity * saleLine.UnitCost);
        }

        var allocTxnDiscount = sale.Subtotal == 0m ? 0m : Round(sale.TransactionDiscount * refundSubtotal / sale.Subtotal);
        var baseAmount = refundSubtotal - allocTxnDiscount;
        var taxTotal = Round(baseAmount * sale.TaxRateSnapshot / 100m);
        var grandTotal = baseAmount + taxTotal;
        refund.SetTotals(refundSubtotal, allocTxnDiscount, taxTotal, grandTotal, cogsTotal);

        shift.RecordRefund(sale.PaymentMethodId, sale.IsCashPayment, grandTotal);

        db.PosRefunds.Add(refund);
        await db.SaveChangesAsync(ct);

        await journalPoster.PostPosRefundAsync(refund, ct);

        await tx.CommitAsync(ct);
        return (await GetByIdAsync(refund.Id, ct))!;
    }

    public async Task<IReadOnlyList<PosRefundDto>> GetBySaleAsync(int posSaleId, CancellationToken ct = default)
    {
        var ids = await db.PosRefunds.AsNoTracking().Where(r => r.PosSaleId == posSaleId)
            .OrderByDescending(r => r.Id).Select(r => r.Id).ToListAsync(ct);
        var list = new List<PosRefundDto>();
        foreach (var id in ids) list.Add((await GetByIdAsync(id, ct))!);
        return list;
    }

    public async Task<PagedResult<PosRefundListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, int? shiftId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.PosRefunds.AsNoTracking();
        if (shiftId is { } sid) query = query.Where(r => r.CashierShiftId == sid);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(r => r.RefundNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.RefundNumber, r.RefundDate, r.PosSaleId, r.PaymentMethodId, r.GrandTotal, r.CashierName
            }).ToListAsync(ct);

        var saleIds = rows.Select(x => x.PosSaleId).Distinct().ToList();
        var pmIds = rows.Select(x => x.PaymentMethodId).Distinct().ToList();
        var saleNos = await db.PosSales.AsNoTracking().Where(s => saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.SaleNumber }).ToListAsync(ct);
        var pms = await db.PaymentMethods.AsNoTracking().Where(m => pmIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var items = rows.Select(r => new PosRefundListItemDto(
            r.Id, r.RefundNumber, r.RefundDate,
            saleNos.FirstOrDefault(s => s.Id == r.PosSaleId)?.SaleNumber ?? "—",
            pms.FirstOrDefault(p => p.Id == r.PaymentMethodId)?.Name ?? "—",
            r.GrandTotal, r.CashierName)).ToList();
        return new PagedResult<PosRefundListItemDto>(items, total, page, pageSize);
    }

    private async Task<Dictionary<int, int>> RefundedQtyByLineAsync(int posSaleId, CancellationToken ct) =>
        await (from rl in db.PosRefundLines.AsNoTracking()
               join r in db.PosRefunds.AsNoTracking() on rl.PosRefundId equals r.Id
               where r.PosSaleId == posSaleId
               group rl by rl.PosSaleLineId into g
               select new { LineId = g.Key, Qty = g.Sum(x => x.Quantity) })
              .ToDictionaryAsync(x => x.LineId, x => x.Qty, ct);

    private async Task<PosRefundDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var r = await db.PosRefunds.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var saleNo = await db.PosSales.Where(s => s.Id == r.PosSaleId).Select(s => s.SaleNumber).FirstOrDefaultAsync(ct) ?? "—";
        var pmName = await db.PaymentMethods.Where(m => m.Id == r.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(ct) ?? "—";
        var lines = r.Lines.OrderBy(l => l.Id).Select(l => new PosRefundLineDto(
            l.Id, l.PosSaleLineId, l.ProductVariantId, l.VariantSku, l.ProductName,
            l.Quantity, l.UnitPrice, l.DiscountPercent, l.LineTotal)).ToList();
        return new PosRefundDto(r.Id, r.RefundNumber, r.PosSaleId, saleNo, r.RefundDate,
            r.PaymentMethodId, pmName, r.IsCashPayment,
            r.Subtotal, r.TransactionDiscount, r.TaxTotal, r.GrandTotal,
            r.Reason, r.AuthorizedBy, r.CashierName, lines);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PosRefund", message)]);
}
