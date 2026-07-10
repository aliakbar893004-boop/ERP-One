namespace ErpOne.Application.CompanySettings;

public record CompanySettingDto(int Id, string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter);
public record UpdateCompanySettingRequest(string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter);
