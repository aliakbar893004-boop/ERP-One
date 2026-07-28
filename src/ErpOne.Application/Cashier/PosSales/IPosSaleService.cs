using ErpOne.Application.Common;

namespace ErpOne.Application.PosSales;

public interface IPosSaleService
{
    Task<IReadOnlyList<PosProductOptionDto>> SearchProductsAsync(int warehouseId, string? term, CancellationToken ct = default);
    /// <summary>roleNames adalah parameter method, BUKAN bagian request: DTO datang dari client dan
    /// bisa dipalsukan. null/kosong → batas diskon jatuh ke default global. Diletakkan setelah
    /// request agar pemanggil lama tetap ter-kompilasi.</summary>
    Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request,
        IReadOnlyList<string>? roleNames = null, CancellationToken ct = default);
    Task<PosSaleDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<PosSaleListItemDto>> GetPagedAsync(int page, int pageSize, string? search, int? shiftId, int? paymentMethodId = null, string? cashierUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<PosCashierDto>> GetCashiersAsync(CancellationToken ct = default);
}
