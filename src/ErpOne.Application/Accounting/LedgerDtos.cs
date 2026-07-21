using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record TrialBalanceRowDto(int AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit);

public record TrialBalanceDto(DateTime From, DateTime To, IReadOnlyList<TrialBalanceRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit);

public record GeneralLedgerLineDto(DateTime EntryDate, string EntryNumber, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

public record GeneralLedgerDto(int AccountId, string Code, string Name, AccountType Type,
    DateTime From, DateTime To, decimal OpeningBalance, IReadOnlyList<GeneralLedgerLineDto> Lines, decimal ClosingBalance);
