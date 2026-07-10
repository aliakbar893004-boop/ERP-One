namespace ErpOne.Domain.Entities;

/// <summary>Alokasi satu receipt ke satu invoice customer.</summary>
public class CustomerReceiptAllocation
{
    public int Id { get; private set; }
    public int CustomerReceiptId { get; private set; }
    public int CustomerInvoiceId { get; private set; }
    public decimal Amount { get; private set; }

    private CustomerReceiptAllocation() { } // EF Core

    public CustomerReceiptAllocation(int customerInvoiceId, decimal amount)
    {
        if (customerInvoiceId <= 0) throw new ArgumentException("CustomerInvoiceId is required.", nameof(customerInvoiceId));
        if (amount <= 0) throw new ArgumentException("Allocation amount must be > 0.", nameof(amount));
        CustomerInvoiceId = customerInvoiceId;
        Amount = amount;
    }
}
