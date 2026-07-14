using ErpOne.Application.Common;

namespace ErpOne.Application.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<CustomerDto>> GetPagedAsync(int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default);
    Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
