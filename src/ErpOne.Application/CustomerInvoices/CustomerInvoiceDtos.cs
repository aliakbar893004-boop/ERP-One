namespace ErpOne.Application.CustomerInvoices;

public record CustomerInvoiceListItemDto(int Id, string InvoiceNumber, string CustomerName, DateTime InvoiceDate, DateTime DueDate, string Currency, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, string Status);

public record CustomerInvoiceLineDto(int Id, int SalesOrderId, string SoNumber, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal);

public record CustomerInvoiceDto(int Id, string InvoiceNumber, string? CustomerRef, int CustomerId, string CustomerName, string Currency, DateTime InvoiceDate, DateTime DueDate, string? Notes, string Status, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<CustomerInvoiceLineDto> Lines);

public record UninvoicedSalesOrderDto(int SalesOrderId, string SoNumber, DateTime OrderDate, decimal GrandTotal, IReadOnlyList<CustomerInvoiceLineDto> Lines);

public record CustomerInvoiceDashboardDto(int Total, int Open, int PartiallyPaid, int Paid, decimal TotalOutstanding);

public record CustomerCreditDto(decimal CreditLimit, decimal Outstanding, decimal Available);

public record CreateCustomerInvoiceRequest(int CustomerId, DateTime InvoiceDate, DateTime? DueDate, string? CustomerRef, string? Notes, IReadOnlyList<int> SalesOrderIds);

public record UpdateCustomerInvoiceHeaderRequest(DateTime InvoiceDate, DateTime DueDate, string? CustomerRef, string? Notes);
