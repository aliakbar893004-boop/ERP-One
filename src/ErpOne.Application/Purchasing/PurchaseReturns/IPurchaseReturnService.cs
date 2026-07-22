using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public interface IPurchaseReturnService
{
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableGrnsAsync(string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default);
    Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default);

    Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<PurchaseReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        PurchaseReturnStatus? status = null, CancellationToken ct = default);

    Task<PurchaseReturnDto> CreateAsync(CreatePurchaseReturnRequest request, CancellationToken ct = default);
    Task<PurchaseReturnDto> UpdateAsync(int id, UpdatePurchaseReturnRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
