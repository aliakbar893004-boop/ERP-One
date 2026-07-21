using ErpOne.Application.Reports;

namespace ErpOne.Application.Accounting;

public interface IFinancialStatementService
{
    Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOf, CancellationToken ct = default);
    Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildBalanceSheetReportAsync(DateTime asOf, CancellationToken ct = default);
    Task<ReportDocument> BuildIncomeStatementReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
