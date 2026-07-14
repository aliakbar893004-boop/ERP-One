using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SupplierInvoiceService(
    AppDbContext db,
    IValidator<CreateSupplierInvoiceRequest> createValidator,
    IValidator<UpdateSupplierInvoiceHeaderRequest> updateValidator,
    IDocumentNumberService docNumbers) : ISupplierInvoiceService
{
    public async Task<PagedResult<SupplierInvoiceListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SupplierInvoiceStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.SupplierInvoices.AsNoTracking();
        if (status is { } st) query = query.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.InvoiceNumber.Contains(search) || (i.SupplierInvoiceNo != null && i.SupplierInvoiceNo.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new SupplierInvoiceListItemDto(
                i.Id, i.InvoiceNumber,
                db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                i.InvoiceDate, i.DueDate, i.Currency, i.GrandTotal, i.PaidAmount, i.GrandTotal - i.PaidAmount, i.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<SupplierInvoiceListItemDto>(items, total, page, pageSize);
    }

    public async Task<SupplierInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.SupplierInvoices.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Outstanding = g.Sum(x => x.GrandTotal - x.PaidAmount) })
            .ToListAsync(ct);

        int CountOf(SupplierInvoiceStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var outstanding = rows.Where(r => r.Status != SupplierInvoiceStatus.Cancelled).Sum(r => r.Outstanding);

        return new SupplierInvoiceDashboardDto(
            rows.Sum(r => r.Count),
            CountOf(SupplierInvoiceStatus.Open),
            CountOf(SupplierInvoiceStatus.PartiallyPaid),
            CountOf(SupplierInvoiceStatus.Paid),
            outstanding);
    }

    public async Task<SupplierInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.SupplierInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == inv.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var lines = await BuildLineDtosAsync(inv.Lines, ct);

        return new SupplierInvoiceDto(inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNo, inv.SupplierId, supplierName,
            inv.Currency, inv.InvoiceDate, inv.DueDate, inv.Notes, inv.Status.ToString(),
            inv.Subtotal, inv.DiscountTotal, inv.TaxTotal, inv.GrandTotal, inv.PaidAmount, inv.Outstanding,
            inv.CreatedAt, inv.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<UninvoicedGrnDto>> GetUninvoicedGrnsAsync(int supplierId, CancellationToken ct = default)
    {
        var invoicedGrnIds = await (
            from l in db.SupplierInvoiceLines.AsNoTracking()
            join inv in db.SupplierInvoices.AsNoTracking() on l.SupplierInvoiceId equals inv.Id
            where inv.Status != SupplierInvoiceStatus.Cancelled
            select l.GoodsReceiptId).Distinct().ToListAsync(ct);

        var grns = await (
            from g in db.GoodsReceipts.AsNoTracking()
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            where g.Status == GoodsReceiptStatus.Posted
                  && po.SupplierId == supplierId
                  && !invoicedGrnIds.Contains(g.Id)
            select new { g.Id, g.GrnNumber, g.ReceiptDate, po.PoNumber }).ToListAsync(ct);

        var result = new List<UninvoicedGrnDto>();
        foreach (var g in grns)
        {
            var grnLines = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.GoodsReceiptId == g.Id).ToListAsync(ct);
            var invLines = await BuildDerivedLinesAsync(g.Id, g.GrnNumber, grnLines, ct);
            result.Add(new UninvoicedGrnDto(g.Id, g.GrnNumber, g.ReceiptDate, g.PoNumber,
                invLines.Sum(l => l.LineTotal), invLines));
        }
        return result;
    }

    public async Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grnIds = request.GrnIds.Distinct().ToList();

        var grns = await db.GoodsReceipts.AsNoTracking().Where(g => grnIds.Contains(g.Id)).ToListAsync(ct);
        if (grns.Count != grnIds.Count) throw Fail("One or more goods receipts were not found.");
        if (grns.Any(g => g.Status != GoodsReceiptStatus.Posted)) throw Fail("Only posted goods receipts can be invoiced.");

        var poIds = grns.Select(g => g.PurchaseOrderId).Distinct().ToList();
        var pos = await db.PurchaseOrders.AsNoTracking().Where(p => poIds.Contains(p.Id)).ToListAsync(ct);
        if (pos.Any(p => p.SupplierId != request.SupplierId))
            throw Fail("All goods receipts must belong to the selected supplier.");
        if (pos.Select(p => p.Currency).Distinct().Count() > 1)
            throw Fail("All goods receipts must share the same currency.");

        var alreadyInvoiced = await (
            from l in db.SupplierInvoiceLines
            join inv in db.SupplierInvoices on l.SupplierInvoiceId equals inv.Id
            where inv.Status != SupplierInvoiceStatus.Cancelled && grnIds.Contains(l.GoodsReceiptId)
            select l.GoodsReceiptId).AnyAsync(ct);
        if (alreadyInvoiced) throw Fail("One or more goods receipts have already been invoiced.");

        var supplier = await db.Suppliers.Where(s => s.Id == request.SupplierId)
            .Select(s => new { s.DefaultCurrency, s.PaymentTermDays }).FirstOrDefaultAsync(ct)
            ?? throw Fail("Supplier not found.");
        var currency = pos.FirstOrDefault()?.Currency ?? supplier.DefaultCurrency ?? "IDR";
        var dueDate = request.DueDate ?? request.InvoiceDate.AddDays(supplier.PaymentTermDays);

        var lines = new List<SupplierInvoiceLine>();
        foreach (var g in grns)
        {
            var grnLines = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.GoodsReceiptId == g.Id).ToListAsync(ct);
            var poLineIds = grnLines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
            var poLines = await db.PurchaseOrderLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);
            foreach (var gl in grnLines)
            {
                var pol = poLines[gl.PurchaseOrderLineId];
                lines.Add(new SupplierInvoiceLine(g.Id, gl.Id, gl.ProductVariantId,
                    gl.QuantityReceived, pol.UnitPrice, pol.DiscountPercent, pol.TaxRateSnapshot));
            }
        }

        var number = await docNumbers.NextAsync(DocumentTypes.SupplierInvoice, request.InvoiceDate, ct);
        var invoice = new SupplierInvoice(number, request.SupplierId, currency,
            request.InvoiceDate, dueDate, request.SupplierInvoiceNo, request.Notes);
        invoice.SetLines(lines);

        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(invoice.Id, ct))!;
    }

    public async Task<bool> UpdateHeaderAsync(int id, UpdateSupplierInvoiceHeaderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return false;
        inv.UpdateHeader(request.InvoiceDate, request.DueDate, request.SupplierInvoiceNo, request.Notes);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw Fail("Invoice not found.");
        inv.Cancel();
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<SupplierInvoiceLineDto>> BuildDerivedLinesAsync(
        int grnId, string grnNumber, List<GoodsReceiptLine> grnLines, CancellationToken ct)
    {
        var poLineIds = grnLines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
        var poLines = await db.PurchaseOrderLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var variantIds = grnLines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        var result = new List<SupplierInvoiceLineDto>();
        foreach (var gl in grnLines)
        {
            var pol = poLines[gl.PurchaseOrderLineId];
            var subtotal = Round(gl.QuantityReceived * pol.UnitPrice);
            var discount = Round(subtotal * pol.DiscountPercent / 100m);
            var tax = Round((subtotal - discount) * pol.TaxRateSnapshot / 100m);
            var totalLine = subtotal - discount + tax;
            result.Add(new SupplierInvoiceLineDto(0, grnId, grnNumber, gl.ProductVariantId,
                skus.GetValueOrDefault(gl.ProductVariantId, "—"), names.GetValueOrDefault(gl.ProductVariantId, "—"),
                gl.QuantityReceived, pol.UnitPrice, pol.DiscountPercent, pol.TaxRateSnapshot,
                subtotal, discount, tax, totalLine));
        }
        return result;
    }

    private async Task<IReadOnlyList<SupplierInvoiceLineDto>> BuildLineDtosAsync(IReadOnlyCollection<SupplierInvoiceLine> lines, CancellationToken ct)
    {
        var grnIds = lines.Select(l => l.GoodsReceiptId).Distinct().ToList();
        var grnNumbers = await db.GoodsReceipts.AsNoTracking().Where(g => grnIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.GrnNumber, ct);
        var variantIds = lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        return lines.OrderBy(l => l.Id).Select(l => new SupplierInvoiceLineDto(
            l.Id, l.GoodsReceiptId, grnNumbers.GetValueOrDefault(l.GoodsReceiptId, "—"), l.ProductVariantId,
            skus.GetValueOrDefault(l.ProductVariantId, "—"), names.GetValueOrDefault(l.ProductVariantId, "—"),
            l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRateSnapshot,
            l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal)).ToList();
    }

    private async Task<(Dictionary<int, string> skus, Dictionary<int, string> names)> LoadVariantNamesAsync(List<int> variantIds, CancellationToken ct)
    {
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var skus = variants.ToDictionary(v => v.Id, v => v.Sku);
        var names = variants.ToDictionary(v => v.Id, v => products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—");
        return (skus, names);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SupplierInvoice", message)]);
}
