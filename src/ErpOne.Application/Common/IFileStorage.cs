namespace ErpOne.Application.Common;

/// <summary>Hasil penyimpanan file: path relatif terhadap web root + ukuran byte.</summary>
public record StoredFile(string RelativePath, long Size);

/// <summary>Penyimpanan file biner (gambar produk) di disk lokal komputer.</summary>
public interface IFileStorage
{
    /// <summary>Simpan konten ke <paramref name="subFolder"/>; kembalikan path relatif untuk membentuk URL.</summary>
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, string subFolder, CancellationToken ct = default);

    /// <summary>Hapus file berdasarkan path relatif (diabaikan jika tidak ada).</summary>
    void Delete(string relativePath);
}
