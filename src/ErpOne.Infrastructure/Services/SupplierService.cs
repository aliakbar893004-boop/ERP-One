using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Suppliers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SupplierService(
    AppDbContext db,
    IValidator<CreateSupplierRequest> createValidator,
    IValidator<UpdateSupplierRequest> updateValidator) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<SupplierDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        if (active is not null)
            query = query.Where(x => x.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<SupplierDto>(items, total, page, pageSize);
    }

    public async Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var total = await db.Suppliers.CountAsync(ct);
        var active = await db.Suppliers.CountAsync(x => x.IsActive, ct);
        return new MasterListSummary(total, active, total - active);
    }

    public async Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Supplier(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.BankName, request.BankAccountNumber, request.BankAccountName, request.IsActive);
        db.Suppliers.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Suppliers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.BankName, request.BankAccountNumber, request.BankAccountName, request.IsActive);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Suppliers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Suppliers.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateSupplierRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static SupplierDto ToDto(Supplier x) =>
        new(x.Id, x.Code, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.TaxId,
            x.PaymentTermDays, x.DefaultCurrency, x.BankName, x.BankAccountNumber, x.BankAccountName,
            x.IsActive, x.CreatedAt, x.CreatedBy);
}
