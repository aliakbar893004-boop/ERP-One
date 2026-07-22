using ErpOne.Application.Approvals;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public record ReturnableLineDto(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, int WarehouseId, string WarehouseName, int SourceQty, int AlreadyReturnedQty,
    int RemainingQty, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot);

public record ReturnableSourceDto(string SourceType, int? GoodsReceiptId, int? SupplierInvoiceId, string SourceNumber,
    int SupplierId, string SupplierName, IReadOnlyList<ReturnableLineDto> Lines);

public record ReturnableSourceOptionDto(string SourceType, int DocId, string DocNumber, DateTime DocDate, string SupplierName);

public record PurchaseReturnLineInput(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int Quantity);

public record CreatePurchaseReturnRequest(string SourceType, int? GoodsReceiptId, int? SupplierInvoiceId,
    DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines);

public record UpdatePurchaseReturnRequest(DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines);

public record PurchaseReturnLineDto(int Id, int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, string WarehouseName, int Quantity, decimal UnitCost, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineTotal);

public record PurchaseReturnDto(int Id, string ReturnNumber, string SourceType, int? GoodsReceiptId, string? GrnNumber,
    int? SupplierInvoiceId, string? InvoiceNumber, int SupplierId, string SupplierName, DateTime ReturnDate, string? Notes,
    string Status, string? RejectionNote, string? CreatedBy, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrandTotal, decimal InventoryTotal, IReadOnlyList<PurchaseReturnLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record PurchaseReturnListItemDto(int Id, string ReturnNumber, DateTime ReturnDate, string SourceType,
    string SupplierName, int LineCount, decimal GrandTotal, string Status);
