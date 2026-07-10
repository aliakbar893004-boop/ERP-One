using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Tagihan hutang dari 1+ GRN. Baris immutable; header bisa diubah saat Open &amp; belum dibayar.</summary>
public class SupplierInvoice : AuditableEntity
{
    private readonly List<SupplierInvoiceLine> _lines = [];

    public int Id { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public string? SupplierInvoiceNo { get; private set; }
    public int SupplierId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public string? Notes { get; private set; }
    public SupplierInvoiceStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal PaidAmount { get; private set; }

    public decimal Outstanding => GrandTotal - PaidAmount;
    public IReadOnlyCollection<SupplierInvoiceLine> Lines => _lines;

    private SupplierInvoice() { } // EF Core

    public SupplierInvoice(string invoiceNumber, int supplierId, string currency,
        DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("InvoiceNumber is required.", nameof(invoiceNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        InvoiceNumber = invoiceNumber.Trim();
        SupplierId = supplierId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        SetHeader(invoiceDate, dueDate, supplierInvoiceNo, notes);
        Status = SupplierInvoiceStatus.Open;
    }

    public void SetLines(IEnumerable<SupplierInvoiceLine> lines)
    {
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
        RecomputeTotals();
    }

    public void UpdateHeader(DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (Status != SupplierInvoiceStatus.Open || PaidAmount != 0)
            throw new InvalidOperationException("Only an open, unpaid invoice can be edited.");
        SetHeader(invoiceDate, dueDate, supplierInvoiceNo, notes);
    }

    public void Cancel()
    {
        if (Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Invoice is already cancelled.");
        if (PaidAmount != 0)
            throw new InvalidOperationException("A paid or partially-paid invoice cannot be cancelled.");
        Status = SupplierInvoiceStatus.Cancelled;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Payment amount must be > 0.", nameof(amount));
        if (Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot pay a cancelled invoice.");
        if (PaidAmount + amount > GrandTotal)
            throw new InvalidOperationException("Payment exceeds the invoice outstanding amount.");
        PaidAmount += amount;
        Status = PaidAmount >= GrandTotal ? SupplierInvoiceStatus.Paid : SupplierInvoiceStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Reversal amount must be > 0.", nameof(amount));
        if (amount > PaidAmount)
            throw new InvalidOperationException("Reversal exceeds the paid amount.");
        PaidAmount -= amount;
        Status = PaidAmount <= 0 ? SupplierInvoiceStatus.Open : SupplierInvoiceStatus.PartiallyPaid;
    }

    private void SetHeader(DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (dueDate.Date < invoiceDate.Date)
            throw new ArgumentException("DueDate cannot be before InvoiceDate.", nameof(dueDate));
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        SupplierInvoiceNo = string.IsNullOrWhiteSpace(supplierInvoiceNo) ? null : supplierInvoiceNo.Trim();
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
