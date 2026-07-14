using ErpOne.Application.Common;

namespace ErpOne.Application.Warehouses;

public interface IWarehouseService
{
    Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<WarehouseDto>> GetPagedAsync(int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default);
    Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<WarehouseDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<WarehouseDto?> GetDefaultAsync(CancellationToken ct = default);
    Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateWarehouseRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
