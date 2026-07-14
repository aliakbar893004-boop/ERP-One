namespace ErpOne.Application.Products;

/// <summary>Satu baris mentah hasil baca file impor (semua kolom string, dipetakan dari header CSV).</summary>
public record ProductImportRow(
    int RowNumber,
    string? CategoryCode,
    string? Name,
    string? Description,
    string? Price,
    string? DiscountPrice,
    string? Stock,
    string? Weight,
    string? Dimensions,
    string? Status);

public record ProductImportError(int RowNumber, string Message);

public record ProductImportResult(int SuccessCount, int FailedCount, IReadOnlyList<ProductImportError> Errors)
{
    public int Total => SuccessCount + FailedCount;
}
