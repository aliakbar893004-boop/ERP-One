namespace ErpOne.Application.LowStock;

public record LowStockRowDto(
    int VariantId, string Sku, int ProductId, string ProductName,
    int WarehouseId, string WarehouseName,
    int Quantity, int ReorderLevel, int ReorderQty, int SuggestedOrderQty, bool IsOutOfStock);

public record LowStockSummaryDto(IReadOnlyList<LowStockRowDto> Rows, int LowCount, int OutOfStockCount);
