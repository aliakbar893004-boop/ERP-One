namespace ErpOne.Application.PurchaseOrders;

public record PurchaseOrderLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal,
    int ReceivedQuantity);

public record PurchaseOrderDto(
    int Id, string PoNumber, int SupplierId, string SupplierName, int WarehouseId, string WarehouseName,
    DateTime OrderDate, DateTime? ExpectedDate, string Currency, string? Notes,
    string Status, string? RejectionNote,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<PurchaseOrderLineDto> Lines);

public record PurchaseOrderListItemDto(
    int Id, string PoNumber, string SupplierName, DateTime OrderDate,
    string Currency, decimal GrandTotal, string Status);

public record PurchaseOrderDashboardDto(
    int TotalCount, int DraftCount, int PendingApprovalCount, int ConfirmedCount);

public record PurchaseOrderVariantOptionDto(int VariantId, string Sku, string ProductName, decimal CostPrice);

public record PurchaseOrderLineRequest(
    int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId);

public record CreatePurchaseOrderRequest(
    int SupplierId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public record UpdatePurchaseOrderRequest(
    int SupplierId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);
