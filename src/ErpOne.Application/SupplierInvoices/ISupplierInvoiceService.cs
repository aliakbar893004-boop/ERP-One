using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.SupplierInvoices;

public interface ISupplierInvoiceService
{
    Task<PagedResult<SupplierInvoiceListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, SupplierInvoiceStatus? status = null, CancellationToken ct = default);
    Task<SupplierInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UninvoicedGrnDto>> GetUninvoicedGrnsAsync(int supplierId, CancellationToken ct = default);
    Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<bool> UpdateHeaderAsync(int id, UpdateSupplierInvoiceHeaderRequest request, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
}
