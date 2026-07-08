using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Customers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CustomerService(
    AppDbContext db,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<CustomerDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        if (active is not null)
            query = query.Where(x => x.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, total, page, pageSize);
    }

    public async Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var total = await db.Customers.CountAsync(ct);
        var active = await db.Customers.CountAsync(x => x.IsActive, ct);
        return new MasterListSummary(total, active, total - active);
    }

    public async Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Customers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Customer(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.CreditLimit, request.IsActive);
        db.Customers.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Customers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.CreditLimit, request.IsActive);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Customers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Customers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Customers.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateCustomerRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static CustomerDto ToDto(Customer x) =>
        new(x.Id, x.Code, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.TaxId,
            x.PaymentTermDays, x.DefaultCurrency, x.CreditLimit, x.IsActive, x.CreatedAt, x.CreatedBy);
}
