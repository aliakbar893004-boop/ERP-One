using ErpOne.Application.Common;

namespace ErpOne.Application.Accounting;

public interface IJournalEntryService
{
    Task<PagedResult<JournalEntryListItemDto>> GetPagedAsync(JournalEntryFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<JournalEntryDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<JournalEntryDto> CreateDraftAsync(CreateJournalEntryRequest request, CancellationToken ct = default);
    Task<JournalEntryDto> UpdateDraftAsync(int id, CreateJournalEntryRequest request, CancellationToken ct = default);
    Task DeleteDraftAsync(int id, CancellationToken ct = default);
    Task PostAsync(int id, CancellationToken ct = default);
    Task<JournalEntryDto> ReverseAsync(int id, DateTime reversalDate, string? note, CancellationToken ct = default);
}
