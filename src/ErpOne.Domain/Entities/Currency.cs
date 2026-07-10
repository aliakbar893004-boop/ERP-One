using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Mata uang (master). Tanpa kurs — konversi di luar scope Fase 0.</summary>
public class Currency : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;   // ISO 4217, mis. IDR
    public string Name { get; private set; } = default!;
    public string Symbol { get; private set; } = default!;
    public int DecimalPlaces { get; private set; }
    public bool IsBase { get; private set; }
    public bool IsActive { get; private set; }

    private Currency() { } // EF Core

    public Currency(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
        => Set(code, name, symbol, decimalPlaces, isBase, isActive);

    public void Update(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
        => Set(code, name, symbol, decimalPlaces, isBase, isActive);

    private void Set(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (decimalPlaces is < 0 or > 6) throw new ArgumentException("DecimalPlaces must be 0-6.", nameof(decimalPlaces));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Symbol = symbol.Trim();
        DecimalPlaces = decimalPlaces;
        IsBase = isBase;
        IsActive = isActive;
    }
}
