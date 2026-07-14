using ErpOne.Application.Common;

namespace ErpOne.Application.Suppliers;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<SupplierDto>> GetPagedAsync(int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default);
    Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
