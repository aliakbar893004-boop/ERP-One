namespace ErpOne.Application.Reports;

public interface ICashierShiftReportService
{
    Task<ShiftReportResultDto> GetShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<ReportDocument> BuildShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CashierOptionDto>> GetCashiersAsync(CancellationToken ct = default);
}
