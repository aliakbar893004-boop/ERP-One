using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pesanan pembelian ke supplier. Baris hanya bisa diubah saat Draft.</summary>
public class PurchaseOrder : AuditableEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    public int Id { get; private set; }
    public string PoNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public int WarehouseId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    private PurchaseOrder() { } // EF Core

    public PurchaseOrder(string poNumber, int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (string.IsNullOrWhiteSpace(poNumber))
            throw new ArgumentException("PoNumber is required.", nameof(poNumber));
        PoNumber = poNumber.Trim();
        SetHeader(supplierId, warehouseId, orderDate, expectedDate, currency, notes);
        Status = PurchaseOrderStatus.Draft;
    }

    public void UpdateHeader(int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        EnsureDraft();
        SetHeader(supplierId, warehouseId, orderDate, expectedDate, currency, notes);
    }

    private void SetHeader(int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (expectedDate is { } ed && ed.Date < orderDate.Date)
            throw new ArgumentException("ExpectedDate cannot be before OrderDate.", nameof(expectedDate));

        SupplierId = supplierId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<PurchaseOrderLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
        RecomputeTotals();
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot submit a purchase order without lines.");
        Status = PurchaseOrderStatus.PendingApproval;
    }

    public void MarkConfirmed()
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending purchase order can be confirmed.");
        Status = PurchaseOrderStatus.Confirmed;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending purchase order can be returned to draft.");
        Status = PurchaseOrderStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval))
            throw new InvalidOperationException("Only draft or pending purchase orders can be cancelled.");
        Status = PurchaseOrderStatus.Cancelled;
    }

    public bool CanReceive =>
        Status is PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived;

    public void MarkPartiallyReceived()
    {
        if (!CanReceive)
            throw new InvalidOperationException("Only a confirmed or partially-received purchase order can record receipts.");
        Status = PurchaseOrderStatus.PartiallyReceived;
    }

    public void MarkReceived()
    {
        if (!CanReceive)
            throw new InvalidOperationException("Only a confirmed or partially-received purchase order can record receipts.");
        Status = PurchaseOrderStatus.Received;
    }

    public void Close()
    {
        if (Status != PurchaseOrderStatus.PartiallyReceived)
            throw new InvalidOperationException("Only a partially-received purchase order can be closed.");
        Status = PurchaseOrderStatus.Closed;
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft purchase order can be modified.");
    }
}
