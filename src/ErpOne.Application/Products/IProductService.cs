using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Products;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<ProductDto>> GetPagedAsync(int page, int pageSize, string? search = null, ProductStatus? status = null, CancellationToken ct = default);
    Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductUpdateResult> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Tambah gambar (maks <see cref="Domain.Entities.Product.MaxImages"/> total). Null jika produk tak ditemukan.</summary>
    Task<ProductDto?> AddImagesAsync(int productId, IReadOnlyList<ProductImageUpload> uploads, CancellationToken ct = default);

    /// <summary>Hapus satu gambar produk beserta file fisiknya.</summary>
    Task<bool> DeleteImageAsync(int productId, int imageId, CancellationToken ct = default);

    /// <summary>Tetapkan gambar utama (gambar_utama) produk.</summary>
    Task<bool> SetPrimaryImageAsync(int productId, int imageId, CancellationToken ct = default);

    /// <summary>Impor produk secara massal; SKU tetap di-generate otomatis per kategori. Baris invalid dilewati &amp; dilaporkan.</summary>
    Task<ProductImportResult> ImportAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken ct = default);

    /// <summary>Ringkasan untuk dashboard: total produk, stok, nilai inventori, status, &amp; stok menipis.</summary>
    Task<ProductDashboardDto> GetDashboardAsync(CancellationToken ct = default);
}
