using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Expenses;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ExpenseCategoryService(
    AppDbContext db,
    IValidator<CreateExpenseCategoryRequest> createValidator,
    IValidator<UpdateExpenseCategoryRequest> updateValidator) : IExpenseCategoryService
{
    public async Task<IReadOnlyList<ExpenseCategoryDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.ExpenseCategories.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<IReadOnlyList<ExpenseCategoryDto>> GetActiveAsync(CancellationToken ct = default) =>
        await db.ExpenseCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<ExpenseCategoryDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.ExpenseCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).Select(x => ToDto(x)).ToListAsync(ct);
        return new PagedResult<ExpenseCategoryDto>(items, total, page, pageSize);
    }

    public async Task<ExpenseCategoryDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<ExpenseCategoryDto> CreateAsync(CreateExpenseCategoryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);
        var entity = new ExpenseCategory(code, request.Name, request.IsActive);
        db.ExpenseCategories.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateExpenseCategoryRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        entity.Update(code, request.Name, request.IsActive);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.ExpenseCategories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.ExpenseCategories.AsNoTracking().AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                nameof(CreateExpenseCategoryRequest.Code), $"Code '{code}' is already in use.")]);
    }

    private static ExpenseCategoryDto ToDto(ExpenseCategory x) => new(x.Id, x.Code, x.Name, x.IsActive, x.CreatedAt, x.CreatedBy);
}
