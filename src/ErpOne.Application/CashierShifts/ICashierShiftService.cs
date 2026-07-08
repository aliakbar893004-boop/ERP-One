using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.CashierShifts;

public interface ICashierShiftService
{
    Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken ct = default);
    Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken ct = default);
    Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest request, CancellationToken ct = default);
    Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest request, CancellationToken ct = default);
    Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken ct = default);
}
