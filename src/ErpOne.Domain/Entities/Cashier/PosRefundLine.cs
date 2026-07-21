namespace ErpOne.Domain.Entities;

/// <summary>Baris refund POS — merujuk PosSaleLine asli; snapshot harga/COGS dari sale.</summary>
public class PosRefundLine
{
    public int Id { get; private set; }
    public int PosRefundId { get; private set; }
    public int PosSaleLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal { get; private set; }

    private PosRefundLine() { } // EF Core

    public PosRefundLine(int posSaleLineId, int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost)
    {
        if (posSaleLineId <= 0) throw new ArgumentException("PosSaleLineId must be > 0.", nameof(posSaleLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        PosSaleLineId = posSaleLineId;
        ProductVariantId = productVariantId;
        VariantSku = variantSku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        UnitCost = unitCost;
        LineTotal = Round(Round(quantity * unitPrice) * (100m - discountPercent) / 100m);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
