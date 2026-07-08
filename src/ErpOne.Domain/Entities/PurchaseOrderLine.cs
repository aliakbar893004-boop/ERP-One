namespace ErpOne.Domain.Entities;

/// <summary>Baris item pada Purchase Order. Amount dihitung di domain.</summary>
public class PurchaseOrderLine
{
    public int Id { get; private set; }
    public int PurchaseOrderId { get; private set; }
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

    public int ReceivedQuantity { get; private set; }

    public bool IsFullyReceived => ReceivedQuantity >= Quantity;

    /// <summary>HPP per unit default = harga netto setelah diskon (tanpa PPN), dibulatkan.</summary>
    public decimal DefaultUnitCost =>
        Math.Round(UnitPrice * (1 - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Catat penerimaan; tolak bila melebihi qty pesan × (1 + toleransi%).</summary>
    public void ApplyReceipt(int qty, int tolerancePercent)
    {
        if (qty <= 0) throw new ArgumentException("Receipt quantity must be > 0.", nameof(qty));
        if (tolerancePercent < 0) throw new ArgumentException("Tolerance percent must be >= 0.", nameof(tolerancePercent));
        var maxAllowed = (int)Math.Floor(Quantity * (1 + tolerancePercent / 100m));
        if (ReceivedQuantity + qty > maxAllowed)
            throw new InvalidOperationException(
                $"Receiving {qty} would exceed the allowed quantity ({maxAllowed}) for this line.");
        ReceivedQuantity += qty;
    }

    private PurchaseOrderLine() { } // EF Core

    public PurchaseOrderLine(int productVariantId, int quantity, decimal unitPrice,
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
}
