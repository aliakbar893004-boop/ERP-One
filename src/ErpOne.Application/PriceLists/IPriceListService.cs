using ErpOne.Application.Common;

namespace ErpOne.Application.PriceLists;

public interface IPriceListService
{
    Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PriceListDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<PriceListDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<PriceListDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Lookup varian untuk editor baris. Pola sama seperti PO/SO agar halaman master
    /// tidak perlu bergantung pada service transaksi.</summary>
    Task<IReadOnlyList<PriceListVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default);
    Task<PriceListDetailDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdatePriceListRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
