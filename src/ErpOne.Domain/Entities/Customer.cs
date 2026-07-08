using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pelanggan untuk transaksi penjualan.</summary>
public class Customer : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? TaxId { get; private set; }
    public int PaymentTermDays { get; private set; }
    public string DefaultCurrency { get; private set; } = "IDR";
    public decimal CreditLimit { get; private set; }
    public bool IsActive { get; private set; }

    private Customer() { } // EF Core

    public Customer(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, creditLimit, isActive);
    }

    public void Update(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, creditLimit, isActive);
    }

    private void Apply(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        SetCode(code);
        SetName(name);
        ContactPerson = Clean(contactPerson);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        TaxId = Clean(taxId);
        SetPaymentTermDays(paymentTermDays);
        SetCurrency(defaultCurrency);
        SetCreditLimit(creditLimit);
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

    private void SetPaymentTermDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Payment term days cannot be negative.", nameof(days));
        PaymentTermDays = days;
    }

    private void SetCreditLimit(decimal limit)
    {
        if (limit < 0)
            throw new ArgumentException("Credit limit cannot be negative.", nameof(limit));
        CreditLimit = limit;
    }

    private void SetCurrency(string? currency) =>
        DefaultCurrency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
