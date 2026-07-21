using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Application.Stock;
using ErpOne.Application.StockOpnames;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockOpnameService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    IValidator<CreateStockOpnameRequest> createValidator,
    IValidator<UpdateStockOpnameRequest> updateValidator,
    IDocumentNumberService docNumbers,
    ICostingService costing) : IStockOpnameService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.StockOpname;

    public async Task<PagedResult<StockOpnameListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, StockOpnameStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.StockOpnames.AsNoTracking();
        if (status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.OpnameNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockOpnameListItemDto(
                x.Id, x.OpnameNumber, x.OpnameDate,
                db.Warehouses.Where(w => w.Id == x.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                x.Lines.Count,
                x.Lines.Sum(l => l.PhysicalQty - l.SystemQty),
                x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<StockOpnameListItemDto>(items, total, page, pageSize);
    }

    public async Task<StockOpnameDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var o = await db.StockOpnames.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return null;
        var whName = await db.Warehouses.Where(w => w.Id == o.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var variantIds = o.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variantInfo = await (from v in db.ProductVariants.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                                 where variantIds.Contains(v.Id)
                                 select new { v.Id, v.Sku, p.Name }).ToListAsync(ct);
        var info = variantInfo.ToDictionary(x => x.Id, x => (x.Sku, x.Name));
        var lines = new List<StockOpnameLineDto>();
        foreach (var l in o.Lines.OrderBy(l => l.Id))
        {
            var onHand = await stock.GetOnHandAsync(l.ProductVariantId, o.WarehouseId, ct);
            var (sku, name) = info.TryGetValue(l.ProductVariantId, out var x) ? x : ("?", "(unknown)");
            lines.Add(new StockOpnameLineDto(l.Id, l.ProductVariantId, sku, name,
                l.SystemQty, l.PhysicalQty, l.PhysicalQty - l.SystemQty, onHand));
        }
        var steps = await approval.GetStepsAsync(DocType, o.Id, ct);
        return new StockOpnameDto(o.Id, o.OpnameNumber, o.OpnameDate, o.WarehouseId, whName,
            o.Notes, o.Status.ToString(), o.RejectionNote, o.CreatedBy, lines, steps);
    }

    public async Task<StockOpnameDto> CreateAsync(CreateStockOpnameRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var whExists = await db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId, ct);
        if (!whExists) throw Fail("Warehouse not found.");

        var number = await docNumbers.NextAsync(DocumentTypes.StockOpname, request.OpnameDate, ct);
        var o = new StockOpname(number, request.OpnameDate, request.WarehouseId, request.Notes);

        var stocks = await db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == request.WarehouseId)
            .Select(s => new { s.ProductVariantId, s.Quantity })
            .ToListAsync(ct);
        // PhysicalQty initialized to SystemQty; user edits it on the count sheet.
        o.SetLines(stocks.Select(s => (s.ProductVariantId, s.Quantity, s.Quantity)));

        db.StockOpnames.Add(o);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(o.Id, ct))!;
    }

    public async Task<StockOpnameDto> UpdateAsync(int id, UpdateStockOpnameRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        o.UpdateHeader(request.OpnameDate, request.Notes);
        o.SetPhysicalCounts(request.Counts.Select(c => (c.LineId, c.PhysicalQty)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var o = await db.StockOpnames.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Opname not found.");
        if (o.Status != StockOpnameStatus.Draft) throw Fail("Only a draft opname can be deleted.");
        db.StockOpnames.Remove(o);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        o.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, o.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, o.Id, ct);
        if (fullyApproved) await PostAsync(o, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, o.Id, actingUserName, isInRole, o.CreatedBy, ct);
        if (fullyApproved) await PostAsync(o, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Opname not found.");
        await approval.RejectAsync(DocType, o.Id, actingUserName, isInRole, o.CreatedBy, reason, ct);
        o.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, o.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Posts variance vs LIVE on-hand at post time; caller saves + commits. No moving-average change, no GL.
    private async Task PostAsync(StockOpname o, CancellationToken ct)
    {
        foreach (var line in o.Lines)
        {
            var onHand = await stock.GetOnHandAsync(line.ProductVariantId, o.WarehouseId, ct);
            var delta = line.PhysicalQty - onHand;
            if (delta == 0) continue;
            var cost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, o.WarehouseId, Math.Abs(delta), ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, o.WarehouseId, MovementType.Adjustment,
                delta, cost, o.OpnameDate, "StockOpname", o.Id, o.OpnameNumber));
            await db.UpsertStockAsync(line.ProductVariantId, o.WarehouseId, delta, ct);
        }
        o.MarkPosted();
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("StockOpname", message)]);
}
