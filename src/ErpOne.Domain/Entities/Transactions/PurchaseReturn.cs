using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Dokumen retur barang ke supplier (debit note). Draft → PendingApproval → Posted.</summary>
public class PurchaseReturn : AuditableEntity
{
    private readonly List<PurchaseReturnLine> _lines = [];

    public int Id { get; private set; }
    public string ReturnNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public PurchaseReturnSource SourceType { get; private set; }
    public int? GoodsReceiptId { get; private set; }
    public int? SupplierInvoiceId { get; private set; }
    public DateTime ReturnDate { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseReturnStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal InventoryTotal { get; private set; }
    public IReadOnlyCollection<PurchaseReturnLine> Lines => _lines;

    private PurchaseReturn() { } // EF Core

    public PurchaseReturn(string returnNumber, int supplierId, PurchaseReturnSource sourceType,
        int? goodsReceiptId, int? supplierInvoiceId, DateTime returnDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(returnNumber)) throw new ArgumentException("ReturnNumber is required.", nameof(returnNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        if (sourceType == PurchaseReturnSource.GoodsReceipt && goodsReceiptId is not > 0)
            throw new ArgumentException("GoodsReceiptId is required for a GRN-sourced return.", nameof(goodsReceiptId));
        if (sourceType == PurchaseReturnSource.SupplierInvoice && supplierInvoiceId is not > 0)
            throw new ArgumentException("SupplierInvoiceId is required for an invoice-sourced return.", nameof(supplierInvoiceId));

        ReturnNumber = returnNumber.Trim();
        SupplierId = supplierId;
        SourceType = sourceType;
        GoodsReceiptId = goodsReceiptId;
        SupplierInvoiceId = supplierInvoiceId;
        SetHeader(returnDate, notes);
        Status = PurchaseReturnStatus.Draft;
    }

    public void SetLines(IEnumerable<PurchaseReturnLine> lines)
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

    /// <summary>Hitung ulang InventoryTotal dari UnitCost baris terkini (dipanggil setelah refresh biaya seam saat post).</summary>
    public void RecomputeInventoryTotal() =>
        InventoryTotal = _lines.Sum(l => Round(l.Quantity * l.UnitCost));

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit a return without lines.");
        Status = PurchaseReturnStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != PurchaseReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be posted.");
        Status = PurchaseReturnStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != PurchaseReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be returned to draft.");
        Status = PurchaseReturnStatus.Draft;
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
        if (Status != PurchaseReturnStatus.Draft)
            throw new InvalidOperationException("Only a draft return can be modified.");
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
