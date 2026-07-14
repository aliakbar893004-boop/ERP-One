using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pengiriman barang atas satu SO. Baris hanya bisa diubah saat Draft; Post mengunci.</summary>
public class DeliveryOrder : AuditableEntity
{
    private readonly List<DeliveryOrderLine> _lines = [];

    public int Id { get; private set; }
    public string DoNumber { get; private set; } = default!;
    public int SalesOrderId { get; private set; }
    public DateTime DeliveryDate { get; private set; }
    public string? Notes { get; private set; }
    public DeliveryOrderStatus Status { get; private set; }

    public IReadOnlyCollection<DeliveryOrderLine> Lines => _lines;

    private DeliveryOrder() { } // EF Core

    public DeliveryOrder(string doNumber, int salesOrderId, DateTime deliveryDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(doNumber))
            throw new ArgumentException("DoNumber is required.", nameof(doNumber));
        if (salesOrderId <= 0)
            throw new ArgumentException("SalesOrderId is required.", nameof(salesOrderId));
        DoNumber = doNumber.Trim();
        SalesOrderId = salesOrderId;
        SetHeader(deliveryDate, notes);
        Status = DeliveryOrderStatus.Draft;
    }

    public void UpdateHeader(DateTime deliveryDate, string? notes)
    {
        EnsureDraft();
        SetHeader(deliveryDate, notes);
    }

    private void SetHeader(DateTime deliveryDate, string? notes)
    {
        DeliveryDate = deliveryDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<DeliveryOrderLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
    }

    public void Post()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot post a delivery order without lines.");
        Status = DeliveryOrderStatus.Posted;
    }

    private void EnsureDraft()
    {
        if (Status != DeliveryOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft delivery order can be modified.");
    }
}
