using ErpOne.Domain.Entities;

namespace ErpOne.Application.Products;

public record StatusCount(ProductStatus Status, int Count);

public record CategoryStock(string CategoryName, int ProductCount, int TotalStock);

public record LowStockItem(int Id, string Sku, string Name, int Stock, ProductStatus Status, string? ImageUrl);

public record ProductDashboardDto(
    int TotalProducts,
    int TotalCategories,
    int TotalStock,
    decimal InventoryValue,
    int ActiveCount,
    int OutOfStockCount,
    int LowStockCount,
    IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<CategoryStock> ByCategory,
    IReadOnlyList<LowStockItem> LowStock);
