namespace ErpOne.Application.Units;

public record UnitDto(int Id, string Code, string Name, string? Description, DateTime CreatedAt, string? CreatedBy);

public record CreateUnitRequest(string Code, string Name, string? Description);

public record UpdateUnitRequest(string Code, string Name, string? Description);
