using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.StockOpnames;

public interface IStockOpnameService
{
    Task<PagedResult<StockOpnameListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, StockOpnameStatus? status = null, CancellationToken ct = default);
    Task<StockOpnameDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<StockOpnameDto> CreateAsync(CreateStockOpnameRequest request, CancellationToken ct = default);
    Task<StockOpnameDto> UpdateAsync(int id, UpdateStockOpnameRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
