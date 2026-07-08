namespace ErpOne.Domain.Entities;

/// <summary>Gambar produk; file fisik disimpan di disk lokal, kolom ini menyimpan metadata.</summary>
public class ProductImage
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }

    /// <summary>Path relatif terhadap web root, mis. "uploads/products/ab12.jpg" (untuk membentuk URL).</summary>
    public string StoredPath { get; private set; } = default!;

    /// <summary>Nama file asli saat diunggah.</summary>
    public string OriginalFileName { get; private set; } = default!;

    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>Penanda gambar utama (gambar_utama).</summary>
    public bool IsPrimary { get; private set; }

    private ProductImage() { } // EF Core

    public ProductImage(string storedPath, string originalFileName, string contentType, long fileSize, int sortOrder)
    {
        StoredPath = storedPath;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        FileSize = fileSize;
        SortOrder = sortOrder;
    }

    internal void SetPrimary(bool value) => IsPrimary = value;
}
