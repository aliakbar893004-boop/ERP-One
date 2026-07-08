using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Buku besar mutasi stok — append-only (tidak pernah diubah/dihapus).</summary>
public class StockMovement : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public MovementType Type { get; private set; }
    public int Quantity { get; private set; }          // bertanda: + masuk / − keluar
    public decimal UnitCost { get; private set; }       // HPP per unit pada mutasi
    public DateTime MovementDate { get; private set; }
    public string? RefType { get; private set; }
    public int? RefId { get; private set; }
    public string? Note { get; private set; }

    private StockMovement() { } // EF Core

    public StockMovement(int productVariantId, int warehouseId, MovementType type, int quantity,
        decimal unitCost, DateTime movementDate, string? refType = null, int? refId = null, string? note = null)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity == 0) throw new ArgumentException("Quantity must not be zero.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("Unit cost must be >= 0.", nameof(unitCost));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        Type = type;
        Quantity = quantity;
        UnitCost = unitCost;
        MovementDate = movementDate;
        RefType = string.IsNullOrWhiteSpace(refType) ? null : refType.Trim();
        RefId = refId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
