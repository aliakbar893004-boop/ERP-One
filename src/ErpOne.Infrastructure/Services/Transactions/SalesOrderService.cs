using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SalesOrderService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreateSalesOrderRequest> createValidator,
    IValidator<UpdateSalesOrderRequest> updateValidator,
    IDocumentNumberService docNumbers) : ISalesOrderService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.SalesOrder;

    public async Task<PagedResult<SalesOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SalesOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.SalesOrders.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.SoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SalesOrderListItemDto(
                p.Id, p.SoNumber,
                db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Currency, p.GrandTotal, p.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<SalesOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<SalesOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.SalesOrders
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(SalesOrderStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new SalesOrderDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(SalesOrderStatus.Draft),
            CountOf(SalesOrderStatus.PendingApproval),
            CountOf(SalesOrderStatus.Confirmed));
    }

    public async Task<SalesOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.AsNoTracking()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return null;

        var customerName = await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = so.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = so.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            return new SalesOrderLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, l.TaxRateSnapshot,
                l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal,
                l.DeliveredQuantity);
        }).ToList();

        return new SalesOrderDto(so.Id, so.SoNumber, so.CustomerId, customerName, so.WarehouseId, warehouseName,
            so.OrderDate, so.ExpectedDate, so.Currency, so.Notes, so.Status.ToString(), so.RejectionNote,
            so.Subtotal, so.DiscountTotal, so.TaxTotal, so.GrandTotal, so.CreatedAt, so.CreatedBy, lines);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<SalesOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, ProductName = p.Name, v.Price, v.DiscountPrice };
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Sku.Contains(term) || x.ProductName.Contains(term));

        return await q.OrderBy(x => x.ProductName).Take(50)
            .Select(x => new SalesOrderVariantOptionDto(x.Id, x.Sku, x.ProductName, x.Price, x.DiscountPrice))
            .ToListAsync(ct);
    }

    public async Task<SalesOrderCreditInfoDto> GetCreditInfoAsync(
        int customerId, decimal thisOrderTotal, int? excludeSoId, CancellationToken ct = default)
    {
        var creditLimit = await db.Customers.Where(c => c.Id == customerId)
            .Select(c => c.CreditLimit).FirstOrDefaultAsync(ct);

        // Outstanding proxy: Σ GrandTotal of this customer's committed SOs (Confirmed +
        // PartiallyDelivered + Delivered), excluding excludeSoId. EF-translatable OR form.
        var estimatedOutstanding = await db.SalesOrders.AsNoTracking()
            .Where(s => s.CustomerId == customerId
                        && (s.Status == SalesOrderStatus.Confirmed
                            || s.Status == SalesOrderStatus.PartiallyDelivered
                            || s.Status == SalesOrderStatus.Delivered)
                        && (excludeSoId == null || s.Id != excludeSoId))
            .SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;

        var exceedsLimit = creditLimit > 0 && (estimatedOutstanding + thisOrderTotal) > creditLimit;
        return new SalesOrderCreditInfoDto(creditLimit, estimatedOutstanding, thisOrderTotal, exceedsLimit);
    }

    public async Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await db.Customers.Where(c => c.Id == request.CustomerId)
            .Select(c => c.DefaultCurrency).FirstOrDefaultAsync(ct) ?? "IDR";
        var soNumber = await docNumbers.NextAsync(DocumentTypes.SalesOrder, request.OrderDate, ct);

        var so = new SalesOrder(soNumber, request.CustomerId, request.WarehouseId,
            request.OrderDate, request.ExpectedDate, currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, ct));

        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(so.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;

        var oldLines = await db.SalesOrderLines.Where(l => l.SalesOrderId == id).ToListAsync(ct);
        db.SalesOrderLines.RemoveRange(oldLines);

        so.UpdateHeader(so.CustomerId, request.WarehouseId, request.OrderDate, request.ExpectedDate, so.Currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, ct));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;
        if (so.Status != SalesOrderStatus.Draft)
            throw Fail("Only a draft sales order can be deleted.");
        db.SalesOrders.Remove(so);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        so.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, so.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, so.Id, ct);
        if (fullyApproved) so.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        var fullyApproved = await approval.ApproveAsync(DocType, so.Id, actingUserName, isInRole, so.CreatedBy, ct);
        if (fullyApproved) so.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        await approval.RejectAsync(DocType, so.Id, actingUserName, isInRole, so.CreatedBy, reason, ct);
        so.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, so.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        so.Cancel();
        await approval.ResetAsync(DocType, so.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> CloseAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        so.Close();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }


    private async Task<List<SalesOrderLine>> BuildLinesAsync(
        IReadOnlyList<SalesOrderLineRequest> requests, CancellationToken ct)
    {
        var taxIds = requests.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        var rates = taxIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.Taxes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Rate, ct);

        var lines = new List<SalesOrderLine>();
        foreach (var l in requests)
        {
            var rate = l.TaxId.HasValue && rates.TryGetValue(l.TaxId.Value, out var r) ? r : 0m;
            lines.Add(new SalesOrderLine(l.ProductVariantId, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, rate));
        }
        return lines;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SalesOrder", message)]);
}
