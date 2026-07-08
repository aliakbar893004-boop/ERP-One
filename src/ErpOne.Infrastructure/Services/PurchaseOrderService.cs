using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PurchaseOrderService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreatePurchaseOrderRequest> createValidator,
    IValidator<UpdatePurchaseOrderRequest> updateValidator) : IPurchaseOrderService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.PurchaseOrder;

    public async Task<PagedResult<PurchaseOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, PurchaseOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.PurchaseOrders.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.PoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PurchaseOrderListItemDto(
                p.Id, p.PoNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Currency, p.GrandTotal, p.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<PurchaseOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<PurchaseOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.PurchaseOrders
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(PurchaseOrderStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new PurchaseOrderDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(PurchaseOrderStatus.Draft),
            CountOf(PurchaseOrderStatus.PendingApproval),
            CountOf(PurchaseOrderStatus.Confirmed));
    }

    public async Task<PurchaseOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = po.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = po.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            return new PurchaseOrderLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, l.TaxRateSnapshot,
                l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal, l.ReceivedQuantity);
        }).ToList();

        return new PurchaseOrderDto(po.Id, po.PoNumber, po.SupplierId, supplierName, po.WarehouseId, warehouseName,
            po.OrderDate, po.ExpectedDate, po.Currency, po.Notes, po.Status.ToString(), po.RejectionNote,
            po.Subtotal, po.DiscountTotal, po.TaxTotal, po.GrandTotal, po.CreatedAt, po.CreatedBy, lines);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<PurchaseOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, ProductName = p.Name, v.CostPrice };
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Sku.Contains(term) || x.ProductName.Contains(term));

        return await q.OrderBy(x => x.ProductName).Take(50)
            .Select(x => new PurchaseOrderVariantOptionDto(x.Id, x.Sku, x.ProductName, x.CostPrice))
            .ToListAsync(ct);
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await db.Suppliers.Where(s => s.Id == request.SupplierId)
            .Select(s => s.DefaultCurrency).FirstOrDefaultAsync(ct) ?? "IDR";
        var poNumber = await GenerateNumberAsync(request.OrderDate, ct);

        var po = new PurchaseOrder(poNumber, request.SupplierId, request.WarehouseId,
            request.OrderDate, request.ExpectedDate, currency, request.Notes);
        po.SetLines(await BuildLinesAsync(request.Lines, ct));

        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(po.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;

        var oldLines = await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(ct);
        db.PurchaseOrderLines.RemoveRange(oldLines);

        po.UpdateHeader(request.SupplierId, request.WarehouseId, request.OrderDate, request.ExpectedDate, po.Currency, request.Notes);
        po.SetLines(await BuildLinesAsync(request.Lines, ct));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;
        if (po.Status != PurchaseOrderStatus.Draft)
            throw Fail("Only a draft purchase order can be deleted.");
        db.PurchaseOrders.Remove(po);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        po.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, po.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, po.Id, ct);
        if (fullyApproved) po.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        var fullyApproved = await approval.ApproveAsync(DocType, po.Id, actingUserName, isInRole, po.CreatedBy, ct);
        if (fullyApproved) po.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        await approval.RejectAsync(DocType, po.Id, actingUserName, isInRole, po.CreatedBy, reason, ct);
        po.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, po.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        po.Cancel();
        await approval.ResetAsync(DocType, po.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> CloseAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        po.Close();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<string> GenerateNumberAsync(DateTime orderDate, CancellationToken ct)
    {
        var prefix = $"PO-{orderDate:yyyyMM}-";
        var last = await db.PurchaseOrders.AsNoTracking()
            .Where(p => p.PoNumber.StartsWith(prefix))
            .OrderByDescending(p => p.PoNumber)
            .Select(p => p.PoNumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private async Task<List<PurchaseOrderLine>> BuildLinesAsync(
        IReadOnlyList<PurchaseOrderLineRequest> requests, CancellationToken ct)
    {
        var taxIds = requests.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        var rates = taxIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.Taxes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Rate, ct);

        var lines = new List<PurchaseOrderLine>();
        foreach (var l in requests)
        {
            var rate = l.TaxId.HasValue && rates.TryGetValue(l.TaxId.Value, out var r) ? r : 0m;
            lines.Add(new PurchaseOrderLine(l.ProductVariantId, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, rate));
        }
        return lines;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PurchaseOrder", message)]);
}
