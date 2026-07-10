using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Expenses;

public record ExpenseListItemDto(int Id, string ExpenseNumber, DateTime ExpenseDate, string CategoryName, string CashBankAccountName, string? Payee, string Description, string Currency, decimal Amount, string Status);
public record ExpenseDto(int Id, string ExpenseNumber, DateTime ExpenseDate, int CashBankAccountId, string CashBankAccountName, int ExpenseCategoryId, string CategoryName, string Currency, decimal Amount, string? Payee, string Description, string? Notes, string Status, DateTime CreatedAt, string? CreatedBy);
public record ExpenseDashboardDto(int Total, int Posted, int Voided, decimal PostedThisMonth);
public record CreateExpenseRequest(DateTime ExpenseDate, int CashBankAccountId, int ExpenseCategoryId, decimal Amount, string? Payee, string Description, string? Notes);

public interface IExpenseService
{
    Task<PagedResult<ExpenseListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, ExpenseStatus? status = null, CancellationToken ct = default);
    Task<ExpenseDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ExpenseDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(CreateExpenseRequest request, CancellationToken ct = default);
    Task VoidAsync(int id, string authorizedBy, CancellationToken ct = default);
}
