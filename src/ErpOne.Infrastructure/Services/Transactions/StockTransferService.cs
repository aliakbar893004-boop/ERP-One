using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Application.Stock;
using ErpOne.Application.StockTransfers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockTransferService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    IValidator<CreateStockTransferRequest> validator,
    IDocumentNumberService docNumbers,
    ICostingService costing) : IStockTransferService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.StockTransfer;

    public async Task<PagedResult<StockTransferListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, StockTransferStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.StockTransfers.AsNoTracking();
        if (status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.TransferNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockTransferListItemDto(
                x.Id, x.TransferNumber, x.TransferDate,
                db.Warehouses.Where(w => w.Id == x.SourceWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                db.Warehouses.Where(w => w.Id == x.DestinationWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                x.Lines.Sum(l => l.Quantity), x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<StockTransferListItemDto>(items, total, page, pageSize);
    }

    public async Task<StockTransferDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;
        var srcName = await db.Warehouses.Where(w => w.Id == t.SourceWarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var dstName = await db.Warehouses.Where(w => w.Id == t.DestinationWarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var variantIds = t.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variantInfo = await (from v in db.ProductVariants.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                                 where variantIds.Contains(v.Id)
                                 select new { v.Id, v.Sku, p.Name }).ToListAsync(ct);
        var info = variantInfo.ToDictionary(x => x.Id, x => (x.Sku, x.Name));
        var lines = new List<StockTransferLineDto>();
        foreach (var l in t.Lines)
        {
            var onHand = await stock.GetOnHandAsync(l.ProductVariantId, t.SourceWarehouseId, ct);
            var (sku, name) = info.TryGetValue(l.ProductVariantId, out var x) ? x : ("?", "(unknown)");
            lines.Add(new StockTransferLineDto(l.Id, l.ProductVariantId, sku, name, l.Quantity, onHand));
        }
        var steps = await approval.GetStepsAsync(DocType, t.Id, ct);
        return new StockTransferDto(t.Id, t.TransferNumber, t.TransferDate, t.SourceWarehouseId, srcName,
            t.DestinationWarehouseId, dstName, t.Notes, t.Status.ToString(), t.RejectionNote, t.CreatedBy, lines, steps);
    }

    public async Task<StockTransferDto> CreateAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateWarehousesAsync(request, ct);
        var number = await docNumbers.NextAsync(DocumentTypes.StockTransfer, request.TransferDate, ct);
        var t = new StockTransfer(number, request.TransferDate, request.SourceWarehouseId, request.DestinationWarehouseId, request.Notes);
        t.SetLines(request.Lines.Select(l => (l.ProductVariantId, l.Quantity)));
        db.StockTransfers.Add(t);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(t.Id, ct))!;
    }

    public async Task<StockTransferDto> UpdateAsync(int id, CreateStockTransferRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await ValidateWarehousesAsync(request, ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        t.UpdateHeader(request.TransferDate, request.SourceWarehouseId, request.DestinationWarehouseId, request.Notes);
        t.SetLines(request.Lines.Select(l => (l.ProductVariantId, l.Quantity)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var t = await db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Transfer not found.");
        if (t.Status != StockTransferStatus.Draft) throw Fail("Only a draft transfer can be deleted.");
        db.StockTransfers.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        t.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, t.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, t.Id, ct);
        if (fullyApproved) await PostAsync(t, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, t.Id, actingUserName, isInRole, t.CreatedBy, ct);
        if (fullyApproved) await PostAsync(t, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Transfer not found.");
        await approval.RejectAsync(DocType, t.Id, actingUserName, isInRole, t.CreatedBy, reason, ct);
        t.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, t.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Moves stock; caller saves + commits.
    private async Task PostAsync(StockTransfer t, CancellationToken ct)
    {
        foreach (var g in t.Lines.GroupBy(l => l.ProductVariantId))
        {
            var need = g.Sum(l => l.Quantity);
            var onHand = await stock.GetOnHandAsync(g.Key, t.SourceWarehouseId, ct);
            if (onHand < need) throw Fail($"Insufficient stock at source for variant {g.Key} (need {need}, have {onHand}).");
        }
        foreach (var line in t.Lines)
        {
            var cost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, t.SourceWarehouseId, line.Quantity, ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.SourceWarehouseId, MovementType.Transfer,
                -line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.DestinationWarehouseId, MovementType.Transfer,
                line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            await db.UpsertStockAsync(line.ProductVariantId, t.SourceWarehouseId, -line.Quantity, ct);
            await db.UpsertStockAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, ct);
        }
        t.MarkPosted();
    }

    private async Task ValidateWarehousesAsync(CreateStockTransferRequest r, CancellationToken ct)
    {
        var count = await db.Warehouses.CountAsync(w => (w.Id == r.SourceWarehouseId || w.Id == r.DestinationWarehouseId), ct);
        if (count < 2) throw Fail("Source or destination warehouse not found.");
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("StockTransfer", message)]);
}
