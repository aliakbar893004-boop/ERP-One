using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Nilai dari sebuah atribut (mis. S/M/L untuk Ukuran).</summary>
public class AttributeValue : AuditableEntity
{
    public int Id { get; private set; }
    public int AttributeId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    private AttributeValue() { }

    public AttributeValue(string code, string value)
    {
        SetCode(code); SetValue(value);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Value code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", nameof(value));
        Value = value.Trim();
    }
}
