using ErpOne.Application.Common;

namespace ErpOne.Application.Taxes;

public interface ITaxService
{
    Task<IReadOnlyList<TaxDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<TaxDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<TaxDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TaxDto> CreateAsync(CreateTaxRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateTaxRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
