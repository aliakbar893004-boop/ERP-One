namespace ErpOne.Domain.Entities;

/// <summary>Baris invoice customer, diturunkan dari SO line (qty pesan × pricing SO line).</summary>
public class CustomerInvoiceLine
{
    public int Id { get; private set; }
    public int CustomerInvoiceId { get; private set; }
    public int SalesOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private CustomerInvoiceLine() { } // EF Core

    public CustomerInvoiceLine(int salesOrderId, int salesOrderLineId, int productVariantId,
        int quantity, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (salesOrderId <= 0) throw new ArgumentException("SalesOrderId is required.", nameof(salesOrderId));
        if (salesOrderLineId <= 0) throw new ArgumentException("SalesOrderLineId is required.", nameof(salesOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        SalesOrderId = salesOrderId;
        SalesOrderLineId = salesOrderLineId;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxRateSnapshot = taxRateSnapshot;
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
