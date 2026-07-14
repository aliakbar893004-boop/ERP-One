namespace ErpOne.Application.Numbering;

/// <summary>Penomoran dokumen terpusat, race-safe, dengan format per NumberSequence.</summary>
public interface IDocumentNumberService
{
    /// <summary>Alokasikan nomor berikutnya untuk jenis dokumen <paramref name="code"/> (lihat DocumentTypes).</summary>
    Task<string> NextAsync(string code, DateTime docDate, CancellationToken ct = default);
}
