using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Saldo stok materialized per (varian, gudang). Sumber kebenaran tetap StockMovement.</summary>
public class ProductStock : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public int Quantity { get; private set; }

    private ProductStock() { } // EF Core

    public ProductStock(int productVariantId, int warehouseId, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity < 0) throw new ArgumentException("Quantity must be >= 0.", nameof(quantity));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        Quantity = quantity;
    }

    /// <summary>Tambah/kurangi saldo; tolak hasil negatif.</summary>
    public void ApplyDelta(int delta)
    {
        var result = Quantity + delta;
        if (result < 0)
            throw new InvalidOperationException("Stock cannot go negative.");
        Quantity = result;
    }
}
