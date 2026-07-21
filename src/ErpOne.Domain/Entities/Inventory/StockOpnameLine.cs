namespace ErpOne.Domain.Entities;

public class StockOpnameLine
{
    public int Id { get; private set; }
    public int StockOpnameId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int SystemQty { get; private set; }    // snapshot on-hand at draft creation (variance report)
    public int PhysicalQty { get; private set; }  // physical count result

    private StockOpnameLine() { } // EF Core

    public StockOpnameLine(int productVariantId, int systemQty, int physicalQty)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (systemQty < 0) throw new ArgumentException("SystemQty must be >= 0.", nameof(systemQty));
        if (physicalQty < 0) throw new ArgumentException("PhysicalQty must be >= 0.", nameof(physicalQty));
        ProductVariantId = productVariantId;
        SystemQty = systemQty;
        PhysicalQty = physicalQty;
    }

    public void SetPhysicalQty(int qty)
    {
        if (qty < 0) throw new ArgumentException("PhysicalQty must be >= 0.", nameof(qty));
        PhysicalQty = qty;
    }
}
