using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Satuan barang (unit of measure), mis. PCS, KG, BOX.</summary>
public class Unit : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    private Unit() { } // EF Core

    public Unit(string code, string name, string? description)
    {
        SetCode(code);
        SetName(name);
        SetDescription(description);
    }

    public void Update(string code, string name, string? description)
    {
        SetCode(code);
        SetName(name);
        SetDescription(description);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }

    private void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
