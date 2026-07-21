using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CustomerInvoiceService(
    AppDbContext db,
    IValidator<CreateCustomerInvoiceRequest> createValidator,
    IValidator<UpdateCustomerInvoiceHeaderRequest> updateValidator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : ICustomerInvoiceService
{
    private static readonly SalesOrderStatus[] InvoiceableStatuses =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.PartiallyDelivered, SalesOrderStatus.Delivered, SalesOrderStatus.Closed];

    public async Task<PagedResult<CustomerInvoiceListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CustomerInvoiceStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.CustomerInvoices.AsNoTracking();
        if (status is { } st) query = query.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.InvoiceNumber.Contains(search) || (i.CustomerRef != null && i.CustomerRef.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new CustomerInvoiceListItemDto(
                i.Id, i.InvoiceNumber,
                db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                i.InvoiceDate, i.DueDate, i.Currency, i.GrandTotal, i.PaidAmount, i.GrandTotal - i.PaidAmount, i.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<CustomerInvoiceListItemDto>(items, total, page, pageSize);
    }

    public async Task<CustomerInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.CustomerInvoices.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Outstanding = g.Sum(x => x.GrandTotal - x.PaidAmount) })
            .ToListAsync(ct);

        int CountOf(CustomerInvoiceStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var outstanding = rows.Where(r => r.Status != CustomerInvoiceStatus.Cancelled).Sum(r => r.Outstanding);

        return new CustomerInvoiceDashboardDto(
            rows.Sum(r => r.Count),
            CountOf(CustomerInvoiceStatus.Open),
            CountOf(CustomerInvoiceStatus.PartiallyPaid),
            CountOf(CustomerInvoiceStatus.Paid),
            outstanding);
    }

    public async Task<CustomerInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.CustomerInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return null;

        var customerName = await db.Customers.Where(c => c.Id == inv.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var lines = await BuildLineDtosAsync(inv.Lines, ct);

        return new CustomerInvoiceDto(inv.Id, inv.InvoiceNumber, inv.CustomerRef, inv.CustomerId, customerName,
            inv.Currency, inv.InvoiceDate, inv.DueDate, inv.Notes, inv.Status.ToString(),
            inv.Subtotal, inv.DiscountTotal, inv.TaxTotal, inv.GrandTotal, inv.PaidAmount, inv.Outstanding,
            inv.CreatedAt, inv.CreatedBy, lines);
    }

    public async Task<CustomerCreditDto> GetCustomerCreditAsync(int customerId, CancellationToken ct = default)
    {
        var limit = await db.Customers.Where(c => c.Id == customerId).Select(c => c.CreditLimit).FirstOrDefaultAsync(ct);
        var outstanding = await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status != CustomerInvoiceStatus.Cancelled)
            .SumAsync(i => (decimal?)(i.GrandTotal - i.PaidAmount), ct) ?? 0m;
        return new CustomerCreditDto(limit, outstanding, limit - outstanding);
    }

    public async Task<IReadOnlyList<UninvoicedSalesOrderDto>> GetUninvoicedSalesOrdersAsync(int customerId, CancellationToken ct = default)
    {
        var invoicedSoIds = await (
            from l in db.CustomerInvoiceLines.AsNoTracking()
            join inv in db.CustomerInvoices.AsNoTracking() on l.CustomerInvoiceId equals inv.Id
            where inv.Status != CustomerInvoiceStatus.Cancelled
            select l.SalesOrderId).Distinct().ToListAsync(ct);

        var sos = await db.SalesOrders.AsNoTracking()
            .Where(so => so.CustomerId == customerId && InvoiceableStatuses.Contains(so.Status) && !invoicedSoIds.Contains(so.Id))
            .Select(so => new { so.Id, so.SoNumber, so.OrderDate })
            .ToListAsync(ct);

        var result = new List<UninvoicedSalesOrderDto>();
        foreach (var so in sos)
        {
            var soLines = await db.SalesOrderLines.AsNoTracking().Where(l => l.SalesOrderId == so.Id).ToListAsync(ct);
            var invLines = await BuildDerivedLinesAsync(so.Id, so.SoNumber, soLines, ct);
            result.Add(new UninvoicedSalesOrderDto(so.Id, so.SoNumber, so.OrderDate, invLines.Sum(l => l.LineTotal), invLines));
        }
        return result;
    }

    public async Task<CustomerInvoiceDto> CreateAsync(CreateCustomerInvoiceRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var soIds = request.SalesOrderIds.Distinct().ToList();

        var sos = await db.SalesOrders.AsNoTracking().Where(so => soIds.Contains(so.Id)).ToListAsync(ct);
        if (sos.Count != soIds.Count) throw Fail("One or more sales orders were not found.");
        if (sos.Any(so => !InvoiceableStatuses.Contains(so.Status))) throw Fail("Only confirmed (or later) sales orders can be invoiced.");
        if (sos.Any(so => so.CustomerId != request.CustomerId)) throw Fail("All sales orders must belong to the selected customer.");
        if (sos.Select(so => so.Currency).Distinct().Count() > 1) throw Fail("All sales orders must share the same currency.");

        var alreadyInvoiced = await (
            from l in db.CustomerInvoiceLines
            join inv in db.CustomerInvoices on l.CustomerInvoiceId equals inv.Id
            where inv.Status != CustomerInvoiceStatus.Cancelled && soIds.Contains(l.SalesOrderId)
            select l.SalesOrderId).AnyAsync(ct);
        if (alreadyInvoiced) throw Fail("One or more sales orders have already been invoiced.");

        var customer = await db.Customers.Where(c => c.Id == request.CustomerId)
            .Select(c => new { c.DefaultCurrency, c.PaymentTermDays }).FirstOrDefaultAsync(ct)
            ?? throw Fail("Customer not found.");
        var currency = sos.FirstOrDefault()?.Currency ?? customer.DefaultCurrency ?? "IDR";
        var dueDate = request.DueDate ?? request.InvoiceDate.AddDays(customer.PaymentTermDays);

        var lines = new List<CustomerInvoiceLine>();
        foreach (var so in sos)
        {
            var soLines = await db.SalesOrderLines.AsNoTracking().Where(l => l.SalesOrderId == so.Id).ToListAsync(ct);
            foreach (var sol in soLines)
                lines.Add(new CustomerInvoiceLine(so.Id, sol.Id, sol.ProductVariantId,
                    sol.Quantity, sol.UnitPrice, sol.DiscountPercent, sol.TaxRateSnapshot));
        }

        var number = await docNumbers.NextAsync(DocumentTypes.CustomerInvoice, request.InvoiceDate, ct);
        var invoice = new CustomerInvoice(number, request.CustomerId, currency,
            request.InvoiceDate, dueDate, request.CustomerRef, request.Notes);
        invoice.SetLines(lines);

        db.CustomerInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        await journalPoster.PostCustomerInvoiceAsync(invoice, ct);

        await tx.CommitAsync(ct);

        return (await GetByIdAsync(invoice.Id, ct))!;
    }

    public async Task<bool> UpdateHeaderAsync(int id, UpdateCustomerInvoiceHeaderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return false;
        inv.UpdateHeader(request.InvoiceDate, request.DueDate, request.CustomerRef, request.Notes);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw Fail("Invoice not found.");
        inv.Cancel();
        await journalPoster.ReverseForAsync("CustomerInvoice", id, DateTime.UtcNow.Date, "Invoice cancelled", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<CustomerInvoiceLineDto>> BuildDerivedLinesAsync(
        int soId, string soNumber, List<SalesOrderLine> soLines, CancellationToken ct)
    {
        var variantIds = soLines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        return soLines.Select(sol => new CustomerInvoiceLineDto(0, soId, soNumber, sol.ProductVariantId,
            skus.GetValueOrDefault(sol.ProductVariantId, "—"), names.GetValueOrDefault(sol.ProductVariantId, "—"),
            sol.Quantity, sol.UnitPrice, sol.DiscountPercent, sol.TaxRateSnapshot,
            sol.LineSubtotal, sol.LineDiscount, sol.LineTax, sol.LineTotal)).ToList();
    }

    private async Task<IReadOnlyList<CustomerInvoiceLineDto>> BuildLineDtosAsync(IReadOnlyCollection<CustomerInvoiceLine> lines, CancellationToken ct)
    {
        var soIds = lines.Select(l => l.SalesOrderId).Distinct().ToList();
        var soNumbers = await db.SalesOrders.AsNoTracking().Where(so => soIds.Contains(so.Id))
            .ToDictionaryAsync(so => so.Id, so => so.SoNumber, ct);
        var variantIds = lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        return lines.OrderBy(l => l.Id).Select(l => new CustomerInvoiceLineDto(
            l.Id, l.SalesOrderId, soNumbers.GetValueOrDefault(l.SalesOrderId, "—"), l.ProductVariantId,
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

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("CustomerInvoice", message)]);
}
