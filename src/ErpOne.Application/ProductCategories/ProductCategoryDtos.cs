namespace ErpOne.Application.ProductCategories;

public record ProductCategoryDto(int Id, string Code, string Name, string? Description, DateTime CreatedAt, string? CreatedBy);

public record CreateProductCategoryRequest(string Code, string Name, string? Description);

public record UpdateProductCategoryRequest(string Code, string Name, string? Description);
