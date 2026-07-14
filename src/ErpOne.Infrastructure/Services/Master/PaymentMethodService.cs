using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.PaymentMethods;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PaymentMethodService(
    AppDbContext db,
    IValidator<CreatePaymentMethodRequest> createValidator,
    IValidator<UpdatePaymentMethodRequest> updateValidator) : IPaymentMethodService
{
    public async Task<IReadOnlyList<PaymentMethodDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.PaymentMethods.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<PaymentMethodDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.PaymentMethods.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        if (active is not null)
            query = query.Where(x => x.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<PaymentMethodDto>(items, total, page, pageSize);
    }

    public async Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var total = await db.PaymentMethods.CountAsync(ct);
        var active = await db.PaymentMethods.CountAsync(x => x.IsActive, ct);
        return new MasterListSummary(total, active, total - active);
    }

    public async Task<PaymentMethodDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<PaymentMethodDto> CreateAsync(CreatePaymentMethodRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new PaymentMethod(request.Code, request.Name, request.Type, request.IsActive);
        db.PaymentMethods.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdatePaymentMethodRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.PaymentMethods.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.Type, request.IsActive);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.PaymentMethods.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.PaymentMethods.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.PaymentMethods.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreatePaymentMethodRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static PaymentMethodDto ToDto(PaymentMethod x) =>
        new(x.Id, x.Code, x.Name, x.Type, x.IsActive, x.CreatedAt, x.CreatedBy);
}
