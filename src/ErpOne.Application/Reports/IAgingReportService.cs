namespace ErpOne.Application.Reports;

public interface IAgingReportService
{
    Task<AgingResultDto> GetArAgingAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<AgingResultDto> GetApAgingAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
    Task<ReportDocument> BuildArAgingReportAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<ReportDocument> BuildApAgingReportAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
}
