namespace ErpOne.Domain.Entities;

/// <summary>Baris retur penjualan; jangkar fisik = DeliveryOrderLineId (stok, sisa qty, COGS).</summary>
public class SalesReturnLine
{
    public int Id { get; private set; }
    public int SalesReturnId { get; private set; }
    public int DeliveryOrderLineId { get; private set; }
    public int? CustomerInvoiceLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }          // COGS snapshot dari DO line (Dr Inventory / Cr COGS)
    public decimal UnitPrice { get; private set; }         // jalur Invoice; jalur DO: = UnitCost
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private SalesReturnLine() { } // EF Core

    public SalesReturnLine(int deliveryOrderLineId, int? customerInvoiceLineId, int productVariantId,
        int warehouseId, string variantSku, string productName, int quantity, decimal unitCost,
        decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (deliveryOrderLineId <= 0) throw new ArgumentException("DeliveryOrderLineId is required.", nameof(deliveryOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        DeliveryOrderLineId = deliveryOrderLineId;
        CustomerInvoiceLineId = customerInvoiceLineId;
        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        VariantSku = variantSku;
        ProductName = productName;
        Quantity = quantity;
        UnitCost = unitCost;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxRateSnapshot = taxRateSnapshot;
        Recompute();
    }

    /// <summary>Perbarui basis biaya (COGS snapshot). Tidak mengubah total tagih (UnitPrice).</summary>
    public void SetUnitCost(decimal unitCost)
    {
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        UnitCost = unitCost;
    }

    private void Recompute()
    {
        LineSubtotal = Round(Quantity * UnitPrice);
        LineDiscount = Round(LineSubtotal * DiscountPercent / 100m);
        LineTax = Round((LineSubtotal - LineDiscount) * TaxRateSnapshot / 100m);
        LineTotal = LineSubtotal - LineDiscount + LineTax;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
