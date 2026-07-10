using ErpOne.Application.Common;

namespace ErpOne.Application.Expenses;

public record ExpenseCategoryDto(int Id, string Code, string Name, bool IsActive, DateTime CreatedAt, string? CreatedBy);
public record CreateExpenseCategoryRequest(string Code, string Name, bool IsActive);
public record UpdateExpenseCategoryRequest(string Code, string Name, bool IsActive);

public interface IExpenseCategoryService
{
    Task<IReadOnlyList<ExpenseCategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ExpenseCategoryDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<ExpenseCategoryDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<ExpenseCategoryDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ExpenseCategoryDto> CreateAsync(CreateExpenseCategoryRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateExpenseCategoryRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
