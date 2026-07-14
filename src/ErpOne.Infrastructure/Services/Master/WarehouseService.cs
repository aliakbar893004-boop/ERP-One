using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Warehouses;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class WarehouseService(
    AppDbContext db,
    IValidator<CreateWarehouseRequest> createValidator,
    IValidator<UpdateWarehouseRequest> updateValidator) : IWarehouseService
{
    public async Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<WarehouseDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Warehouses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        if (active is not null)
            query = query.Where(x => x.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<WarehouseDto>(items, total, page, pageSize);
    }

    public async Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var total = await db.Warehouses.CountAsync(ct);
        var active = await db.Warehouses.CountAsync(x => x.IsActive, ct);
        return new MasterListSummary(total, active, total - active);
    }

    public async Task<WarehouseDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<WarehouseDto?> GetDefaultAsync(CancellationToken ct = default)
    {
        var x = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(e => e.IsDefault, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Warehouse(request.Code, request.Name, request.Address, request.IsActive, request.IsDefault);
        db.Warehouses.Add(entity);

        if (request.IsDefault)
            await ClearOtherDefaultsAsync(null, ct);

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateWarehouseRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Warehouses.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.Address, request.IsActive, request.IsDefault);

        if (request.IsDefault)
            await ClearOtherDefaultsAsync(id, ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Warehouses.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Warehouses.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Warehouses.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateWarehouseRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private async Task ClearOtherDefaultsAsync(int? exceptId, CancellationToken ct)
    {
        var others = await db.Warehouses.Where(e => e.IsDefault && (exceptId == null || e.Id != exceptId)).ToListAsync(ct);
        foreach (var o in others) o.ClearDefault();
    }

    private static WarehouseDto ToDto(Warehouse x) =>
        new(x.Id, x.Code, x.Name, x.Address, x.IsActive, x.IsDefault, x.CreatedAt, x.CreatedBy);
}
