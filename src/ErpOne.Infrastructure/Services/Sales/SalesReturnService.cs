using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Application.Sales.SalesReturns;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SalesReturnService(
    AppDbContext db,
    IApprovalService approval,
    ICostingService costing,
    IValidator<CreateSalesReturnRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : ISalesReturnService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.SalesReturn;

    // ---- Returnable source discovery ------------------------------------------------

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableDeliveryOrdersAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByDoLine = await ReturnedQtyByDoLineAsync(ct);
        var q =
            from d in db.DeliveryOrders.AsNoTracking()
            where d.Status == DeliveryOrderStatus.Posted
            join so in db.SalesOrders.AsNoTracking() on d.SalesOrderId equals so.Id
            join cust in db.Customers.AsNoTracking() on so.CustomerId equals cust.Id
            select new { d.Id, d.DoNumber, d.DeliveryDate, so.CustomerId, CustomerName = cust.Name, d.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.DoNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        return rows.Where(x => x.Lines.Any(l => l.QuantityDelivered - returnedByDoLine.GetValueOrDefault(l.Id) > 0))
            .Select(x => new ReturnableSourceOptionDto("DeliveryOrder", x.Id, x.DoNumber, x.DeliveryDate, x.CustomerName))
            .ToList();
    }

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByInvLine = await ReturnedQtyByInvoiceLineAsync(ct);
        var q =
            from inv in db.CustomerInvoices.AsNoTracking()
            where inv.Status != CustomerInvoiceStatus.Cancelled && (inv.GrandTotal - inv.PaidAmount - inv.CreditedAmount) > 0
            join cust in db.Customers.AsNoTracking() on inv.CustomerId equals cust.Id
            select new { inv.Id, inv.InvoiceNumber, inv.InvoiceDate, inv.CustomerId, CustomerName = cust.Name, inv.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.InvoiceNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        return rows.Where(x => x.Lines.Any(l => l.Quantity - returnedByInvLine.GetValueOrDefault(l.Id) > 0))
            .Select(x => new ReturnableSourceOptionDto("CustomerInvoice", x.Id, x.InvoiceNumber, x.InvoiceDate, x.CustomerName))
            .ToList();
    }

    public async Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default)
    {
        var returnedByDoLine = await ReturnedQtyByDoLineAsync(ct);

        if (sourceType == "DeliveryOrder")
        {
            var doc = await db.DeliveryOrders.AsNoTracking().Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == docId && d.Status == DeliveryOrderStatus.Posted, ct);
            if (doc is null) return null;
            var so = await db.SalesOrders.AsNoTracking().FirstAsync(s => s.Id == doc.SalesOrderId, ct);
            var cust = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == so.CustomerId, ct);
            var whName = await WarehouseNameAsync(so.WarehouseId, ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var dl in doc.Lines)
            {
                var remaining = dl.QuantityDelivered - returnedByDoLine.GetValueOrDefault(dl.Id);
                if (remaining <= 0) continue;
                var (sku, name) = await VariantInfoAsync(dl.ProductVariantId, ct);
                lines.Add(new ReturnableLineDto(dl.Id, null, dl.ProductVariantId, sku, name, so.WarehouseId, whName,
                    dl.QuantityDelivered, returnedByDoLine.GetValueOrDefault(dl.Id), remaining, dl.UnitCost, dl.UnitCost, 0m, 0m));
            }
            return new ReturnableSourceDto("DeliveryOrder", doc.Id, null, doc.DoNumber, so.CustomerId, cust.Name, lines);
        }

        if (sourceType == "CustomerInvoice")
        {
            var inv = await db.CustomerInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == docId, ct);
            if (inv is null) return null;
            var cust = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == inv.CustomerId, ct);
            var returnedByInvLine = await ReturnedQtyByInvoiceLineAsync(ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var il in inv.Lines)
            {
                var invRemaining = il.Quantity - returnedByInvLine.GetValueOrDefault(il.Id);
                if (invRemaining <= 0) continue;
                var so = await db.SalesOrders.AsNoTracking().FirstAsync(s => s.Id == il.SalesOrderId, ct);
                var whName = await WarehouseNameAsync(so.WarehouseId, ct);
                var doLines = await (from dl in db.DeliveryOrderLines.AsNoTracking()
                                     join d in db.DeliveryOrders.AsNoTracking() on dl.DeliveryOrderId equals d.Id
                                     where dl.SalesOrderLineId == il.SalesOrderLineId && d.Status == DeliveryOrderStatus.Posted
                                     select dl).ToListAsync(ct);
                foreach (var dl in doLines)
                {
                    var doRemaining = dl.QuantityDelivered - returnedByDoLine.GetValueOrDefault(dl.Id);
                    var remaining = Math.Min(doRemaining, invRemaining);
                    if (remaining <= 0) continue;
                    var (sku, name) = await VariantInfoAsync(dl.ProductVariantId, ct);
                    lines.Add(new ReturnableLineDto(dl.Id, il.Id, dl.ProductVariantId, sku, name, so.WarehouseId, whName,
                        il.Quantity, il.Quantity - invRemaining, remaining, dl.UnitCost, il.UnitPrice, il.DiscountPercent, il.TaxRateSnapshot));
                }
            }
            return new ReturnableSourceDto("CustomerInvoice", null, inv.Id, inv.InvoiceNumber, inv.CustomerId, cust.Name, lines);
        }

        return null;
    }

    // ---- CRUD -----------------------------------------------------------------------

    public async Task<SalesReturnDto> CreateAsync(CreateSalesReturnRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var docId = request.SourceType == "DeliveryOrder" ? request.DeliveryOrderId!.Value : request.CustomerInvoiceId!.Value;
        var source = await GetReturnableSourceAsync(request.SourceType, docId, ct)
            ?? throw Fail("Source document not found or not returnable.");

        var number = await docNumbers.NextAsync(DocumentTypes.SalesReturn, request.ReturnDate, ct);
        var sourceType = Enum.Parse<SalesReturnSource>(request.SourceType);
        var sr = new SalesReturn(number, source.CustomerId, sourceType, source.DeliveryOrderId, source.CustomerInvoiceId,
            request.ReturnDate, request.Notes);
        sr.SetLines(BuildLines(request.Lines, source));
        db.SalesReturns.Add(sr);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(sr.Id, ct))!;
    }

    public async Task<SalesReturnDto> UpdateAsync(int id, UpdateSalesReturnRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sr = await db.SalesReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Return not found.");
        var docId = sr.SourceType == SalesReturnSource.DeliveryOrder ? sr.DeliveryOrderId!.Value : sr.CustomerInvoiceId!.Value;
        var source = await GetReturnableSourceForUpdateAsync(sr.SourceType.ToString(), docId, id, ct)
            ?? throw Fail("Source document not found or not returnable.");

        sr.UpdateHeader(request.ReturnDate, request.Notes);
        sr.SetLines(BuildLines(request.Lines, source));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var sr = await db.SalesReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        if (sr.Status != SalesReturnStatus.Draft) throw Fail("Only a draft return can be deleted.");
        db.SalesReturns.Remove(sr);
        await db.SaveChangesAsync(ct);
    }

    // ---- Approval lifecycle ---------------------------------------------------------

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sr = await db.SalesReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        sr.Submit();
        await db.SaveChangesAsync(ct);
        await approval.ResetAsync(DocType, sr.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, sr.Id, ct);
        if (fullyApproved) await PostAsync(sr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sr = await db.SalesReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, sr.Id, actingUserName, isInRole, sr.CreatedBy, ct);
        if (fullyApproved) await PostAsync(sr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sr = await db.SalesReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        await approval.RejectAsync(DocType, sr.Id, actingUserName, isInRole, sr.CreatedBy, reason, ct);
        sr.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, sr.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Posting: stock IN via seam at DO COGS snapshot, AR credit (Invoice), GL. Caller saves + commits.
    private async Task PostAsync(SalesReturn r, CancellationToken ct)
    {
        foreach (var line in r.Lines)
        {
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, line.WarehouseId, MovementType.In,
                line.Quantity, line.UnitCost, r.ReturnDate, "SalesReturn", r.Id, r.ReturnNumber));
            await db.UpsertStockAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, ct);
            await costing.OnInboundAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, line.UnitCost, ct);
        }
        r.RecomputeInventoryTotal();

        if (r.SourceType == SalesReturnSource.CustomerInvoice)
        {
            var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == r.CustomerInvoiceId, ct)
                ?? throw Fail("Customer invoice not found.");
            if (r.GrandTotal > inv.Outstanding) throw Fail("Retur melebihi Outstanding invoice.");
            inv.ApplyCredit(r.GrandTotal);
        }

        await journalPoster.PostSalesReturnAsync(r, ct);
        r.MarkPosted();
    }

    // ---- Queries --------------------------------------------------------------------

    public async Task<SalesReturnDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await db.SalesReturns.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var customerName = await db.Customers.AsNoTracking().Where(c => c.Id == r.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        string? doNumber = r.DeliveryOrderId is int did
            ? await db.DeliveryOrders.AsNoTracking().Where(d => d.Id == did).Select(d => d.DoNumber).FirstOrDefaultAsync(ct) : null;
        string? invNumber = r.CustomerInvoiceId is int iid
            ? await db.CustomerInvoices.AsNoTracking().Where(i => i.Id == iid).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct) : null;
        var steps = await approval.GetStepsAsync(DocType, r.Id, ct);

        var lineDtos = new List<SalesReturnLineDto>();
        foreach (var l in r.Lines)
            lineDtos.Add(new SalesReturnLineDto(l.Id, l.DeliveryOrderLineId, l.CustomerInvoiceLineId, l.ProductVariantId,
                l.VariantSku, l.ProductName, await WarehouseNameAsync(l.WarehouseId, ct), l.Quantity, l.UnitCost,
                l.UnitPrice, l.DiscountPercent, l.TaxRateSnapshot, l.LineTotal));

        return new SalesReturnDto(r.Id, r.ReturnNumber, r.SourceType.ToString(), r.DeliveryOrderId, doNumber,
            r.CustomerInvoiceId, invNumber, r.CustomerId, customerName, r.ReturnDate, r.Notes, r.Status.ToString(),
            r.RejectionNote, r.CreatedBy, r.Subtotal, r.DiscountTotal, r.TaxTotal, r.GrandTotal, r.InventoryTotal, lineDtos, steps);
    }

    public async Task<PagedResult<SalesReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        SalesReturnStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.SalesReturns.AsNoTracking();
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.ReturnNumber.Contains(search));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SalesReturnListItemDto(x.Id, x.ReturnNumber, x.ReturnDate, x.SourceType.ToString(),
                db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                x.Lines.Count, x.GrandTotal, x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<SalesReturnListItemDto>(items, total, page, pageSize);
    }

    // ---- Helpers --------------------------------------------------------------------

    private IEnumerable<SalesReturnLine> BuildLines(IReadOnlyList<SalesReturnLineInput> inputs, ReturnableSourceDto source)
    {
        // Per-invoice-line aggregate cap (a SO line may map to several DO lines).
        foreach (var invGroup in inputs.Where(i => i.CustomerInvoiceLineId is > 0).GroupBy(i => i.CustomerInvoiceLineId!.Value))
        {
            var cand = source.Lines.FirstOrDefault(l => l.CustomerInvoiceLineId == invGroup.Key);
            if (cand is null) continue;
            var invRemaining = cand.SourceQty - cand.AlreadyReturnedQty;
            if (invGroup.Sum(i => i.Quantity) > invRemaining)
                throw Fail($"Total return {invGroup.Sum(i => i.Quantity)} exceeds invoiced remaining {invRemaining} for invoice line {invGroup.Key}.");
        }

        foreach (var input in inputs)
        {
            var cand = source.Lines.FirstOrDefault(l => l.DeliveryOrderLineId == input.DeliveryOrderLineId
                && l.CustomerInvoiceLineId == input.CustomerInvoiceLineId)
                ?? throw Fail($"Line {input.DeliveryOrderLineId} is not returnable on this source.");
            if (input.Quantity <= 0 || input.Quantity > cand.RemainingQty)
                throw Fail($"Return quantity {input.Quantity} exceeds remaining {cand.RemainingQty} for line {input.DeliveryOrderLineId}.");
            yield return new SalesReturnLine(cand.DeliveryOrderLineId, cand.CustomerInvoiceLineId, cand.ProductVariantId,
                cand.WarehouseId, cand.Sku, cand.ProductName, input.Quantity, cand.UnitCost, cand.UnitPrice,
                cand.DiscountPercent, cand.TaxRateSnapshot);
        }
    }

    private async Task<Dictionary<int, int>> ReturnedQtyByDoLineAsync(CancellationToken ct) =>
        await db.SalesReturnLines.AsNoTracking()
            .Where(l => db.SalesReturns.Any(r => r.Id == l.SalesReturnId
                && (r.Status == SalesReturnStatus.PendingApproval || r.Status == SalesReturnStatus.Posted)))
            .GroupBy(l => l.DeliveryOrderLineId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<Dictionary<int, int>> ReturnedQtyByInvoiceLineAsync(CancellationToken ct) =>
        await db.SalesReturnLines.AsNoTracking()
            .Where(l => l.CustomerInvoiceLineId != null && db.SalesReturns.Any(r => r.Id == l.SalesReturnId
                && (r.Status == SalesReturnStatus.PendingApproval || r.Status == SalesReturnStatus.Posted)))
            .GroupBy(l => l.CustomerInvoiceLineId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<ReturnableSourceDto?> GetReturnableSourceForUpdateAsync(string sourceType, int docId, int excludeReturnId, CancellationToken ct)
    {
        var basis = await GetReturnableSourceAsync(sourceType, docId, ct);
        if (basis is null) return null;
        var mine = await db.SalesReturnLines.AsNoTracking()
            .Where(l => l.SalesReturnId == excludeReturnId)
            .GroupBy(l => l.DeliveryOrderLineId).Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var lines = basis.Lines.Select(l => l with { RemainingQty = l.RemainingQty + mine.GetValueOrDefault(l.DeliveryOrderLineId) }).ToList();
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
        new([new FluentValidation.Results.ValidationFailure("SalesReturn", message)]);
}
