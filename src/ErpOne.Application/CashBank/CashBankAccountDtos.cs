namespace ErpOne.Application.CashBank;

public record CashBankAccountDto(int Id, string Code, string Name, string Type, string Currency, decimal OpeningBalance, string? BankName, string? AccountNumber, string? AccountHolder, bool IsActive, DateTime CreatedAt, string? CreatedBy);
public record CreateCashBankAccountRequest(string Code, string Name, string Type, string Currency, decimal OpeningBalance, string? BankName, string? AccountNumber, string? AccountHolder, bool IsActive);
public record UpdateCashBankAccountRequest(string Code, string Name, string Type, string Currency, decimal OpeningBalance, string? BankName, string? AccountNumber, string? AccountHolder, bool IsActive);
