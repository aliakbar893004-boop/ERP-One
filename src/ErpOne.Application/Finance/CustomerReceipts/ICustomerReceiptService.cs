using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.CustomerReceipts;

public interface ICustomerReceiptService
{
    Task<PagedResult<CustomerReceiptListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, CustomerReceiptStatus? status = null, CancellationToken ct = default);
    Task<CustomerReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerReceiptDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OpenInvoiceDto>> GetOpenInvoicesAsync(int customerId, CancellationToken ct = default);
    Task<CustomerReceiptDto> CreateAsync(CreateCustomerReceiptRequest request, CancellationToken ct = default);
    Task VoidAsync(int id, string authorizedBy, CancellationToken ct = default);
}
