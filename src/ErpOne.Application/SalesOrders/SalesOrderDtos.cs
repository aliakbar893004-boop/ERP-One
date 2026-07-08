namespace ErpOne.Application.SalesOrders;

public record SalesOrderLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal,
    int DeliveredQuantity);

public record SalesOrderDto(
    int Id, string SoNumber, int CustomerId, string CustomerName, int WarehouseId, string WarehouseName,
    DateTime OrderDate, DateTime? ExpectedDate, string Currency, string? Notes,
    string Status, string? RejectionNote,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<SalesOrderLineDto> Lines);

public record SalesOrderListItemDto(
    int Id, string SoNumber, string CustomerName, DateTime OrderDate,
    string Currency, decimal GrandTotal, string Status);

public record SalesOrderDashboardDto(
    int TotalCount, int DraftCount, int PendingApprovalCount, int ConfirmedCount);

public record SalesOrderVariantOptionDto(
    int VariantId, string Sku, string ProductName, decimal Price, decimal? DiscountPrice);

public record SalesOrderCreditInfoDto(
    decimal CreditLimit, decimal EstimatedOutstanding, decimal ThisOrderTotal, bool ExceedsLimit);

public record SalesOrderLineRequest(
    int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId);

public record CreateSalesOrderRequest(
    int CustomerId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<SalesOrderLineRequest> Lines);

public record UpdateSalesOrderRequest(
    int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<SalesOrderLineRequest> Lines);
