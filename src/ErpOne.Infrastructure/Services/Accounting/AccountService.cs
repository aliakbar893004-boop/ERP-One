using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class AccountService(
    AppDbContext db,
    IValidator<CreateAccountRequest> createValidator,
    IValidator<UpdateAccountRequest> updateValidator) : IAccountService
{
    public async Task<IReadOnlyList<AccountDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Accounts.AsNoTracking().OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccountDto>> GetPostableAsync(CancellationToken ct = default)
    {
        return await db.Accounts.AsNoTracking().Where(a => a.IsPostable && a.IsActive).OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccountTreeNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        var byParent = all.ToLookup(a => a.ParentId);
        List<AccountTreeNodeDto> Build(int? parentId) =>
            byParent[parentId].OrderBy(a => a.Code)
                .Select(a => new AccountTreeNodeDto(a, Build(a.Id)))
                .ToList();
        return Build(null);
    }

    public async Task<AccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description);
    }

    public async Task<AccountDto> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim();
        if (await db.Accounts.AnyAsync(a => a.Code == code, ct)) throw Fail("Account code already exists.");
        if (request.ParentId is int pid && !await db.Accounts.AnyAsync(a => a.Id == pid, ct)) throw Fail("Parent account not found.");

        var account = new Account(code, request.Name, request.Type, request.ParentId, request.IsPostable, request.Description);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(account.Id, ct))!;
    }

    public async Task<AccountDto> UpdateAsync(int id, UpdateAccountRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        if (request.ParentId == id) throw Fail("An account cannot be its own parent.");
        if (request.ParentId is int pid && !await db.Accounts.AnyAsync(a => a.Id == pid, ct)) throw Fail("Parent account not found.");
        if (!request.IsPostable && await db.JournalEntryLines.AnyAsync(l => l.AccountId == id, ct))
            throw Fail("Cannot mark a used account as non-postable.");

        account.Update(request.Name, request.Type, request.ParentId, request.IsPostable, request.Description);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        if (await db.Accounts.AnyAsync(a => a.ParentId == id, ct)) throw Fail("Cannot delete an account that has children.");
        if (await db.JournalEntryLines.AnyAsync(l => l.AccountId == id, ct)) throw Fail("Cannot delete an account used in journal entries.");
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        account.SetActive(active);
        await db.SaveChangesAsync(ct);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("Account", message)]);
}
