using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Atribut varian (mis. Ukuran, Warna) — memiliki koleksi nilai.</summary>
public class ProductAttribute : AuditableEntity
{
    private readonly List<AttributeValue> _values = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public IReadOnlyList<AttributeValue> Values => _values;

    private ProductAttribute() { }

    public ProductAttribute(string code, string name)
    {
        SetCode(code); SetName(name);
    }

    public void Update(string code, string name)
    {
        SetCode(code); SetName(name);
    }

    public AttributeValue AddValue(string code, string value)
    {
        var v = new AttributeValue(code, value);
        _values.Add(v);
        return v;
    }

    public void RemoveValue(int valueId)
    {
        var v = _values.FirstOrDefault(x => x.Id == valueId);
        if (v is not null) _values.Remove(v);
    }

    public void ClearValues() => _values.Clear();

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
}
