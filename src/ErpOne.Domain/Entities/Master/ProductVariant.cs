using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Varian produk: unit jual nyata dengan SKU, harga, dan stok sendiri.</summary>
public class ProductVariant : AuditableEntity
{
    private readonly List<ProductVariantAttribute> _attributes = new();

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string Sku { get; private set; } = default!;     // dikunci setelah dibuat
    public string? Barcode { get; private set; }
    public decimal Price { get; private set; }
    public decimal? DiscountPrice { get; private set; }
    public decimal? DiscountPercent { get; private set; }   // penanda diskon berbasis persen (tampilan)
    public decimal CostPrice { get; private set; }          // HPP (Moving Average di F2)
    public decimal? Weight { get; private set; }
    public string? Dimensions { get; private set; }
    public bool IsActive { get; private set; }
    public int ReorderLevel { get; private set; }
    public int ReorderQty { get; private set; }

    public IReadOnlyList<ProductVariantAttribute> Attributes => _attributes;

    private ProductVariant() { } // EF Core

    public ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        SetSku(sku);
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetDiscountPercent(discountPercent);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
        SetReorder(reorderLevel, reorderQty);
    }

    /// <summary>Perbarui; SKU sengaja tidak diubah (dikunci). Stok TIDAK diubah di sini (lewat StockMovement).</summary>
    public void Update(string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetDiscountPercent(discountPercent);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
        SetReorder(reorderLevel, reorderQty);
    }

    public void SetAttributeValues(IEnumerable<int> attributeValueIds)
    {
        _attributes.Clear();
        foreach (var id in attributeValueIds.Distinct())
            _attributes.Add(new ProductVariantAttribute(id));
    }

    /// <summary>Nonaktifkan varian (mis. dihapus dari form tapi masih punya stok/riwayat — disimpan, bukan dihapus).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Hitung ulang HPP (Moving Average) saat ada mutasi masuk. CostPrice di-bulatkan 2 desimal.</summary>
    public void ApplyMovingAverage(int totalQtyBefore, int inQty, decimal inUnitCost)
    {
        if (inQty <= 0) return;
        if (inUnitCost < 0) throw new ArgumentException("Unit cost must be >= 0.", nameof(inUnitCost));
        if (totalQtyBefore < 0) totalQtyBefore = 0;
        var totalAfter = totalQtyBefore + inQty;
        var newCost = (totalQtyBefore * CostPrice + inQty * inUnitCost) / totalAfter;
        CostPrice = Math.Round(newCost, 2, MidpointRounding.AwayFromZero);
    }

    private void SetSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        Sku = sku.Trim();
    }
    private void SetPrice(decimal price)
    {
        if (price < 0) throw new ArgumentException("Price must be >= 0.", nameof(price));
        Price = price;
    }
    private void SetDiscountPrice(decimal? discountPrice, decimal price)
    {
        if (discountPrice is < 0) throw new ArgumentException("Discount price must be >= 0.", nameof(discountPrice));
        if (discountPrice.HasValue && discountPrice.Value > price)
            throw new ArgumentException("Discount price must not exceed the selling price.", nameof(discountPrice));
        DiscountPrice = discountPrice;
    }
    private void SetDiscountPercent(decimal? discountPercent)
    {
        if (discountPercent is { } p && (p < 0 || p > 100))
            throw new ArgumentException("Discount percent must be 0..100.", nameof(discountPercent));
        DiscountPercent = discountPercent;
    }
    private void SetCostPrice(decimal costPrice)
    {
        if (costPrice < 0) throw new ArgumentException("Cost price must be >= 0.", nameof(costPrice));
        CostPrice = costPrice;
    }
    private void SetWeight(decimal? weight)
    {
        if (weight is < 0) throw new ArgumentException("Weight must be >= 0.", nameof(weight));
        Weight = weight;
    }
    private void SetReorder(int reorderLevel, int reorderQty)
    {
        if (reorderLevel < 0) throw new ArgumentException("ReorderLevel must be >= 0.", nameof(reorderLevel));
        if (reorderQty < 0) throw new ArgumentException("ReorderQty must be >= 0.", nameof(reorderQty));
        ReorderLevel = reorderLevel;
        ReorderQty = reorderQty;
    }
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
