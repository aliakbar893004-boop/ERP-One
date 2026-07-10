using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CashierShiftService(
    AppDbContext db,
    IValidator<OpenShiftRequest> openValidator,
    IValidator<CloseShiftRequest> closeValidator,
    IDocumentNumberService docNumbers) : ICashierShiftService
{
    public async Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken ct = default)
    {
        var ids = await db.CashierShifts.AsNoTracking()
            .Where(s => s.Status == CashierShiftStatus.Open)
            .OrderBy(s => s.OpenedAt)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var list = new List<CashierShiftDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await GetByIdAsync(id, ct);
            if (dto is not null) list.Add(dto);
        }
        return list;
    }

    public async Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken ct = default)
    {
        var id = await db.CashierShifts.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.Status == CashierShiftStatus.Open)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        return id is null ? null : await GetByIdAsync(id.Value, ct);
    }

    public async Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest request, CancellationToken ct = default)
    {
        await openValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.CashierShifts.AnyAsync(s => s.WarehouseId == request.WarehouseId && s.Status == CashierShiftStatus.Open, ct))
            throw Fail("Gudang ini sudah punya shift terbuka. Tutup dulu sebelum membuka yang baru.");

        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId, ct);
        if (warehouse is null || !warehouse.IsActive)
            throw Fail("Gudang tidak ditemukan atau tidak aktif.");

        var now = DateTime.Now;
        var shift = new CashierShift(await docNumbers.NextAsync(DocumentTypes.CashierShift, now, ct),
            request.WarehouseId, userId, userName, request.OpeningFloat, now);

        db.CashierShifts.Add(shift);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(shift.Id, ct))!;
    }

    public async Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest request, CancellationToken ct = default)
    {
        await closeValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
        if (shift is null) return false;
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup.");
        if (shift.CashierUserId != userId) throw Fail("Anda hanya bisa menutup shift milik sendiri.");

        shift.Close(request.CountedCash, request.ClosingNote, DateTime.Now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var shift = await db.CashierShifts.AsNoTracking().Include(s => s.Totals)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shift is null) return null;

        var warehouseName = await db.Warehouses.Where(w => w.Id == shift.WarehouseId)
            .Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var methodIds = shift.Totals.Select(t => t.PaymentMethodId).Distinct().ToList();
        var methods = await db.PaymentMethods.AsNoTracking()
            .Where(m => methodIds.Contains(m.Id)).Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var methodTotals = shift.Totals
            .OrderBy(t => t.Id)
            .Select(t => new ShiftMethodTotalDto(t.PaymentMethodId,
                methods.FirstOrDefault(m => m.Id == t.PaymentMethodId)?.Name ?? "—",
                t.TotalAmount, t.TransactionCount))
            .ToList();

        return new CashierShiftDto(shift.Id, shift.ShiftNumber, shift.WarehouseId, warehouseName,
            shift.CashierUserId, shift.CashierName, shift.Status.ToString(),
            shift.OpenedAt, shift.OpeningFloat, shift.CashSalesTotal, shift.ExpectedCash,
            shift.ClosedAt, shift.CountedCash, shift.CashVariance, shift.ClosingNote,
            shift.TotalSalesAmount, shift.TransactionCount, methodTotals);
    }

    public async Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.CashierShifts.AsNoTracking().AsQueryable();
        if (status is { } st) query = query.Where(s => s.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.ShiftNumber.Contains(search) || s.CashierName.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id, s.ShiftNumber, s.WarehouseId, s.CashierName, s.OpenedAt, s.ClosedAt, s.Status,
                TotalSales = db.CashierShiftTotals.Where(t => t.CashierShiftId == s.Id).Sum(t => (decimal?)t.TotalAmount) ?? 0m
            })
            .ToListAsync(ct);

        var whIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => whIds.Contains(w.Id)).Select(w => new { w.Id, w.Name }).ToListAsync(ct);

        var items = rows.Select(r => new CashierShiftListItemDto(
            r.Id, r.ShiftNumber, r.WarehouseId,
            warehouses.FirstOrDefault(w => w.Id == r.WarehouseId)?.Name ?? "—",
            r.CashierName, r.OpenedAt, r.ClosedAt, r.TotalSales, r.Status.ToString())).ToList();

        return new PagedResult<CashierShiftListItemDto>(items, total, page, pageSize);
    }


    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("CashierShift", message)]);
}
