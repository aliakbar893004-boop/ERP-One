namespace ErpOne.Domain.Entities;

/// <summary>Baris penerimaan: qty diterima + HPP per unit untuk satu baris PO.</summary>
public class GoodsReceiptLine
{
    public int Id { get; private set; }
    public int GoodsReceiptId { get; private set; }
    public int PurchaseOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int QuantityReceived { get; private set; }
    public decimal UnitCost { get; private set; }

    private GoodsReceiptLine() { } // EF Core

    public GoodsReceiptLine(int purchaseOrderLineId, int productVariantId, int quantityReceived, decimal unitCost)
    {
        if (purchaseOrderLineId <= 0) throw new ArgumentException("PurchaseOrderLineId is required.", nameof(purchaseOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantityReceived <= 0) throw new ArgumentException("QuantityReceived must be > 0.", nameof(quantityReceived));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        PurchaseOrderLineId = purchaseOrderLineId;
        ProductVariantId = productVariantId;
        QuantityReceived = quantityReceived;
        UnitCost = unitCost;
    }
}
