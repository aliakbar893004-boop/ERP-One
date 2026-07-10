using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Penerimaan uang dari customer atas 1+ invoice. Dibuat langsung Posted (mutasi kas In).</summary>
public class CustomerReceipt : AuditableEntity
{
    private readonly List<CustomerReceiptAllocation> _allocations = [];

    public int Id { get; private set; }
    public string ReceiptNumber { get; private set; } = default!;
    public int CustomerId { get; private set; }
    public int CashBankAccountId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime ReceiptDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Notes { get; private set; }
    public CustomerReceiptStatus Status { get; private set; }

    public IReadOnlyCollection<CustomerReceiptAllocation> Allocations => _allocations;

    private CustomerReceipt() { } // EF Core

    public CustomerReceipt(string receiptNumber, int customerId, int cashBankAccountId, string currency,
        DateTime receiptDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber)) throw new ArgumentException("ReceiptNumber is required.", nameof(receiptNumber));
        if (customerId <= 0) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        ReceiptNumber = receiptNumber.Trim();
        CustomerId = customerId;
        CashBankAccountId = cashBankAccountId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        ReceiptDate = receiptDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = CustomerReceiptStatus.Posted;
    }

    /// <summary>Set alokasi (sekali, saat pembuatan) &amp; hitung Amount = Σ alokasi.</summary>
    public void SetAllocations(IEnumerable<CustomerReceiptAllocation> allocations)
    {
        _allocations.Clear();
        foreach (var a in allocations) _allocations.Add(a);
        Amount = _allocations.Sum(a => a.Amount);
    }

    public void Void()
    {
        if (Status != CustomerReceiptStatus.Posted)
            throw new InvalidOperationException("Only a posted receipt can be voided.");
        Status = CustomerReceiptStatus.Voided;
    }
}
