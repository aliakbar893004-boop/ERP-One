using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record AccountDto(int Id, string Code, string Name, AccountType Type, int? ParentId,
    bool IsPostable, bool IsActive, string? Description);

public record AccountTreeNodeDto(AccountDto Account, IReadOnlyList<AccountTreeNodeDto> Children);

public record CreateAccountRequest(string Code, string Name, AccountType Type, int? ParentId,
    bool IsPostable, string? Description);

public record UpdateAccountRequest(string Name, AccountType Type, int? ParentId,
    bool IsPostable, string? Description);
