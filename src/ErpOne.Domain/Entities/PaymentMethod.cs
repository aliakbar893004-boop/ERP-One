using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Metode pembayaran POS.</summary>
public class PaymentMethod : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public PaymentType Type { get; private set; }
    public bool IsActive { get; private set; }

    private PaymentMethod() { }

    public PaymentMethod(string code, string name, PaymentType type, bool isActive)
    {
        SetCode(code); SetName(name); Type = type; IsActive = isActive;
    }

    public void Update(string code, string name, PaymentType type, bool isActive)
    {
        SetCode(code); SetName(name); Type = type; IsActive = isActive;
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
}
