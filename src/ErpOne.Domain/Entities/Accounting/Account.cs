using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Akun Chart of Accounts. Hierarkis (ParentId); hanya akun postable (leaf) boleh dijurnal.</summary>
public class Account : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public AccountType Type { get; private set; }
    public int? ParentId { get; private set; }
    public bool IsPostable { get; private set; }
    public bool IsActive { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Sisi normal dihitung dari Type (tidak disimpan).</summary>
    public NormalBalanceSide NormalBalance =>
        Type is AccountType.Asset or AccountType.Expense ? NormalBalanceSide.Debit : NormalBalanceSide.Credit;

    private Account() { } // EF Core

    public Account(string code, string name, AccountType type, int? parentId, bool isPostable, string? description)
    {
        SetCode(code);
        SetName(name);
        Type = type;
        ParentId = parentId;
        IsPostable = isPostable;
        Description = Trim(description);
        IsActive = true;
    }

    public void Update(string name, AccountType type, int? parentId, bool isPostable, string? description)
    {
        SetName(name);
        Type = type;
        ParentId = parentId;
        IsPostable = isPostable;
        Description = Trim(description);
    }

    public void SetActive(bool active) => IsActive = active;

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
