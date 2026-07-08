using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, PurchaseOrderStatus? status = null, CancellationToken ct = default);
    Task<PurchaseOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PurchaseOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default);

    Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
    Task<bool> CloseAsync(int id, CancellationToken ct = default);
}
