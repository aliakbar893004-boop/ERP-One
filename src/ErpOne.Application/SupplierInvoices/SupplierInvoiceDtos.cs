namespace ErpOne.Application.SupplierInvoices;

public record SupplierInvoiceListItemDto(int Id, string InvoiceNumber, string SupplierName, DateTime InvoiceDate, DateTime DueDate, string Currency, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, string Status);

public record SupplierInvoiceLineDto(int Id, int GoodsReceiptId, string GrnNumber, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal);

public record SupplierInvoiceDto(int Id, string InvoiceNumber, string? SupplierInvoiceNo, int SupplierId, string SupplierName, string Currency, DateTime InvoiceDate, DateTime DueDate, string? Notes, string Status, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierInvoiceLineDto> Lines);

public record UninvoicedGrnDto(int GoodsReceiptId, string GrnNumber, DateTime ReceiptDate, string PoNumber, decimal GrandTotal, IReadOnlyList<SupplierInvoiceLineDto> Lines);

public record SupplierInvoiceDashboardDto(int Total, int Open, int PartiallyPaid, int Paid, decimal TotalOutstanding);

public record CreateSupplierInvoiceRequest(int SupplierId, DateTime InvoiceDate, DateTime? DueDate, string? SupplierInvoiceNo, string? Notes, IReadOnlyList<int> GrnIds);

public record UpdateSupplierInvoiceHeaderRequest(DateTime InvoiceDate, DateTime DueDate, string? SupplierInvoiceNo, string? Notes);
