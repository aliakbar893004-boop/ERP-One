using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.GoodsReceipts;

public interface IGoodsReceiptService
{
    Task<PagedResult<GoodsReceiptListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, GoodsReceiptStatus? status = null, CancellationToken ct = default);
    Task<GoodsReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<GoodsReceiptDashboardDto> GetDashboardAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReceivablePoDto>> GetReceivablePosAsync(CancellationToken ct = default);
    Task<PoForReceiptDto?> GetPoForReceiptAsync(int purchaseOrderId, CancellationToken ct = default);

    Task<GoodsReceiptDto> CreateDraftAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateGoodsReceiptRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task<bool> PostAsync(int id, CancellationToken ct = default);
}
