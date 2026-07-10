namespace ErpOne.Domain.Entities;

/// <summary>Alokasi satu payment ke satu invoice.</summary>
public class SupplierPaymentAllocation
{
    public int Id { get; private set; }
    public int SupplierPaymentId { get; private set; }
    public int SupplierInvoiceId { get; private set; }
    public decimal Amount { get; private set; }

    private SupplierPaymentAllocation() { } // EF Core

    public SupplierPaymentAllocation(int supplierInvoiceId, decimal amount)
    {
        if (supplierInvoiceId <= 0) throw new ArgumentException("SupplierInvoiceId is required.", nameof(supplierInvoiceId));
        if (amount <= 0) throw new ArgumentException("Allocation amount must be > 0.", nameof(amount));
        SupplierInvoiceId = supplierInvoiceId;
        Amount = amount;
    }
}
