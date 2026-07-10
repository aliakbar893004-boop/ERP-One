using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.CustomerReceipts;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CustomerReceiptService(
    AppDbContext db,
    IValidator<CreateCustomerReceiptRequest> createValidator,
    IDocumentNumberService docNumbers) : ICustomerReceiptService
{
    public async Task<PagedResult<CustomerReceiptListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CustomerReceiptStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.CustomerReceipts.AsNoTracking();
        if (status is { } st) query = query.Where(r => r.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(r => r.ReceiptNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new CustomerReceiptListItemDto(
                r.Id, r.ReceiptNumber,
                db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                r.ReceiptDate,
                db.CashBankAccounts.Where(a => a.Id == r.CashBankAccountId).Select(a => a.Name).FirstOrDefault() ?? "—",
                r.Currency, r.Amount, r.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<CustomerReceiptListItemDto>(items, total, page, pageSize);
    }

    public async Task<CustomerReceiptDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.CustomerReceipts.AsNoTracking()
            .GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(CustomerReceiptStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var posted = await db.CustomerReceipts.AsNoTracking().Where(r => r.Status == CustomerReceiptStatus.Posted).SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        return new CustomerReceiptDashboardDto(rows.Sum(r => r.Count), CountOf(CustomerReceiptStatus.Posted), CountOf(CustomerReceiptStatus.Voided), posted);
    }

    public async Task<CustomerReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await db.CustomerReceipts.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var customerName = await db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var accName = await db.CashBankAccounts.Where(a => a.Id == r.CashBankAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? "—";

        var invIds = r.Allocations.Select(a => a.CustomerInvoiceId).Distinct().ToList();
        var invs = await db.CustomerInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id))
            .Select(i => new { i.Id, i.InvoiceNumber, i.DueDate, i.Status, i.GrandTotal, i.PaidAmount }).ToListAsync(ct);
        var allocs = r.Allocations.Select(a =>
        {
            var i = invs.FirstOrDefault(x => x.Id == a.CustomerInvoiceId);
            return new CustomerReceiptAllocationDto(a.Id, a.CustomerInvoiceId, i?.InvoiceNumber ?? "—",
                i?.DueDate ?? default, i?.Status.ToString() ?? "—",
                i?.GrandTotal ?? 0m, (i?.GrandTotal ?? 0m) - (i?.PaidAmount ?? 0m), a.Amount);
        }).ToList();

        return new CustomerReceiptDto(r.Id, r.ReceiptNumber, r.CustomerId, customerName, r.CashBankAccountId, accName,
            r.Currency, r.ReceiptDate, r.Amount, r.Notes, r.Status.ToString(), r.CreatedAt, r.CreatedBy, allocs);
    }

    public async Task<IReadOnlyList<OpenInvoiceDto>> GetOpenInvoicesAsync(int customerId, CancellationToken ct = default) =>
        await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId
                && (i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
                && i.GrandTotal - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate)
            .Select(i => new OpenInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.GrandTotal, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);

    public async Task<CustomerReceiptDto> CreateAsync(CreateCustomerReceiptRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await ValidateAsync(request.CustomerId, request.CashBankAccountId, request.Allocations, ct);
        var number = await docNumbers.NextAsync(DocumentTypes.CustomerReceipt, request.ReceiptDate, ct);
        var receipt = new CustomerReceipt(number, request.CustomerId, request.CashBankAccountId, currency, request.ReceiptDate, request.Notes);
        receipt.SetAllocations(request.Allocations.Select(a => new CustomerReceiptAllocation(a.CustomerInvoiceId, a.Amount)));
        db.CustomerReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        // Post immediately: cash In + apply to invoices.
        db.CashBankMovements.Add(new CashBankMovement(receipt.CashBankAccountId, receipt.ReceiptDate,
            CashBankMovementDirection.In, receipt.Amount, "CustomerReceipt", receipt.Id, receipt.ReceiptNumber));
        foreach (var a in request.Allocations)
        {
            var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == a.CustomerInvoiceId, ct)
                ?? throw Fail($"Invoice {a.CustomerInvoiceId} not found.");
            inv.ApplyPayment(a.Amount);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(receipt.Id, ct))!;
    }

    public async Task VoidAsync(int id, string authorizedBy, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await db.CustomerReceipts.Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw Fail("Receipt not found.");
        receipt.Void();

        var note = string.IsNullOrWhiteSpace(authorizedBy)
            ? $"Void {receipt.ReceiptNumber}"
            : $"Void {receipt.ReceiptNumber} authorized by {authorizedBy}";
        db.CashBankMovements.Add(new CashBankMovement(receipt.CashBankAccountId, DateTime.UtcNow.Date,
            CashBankMovementDirection.Out, receipt.Amount, "CustomerReceiptVoid", receipt.Id, note));

        foreach (var a in receipt.Allocations)
        {
            var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == a.CustomerInvoiceId, ct);
            inv?.ReversePayment(a.Amount);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<string> ValidateAsync(int customerId, int accountId, IReadOnlyList<ReceiptAllocationInput> allocations, CancellationToken ct)
    {
        var account = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw Fail("Cash/bank account not found.");
        if (!account.IsActive) throw Fail("Cash/bank account is inactive.");

        var invIds = allocations.Select(a => a.CustomerInvoiceId).Distinct().ToList();
        if (invIds.Count != allocations.Count) throw Fail("Duplicate invoice in allocations.");

        var invs = await db.CustomerInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id)).ToListAsync(ct);
        if (invs.Count != invIds.Count) throw Fail("One or more invoices were not found.");
        if (invs.Any(i => i.CustomerId != customerId)) throw Fail("All invoices must belong to the selected customer.");
        if (invs.Any(i => i.Status is CustomerInvoiceStatus.Cancelled or CustomerInvoiceStatus.Paid)) throw Fail("Only open or partially-paid invoices can be received against.");
        if (invs.Select(i => i.Currency).Distinct().Count() > 1) throw Fail("All invoices must share the same currency.");

        var invCurrency = invs.First().Currency;
        if (!string.Equals(account.Currency, invCurrency, StringComparison.OrdinalIgnoreCase))
            throw Fail($"Account currency ({account.Currency}) must match the invoice currency ({invCurrency}).");

        foreach (var a in allocations)
        {
            var inv = invs.First(i => i.Id == a.CustomerInvoiceId);
            var outstanding = inv.GrandTotal - inv.PaidAmount;
            if (a.Amount > outstanding) throw Fail($"Allocation {a.Amount} exceeds outstanding {outstanding} for {inv.InvoiceNumber}.");
        }
        return invCurrency;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("CustomerReceipt", message)]);
}
