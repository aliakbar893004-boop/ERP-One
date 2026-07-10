namespace ErpOne.Application.CustomerReceipts;

public record CustomerReceiptListItemDto(int Id, string ReceiptNumber, string CustomerName, DateTime ReceiptDate, string CashBankAccountName, string Currency, decimal Amount, string Status);
public record CustomerReceiptAllocationDto(int Id, int CustomerInvoiceId, string InvoiceNumber, DateTime DueDate, string InvoiceStatus, decimal InvoiceGrandTotal, decimal InvoiceOutstanding, decimal Amount);
public record CustomerReceiptDto(int Id, string ReceiptNumber, int CustomerId, string CustomerName, int CashBankAccountId, string CashBankAccountName, string Currency, DateTime ReceiptDate, decimal Amount, string? Notes, string Status, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<CustomerReceiptAllocationDto> Allocations);
public record OpenInvoiceDto(int CustomerInvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal, decimal Outstanding);
public record CustomerReceiptDashboardDto(int Total, int Posted, int Voided, decimal PostedThisMonth);
public record ReceiptAllocationInput(int CustomerInvoiceId, decimal Amount);
public record CreateCustomerReceiptRequest(int CustomerId, int CashBankAccountId, DateTime ReceiptDate, string? Notes, IReadOnlyList<ReceiptAllocationInput> Allocations);
