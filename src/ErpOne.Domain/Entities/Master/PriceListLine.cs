namespace ErpOne.Domain.Entities;

/// <summary>Satu tier harga: berlaku bila qty >= MinQty. Tier = beberapa baris dengan MinQty berbeda.</summary>
public class PriceListLine
{
    public int Id { get; private set; }
    public int PriceListId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int MinQty { get; private set; }
    public decimal UnitPrice { get; private set; }

    private PriceListLine() { } // EF Core

    public PriceListLine(int productVariantId, int minQty, decimal unitPrice)
    {
        if (productVariantId <= 0)
            throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (minQty < 1)
            throw new ArgumentException("MinQty must be >= 1.", nameof(minQty));
        if (unitPrice < 0)
            throw new ArgumentException("UnitPrice must be >= 0.", nameof(unitPrice));

        ProductVariantId = productVariantId;
        MinQty = minQty;
        UnitPrice = unitPrice;
    }
}
