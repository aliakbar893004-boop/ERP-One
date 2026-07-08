using ErpOne.Application.Common;

namespace ErpOne.Application.Attributes;

public interface IAttributeService
{
    Task<IReadOnlyList<AttributeDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<AttributeDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<AttributeDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AttributeDto> CreateAsync(CreateAttributeRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateAttributeRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
