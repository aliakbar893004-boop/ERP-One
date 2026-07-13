using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface IPurchaseReportService
{
    Task<PagedResult<PurchaseRowDto>> GetPurchasesPagedAsync(
        PurchaseFilter f, int page, int pageSize, CancellationToken ct = default);

    Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(PurchaseFilter f, CancellationToken ct = default);

    Task<ReportDocument> BuildPurchaseReportAsync(PurchaseFilter f, CancellationToken ct = default);
}
