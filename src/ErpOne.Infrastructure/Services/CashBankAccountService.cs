using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.CashBank;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CashBankAccountService(
    AppDbContext db,
    IValidator<CreateCashBankAccountRequest> createValidator,
    IValidator<UpdateCashBankAccountRequest> updateValidator) : ICashBankAccountService
{
    public async Task<IReadOnlyList<CashBankAccountDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.CashBankAccounts.AsNoTracking().OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<IReadOnlyList<CashBankAccountDto>> GetActiveAsync(CancellationToken ct = default) =>
        await db.CashBankAccounts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<CashBankAccountDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.CashBankAccounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<CashBankAccountDto>(items, total, page, pageSize);
    }

    public async Task<CashBankAccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<CashBankAccountDto> CreateAsync(CreateCashBankAccountRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);

        var entity = new CashBankAccount(code, request.Name, ParseType(request.Type), request.Currency,
            request.OpeningBalance, request.BankName, request.AccountNumber, request.AccountHolder, request.IsActive);
        db.CashBankAccounts.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCashBankAccountRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.CashBankAccounts.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        entity.Update(code, request.Name, ParseType(request.Type), request.Currency,
            request.OpeningBalance, request.BankName, request.AccountNumber, request.AccountHolder, request.IsActive);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.CashBankAccounts.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.CashBankAccounts.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static CashBankType ParseType(string type) =>
        Enum.TryParse<CashBankType>(type, out var t) ? t : CashBankType.Cash;

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.CashBankAccounts.AsNoTracking()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateCashBankAccountRequest.Code), $"Code '{code}' is already in use.")
            ]);
    }

    private static CashBankAccountDto ToDto(CashBankAccount x) =>
        new(x.Id, x.Code, x.Name, x.Type.ToString(), x.Currency, x.OpeningBalance,
            x.BankName, x.AccountNumber, x.AccountHolder, x.IsActive, x.CreatedAt, x.CreatedBy);
}
