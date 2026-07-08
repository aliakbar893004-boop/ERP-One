namespace ErpOne.Application.Warehouses;

public record WarehouseDto(int Id, string Code, string Name, string? Address, bool IsActive, bool IsDefault, DateTime CreatedAt, string? CreatedBy);
public record CreateWarehouseRequest(string Code, string Name, string? Address, bool IsActive, bool IsDefault);
public record UpdateWarehouseRequest(string Code, string Name, string? Address, bool IsActive, bool IsDefault);
