using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pajak (mis. PPN 11%).</summary>
public class Tax : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public decimal Rate { get; private set; }       // persen, 0..100
    public bool IsInclusive { get; private set; }
    public string? Description { get; private set; }

    private Tax() { }

    public Tax(string code, string name, decimal rate, bool isInclusive, string? description)
    {
        SetCode(code); SetName(name); SetRate(rate); IsInclusive = isInclusive; SetDescription(description);
    }

    public void Update(string code, string name, decimal rate, bool isInclusive, string? description)
    {
        SetCode(code); SetName(name); SetRate(rate); IsInclusive = isInclusive; SetDescription(description);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
    private void SetRate(decimal rate)
    {
        if (rate is < 0 or > 100) throw new ArgumentException("Rate must be between 0 and 100.", nameof(rate));
        Rate = rate;
    }
    private void SetDescription(string? d) => Description = string.IsNullOrWhiteSpace(d) ? null : d.Trim();
}
