namespace ErpOne.Domain.Entities;

/// <summary>Baris item pada Sales Order. Amount dihitung di domain (pajak exclusive).</summary>
public class SalesOrderLine
{
    public int Id { get; private set; }
    public int SalesOrderId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public int? TaxId { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private SalesOrderLine() { } // EF Core

    public SalesOrderLine(int productVariantId, int quantity, decimal unitPrice,
        decimal discountPercent, int? taxId, decimal taxRateSnapshot)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxId = taxId;
        TaxRateSnapshot = taxId is null ? 0m : taxRateSnapshot;
        Recompute();
    }

    private void Recompute()
    {
        LineSubtotal = Round(Quantity * UnitPrice);
        LineDiscount = Round(LineSubtotal * DiscountPercent / 100m);
        LineTax = Round((LineSubtotal - LineDiscount) * TaxRateSnapshot / 100m);
        LineTotal = LineSubtotal - LineDiscount + LineTax;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public int DeliveredQuantity { get; private set; }

    public bool IsFullyDelivered => DeliveredQuantity >= Quantity;

    /// <summary>Catat pengiriman; tolak bila melebihi qty dipesan (STRICT, tanpa toleransi).</summary>
    public void ApplyDelivery(int qty)
    {
        if (qty <= 0) throw new ArgumentException("Delivery quantity must be > 0.", nameof(qty));
        if (DeliveredQuantity + qty > Quantity)
            throw new InvalidOperationException(
                $"Delivering {qty} would exceed the ordered quantity ({Quantity}) for this line.");
        DeliveredQuantity += qty;
    }
}
