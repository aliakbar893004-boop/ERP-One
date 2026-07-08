using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Gudang / lokasi penyimpanan stok.</summary>
public class Warehouse : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDefault { get; private set; }

    private Warehouse() { } // EF Core

    public Warehouse(string code, string name, string? address, bool isActive, bool isDefault)
    {
        SetCode(code); SetName(name); SetAddress(address);
        IsActive = isActive; IsDefault = isDefault;
    }

    public void Update(string code, string name, string? address, bool isActive, bool isDefault)
    {
        SetCode(code); SetName(name); SetAddress(address);
        IsActive = isActive; IsDefault = isDefault;
    }

    public void SetAsDefault() => IsDefault = true;
    public void ClearDefault() => IsDefault = false;

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

    private void SetAddress(string? address) =>
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
}
