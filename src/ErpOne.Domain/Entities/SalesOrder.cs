using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pesanan penjualan ke customer. Baris hanya bisa diubah saat Draft. Gudang = sumber pengiriman.</summary>
public class SalesOrder : AuditableEntity
{
    private readonly List<SalesOrderLine> _lines = [];

    public int Id { get; private set; }
    public string SoNumber { get; private set; } = default!;
    public int CustomerId { get; private set; }
    public int WarehouseId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public string? Notes { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;

    private SalesOrder() { } // EF Core

    public SalesOrder(string soNumber, int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (string.IsNullOrWhiteSpace(soNumber))
            throw new ArgumentException("SoNumber is required.", nameof(soNumber));
        SoNumber = soNumber.Trim();
        SetHeader(customerId, warehouseId, orderDate, expectedDate, currency, notes);
        Status = SalesOrderStatus.Draft;
    }

    public void UpdateHeader(int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        EnsureDraft();
        SetHeader(customerId, warehouseId, orderDate, expectedDate, currency, notes);
    }

    private void SetHeader(int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (customerId <= 0) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (expectedDate is { } ed && ed.Date < orderDate.Date)
            throw new ArgumentException("ExpectedDate cannot be before OrderDate.", nameof(expectedDate));

        CustomerId = customerId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<SalesOrderLine> lines)
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
            throw new InvalidOperationException("Cannot submit a sales order without lines.");
        Status = SalesOrderStatus.PendingApproval;
    }

    public void MarkConfirmed()
    {
        if (Status != SalesOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending sales order can be confirmed.");
        Status = SalesOrderStatus.Confirmed;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != SalesOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending sales order can be returned to draft.");
        Status = SalesOrderStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (SalesOrderStatus.Draft or SalesOrderStatus.PendingApproval))
            throw new InvalidOperationException("Only draft or pending sales orders can be cancelled.");
        Status = SalesOrderStatus.Cancelled;
    }

    public bool CanDeliver =>
        Status is SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyDelivered;

    public void MarkPartiallyDelivered()
    {
        if (!CanDeliver)
            throw new InvalidOperationException("Only a confirmed or partially-delivered sales order can record deliveries.");
        Status = SalesOrderStatus.PartiallyDelivered;
    }

    public void MarkDelivered()
    {
        if (!CanDeliver)
            throw new InvalidOperationException("Only a confirmed or partially-delivered sales order can record deliveries.");
        Status = SalesOrderStatus.Delivered;
    }

    public void Close()
    {
        if (Status != SalesOrderStatus.PartiallyDelivered)
            throw new InvalidOperationException("Only a partially-delivered sales order can be closed.");
        Status = SalesOrderStatus.Closed;
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
        if (Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft sales order can be modified.");
    }
}
