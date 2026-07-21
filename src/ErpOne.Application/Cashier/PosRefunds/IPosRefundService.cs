using ErpOne.Application.Common;

namespace ErpOne.Application.PosRefunds;

public interface IPosRefundService
{
    Task<RefundableSaleDto?> GetRefundableAsync(int posSaleId, CancellationToken ct = default);
    Task<PosRefundDto> RefundAsync(int posSaleId, CreatePosRefundRequest request,
        string cashierUserId, string cashierName, string authorizedBy, CancellationToken ct = default);
    Task<IReadOnlyList<PosRefundDto>> GetBySaleAsync(int posSaleId, CancellationToken ct = default);
    Task<PagedResult<PosRefundListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, int? shiftId = null, CancellationToken ct = default);
}
