using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Tagihan piutang dari 1+ SO. Baris immutable; header bisa diubah saat Open &amp; belum dibayar.</summary>
public class CustomerInvoice : AuditableEntity
{
    private readonly List<CustomerInvoiceLine> _lines = [];

    public int Id { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public string? CustomerRef { get; private set; }
    public int CustomerId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public string? Notes { get; private set; }
    public CustomerInvoiceStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal PaidAmount { get; private set; }

    public decimal Outstanding => GrandTotal - PaidAmount;
    public IReadOnlyCollection<CustomerInvoiceLine> Lines => _lines;

    private CustomerInvoice() { } // EF Core

    public CustomerInvoice(string invoiceNumber, int customerId, string currency,
        DateTime invoiceDate, DateTime dueDate, string? customerRef, string? notes)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("InvoiceNumber is required.", nameof(invoiceNumber));
        if (customerId <= 0) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        InvoiceNumber = invoiceNumber.Trim();
        CustomerId = customerId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        SetHeader(invoiceDate, dueDate, customerRef, notes);
        Status = CustomerInvoiceStatus.Open;
    }

    public void SetLines(IEnumerable<CustomerInvoiceLine> lines)
    {
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
        RecomputeTotals();
    }

    public void UpdateHeader(DateTime invoiceDate, DateTime dueDate, string? customerRef, string? notes)
    {
        if (Status != CustomerInvoiceStatus.Open || PaidAmount != 0)
            throw new InvalidOperationException("Only an open, unpaid invoice can be edited.");
        SetHeader(invoiceDate, dueDate, customerRef, notes);
    }

    public void Cancel()
    {
        if (Status == CustomerInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Invoice is already cancelled.");
        if (PaidAmount != 0)
            throw new InvalidOperationException("A paid or partially-paid invoice cannot be cancelled.");
        Status = CustomerInvoiceStatus.Cancelled;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Payment amount must be > 0.", nameof(amount));
        if (Status == CustomerInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot receive against a cancelled invoice.");
        if (PaidAmount + amount > GrandTotal)
            throw new InvalidOperationException("Receipt exceeds the invoice outstanding amount.");
        PaidAmount += amount;
        Status = PaidAmount >= GrandTotal ? CustomerInvoiceStatus.Paid : CustomerInvoiceStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Reversal amount must be > 0.", nameof(amount));
        if (amount > PaidAmount)
            throw new InvalidOperationException("Reversal exceeds the paid amount.");
        PaidAmount -= amount;
        Status = PaidAmount <= 0 ? CustomerInvoiceStatus.Open : CustomerInvoiceStatus.PartiallyPaid;
    }

    private void SetHeader(DateTime invoiceDate, DateTime dueDate, string? customerRef, string? notes)
    {
        if (dueDate.Date < invoiceDate.Date)
            throw new ArgumentException("DueDate cannot be before InvoiceDate.", nameof(dueDate));
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        CustomerRef = string.IsNullOrWhiteSpace(customerRef) ? null : customerRef.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
    }
}
