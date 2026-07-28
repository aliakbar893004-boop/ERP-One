using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Daftar harga struktural (Retail/Grosir/Reseller). Dimensi waktu adalah urusan promo, bukan di sini.</summary>
public class PriceList : AuditableEntity
{
    private readonly List<PriceListLine> _lines = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<PriceListLine> Lines => _lines;

    private PriceList() { } // EF Core

    public PriceList(string code, string name, string? description, bool isActive)
        => Apply(code, name, description, isActive);

    public void Update(string code, string name, string? description, bool isActive)
        => Apply(code, name, description, isActive);

    public void SetLines(IEnumerable<PriceListLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }

    private void Apply(string code, string name, string? description, bool isActive)
    {
        SetCode(code);
        SetName(name);
        Description = Clean(description);
        IsActive = isActive;
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
