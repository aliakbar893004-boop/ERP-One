using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Penerimaan barang atas satu PO. Baris hanya bisa diubah saat Draft; Post mengunci.</summary>
public class GoodsReceipt : AuditableEntity
{
    private readonly List<GoodsReceiptLine> _lines = [];

    public int Id { get; private set; }
    public string GrnNumber { get; private set; } = default!;
    public int PurchaseOrderId { get; private set; }
    public DateTime ReceiptDate { get; private set; }
    public string? Notes { get; private set; }
    public GoodsReceiptStatus Status { get; private set; }

    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines;

    private GoodsReceipt() { } // EF Core

    public GoodsReceipt(string grnNumber, int purchaseOrderId, DateTime receiptDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(grnNumber))
            throw new ArgumentException("GrnNumber is required.", nameof(grnNumber));
        if (purchaseOrderId <= 0)
            throw new ArgumentException("PurchaseOrderId is required.", nameof(purchaseOrderId));
        GrnNumber = grnNumber.Trim();
        PurchaseOrderId = purchaseOrderId;
        SetHeader(receiptDate, notes);
        Status = GoodsReceiptStatus.Draft;
    }

    public void UpdateHeader(DateTime receiptDate, string? notes)
    {
        EnsureDraft();
        SetHeader(receiptDate, notes);
    }

    private void SetHeader(DateTime receiptDate, string? notes)
    {
        ReceiptDate = receiptDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<GoodsReceiptLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
    }

    public void Post()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot post a goods receipt without lines.");
        Status = GoodsReceiptStatus.Posted;
    }

    private void EnsureDraft()
    {
        if (Status != GoodsReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft goods receipt can be modified.");
    }
}
