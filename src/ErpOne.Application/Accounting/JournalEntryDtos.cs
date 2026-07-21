using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record JournalEntryLineInput(int AccountId, decimal Debit, decimal Credit, string? Memo);

public record CreateJournalEntryRequest(DateTime EntryDate, string Description, IReadOnlyList<JournalEntryLineInput> Lines);

public record JournalEntryLineDto(int Id, int AccountId, string AccountCode, string AccountName,
    decimal Debit, decimal Credit, string? Memo);

public record JournalEntryDto(int Id, string EntryNumber, DateTime EntryDate, string Description,
    JournalEntryStatus Status, decimal TotalDebit, decimal TotalCredit,
    int? ReversalOfEntryId, int? ReversedByEntryId, IReadOnlyList<JournalEntryLineDto> Lines);

public record JournalEntryListItemDto(int Id, string EntryNumber, DateTime EntryDate, string Description,
    JournalEntryStatus Status, decimal TotalDebit);

public record JournalEntryFilter(DateTime? From, DateTime? To, JournalEntryStatus? Status, string? Search);
