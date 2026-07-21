using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.SupplierPayments;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SupplierPaymentService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreateSupplierPaymentRequest> createValidator,
    IValidator<UpdateSupplierPaymentRequest> updateValidator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : ISupplierPaymentService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.SupplierPayment;

    public async Task<PagedResult<SupplierPaymentListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SupplierPaymentStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.SupplierPayments.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.PaymentNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SupplierPaymentListItemDto(
                p.Id, p.PaymentNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.PaymentDate,
                db.CashBankAccounts.Where(a => a.Id == p.CashBankAccountId).Select(a => a.Name).FirstOrDefault() ?? "—",
                p.Currency, p.Amount, p.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<SupplierPaymentListItemDto>(items, total, page, pageSize);
    }

    public async Task<SupplierPaymentDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.SupplierPayments.AsNoTracking()
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(SupplierPaymentStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var posted = await db.SupplierPayments.AsNoTracking().Where(p => p.Status == SupplierPaymentStatus.Posted).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        return new SupplierPaymentDashboardDto(rows.Sum(r => r.Count),
            CountOf(SupplierPaymentStatus.Draft), CountOf(SupplierPaymentStatus.PendingApproval), CountOf(SupplierPaymentStatus.Posted), posted);
    }

    public async Task<SupplierPaymentDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var p = await db.SupplierPayments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        var supplierName = await db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var accName = await db.CashBankAccounts.Where(a => a.Id == p.CashBankAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? "—";

        var invIds = p.Allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        var invs = await db.SupplierInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id))
            .Select(i => new { i.Id, i.InvoiceNumber, i.SupplierInvoiceNo, i.DueDate, i.Status, i.GrandTotal, i.PaidAmount }).ToListAsync(ct);
        var allocs = p.Allocations.Select(a =>
        {
            var i = invs.FirstOrDefault(x => x.Id == a.SupplierInvoiceId);
            return new SupplierPaymentAllocationDto(a.Id, a.SupplierInvoiceId, i?.InvoiceNumber ?? "—",
                i?.SupplierInvoiceNo, i?.DueDate ?? default, i?.Status.ToString() ?? "—",
                i?.GrandTotal ?? 0m, (i?.GrandTotal ?? 0m) - (i?.PaidAmount ?? 0m), a.Amount);
        }).ToList();

        return new SupplierPaymentDto(p.Id, p.PaymentNumber, p.SupplierId, supplierName, p.CashBankAccountId, accName,
            p.Currency, p.PaymentDate, p.Amount, p.Notes, p.Status.ToString(), p.RejectionNote, p.CreatedAt, p.CreatedBy, allocs);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<PayableInvoiceDto>> GetPayableInvoicesAsync(int supplierId, CancellationToken ct = default) =>
        await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == supplierId
                && (i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
                && i.GrandTotal - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate)
            .Select(i => new PayableInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.GrandTotal, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);

    public async Task<SupplierPaymentDto> CreateDraftAsync(CreateSupplierPaymentRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await ValidateAsync(request.SupplierId, request.CashBankAccountId, request.Allocations, ct);
        var number = await docNumbers.NextAsync(DocumentTypes.SupplierPayment, request.PaymentDate, ct);
        var payment = new SupplierPayment(number, request.SupplierId, request.CashBankAccountId, currency, request.PaymentDate, request.Notes);
        payment.SetAllocations(request.Allocations.Select(a => new SupplierPaymentAllocation(a.SupplierInvoiceId, a.Amount)));

        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(payment.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateSupplierPaymentRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return false;

        var currency = await ValidateAsync(payment.SupplierId, request.CashBankAccountId, request.Allocations, ct);
        var oldAllocs = await db.SupplierPaymentAllocations.Where(a => a.SupplierPaymentId == id).ToListAsync(ct);
        db.SupplierPaymentAllocations.RemoveRange(oldAllocs);
        payment.UpdateHeader(request.CashBankAccountId, request.PaymentDate, request.Notes);
        payment.SetAllocations(request.Allocations.Select(a => new SupplierPaymentAllocation(a.SupplierInvoiceId, a.Amount)));
        _ = currency;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var payment = await db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return false;
        if (payment.Status != SupplierPaymentStatus.Draft) throw Fail("Only a draft payment can be deleted.");
        db.SupplierPayments.Remove(payment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        payment.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, payment.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, payment.Id, ct);
        if (fullyApproved) await PostAsync(payment, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, payment.Id, actingUserName, isInRole, payment.CreatedBy, ct);
        if (fullyApproved) await PostAsync(payment, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw Fail("Payment not found.");
        await approval.RejectAsync(DocType, payment.Id, actingUserName, isInRole, payment.CreatedBy, reason, ct);
        payment.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, payment.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task VoidAsync(int id, string authorizedBy, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        payment.Void();

        var note = string.IsNullOrWhiteSpace(authorizedBy)
            ? $"Void {payment.PaymentNumber}"
            : $"Void {payment.PaymentNumber} authorized by {authorizedBy}";
        db.CashBankMovements.Add(new CashBankMovement(payment.CashBankAccountId, DateTime.UtcNow.Date,
            CashBankMovementDirection.In, payment.Amount, "SupplierPaymentVoid", payment.Id, note));

        foreach (var a in payment.Allocations)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == a.SupplierInvoiceId, ct);
            inv?.ReversePayment(a.Amount);
        }

        await approval.ResetAsync(DocType, payment.Id, ct);
        await journalPoster.ReverseForAsync("SupplierPayment", id, DateTime.UtcNow.Date, "Payment voided", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Applies a fully-approved payment: cash out + invoice ApplyPayment. Caller saves + commits.
    private async Task PostAsync(SupplierPayment payment, CancellationToken ct)
    {
        db.CashBankMovements.Add(new CashBankMovement(payment.CashBankAccountId, payment.PaymentDate,
            CashBankMovementDirection.Out, payment.Amount, "SupplierPayment", payment.Id, payment.PaymentNumber));

        foreach (var a in payment.Allocations)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == a.SupplierInvoiceId, ct)
                ?? throw Fail($"Invoice {a.SupplierInvoiceId} not found.");
            inv.ApplyPayment(a.Amount);
        }
        payment.MarkPosted();
        await journalPoster.PostSupplierPaymentAsync(payment, ct);
    }

    // Validates supplier/account/allocations; returns the payment currency (invoice currency).
    private async Task<string> ValidateAsync(int supplierId, int accountId, IReadOnlyList<PaymentAllocationInput> allocations, CancellationToken ct)
    {
        var account = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw Fail("Cash/bank account not found.");
        if (!account.IsActive) throw Fail("Cash/bank account is inactive.");

        var invIds = allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        if (invIds.Count != allocations.Count) throw Fail("Duplicate invoice in allocations.");

        var invs = await db.SupplierInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id)).ToListAsync(ct);
        if (invs.Count != invIds.Count) throw Fail("One or more invoices were not found.");
        if (invs.Any(i => i.SupplierId != supplierId)) throw Fail("All invoices must belong to the selected supplier.");
        if (invs.Any(i => i.Status is SupplierInvoiceStatus.Cancelled or SupplierInvoiceStatus.Paid)) throw Fail("Only open or partially-paid invoices can be paid.");
        if (invs.Select(i => i.Currency).Distinct().Count() > 1) throw Fail("All invoices must share the same currency.");

        var invCurrency = invs.First().Currency;
        if (!string.Equals(account.Currency, invCurrency, StringComparison.OrdinalIgnoreCase))
            throw Fail($"Account currency ({account.Currency}) must match the invoice currency ({invCurrency}).");

        foreach (var a in allocations)
        {
            var inv = invs.First(i => i.Id == a.SupplierInvoiceId);
            var outstanding = inv.GrandTotal - inv.PaidAmount;
            if (a.Amount > outstanding) throw Fail($"Allocation {a.Amount} exceeds outstanding {outstanding} for {inv.InvoiceNumber}.");
        }
        return invCurrency;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SupplierPayment", message)]);
}
