using ErpOne.Application.Common;

namespace ErpOne.Application.CashBank;

public interface ICashBankAccountService
{
    Task<IReadOnlyList<CashBankAccountDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CashBankAccountDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<CashBankAccountDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<CashBankAccountDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CashBankAccountDto> CreateAsync(CreateCashBankAccountRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateCashBankAccountRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
