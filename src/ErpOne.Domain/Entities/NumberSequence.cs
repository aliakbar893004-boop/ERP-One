using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Konfigurasi format penomoran per jenis dokumen (mis. PO-202607-0001).</summary>
public class NumberSequence : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;      // key jenis dokumen (lihat DocumentTypes)
    public string Prefix { get; private set; } = default!;    // mis. "PO"
    public string DateFormat { get; private set; } = "";      // "yyyyMM" | "yyyyMMdd" | "" (kosong = tanpa tanggal)
    public int Padding { get; private set; }                  // mis. 4
    public ResetPeriod ResetPeriod { get; private set; }
    public string Separator { get; private set; } = "-";

    private NumberSequence() { } // EF Core

    public NumberSequence(string code, string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
        => Set(code, prefix, dateFormat, padding, resetPeriod, separator);

    public void Update(string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
        => Set(Code, prefix, dateFormat, padding, resetPeriod, separator);

    private void Set(string code, string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Prefix is required.", nameof(prefix));
        if (padding is < 1 or > 10) throw new ArgumentException("Padding must be 1-10.", nameof(padding));

        Code = code.Trim();
        Prefix = prefix.Trim().ToUpperInvariant();
        DateFormat = (dateFormat ?? "").Trim();
        Padding = padding;
        ResetPeriod = resetPeriod;
        Separator = separator ?? "-";
    }
}
