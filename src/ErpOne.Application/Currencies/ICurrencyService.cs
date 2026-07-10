using ErpOne.Application.Common;

namespace ErpOne.Application.Currencies;

public interface ICurrencyService
{
    Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CurrencyDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<CurrencyDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CurrencyDto> CreateAsync(CreateCurrencyRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateCurrencyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
