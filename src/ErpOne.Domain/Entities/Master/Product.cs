using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

public class Product : AuditableEntity
{
    /// <summary>Batas jumlah gambar per produk.</summary>
    public const int MaxImages = 5;

    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariant> _variants = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;        // base SKU, dikunci
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int? CategoryId { get; private set; }
    public ProductCategory? Category { get; private set; }
    public int? BrandId { get; private set; }
    public int? BaseUnitId { get; private set; }
    public int? TaxId { get; private set; }
    public ProductStatus Status { get; private set; }

    public IReadOnlyList<ProductImage> Images => _images;
    public IReadOnlyList<ProductVariant> Variants => _variants;

    public ProductImage? PrimaryImage =>
        _images.FirstOrDefault(i => i.IsPrimary) ?? _images.OrderBy(i => i.SortOrder).FirstOrDefault();

    private Product() { } // EF Core

    public Product(string code, string name, string? description, int? categoryId,
        int? brandId, int? baseUnitId, int? taxId, ProductStatus status)
    {
        SetCode(code);
        SetName(name);
        SetDescription(description);
        CategoryId = categoryId;
        BrandId = brandId;
        BaseUnitId = baseUnitId;
        TaxId = taxId;
        Status = status;
    }

    /// <summary>Perbarui data induk; Code sengaja tidak diubah (dikunci sejak pembuatan).</summary>
    public void Update(string name, string? description, int? categoryId,
        int? brandId, int? baseUnitId, int? taxId, ProductStatus status)
    {
        SetName(name);
        SetDescription(description);
        CategoryId = categoryId;
        BrandId = brandId;
        BaseUnitId = baseUnitId;
        TaxId = taxId;
        Status = status;
    }

    public ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        var v = new ProductVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive, discountPercent, reorderLevel, reorderQty);
        _variants.Add(v);
        return v;
    }

    public void RemoveVariant(int variantId)
    {
        var v = _variants.FirstOrDefault(x => x.Id == variantId);
        if (v is not null) _variants.Remove(v);
    }

    // ── Gambar (tidak berubah dari sebelumnya) ──────────────────────────────
    public int RemainingImageSlots => Math.Max(0, MaxImages - _images.Count);
    public bool CanAddImages(int count) => count >= 0 && _images.Count + count <= MaxImages;

    public ProductImage AddImage(string storedPath, string originalFileName, string contentType, long fileSize)
    {
        if (_images.Count >= MaxImages)
            throw new InvalidOperationException($"Maksimal {MaxImages} gambar per produk.");
        var order = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        var image = new ProductImage(storedPath, originalFileName, contentType, fileSize, order);
        _images.Add(image);
        if (_images.Count == 1) image.SetPrimary(true);
        return image;
    }

    public ProductImage? RemoveImage(int imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return null;
        _images.Remove(image);
        if (image.IsPrimary)
            _images.OrderBy(i => i.SortOrder).FirstOrDefault()?.SetPrimary(true);
        return image;
    }

    public bool SetPrimaryImage(int imageId)
    {
        if (_images.All(i => i.Id != imageId)) return false;
        foreach (var i in _images) i.SetPrimary(i.Id == imageId);
        return true;
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
    private void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
