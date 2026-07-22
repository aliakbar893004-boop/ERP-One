using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Dokumen retur barang dari customer (credit note). Draft → PendingApproval → Posted.</summary>
public class SalesReturn : AuditableEntity
{
    private readonly List<SalesReturnLine> _lines = [];

    public int Id { get; private set; }
    public string ReturnNumber { get; private set; } = default!;
    public int CustomerId { get; private set; }
    public SalesReturnSource SourceType { get; private set; }
    public int? DeliveryOrderId { get; private set; }
    public int? CustomerInvoiceId { get; private set; }
    public DateTime ReturnDate { get; private set; }
    public string? Notes { get; private set; }
    public SalesReturnStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal InventoryTotal { get; private set; }
    public IReadOnlyCollection<SalesReturnLine> Lines => _lines;

    private SalesReturn() { } // EF Core

    public SalesReturn(string returnNumber, int customerId, SalesReturnSource sourceType,
        int? deliveryOrderId, int? customerInvoiceId, DateTime returnDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(returnNumber)) throw new ArgumentException("ReturnNumber is required.", nameof(returnNumber));
        if (customerId <= 0) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        if (sourceType == SalesReturnSource.DeliveryOrder && deliveryOrderId is not > 0)
            throw new ArgumentException("DeliveryOrderId is required for a DO-sourced return.", nameof(deliveryOrderId));
        if (sourceType == SalesReturnSource.CustomerInvoice && customerInvoiceId is not > 0)
            throw new ArgumentException("CustomerInvoiceId is required for an invoice-sourced return.", nameof(customerInvoiceId));

        ReturnNumber = returnNumber.Trim();
        CustomerId = customerId;
        SourceType = sourceType;
        DeliveryOrderId = deliveryOrderId;
        CustomerInvoiceId = customerInvoiceId;
        SetHeader(returnDate, notes);
        Status = SalesReturnStatus.Draft;
    }

    public void SetLines(IEnumerable<SalesReturnLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        _lines.AddRange(lines);
        RecomputeTotals();
    }

    public void UpdateHeader(DateTime returnDate, string? notes)
    {
        EnsureDraft();
        SetHeader(returnDate, notes);
    }

    /// <summary>Hitung ulang InventoryTotal dari UnitCost baris terkini (setelah refresh biaya saat post).</summary>
    public void RecomputeInventoryTotal() =>
        InventoryTotal = _lines.Sum(l => Round(l.Quantity * l.UnitCost));

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit a return without lines.");
        Status = SalesReturnStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != SalesReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be posted.");
        Status = SalesReturnStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != SalesReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be returned to draft.");
        Status = SalesReturnStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void SetHeader(DateTime returnDate, string? notes)
    {
        ReturnDate = returnDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
        InventoryTotal = _lines.Sum(l => Round(l.Quantity * l.UnitCost));
    }

    private void EnsureDraft()
    {
        if (Status != SalesReturnStatus.Draft)
            throw new InvalidOperationException("Only a draft return can be modified.");
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
