using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Currencies;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CurrencyService(
    AppDbContext db,
    IValidator<CreateCurrencyRequest> createValidator,
    IValidator<UpdateCurrencyRequest> updateValidator) : ICurrencyService
{
    public async Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking().OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<IReadOnlyList<CurrencyDto>> GetActiveAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<CurrencyDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Currencies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<CurrencyDto>(items, total, page, pageSize);
    }

    public async Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<CurrencyDto> CreateAsync(CreateCurrencyRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);

        var entity = new Currency(code, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase, request.IsActive);
        db.Currencies.Add(entity);
        if (request.IsBase) await DemoteOtherBasesAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCurrencyRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Currencies.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        entity.Update(code, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase, request.IsActive);
        if (request.IsBase) await DemoteOtherBasesAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Currencies.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        if (entity.IsBase)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(UpdateCurrencyRequest.Code),
                    "The base currency cannot be deleted.")
            ]);

        db.Currencies.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task DemoteOtherBasesAsync(Currency keep, CancellationToken ct)
    {
        var others = await db.Currencies.Where(c => c.IsBase && c != keep).ToListAsync(ct);
        foreach (var o in others)
            o.Update(o.Code, o.Name, o.Symbol, o.DecimalPlaces, false, o.IsActive);
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.Currencies.AsNoTracking()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateCurrencyRequest.Code), $"Code '{code}' is already in use.")
            ]);
    }

    private static CurrencyDto ToDto(Currency x) =>
        new(x.Id, x.Code, x.Name, x.Symbol, x.DecimalPlaces, x.IsBase, x.IsActive, x.CreatedAt, x.CreatedBy);
}
