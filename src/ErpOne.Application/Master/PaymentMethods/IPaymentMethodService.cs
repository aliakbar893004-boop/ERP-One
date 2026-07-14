using ErpOne.Application.Common;

namespace ErpOne.Application.PaymentMethods;

public interface IPaymentMethodService
{
    Task<IReadOnlyList<PaymentMethodDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<PaymentMethodDto>> GetPagedAsync(int page, int pageSize, string? search = null, bool? active = null, CancellationToken ct = default);
    Task<MasterListSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<PaymentMethodDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PaymentMethodDto> CreateAsync(CreatePaymentMethodRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdatePaymentMethodRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
