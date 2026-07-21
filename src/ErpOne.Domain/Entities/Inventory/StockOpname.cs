using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Physical count document per warehouse. Draft → PendingApproval → Posted (variance posted when fully approved).</summary>
public class StockOpname : AuditableEntity
{
    private readonly List<StockOpnameLine> _lines = [];

    public int Id { get; private set; }
    public string OpnameNumber { get; private set; } = default!;
    public DateTime OpnameDate { get; private set; }
    public int WarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public StockOpnameStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<StockOpnameLine> Lines => _lines;

    private StockOpname() { } // EF Core

    public StockOpname(string opnameNumber, DateTime opnameDate, int warehouseId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(opnameNumber)) throw new ArgumentException("OpnameNumber is required.", nameof(opnameNumber));
        if (warehouseId <= 0) throw new ArgumentException("Warehouse is required.", nameof(warehouseId));
        OpnameNumber = opnameNumber.Trim();
        OpnameDate = opnameDate;
        WarehouseId = warehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = StockOpnameStatus.Draft;
    }

    // Warehouse intentionally NOT updatable: lines are a warehouse-specific snapshot.
    public void UpdateHeader(DateTime opnameDate, string? notes)
    {
        EnsureDraft();
        OpnameDate = opnameDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<(int VariantId, int SystemQty, int PhysicalQty)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(new StockOpnameLine(l.VariantId, l.SystemQty, l.PhysicalQty));
    }

    // Updates PhysicalQty only; SystemQty snapshot stays stable.
    public void SetPhysicalCounts(IEnumerable<(int LineId, int PhysicalQty)> counts)
    {
        EnsureDraft();
        var map = counts.ToDictionary(c => c.LineId, c => c.PhysicalQty);
        foreach (var line in _lines)
            if (map.TryGetValue(line.Id, out var qty)) line.SetPhysicalQty(qty);
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit an opname without lines.");
        Status = StockOpnameStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != StockOpnameStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending opname can be posted.");
        Status = StockOpnameStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != StockOpnameStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending opname can be returned to draft.");
        Status = StockOpnameStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != StockOpnameStatus.Draft) throw new InvalidOperationException("Only a draft opname can be modified.");
    }
}
