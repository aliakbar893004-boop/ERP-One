using ErpOne.Application.Common;

namespace ErpOne.Application.ProductCategories;

public interface IProductCategoryService
{
    Task<IReadOnlyList<ProductCategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<ProductCategoryDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<ProductCategoryDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ProductCategoryDto> CreateAsync(CreateProductCategoryRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateProductCategoryRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
