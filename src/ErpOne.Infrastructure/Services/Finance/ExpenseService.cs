using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Expenses;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ExpenseService(
    AppDbContext db,
    IValidator<CreateExpenseRequest> createValidator,
    IDocumentNumberService docNumbers) : IExpenseService
{
    public async Task<PagedResult<ExpenseListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, ExpenseStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.Expenses.AsNoTracking();
        if (status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.ExpenseNumber.Contains(search) || x.Description.Contains(search) || (x.Payee != null && x.Payee.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ExpenseListItemDto(
                x.Id, x.ExpenseNumber, x.ExpenseDate,
                db.ExpenseCategories.Where(c => c.Id == x.ExpenseCategoryId).Select(c => c.Name).FirstOrDefault() ?? "—",
                db.CashBankAccounts.Where(a => a.Id == x.CashBankAccountId).Select(a => a.Name).FirstOrDefault() ?? "—",
                x.Payee, x.Description, x.Currency, x.Amount, x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<ExpenseListItemDto>(items, total, page, pageSize);
    }

    public async Task<ExpenseDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.Expenses.AsNoTracking().GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(ExpenseStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var posted = await db.Expenses.AsNoTracking().Where(x => x.Status == ExpenseStatus.Posted).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return new ExpenseDashboardDto(rows.Sum(r => r.Count), CountOf(ExpenseStatus.Posted), CountOf(ExpenseStatus.Voided), posted);
    }

    public async Task<ExpenseDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (x is null) return null;
        var catName = await db.ExpenseCategories.Where(c => c.Id == x.ExpenseCategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var accName = await db.CashBankAccounts.Where(a => a.Id == x.CashBankAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? "—";
        return new ExpenseDto(x.Id, x.ExpenseNumber, x.ExpenseDate, x.CashBankAccountId, accName, x.ExpenseCategoryId, catName,
            x.Currency, x.Amount, x.Payee, x.Description, x.Notes, x.Status.ToString(), x.CreatedAt, x.CreatedBy);
    }

    public async Task<ExpenseDto> CreateAsync(CreateExpenseRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var account = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.CashBankAccountId, ct)
            ?? throw Fail("Cash/bank account not found.");
        if (!account.IsActive) throw Fail("Cash/bank account is inactive.");
        var catActive = await db.ExpenseCategories.AsNoTracking().AnyAsync(c => c.Id == request.ExpenseCategoryId && c.IsActive, ct);
        if (!catActive) throw Fail("Expense category not found or inactive.");

        var number = await docNumbers.NextAsync(DocumentTypes.Expense, request.ExpenseDate, ct);
        var expense = new Expense(number, request.ExpenseDate, request.CashBankAccountId, request.ExpenseCategoryId,
            account.Currency, request.Amount, request.Payee, request.Description, request.Notes);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);

        db.CashBankMovements.Add(new CashBankMovement(expense.CashBankAccountId, expense.ExpenseDate,
            CashBankMovementDirection.Out, expense.Amount, "Expense", expense.Id, expense.ExpenseNumber));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(expense.Id, ct))!;
    }

    public async Task VoidAsync(int id, string authorizedBy, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw Fail("Expense not found.");
        expense.Void();

        var note = string.IsNullOrWhiteSpace(authorizedBy)
            ? $"Void {expense.ExpenseNumber}"
            : $"Void {expense.ExpenseNumber} authorized by {authorizedBy}";
        db.CashBankMovements.Add(new CashBankMovement(expense.CashBankAccountId, DateTime.UtcNow.Date,
            CashBankMovementDirection.In, expense.Amount, "ExpenseVoid", expense.Id, note));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("Expense", message)]);
}
