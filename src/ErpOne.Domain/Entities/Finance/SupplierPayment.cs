using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pembayaran ke supplier atas 1+ invoice. Uang keluar (ledger) saat Posted.</summary>
public class SupplierPayment : AuditableEntity
{
    private readonly List<SupplierPaymentAllocation> _allocations = [];

    public int Id { get; private set; }
    public string PaymentNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public int CashBankAccountId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Notes { get; private set; }
    public SupplierPaymentStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<SupplierPaymentAllocation> Allocations => _allocations;

    private SupplierPayment() { } // EF Core

    public SupplierPayment(string paymentNumber, int supplierId, int cashBankAccountId, string currency,
        DateTime paymentDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(paymentNumber)) throw new ArgumentException("PaymentNumber is required.", nameof(paymentNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        PaymentNumber = paymentNumber.Trim();
        SupplierId = supplierId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        SetHeader(cashBankAccountId, paymentDate, notes);
        Status = SupplierPaymentStatus.Draft;
    }

    public void SetAllocations(IEnumerable<SupplierPaymentAllocation> allocations)
    {
        EnsureDraft();
        _allocations.Clear();
        foreach (var a in allocations) _allocations.Add(a);
        Amount = _allocations.Sum(a => a.Amount);
    }

    public void UpdateHeader(int cashBankAccountId, DateTime paymentDate, string? notes)
    {
        EnsureDraft();
        SetHeader(cashBankAccountId, paymentDate, notes);
    }

    public void Submit()
    {
        EnsureDraft();
        if (_allocations.Count == 0) throw new InvalidOperationException("Cannot submit a payment without allocations.");
        Status = SupplierPaymentStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != SupplierPaymentStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending payment can be posted.");
        Status = SupplierPaymentStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != SupplierPaymentStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending payment can be returned to draft.");
        Status = SupplierPaymentStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Void()
    {
        if (Status != SupplierPaymentStatus.Posted)
            throw new InvalidOperationException("Only a posted payment can be voided.");
        Status = SupplierPaymentStatus.Voided;
    }

    private void SetHeader(int cashBankAccountId, DateTime paymentDate, string? notes)
    {
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        CashBankAccountId = cashBankAccountId;
        PaymentDate = paymentDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != SupplierPaymentStatus.Draft)
            throw new InvalidOperationException("Only a draft payment can be modified.");
    }
}
