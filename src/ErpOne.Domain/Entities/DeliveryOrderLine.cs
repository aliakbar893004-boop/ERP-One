namespace ErpOne.Domain.Entities;

/// <summary>Baris pengiriman: qty terkirim untuk satu baris SO. UnitCost = COGS di-snapshot saat Post.</summary>
public class DeliveryOrderLine
{
    public int Id { get; private set; }
    public int DeliveryOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int QuantityDelivered { get; private set; }
    public decimal UnitCost { get; private set; }

    private DeliveryOrderLine() { } // EF Core

    public DeliveryOrderLine(int salesOrderLineId, int productVariantId, int quantityDelivered)
    {
        if (salesOrderLineId <= 0) throw new ArgumentException("SalesOrderLineId is required.", nameof(salesOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantityDelivered <= 0) throw new ArgumentException("QuantityDelivered must be > 0.", nameof(quantityDelivered));

        SalesOrderLineId = salesOrderLineId;
        ProductVariantId = productVariantId;
        QuantityDelivered = quantityDelivered;
        UnitCost = 0m; // COGS ditetapkan saat Post
    }

    /// <summary>Snapshot COGS per unit (dari ProductVariant.CostPrice) saat Post.</summary>
    public void SetUnitCost(decimal cost)
    {
        if (cost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(cost));
        UnitCost = cost;
    }
}
