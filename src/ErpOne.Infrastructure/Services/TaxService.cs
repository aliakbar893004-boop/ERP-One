using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Taxes;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class TaxService(
    AppDbContext db,
    IValidator<CreateTaxRequest> createValidator,
    IValidator<UpdateTaxRequest> updateValidator) : ITaxService
{
    public async Task<IReadOnlyList<TaxDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Taxes.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<TaxDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Taxes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<TaxDto>(items, total, page, pageSize);
    }

    public async Task<TaxDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Taxes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<TaxDto> CreateAsync(CreateTaxRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Tax(request.Code, request.Name, request.Rate, request.IsInclusive, request.Description);
        db.Taxes.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateTaxRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Taxes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.Rate, request.IsInclusive, request.Description);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Taxes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Taxes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Taxes.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateTaxRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static TaxDto ToDto(Tax x) =>
        new(x.Id, x.Code, x.Name, x.Rate, x.IsInclusive, x.Description, x.CreatedAt, x.CreatedBy);
}
