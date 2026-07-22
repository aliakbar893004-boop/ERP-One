using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Sales.SalesReturns;

public interface ISalesReturnService
{
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableDeliveryOrdersAsync(string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default);
    Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default);

    Task<SalesReturnDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<SalesReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        SalesReturnStatus? status = null, CancellationToken ct = default);

    Task<SalesReturnDto> CreateAsync(CreateSalesReturnRequest request, CancellationToken ct = default);
    Task<SalesReturnDto> UpdateAsync(int id, UpdateSalesReturnRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
