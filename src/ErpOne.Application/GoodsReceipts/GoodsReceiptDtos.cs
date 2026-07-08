namespace ErpOne.Application.GoodsReceipts;

public record GoodsReceiptLineDto(
    int Id, int PurchaseOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int QuantityReceived, decimal UnitCost, decimal LineCost);

public record GoodsReceiptDto(
    int Id, string GrnNumber, int PurchaseOrderId, string PoNumber,
    int SupplierId, string SupplierName, int WarehouseId, string WarehouseName,
    DateTime ReceiptDate, string? Notes, string Status,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<GoodsReceiptLineDto> Lines);

public record GoodsReceiptListItemDto(
    int Id, string GrnNumber, int PurchaseOrderId, string PoNumber, string SupplierName,
    DateTime ReceiptDate, string Status, int TotalQuantity);

public record GoodsReceiptDashboardDto(
    int TotalCount, int DraftCount, int PostedCount);

public record ReceivablePoDto(
    int Id, string PoNumber, string SupplierName, DateTime OrderDate, string Status);

public record PoForReceiptLineDto(
    int PurchaseOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int AlreadyReceivedQuantity, int RemainingQuantity, decimal DefaultUnitCost);

public record PoForReceiptDto(
    int PurchaseOrderId, string PoNumber, int SupplierId, string SupplierName,
    int WarehouseId, string WarehouseName, string Currency,
    IReadOnlyList<PoForReceiptLineDto> Lines);

public record GoodsReceiptLineRequest(int PurchaseOrderLineId, int QuantityReceived, decimal UnitCost);

public record CreateGoodsReceiptRequest(
    int PurchaseOrderId, DateTime ReceiptDate, string? Notes,
    IReadOnlyList<GoodsReceiptLineRequest> Lines);

public record UpdateGoodsReceiptRequest(
    DateTime ReceiptDate, string? Notes,
    IReadOnlyList<GoodsReceiptLineRequest> Lines);
