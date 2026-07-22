namespace ErpOne.Domain.Entities;

/// <summary>Baris retur pembelian; jangkar fisik = GoodsReceiptLineId (stok, sisa qty, HPP).</summary>
public class PurchaseReturnLine
{
    public int Id { get; private set; }
    public int PurchaseReturnId { get; private set; }
    public int GoodsReceiptLineId { get; private set; }
    public int? SupplierInvoiceLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }          // basis Cr Inventory; di-refresh dari seam saat post
    public decimal UnitPrice { get; private set; }         // jalur Invoice; jalur GRN: = UnitCost
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private PurchaseReturnLine() { } // EF Core

    public PurchaseReturnLine(int goodsReceiptLineId, int? supplierInvoiceLineId, int productVariantId,
        int warehouseId, string variantSku, string productName, int quantity, decimal unitCost,
        decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (goodsReceiptLineId <= 0) throw new ArgumentException("GoodsReceiptLineId is required.", nameof(goodsReceiptLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        GoodsReceiptLineId = goodsReceiptLineId;
        SupplierInvoiceLineId = supplierInvoiceLineId;
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

    /// <summary>Perbarui basis biaya (dari seam costing saat post). Tidak mengubah total tagih (UnitPrice).</summary>
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
