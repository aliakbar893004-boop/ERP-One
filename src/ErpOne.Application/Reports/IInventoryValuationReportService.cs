namespace ErpOne.Application.Reports;

public interface IInventoryValuationReportService
{
    Task<ValuationResultDto> GetValuationAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default);

    Task<ReportDocument> BuildValuationReportAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default);
}
