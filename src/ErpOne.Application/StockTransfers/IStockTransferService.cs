using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.StockTransfers;

public interface IStockTransferService
{
    Task<PagedResult<StockTransferListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, StockTransferStatus? status = null, CancellationToken ct = default);
    Task<StockTransferDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<StockTransferDto> CreateAsync(CreateStockTransferRequest request, CancellationToken ct = default);
    Task<StockTransferDto> UpdateAsync(int id, CreateStockTransferRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
