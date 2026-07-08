namespace ErpOne.Application.Suppliers;

public record SupplierDto(
    int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive,
    DateTime CreatedAt, string? CreatedBy);

public record CreateSupplierRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive);

public record UpdateSupplierRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive);
