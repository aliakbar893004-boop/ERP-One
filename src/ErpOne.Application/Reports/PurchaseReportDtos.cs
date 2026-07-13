namespace ErpOne.Application.Reports;

public record PurchaseFilter(
    DateTime? From, DateTime? To, int? SupplierId, int? WarehouseId, string? Search);

public record PurchaseRowDto(
    DateTime Date, string GrnNumber, int SupplierId, string SupplierName,
    int WarehouseId, string WarehouseName, int VariantId, string Sku, string ProductName,
    int Quantity, decimal UnitCost, decimal Value);

public record PurchaseSummaryDto(int Lines, int Qty, decimal TotalCost, int Receipts);
