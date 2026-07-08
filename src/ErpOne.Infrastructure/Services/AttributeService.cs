using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Attributes;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class AttributeService(
    AppDbContext db,
    IValidator<CreateAttributeRequest> createValidator,
    IValidator<UpdateAttributeRequest> updateValidator) : IAttributeService
{
    public async Task<IReadOnlyList<AttributeDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.ProductAttributes.AsNoTracking().Include(a => a.Values)
            .OrderBy(a => a.Name).Select(a => ToDto(a)).ToListAsync(ct);

    public async Task<PagedResult<AttributeDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.ProductAttributes.AsNoTracking().Include(a => a.Values).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => a.Name.Contains(search) || a.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => ToDto(a)).ToListAsync(ct);

        return new PagedResult<AttributeDto>(items, total, page, pageSize);
    }

    public async Task<AttributeDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await db.ProductAttributes.AsNoTracking().Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : ToDto(a);
    }

    public async Task<AttributeDto> CreateAsync(CreateAttributeRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var attr = new ProductAttribute(request.Code, request.Name);
        foreach (var v in request.Values) attr.AddValue(v.Code, v.Value);

        db.ProductAttributes.Add(attr);
        await db.SaveChangesAsync(ct);
        return ToDto(attr);
    }

    public async Task<bool> UpdateAsync(int id, UpdateAttributeRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var attr = await db.ProductAttributes.Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (attr is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);

        attr.Update(request.Code, request.Name);
        attr.ClearValues();                          // ganti penuh daftar nilai
        foreach (var v in request.Values) attr.AddValue(v.Code, v.Value);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var attr = await db.ProductAttributes.Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (attr is null) return false;
        db.ProductAttributes.Remove(attr);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.ProductAttributes.AsNoTracking()
            .AnyAsync(a => a.Code == normalized && (excludeId == null || a.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateAttributeRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static AttributeDto ToDto(ProductAttribute a) =>
        new(a.Id, a.Code, a.Name,
            a.Values.Select(v => new AttributeValueDto(v.Id, v.Code, v.Value)).ToList(),
            a.CreatedAt, a.CreatedBy);
}
