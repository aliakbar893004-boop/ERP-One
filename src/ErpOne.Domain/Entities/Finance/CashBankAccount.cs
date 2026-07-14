using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Akun kas atau bank (master). Saldo berjalan dihitung dari mutasi (AP/AR/Expense).</summary>
public class CashBankAccount : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public CashBankType Type { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public decimal OpeningBalance { get; private set; }
    public string? BankName { get; private set; }
    public string? AccountNumber { get; private set; }
    public string? AccountHolder { get; private set; }
    public bool IsActive { get; private set; }

    private CashBankAccount() { } // EF Core

    public CashBankAccount(string code, string name, CashBankType type, string currency,
        decimal openingBalance, string? bankName, string? accountNumber, string? accountHolder, bool isActive)
        => Set(code, name, type, currency, openingBalance, bankName, accountNumber, accountHolder, isActive);

    public void Update(string code, string name, CashBankType type, string currency,
        decimal openingBalance, string? bankName, string? accountNumber, string? accountHolder, bool isActive)
        => Set(code, name, type, currency, openingBalance, bankName, accountNumber, accountHolder, isActive);

    private void Set(string code, string name, CashBankType type, string currency,
        decimal openingBalance, string? bankName, string? accountNumber, string? accountHolder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (openingBalance < 0) throw new ArgumentException("Opening balance cannot be negative.", nameof(openingBalance));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        OpeningBalance = openingBalance;
        BankName = Clean(bankName);
        AccountNumber = Clean(accountNumber);
        AccountHolder = Clean(accountHolder);
        IsActive = isActive;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
