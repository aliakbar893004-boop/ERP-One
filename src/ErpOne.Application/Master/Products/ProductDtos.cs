using ErpOne.Domain.Entities;

namespace ErpOne.Application.Products;

public record ProductImageDto(int Id, string Url, string OriginalFileName, long FileSize, int SortOrder, bool IsPrimary);

public record AttributeValueRefDto(int AttributeValueId, string AttributeName, string ValueCode, string Value);

public record ProductVariantDto(
    int Id, string Sku, string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int Stock, bool IsActive, decimal? DiscountPercent,
    int ReorderLevel, int ReorderQty,
    IReadOnlyList<AttributeValueRefDto> Attributes);

public record ProductDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int? CategoryId,
    string? CategoryName,
    int? BrandId,
    string? BrandName,
    int? BaseUnitId,
    string? BaseUnitName,
    int? TaxId,
    string? TaxName,
    ProductStatus Status,
    string? PrimaryImageUrl,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductVariantDto> Variants,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalStock,
    int VariantCount,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    string? CreatedBy);

// SKU/Code di-generate otomatis; tidak di input.
public record VariantInput(
    string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int OpeningStock, bool IsActive,
    IReadOnlyList<int> AttributeValueIds, decimal? DiscountPercent = null,
    int ReorderLevel = 0, int ReorderQty = 0);

public record CreateProductRequest(
    string Name, string? Description, int CategoryId,
    int? BrandId, int? BaseUnitId, int? TaxId, ProductStatus Status,
    IReadOnlyList<VariantInput> Variants);

public record UpdateProductRequest(
    string Name, string? Description, int CategoryId,
    int? BrandId, int? BaseUnitId, int? TaxId, ProductStatus Status,
    IReadOnlyList<VariantInput> Variants);

/// <summary>Hasil update produk. Found=false bila produk tak ditemukan.
/// DeactivatedVariantSkus = varian bervstok yang dihapus dari form lalu dinonaktifkan (bukan dihapus).</summary>
public record ProductUpdateResult(bool Found, IReadOnlyList<string> DeactivatedVariantSkus);

/// <summary>Satu gambar yang akan diunggah; bytes dibaca dari unggahan di layer Web.</summary>
public record ProductImageUpload(string OriginalFileName, string ContentType, byte[] Content);
