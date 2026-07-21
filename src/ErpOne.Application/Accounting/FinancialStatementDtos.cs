namespace ErpOne.Application.Accounting;

public record StatementLineDto(int AccountId, string Code, string Name, int Level, bool IsHeader, decimal Amount);

public record StatementSectionDto(string Title, IReadOnlyList<StatementLineDto> Lines, decimal Total);

public record BalanceSheetDto(DateTime AsOf, StatementSectionDto Assets, StatementSectionDto Liabilities,
    StatementSectionDto Equity, decimal CurrentEarnings, decimal TotalAssets, decimal TotalLiabilitiesAndEquity, bool IsBalanced);

public record IncomeStatementDto(DateTime From, DateTime To, StatementSectionDto Revenue, StatementSectionDto Expense,
    decimal TotalRevenue, decimal TotalExpense, decimal NetIncome);
