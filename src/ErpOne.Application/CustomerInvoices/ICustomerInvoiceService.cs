using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.CustomerInvoices;

public interface ICustomerInvoiceService
{
    Task<PagedResult<CustomerInvoiceListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, CustomerInvoiceStatus? status = null, CancellationToken ct = default);
    Task<CustomerInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UninvoicedSalesOrderDto>> GetUninvoicedSalesOrdersAsync(int customerId, CancellationToken ct = default);
    Task<CustomerCreditDto> GetCustomerCreditAsync(int customerId, CancellationToken ct = default);
    Task<CustomerInvoiceDto> CreateAsync(CreateCustomerInvoiceRequest request, CancellationToken ct = default);
    Task<bool> UpdateHeaderAsync(int id, UpdateCustomerInvoiceHeaderRequest request, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
}
