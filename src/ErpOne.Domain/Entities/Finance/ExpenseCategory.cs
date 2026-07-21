using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Kategori biaya operasional (master).</summary>
public class ExpenseCategory : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int? GlAccountId { get; private set; }

    private ExpenseCategory() { } // EF Core

    public ExpenseCategory(string code, string name, bool isActive, int? glAccountId = null) => Set(code, name, isActive, glAccountId);
    public void Update(string code, string name, bool isActive, int? glAccountId = null) => Set(code, name, isActive, glAccountId);

    private void Set(string code, string name, bool isActive, int? glAccountId)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = isActive;
        GlAccountId = glAccountId;
    }
}
