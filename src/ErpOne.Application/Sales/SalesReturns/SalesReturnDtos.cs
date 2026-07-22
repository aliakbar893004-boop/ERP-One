using ErpOne.Application.Approvals;

namespace ErpOne.Application.Sales.SalesReturns;

public record ReturnableLineDto(int DeliveryOrderLineId, int? CustomerInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, int WarehouseId, string WarehouseName, int SourceQty, int AlreadyReturnedQty,
    int RemainingQty, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot);

public record ReturnableSourceDto(string SourceType, int? DeliveryOrderId, int? CustomerInvoiceId, string SourceNumber,
    int CustomerId, string CustomerName, IReadOnlyList<ReturnableLineDto> Lines);

public record ReturnableSourceOptionDto(string SourceType, int DocId, string DocNumber, DateTime DocDate, string CustomerName);

public record SalesReturnLineInput(int DeliveryOrderLineId, int? CustomerInvoiceLineId, int Quantity);

public record CreateSalesReturnRequest(string SourceType, int? DeliveryOrderId, int? CustomerInvoiceId,
    DateTime ReturnDate, string? Notes, IReadOnlyList<SalesReturnLineInput> Lines);

public record UpdateSalesReturnRequest(DateTime ReturnDate, string? Notes, IReadOnlyList<SalesReturnLineInput> Lines);

public record SalesReturnLineDto(int Id, int DeliveryOrderLineId, int? CustomerInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, string WarehouseName, int Quantity, decimal UnitCost, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineTotal);

public record SalesReturnDto(int Id, string ReturnNumber, string SourceType, int? DeliveryOrderId, string? DoNumber,
    int? CustomerInvoiceId, string? InvoiceNumber, int CustomerId, string CustomerName, DateTime ReturnDate, string? Notes,
    string Status, string? RejectionNote, string? CreatedBy, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrandTotal, decimal InventoryTotal, IReadOnlyList<SalesReturnLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record SalesReturnListItemDto(int Id, string ReturnNumber, DateTime ReturnDate, string SourceType,
    string CustomerName, int LineCount, decimal GrandTotal, string Status);
