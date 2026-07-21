using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

/// <summary>Membangun jurnal double-entry otomatis dari dokumen. Ikut transaksi caller (tak buka tx sendiri).</summary>
public interface IJournalPostingService
{
    Task PostGoodsReceiptAsync(GoodsReceipt grn, CancellationToken ct = default);
    Task PostSupplierInvoiceAsync(SupplierInvoice inv, CancellationToken ct = default);
    Task PostSupplierPaymentAsync(SupplierPayment pay, CancellationToken ct = default);
    Task PostCustomerInvoiceAsync(CustomerInvoice inv, CancellationToken ct = default);
    Task PostDeliveryOrderAsync(DeliveryOrder dorder, CancellationToken ct = default);
    Task PostCustomerReceiptAsync(CustomerReceipt rec, CancellationToken ct = default);
    Task PostExpenseAsync(Expense exp, CancellationToken ct = default);
    Task PostPosSaleAsync(PosSale sale, CancellationToken ct = default);
    Task ReverseForAsync(string sourceType, int sourceId, DateTime date, string? note, CancellationToken ct = default);
}
