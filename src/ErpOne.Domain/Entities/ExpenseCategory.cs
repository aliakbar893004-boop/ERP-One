using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Kategori biaya operasional (master).</summary>
public class ExpenseCategory : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }

    private ExpenseCategory() { } // EF Core

    public ExpenseCategory(string code, string name, bool isActive) => Set(code, name, isActive);
    public void Update(string code, string name, bool isActive) => Set(code, name, isActive);

    private void Set(string code, string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = isActive;
    }
}
