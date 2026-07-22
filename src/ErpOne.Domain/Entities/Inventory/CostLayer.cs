using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Satu lapisan biaya FIFO per (varian, gudang). Dibuat tiap mutasi masuk;
/// dikonsumsi tertua-dulu (urut Id) saat mutasi keluar. Layer habis disimpan untuk audit.</summary>
public class CostLayer : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public decimal UnitCost { get; private set; }
    public int OriginalQty { get; private set; }
    public int RemainingQty { get; private set; }

    private CostLayer() { } // EF Core

    public CostLayer(int productVariantId, int warehouseId, decimal unitCost, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (unitCost < 0) throw new ArgumentException("UnitCost must be >= 0.", nameof(unitCost));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        UnitCost = unitCost;
        OriginalQty = quantity;
        RemainingQty = quantity;
    }

    /// <summary>Ambil min(qty, RemainingQty) dari layer; kurangi sisa; kembalikan jumlah yang diambil.</summary>
    public int Consume(int qty)
    {
        if (qty <= 0) throw new ArgumentException("Consume quantity must be > 0.", nameof(qty));
        var take = Math.Min(qty, RemainingQty);
        RemainingQty -= take;
        return take;
    }
}
