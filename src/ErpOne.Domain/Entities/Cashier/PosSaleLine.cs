namespace ErpOne.Domain.Entities;

/// <summary>Baris item POS. Snapshot SKU/nama agar struk lama tetap benar. COGS di-snapshot saat jual.</summary>
public class PosSaleLine
{
    public int Id { get; private set; }
    public int PosSaleId { get; private set; }
    public int ProductVariantId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTotal { get; private set; }

    private PosSaleLine() { } // EF Core

    public PosSaleLine(int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        ProductVariantId = productVariantId;
        VariantSku = variantSku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        UnitCost = unitCost;
        Recompute();
    }

    private void Recompute()
    {
        LineSubtotal = Round(Quantity * UnitPrice);
        LineDiscount = Round(LineSubtotal * DiscountPercent / 100m);
        LineTotal = LineSubtotal - LineDiscount;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
