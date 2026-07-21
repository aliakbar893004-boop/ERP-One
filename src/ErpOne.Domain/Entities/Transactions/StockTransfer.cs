using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Transfer stok antar gudang. Draft → PendingApproval → Posted (stok pindah saat fully-approved).</summary>
public class StockTransfer : AuditableEntity
{
    private readonly List<StockTransferLine> _lines = [];

    public int Id { get; private set; }
    public string TransferNumber { get; private set; } = default!;
    public DateTime TransferDate { get; private set; }
    public int SourceWarehouseId { get; private set; }
    public int DestinationWarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public StockTransferStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<StockTransferLine> Lines => _lines;

    private StockTransfer() { } // EF Core

    public StockTransfer(string transferNumber, DateTime transferDate, int sourceWarehouseId,
        int destinationWarehouseId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(transferNumber)) throw new ArgumentException("TransferNumber is required.", nameof(transferNumber));
        if (sourceWarehouseId <= 0) throw new ArgumentException("Source warehouse is required.", nameof(sourceWarehouseId));
        if (destinationWarehouseId <= 0) throw new ArgumentException("Destination warehouse is required.", nameof(destinationWarehouseId));
        if (sourceWarehouseId == destinationWarehouseId) throw new ArgumentException("Source and destination must differ.");
        TransferNumber = transferNumber.Trim();
        TransferDate = transferDate;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = StockTransferStatus.Draft;
    }

    public void UpdateHeader(DateTime transferDate, int sourceWarehouseId, int destinationWarehouseId, string? notes)
    {
        EnsureDraft();
        if (sourceWarehouseId == destinationWarehouseId) throw new ArgumentException("Source and destination must differ.");
        TransferDate = transferDate;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<(int VariantId, int Quantity)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(new StockTransferLine(l.VariantId, l.Quantity));
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit a transfer without lines.");
        Status = StockTransferStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != StockTransferStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending transfer can be posted.");
        Status = StockTransferStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != StockTransferStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending transfer can be returned to draft.");
        Status = StockTransferStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != StockTransferStatus.Draft) throw new InvalidOperationException("Only a draft transfer can be modified.");
    }
}
