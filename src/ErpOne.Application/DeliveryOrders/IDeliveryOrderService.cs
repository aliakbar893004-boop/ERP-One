using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.DeliveryOrders;

public interface IDeliveryOrderService
{
    Task<PagedResult<DeliveryOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, DeliveryOrderStatus? status = null, CancellationToken ct = default);
    Task<DeliveryOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<DeliveryOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DeliverableSoDto>> GetDeliverableSosAsync(CancellationToken ct = default);
    Task<SoForDeliveryDto?> GetSoForDeliveryAsync(int salesOrderId, CancellationToken ct = default);

    Task<DeliveryOrderDto> CreateDraftAsync(CreateDeliveryOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateDeliveryOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task<bool> PostAsync(int id, CancellationToken ct = default);
}
