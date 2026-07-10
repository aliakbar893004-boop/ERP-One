namespace ErpOne.Application.SupplierPayments;

public record SupplierPaymentListItemDto(int Id, string PaymentNumber, string SupplierName, DateTime PaymentDate, string CashBankAccountName, string Currency, decimal Amount, string Status);
public record SupplierPaymentAllocationDto(int Id, int SupplierInvoiceId, string InvoiceNumber, string? SupplierInvoiceNo, DateTime DueDate, string InvoiceStatus, decimal InvoiceGrandTotal, decimal InvoiceOutstanding, decimal Amount);
public record SupplierPaymentDto(int Id, string PaymentNumber, int SupplierId, string SupplierName, int CashBankAccountId, string CashBankAccountName, string Currency, DateTime PaymentDate, decimal Amount, string? Notes, string Status, string? RejectionNote, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierPaymentAllocationDto> Allocations);
public record PayableInvoiceDto(int SupplierInvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal, decimal Outstanding);
public record SupplierPaymentDashboardDto(int Total, int Draft, int PendingApproval, int Posted, decimal PostedThisMonth);
public record PaymentAllocationInput(int SupplierInvoiceId, decimal Amount);
public record CreateSupplierPaymentRequest(int SupplierId, int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations);
public record UpdateSupplierPaymentRequest(int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations);
