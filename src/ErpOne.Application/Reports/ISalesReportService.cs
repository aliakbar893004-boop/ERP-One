using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface ISalesReportService
{
    Task<PagedResult<SalesFactRow>> GetSalesPagedAsync(
        SalesFilter f, int page, int pageSize, CancellationToken ct = default);

    Task<SalesSummaryDto> GetSalesSummaryAsync(SalesFilter f, CancellationToken ct = default);

    Task<ReportDocument> BuildSalesReportAsync(SalesFilter f, CancellationToken ct = default);
}
