using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.ProductCategories;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ProductCategoryService(
    AppDbContext db,
    IValidator<CreateProductCategoryRequest> createValidator,
    IValidator<UpdateProductCategoryRequest> updateValidator) : IProductCategoryService
{
    public async Task<IReadOnlyList<ProductCategoryDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

    public async Task<PagedResult<ProductCategoryDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.ProductCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

        return new PagedResult<ProductCategoryDto>(items, total, page, pageSize);
    }

    public async Task<ProductCategoryDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var c = await db.ProductCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : ToDto(c);
    }

    public async Task<ProductCategoryDto> CreateAsync(CreateProductCategoryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var category = new ProductCategory(request.Code, request.Name, request.Description);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return ToDto(category);
    }

    public async Task<bool> UpdateAsync(int id, UpdateProductCategoryRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var category = await db.ProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);

        category.Update(request.Code, request.Name, request.Description);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return false;

        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.Code == normalized && (excludeId == null || c.Id != excludeId), ct);

        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateProductCategoryRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static ProductCategoryDto ToDto(ProductCategory c) =>
        new(c.Id, c.Code, c.Name, c.Description, c.CreatedAt, c.CreatedBy);
}
