namespace ErpOne.Application.Customers;

public record CustomerDto(
    int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency,
    decimal CreditLimit, bool IsActive, DateTime CreatedAt, string? CreatedBy);

public record CreateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive);

public record UpdateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive);
