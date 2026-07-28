namespace ErpOne.Application.Pricing;

public enum PriceSource { VariantPrice, VariantDiscountPrice, PriceList }

/// <summary>OnDate belum dipakai di 6b-1; ada sejak awal agar promo terjadwal (6b-2)
/// tidak memaksa perubahan signature di seluruh pemanggil.</summary>
public sealed record PriceRequest(
    int ProductVariantId,
    int Quantity,
    int? CustomerId,
    int? WarehouseId,
    DateOnly OnDate);

public sealed record PriceResult(
    decimal UnitPrice,
    decimal ListPrice,
    PriceSource Source,
    int? PriceListId,
    string? PriceListName,
    int? MatchedMinQty);

public interface IPricingService
{
    Task<PriceResult> ResolveAsync(PriceRequest req, CancellationToken ct = default);

    /// <summary>Batch — dipakai POS search &amp; prefill SO agar tidak N+1.</summary>
    Task<IReadOnlyList<PriceResult>> ResolveManyAsync(
        IReadOnlyList<PriceRequest> reqs, CancellationToken ct = default);

    /// <summary>Batas diskon efektif untuk kumpulan role. Kosong/null → default global.</summary>
    Task<decimal> GetMaxDiscountPercentAsync(
        IEnumerable<string>? roleNames, CancellationToken ct = default);
}
