using ErpOne.Application.Reports;

namespace ErpOne.Application.Accounting;

public interface ILedgerService
{
    Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<GeneralLedgerDto?> GetGeneralLedgerAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildTrialBalanceReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildGeneralLedgerReportAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default);
}
