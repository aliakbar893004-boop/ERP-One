using ErpOne.Domain.Entities;

namespace ErpOne.Application.Costing;

public interface ICostingSettingService
{
    Task<CostingMethod> GetMethodAsync(CancellationToken ct = default);
    Task<CostingSettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default);
}
