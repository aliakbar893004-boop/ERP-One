namespace ErpOne.Application.Brands;

public record BrandDto(int Id, string Code, string Name, string? Description, DateTime CreatedAt, string? CreatedBy);
public record CreateBrandRequest(string Code, string Name, string? Description);
public record UpdateBrandRequest(string Code, string Name, string? Description);
