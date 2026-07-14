using ErpOne.Application.Common;

namespace ErpOne.Application.Brands;

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<BrandDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<BrandDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateBrandRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
