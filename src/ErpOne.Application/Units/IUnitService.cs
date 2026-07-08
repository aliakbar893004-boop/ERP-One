using ErpOne.Application.Common;

namespace ErpOne.Application.Units;

public interface IUnitService
{
    Task<IReadOnlyList<UnitDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<UnitDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<UnitDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateUnitRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
