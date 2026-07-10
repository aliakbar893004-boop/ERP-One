using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.SupplierPayments;

public interface ISupplierPaymentService
{
    Task<PagedResult<SupplierPaymentListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, SupplierPaymentStatus? status = null, CancellationToken ct = default);
    Task<SupplierPaymentDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierPaymentDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PayableInvoiceDto>> GetPayableInvoicesAsync(int supplierId, CancellationToken ct = default);
    Task<SupplierPaymentDto> CreateDraftAsync(CreateSupplierPaymentRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateSupplierPaymentRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task VoidAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
}
