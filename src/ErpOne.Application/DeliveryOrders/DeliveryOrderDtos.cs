namespace ErpOne.Application.DeliveryOrders;

public record DeliveryOrderLineDto(
    int Id, int SalesOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int QuantityDelivered, decimal UnitCost, decimal LineCost);

public record DeliveryOrderDto(
    int Id, string DoNumber, int SalesOrderId, string SoNumber,
    int CustomerId, string CustomerName, int WarehouseId, string WarehouseName,
    DateTime DeliveryDate, string? Notes, string Status,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<DeliveryOrderLineDto> Lines);

public record DeliveryOrderListItemDto(
    int Id, string DoNumber, int SalesOrderId, string SoNumber, string CustomerName,
    DateTime DeliveryDate, string Status, int TotalQuantity);

public record DeliveryOrderDashboardDto(
    int TotalCount, int DraftCount, int PostedCount);

public record DeliverableSoDto(
    int Id, string SoNumber, string CustomerName, DateTime OrderDate, string Status);

public record SoForDeliveryLineDto(
    int SalesOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int AlreadyDeliveredQuantity, int RemainingQuantity);

public record SoForDeliveryDto(
    int SalesOrderId, string SoNumber, int CustomerId, string CustomerName,
    int WarehouseId, string WarehouseName, string Currency,
    IReadOnlyList<SoForDeliveryLineDto> Lines);

public record DeliveryOrderLineRequest(int SalesOrderLineId, int QuantityDelivered);

public record CreateDeliveryOrderRequest(
    int SalesOrderId, DateTime DeliveryDate, string? Notes,
    IReadOnlyList<DeliveryOrderLineRequest> Lines);

public record UpdateDeliveryOrderRequest(
    DateTime DeliveryDate, string? Notes,
    IReadOnlyList<DeliveryOrderLineRequest> Lines);
