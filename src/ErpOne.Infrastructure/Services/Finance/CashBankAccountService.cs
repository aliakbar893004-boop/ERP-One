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
    public async Task<IReadOnlyList<CashBankAccountDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await db.CashBankAccounts.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        var bal = await BalanceMapAsync(list.Select(x => x.Id).ToList(), ct);
        return list.Select(x => ToDto(x, x.OpeningBalance + bal.GetValueOrDefault(x.Id, 0m))).ToList();
    }

    public async Task<IReadOnlyList<CashBankAccountDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var list = await db.CashBankAccounts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        var bal = await BalanceMapAsync(list.Select(x => x.Id).ToList(), ct);
        return list.Select(x => ToDto(x, x.OpeningBalance + bal.GetValueOrDefault(x.Id, 0m))).ToList();
    }

    public async Task<PagedResult<CashBankAccountDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.CashBankAccounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var bal = await BalanceMapAsync(list.Select(x => x.Id).ToList(), ct);
        var items = list.Select(x => ToDto(x, x.OpeningBalance + bal.GetValueOrDefault(x.Id, 0m))).ToList();

        return new PagedResult<CashBankAccountDto>(items, total, page, pageSize);
    }

    public async Task<CashBankAccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x, await GetBalanceAsync(id, ct));
    }

    public async Task<decimal> GetBalanceAsync(int id, CancellationToken ct = default)
    {
        var opening = await db.CashBankAccounts.AsNoTracking().Where(a => a.Id == id).Select(a => a.OpeningBalance).FirstOrDefaultAsync(ct);
        var inSum = await db.CashBankMovements.AsNoTracking().Where(m => m.CashBankAccountId == id && m.Direction == CashBankMovementDirection.In).SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        var outSum = await db.CashBankMovements.AsNoTracking().Where(m => m.CashBankAccountId == id && m.Direction == CashBankMovementDirection.Out).SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        return opening + inSum - outSum;
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
        return ToDto(entity, entity.OpeningBalance);
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

    private async Task<Dictionary<int, decimal>> BalanceMapAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var moves = await db.CashBankMovements.AsNoTracking().Where(m => ids.Contains(m.CashBankAccountId))
            .GroupBy(m => m.CashBankAccountId)
            .Select(g => new
            {
                Id = g.Key,
                In = g.Where(x => x.Direction == CashBankMovementDirection.In).Sum(x => x.Amount),
                Out = g.Where(x => x.Direction == CashBankMovementDirection.Out).Sum(x => x.Amount)
            })
            .ToListAsync(ct);
        return moves.ToDictionary(m => m.Id, m => m.In - m.Out);
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

    private static CashBankAccountDto ToDto(CashBankAccount x, decimal currentBalance) =>
        new(x.Id, x.Code, x.Name, x.Type.ToString(), x.Currency, x.OpeningBalance,
            x.BankName, x.AccountNumber, x.AccountHolder, x.IsActive, x.CreatedAt, x.CreatedBy, currentBalance);
}
