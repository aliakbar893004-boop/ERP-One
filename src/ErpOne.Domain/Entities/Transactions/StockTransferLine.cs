namespace ErpOne.Domain.Entities;

public class StockTransferLine
{
    public int Id { get; private set; }
    public int StockTransferId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }

    private StockTransferLine() { } // EF Core

    public StockTransferLine(int productVariantId, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        ProductVariantId = productVariantId;
        Quantity = quantity;
    }
}
