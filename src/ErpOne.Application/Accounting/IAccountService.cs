namespace ErpOne.Application.Accounting;

public interface IAccountService
{
    Task<IReadOnlyList<AccountTreeNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> GetPostableAsync(CancellationToken ct = default);
    Task<AccountDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AccountDto> CreateAsync(CreateAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAsync(int id, UpdateAccountRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetActiveAsync(int id, bool active, CancellationToken ct = default);
}
