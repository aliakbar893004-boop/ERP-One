namespace ErpOne.Application.Reports;

public interface IGrossProfitReportService
{
    Task<GrossProfitResultDto> GetGrossProfitAsync(GrossProfitFilter f, CancellationToken ct = default);
    Task<ReportDocument> BuildGrossProfitReportAsync(GrossProfitFilter f, CancellationToken ct = default);
}
