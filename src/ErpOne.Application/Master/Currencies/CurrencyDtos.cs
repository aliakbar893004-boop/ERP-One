namespace ErpOne.Application.Currencies;

public record CurrencyDto(int Id, string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive, DateTime CreatedAt, string? CreatedBy);
public record CreateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive);
public record UpdateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive);
