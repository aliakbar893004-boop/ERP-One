using ErpOne.Application.Common;

namespace ErpOne.Application.PosSales;

public interface IPosSaleService
{
    Task<IReadOnlyList<PosProductOptionDto>> SearchProductsAsync(int warehouseId, string? term, CancellationToken ct = default);
    Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken ct = default);
    Task<PosSaleDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<PosSaleListItemDto>> GetPagedAsync(int page, int pageSize, string? search, int? shiftId, int? paymentMethodId = null, string? cashierUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<PosCashierDto>> GetCashiersAsync(CancellationToken ct = default);
}
