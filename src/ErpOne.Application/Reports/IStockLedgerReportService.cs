using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface IStockLedgerReportService
{
    Task<PagedResult<StockMovementRowDto>> GetMovementsPagedAsync(
        StockLedgerFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<StockLedgerSummaryDto> GetSummaryAsync(StockLedgerFilter filter, CancellationToken ct = default);

    Task<StockCardDto?> GetStockCardAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<ReportDocument> BuildMovementsReportAsync(StockLedgerFilter filter, CancellationToken ct = default);

    Task<ReportDocument?> BuildStockCardReportAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default);
}
