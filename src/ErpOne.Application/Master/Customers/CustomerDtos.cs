namespace ErpOne.Application.Customers;

public record CustomerDto(
    int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency,
    decimal CreditLimit, bool IsActive, DateTime CreatedAt, string? CreatedBy,
    int? PriceListId = null);

public record CreateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive, int? PriceListId = null);

public record UpdateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive, int? PriceListId = null);
