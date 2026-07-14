using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Units;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class UnitService(
    AppDbContext db,
    IValidator<CreateUnitRequest> createValidator,
    IValidator<UpdateUnitRequest> updateValidator) : IUnitService
{
    public async Task<IReadOnlyList<UnitDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Units
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .Select(u => ToDto(u))
            .ToListAsync(ct);

    public async Task<PagedResult<UnitDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Units.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Name.Contains(search) || u.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => ToDto(u))
            .ToListAsync(ct);

        return new PagedResult<UnitDto>(items, total, page, pageSize);
    }

    public async Task<UnitDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var u = await db.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? null : ToDto(u);
    }

    public async Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var unit = new Unit(request.Code, request.Name, request.Description);
        db.Units.Add(unit);
        await db.SaveChangesAsync(ct);

        return ToDto(unit);
    }

    public async Task<bool> UpdateAsync(int id, UpdateUnitRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var unit = await db.Units.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (unit is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);

        unit.Update(request.Code, request.Name, request.Description);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (unit is null) return false;

        db.Units.Remove(unit);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Units
            .AsNoTracking()
            .AnyAsync(u => u.Code == normalized && (excludeId == null || u.Id != excludeId), ct);

        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateUnitRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static UnitDto ToDto(Unit u) =>
        new(u.Id, u.Code, u.Name, u.Description, u.CreatedAt, u.CreatedBy);
}
