namespace ErpOne.Application.Taxes;

public record TaxDto(int Id, string Code, string Name, decimal Rate, bool IsInclusive, string? Description, DateTime CreatedAt, string? CreatedBy);
public record CreateTaxRequest(string Code, string Name, decimal Rate, bool IsInclusive, string? Description);
public record UpdateTaxRequest(string Code, string Name, decimal Rate, bool IsInclusive, string? Description);
