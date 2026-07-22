using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Application.Purchasing.PurchaseReturns;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PurchaseReturnService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    ICostingService costing,
    IValidator<CreatePurchaseReturnRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IPurchaseReturnService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.PurchaseReturn;

    // ---- Returnable source discovery ------------------------------------------------

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableGrnsAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);
        var q =
            from grn in db.GoodsReceipts.AsNoTracking()
            where grn.Status == GoodsReceiptStatus.Posted
            join po in db.PurchaseOrders.AsNoTracking() on grn.PurchaseOrderId equals po.Id
            join sup in db.Suppliers.AsNoTracking() on po.SupplierId equals sup.Id
            select new { grn.Id, grn.GrnNumber, grn.ReceiptDate, po.SupplierId, SupplierName = sup.Name, grn.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.GrnNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        return rows.Where(x => x.Lines.Any(l => l.QuantityReceived - returnedByGrnLine.GetValueOrDefault(l.Id) > 0))
            .Select(x => new ReturnableSourceOptionDto("GoodsReceipt", x.Id, x.GrnNumber, x.ReceiptDate, x.SupplierName))
            .ToList();
    }

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);
        var q =
            from inv in db.SupplierInvoices.AsNoTracking()
            where inv.Status != SupplierInvoiceStatus.Cancelled && (inv.GrandTotal - inv.PaidAmount - inv.CreditedAmount) > 0
            join sup in db.Suppliers.AsNoTracking() on inv.SupplierId equals sup.Id
            select new { inv.Id, inv.InvoiceNumber, inv.InvoiceDate, inv.SupplierId, SupplierName = sup.Name, inv.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.InvoiceNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        return rows.Where(x => x.Lines.Any(l => l.Quantity - returnedByGrnLine.GetValueOrDefault(l.GoodsReceiptLineId) > 0))
            .Select(x => new ReturnableSourceOptionDto("SupplierInvoice", x.Id, x.InvoiceNumber, x.InvoiceDate, x.SupplierName))
            .ToList();
    }

    public async Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);

        if (sourceType == "GoodsReceipt")
        {
            var grn = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines)
                .FirstOrDefaultAsync(g => g.Id == docId && g.Status == GoodsReceiptStatus.Posted, ct);
            if (grn is null) return null;
            var po = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == grn.PurchaseOrderId, ct);
            var sup = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == po.SupplierId, ct);
            var whName = await WarehouseNameAsync(po.WarehouseId, ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var gl in grn.Lines)
            {
                var remaining = gl.QuantityReceived - returnedByGrnLine.GetValueOrDefault(gl.Id);
                if (remaining <= 0) continue;
                var (sku, name) = await VariantInfoAsync(gl.ProductVariantId, ct);
                lines.Add(new ReturnableLineDto(gl.Id, null, gl.ProductVariantId, sku, name, po.WarehouseId, whName,
                    gl.QuantityReceived, returnedByGrnLine.GetValueOrDefault(gl.Id), remaining, gl.UnitCost, gl.UnitCost, 0m, 0m));
            }
            return new ReturnableSourceDto("GoodsReceipt", grn.Id, null, grn.GrnNumber, po.SupplierId, sup.Name, lines);
        }

        if (sourceType == "SupplierInvoice")
        {
            var inv = await db.SupplierInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == docId, ct);
            if (inv is null) return null;
            var sup = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == inv.SupplierId, ct);
            var returnedByInvLine = await ReturnedQtyByInvoiceLineAsync(ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var il in inv.Lines)
            {
                var grnLine = await db.GoodsReceiptLines.AsNoTracking().FirstAsync(g => g.Id == il.GoodsReceiptLineId, ct);
                var grn = await db.GoodsReceipts.AsNoTracking().FirstAsync(g => g.Id == grnLine.GoodsReceiptId, ct);
                var po = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == grn.PurchaseOrderId, ct);
                var grnRemaining = grnLine.QuantityReceived - returnedByGrnLine.GetValueOrDefault(il.GoodsReceiptLineId);
                var invRemaining = il.Quantity - returnedByInvLine.GetValueOrDefault(il.Id);
                var remaining = Math.Min(grnRemaining, invRemaining);
                if (remaining <= 0) continue;
                var (sku, name) = await VariantInfoAsync(il.ProductVariantId, ct);
                lines.Add(new ReturnableLineDto(il.GoodsReceiptLineId, il.Id, il.ProductVariantId, sku, name,
                    po.WarehouseId, await WarehouseNameAsync(po.WarehouseId, ct), il.Quantity,
                    il.Quantity - invRemaining, remaining, grnLine.UnitCost, il.UnitPrice, il.DiscountPercent, il.TaxRateSnapshot));
            }
            return new ReturnableSourceDto("SupplierInvoice", null, inv.Id, inv.InvoiceNumber, inv.SupplierId, sup.Name, lines);
        }

        return null;
    }

    // ---- CRUD -----------------------------------------------------------------------

    public async Task<PurchaseReturnDto> CreateAsync(CreatePurchaseReturnRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var docId = request.SourceType == "GoodsReceipt" ? request.GoodsReceiptId!.Value : request.SupplierInvoiceId!.Value;
        var source = await GetReturnableSourceAsync(request.SourceType, docId, ct)
            ?? throw Fail("Source document not found or not returnable.");

        var number = await docNumbers.NextAsync(DocumentTypes.PurchaseReturn, request.ReturnDate, ct);
        var sourceType = Enum.Parse<PurchaseReturnSource>(request.SourceType);
        var pr = new PurchaseReturn(number, source.SupplierId, sourceType, source.GoodsReceiptId, source.SupplierInvoiceId,
            request.ReturnDate, request.Notes);
        pr.SetLines(BuildLines(request.Lines, source));
        db.PurchaseReturns.Add(pr);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(pr.Id, ct))!;
    }

    public async Task<PurchaseReturnDto> UpdateAsync(int id, UpdatePurchaseReturnRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Return not found.");
        var docId = pr.SourceType == PurchaseReturnSource.GoodsReceipt ? pr.GoodsReceiptId!.Value : pr.SupplierInvoiceId!.Value;
        var source = await GetReturnableSourceForUpdateAsync(pr.SourceType.ToString(), docId, id, ct)
            ?? throw Fail("Source document not found or not returnable.");

        pr.UpdateHeader(request.ReturnDate, request.Notes);
        pr.SetLines(BuildLines(request.Lines, source));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var pr = await db.PurchaseReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        if (pr.Status != PurchaseReturnStatus.Draft) throw Fail("Only a draft return can be deleted.");
        db.PurchaseReturns.Remove(pr);
        await db.SaveChangesAsync(ct);
    }

    // ---- Approval lifecycle (mirror StockTransferService) ---------------------------

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        pr.Submit();
        await db.SaveChangesAsync(ct);
        await approval.ResetAsync(DocType, pr.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, pr.Id, ct);
        if (fullyApproved) await PostAsync(pr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, pr.Id, actingUserName, isInRole, pr.CreatedBy, ct);
        if (fullyApproved) await PostAsync(pr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        await approval.RejectAsync(DocType, pr.Id, actingUserName, isInRole, pr.CreatedBy, reason, ct);
        pr.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, pr.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Posting: stock out via seam, AP credit (Invoice), GL. Caller saves + commits.
    private async Task PostAsync(PurchaseReturn r, CancellationToken ct)
    {
        // Phase 1: validate on-hand for every line (DB on-hand − accumulated taken).
        var taken = new Dictionary<(int, int), int>();
        foreach (var line in r.Lines)
        {
            var key = (line.ProductVariantId, line.WarehouseId);
            var onHand = await stock.GetOnHandAsync(line.ProductVariantId, line.WarehouseId, ct);
            var already = taken.GetValueOrDefault(key);
            if (onHand - already < line.Quantity)
                throw Fail($"Stok tidak cukup untuk retur varian {line.ProductVariantId} (butuh {line.Quantity}, tersedia {onHand - already}).");
            taken[key] = already + line.Quantity;
        }
        // Phase 2: mutate — cost from seam, stock out, refresh line cost.
        foreach (var line in r.Lines)
        {
            var unitCost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, line.WarehouseId, MovementType.Out,
                -line.Quantity, unitCost, r.ReturnDate, "PurchaseReturn", r.Id, r.ReturnNumber));
            await db.UpsertStockAsync(line.ProductVariantId, line.WarehouseId, -line.Quantity, ct);
            line.SetUnitCost(unitCost); // COGS/inventory basis snapshot
        }
        r.RecomputeInventoryTotal();

        // AP credit (Invoice path).
        if (r.SourceType == PurchaseReturnSource.SupplierInvoice)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == r.SupplierInvoiceId, ct)
                ?? throw Fail("Supplier invoice not found.");
            if (r.GrandTotal > inv.Outstanding) throw Fail("Retur melebihi Outstanding invoice.");
            inv.ApplyCredit(r.GrandTotal);
        }

        await journalPoster.PostPurchaseReturnAsync(r, ct);
        r.MarkPosted();
    }

    // ---- Queries --------------------------------------------------------------------

    public async Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await db.PurchaseReturns.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var supplierName = await db.Suppliers.AsNoTracking().Where(s => s.Id == r.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        string? grnNumber = r.GoodsReceiptId is int gid
            ? await db.GoodsReceipts.AsNoTracking().Where(g => g.Id == gid).Select(g => g.GrnNumber).FirstOrDefaultAsync(ct) : null;
        string? invNumber = r.SupplierInvoiceId is int iid
            ? await db.SupplierInvoices.AsNoTracking().Where(i => i.Id == iid).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct) : null;
        var steps = await approval.GetStepsAsync(DocType, r.Id, ct);

        var lineDtos = new List<PurchaseReturnLineDto>();
        foreach (var l in r.Lines)
            lineDtos.Add(new PurchaseReturnLineDto(l.Id, l.GoodsReceiptLineId, l.SupplierInvoiceLineId, l.ProductVariantId,
                l.VariantSku, l.ProductName, await WarehouseNameAsync(l.WarehouseId, ct), l.Quantity, l.UnitCost,
                l.UnitPrice, l.DiscountPercent, l.TaxRateSnapshot, l.LineTotal));

        return new PurchaseReturnDto(r.Id, r.ReturnNumber, r.SourceType.ToString(), r.GoodsReceiptId, grnNumber,
            r.SupplierInvoiceId, invNumber, r.SupplierId, supplierName, r.ReturnDate, r.Notes, r.Status.ToString(),
            r.RejectionNote, r.CreatedBy, r.Subtotal, r.DiscountTotal, r.TaxTotal, r.GrandTotal, r.InventoryTotal, lineDtos, steps);
    }

    public async Task<PagedResult<PurchaseReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        PurchaseReturnStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.PurchaseReturns.AsNoTracking();
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.ReturnNumber.Contains(search));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PurchaseReturnListItemDto(x.Id, x.ReturnNumber, x.ReturnDate, x.SourceType.ToString(),
                db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                x.Lines.Count, x.GrandTotal, x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<PurchaseReturnListItemDto>(items, total, page, pageSize);
    }

    // ---- Helpers --------------------------------------------------------------------

    private IEnumerable<PurchaseReturnLine> BuildLines(IReadOnlyList<PurchaseReturnLineInput> inputs, ReturnableSourceDto source)
    {
        foreach (var input in inputs)
        {
            var cand = source.Lines.FirstOrDefault(l => l.GoodsReceiptLineId == input.GoodsReceiptLineId
                && l.SupplierInvoiceLineId == input.SupplierInvoiceLineId)
                ?? throw Fail($"Line {input.GoodsReceiptLineId} is not returnable on this source.");
            if (input.Quantity <= 0 || input.Quantity > cand.RemainingQty)
                throw Fail($"Return quantity {input.Quantity} exceeds remaining {cand.RemainingQty} for line {input.GoodsReceiptLineId}.");
            yield return new PurchaseReturnLine(cand.GoodsReceiptLineId, cand.SupplierInvoiceLineId, cand.ProductVariantId,
                cand.WarehouseId, cand.Sku, cand.ProductName, input.Quantity, cand.UnitCost, cand.UnitPrice,
                cand.DiscountPercent, cand.TaxRateSnapshot);
        }
    }

    // returned qty grouped by GRN line, counting PendingApproval + Posted returns.
    private async Task<Dictionary<int, int>> ReturnedQtyByGrnLineAsync(CancellationToken ct) =>
        await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => db.PurchaseReturns.Any(r => r.Id == l.PurchaseReturnId
                && (r.Status == PurchaseReturnStatus.PendingApproval || r.Status == PurchaseReturnStatus.Posted)))
            .GroupBy(l => l.GoodsReceiptLineId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<Dictionary<int, int>> ReturnedQtyByInvoiceLineAsync(CancellationToken ct) =>
        await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => l.SupplierInvoiceLineId != null && db.PurchaseReturns.Any(r => r.Id == l.PurchaseReturnId
                && (r.Status == PurchaseReturnStatus.PendingApproval || r.Status == PurchaseReturnStatus.Posted)))
            .GroupBy(l => l.SupplierInvoiceLineId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    // Update variant: recompute returnable EXCLUDING this document's own lines (so its own qty is editable).
    private async Task<ReturnableSourceDto?> GetReturnableSourceForUpdateAsync(string sourceType, int docId, int excludeReturnId, CancellationToken ct)
    {
        var basis = await GetReturnableSourceAsync(sourceType, docId, ct);
        if (basis is null) return null;
        var mine = await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => l.PurchaseReturnId == excludeReturnId)
            .GroupBy(l => l.GoodsReceiptLineId).Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var lines = basis.Lines.Select(l => l with { RemainingQty = l.RemainingQty + mine.GetValueOrDefault(l.GoodsReceiptLineId) }).ToList();
        return basis with { Lines = lines };
    }

    private async Task<(string sku, string name)> VariantInfoAsync(int variantId, CancellationToken ct)
    {
        var row = await (from v in db.ProductVariants.AsNoTracking()
                         join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                         where v.Id == variantId select new { v.Sku, p.Name }).FirstAsync(ct);
        return (row.Sku, row.Name);
    }

    private async Task<string> WarehouseNameAsync(int warehouseId, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().Where(w => w.Id == warehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PurchaseReturn", message)]);
}
