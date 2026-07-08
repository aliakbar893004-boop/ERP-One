using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.SalesOrders;

public interface ISalesOrderService
{
    Task<PagedResult<SalesOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SalesOrderStatus? status = null, CancellationToken ct = default);
    Task<SalesOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SalesOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SalesOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default);
    Task<SalesOrderCreditInfoDto> GetCreditInfoAsync(int customerId, decimal thisOrderTotal, int? excludeSoId, CancellationToken ct = default);

    Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
    Task<bool> CloseAsync(int id, CancellationToken ct = default);
}
