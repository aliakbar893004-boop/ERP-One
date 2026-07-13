using ErpOne.Application.Common;
using ErpOne.Application.Reports;

namespace ErpOne.Infrastructure.Services;

public class SalesReportService(SalesFactProvider facts) : ISalesReportService
{
    public async Task<PagedResult<SalesFactRow>> GetSalesPagedAsync(
        SalesFilter f, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200_000);
        var all = await facts.GetAsync(f, ct);
        var ordered = all.OrderByDescending(r => r.Date).ThenBy(r => r.DocNumber).ToList();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<SalesFactRow>(items, ordered.Count, page, pageSize);
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(SalesFilter f, CancellationToken ct = default)
    {
        var all = await facts.GetAsync(f, ct);
        var revenue = all.Sum(r => r.Revenue);
        var cogs = all.Sum(r => r.Cogs);
        var gp = revenue - cogs;
        var margin = revenue == 0 ? 0m : gp / revenue * 100m;
        return new SalesSummaryDto(all.Count, all.Sum(r => r.Quantity), revenue, cogs, gp, margin);
    }

    public async Task<ReportDocument> BuildSalesReportAsync(SalesFilter f, CancellationToken ct = default)
    {
        var all = (await facts.GetAsync(f, ct))
            .OrderByDescending(r => r.Date).ThenBy(r => r.DocNumber).ToList();

        var rows = all.Select(r => new ReportRow
        {
            Cells = [r.Date, r.Channel, r.DocNumber, r.WarehouseName, r.Sku, r.ProductName,
                     r.Party, r.Quantity, r.Revenue, r.Cogs, r.GrossProfit]
        }).ToList();

        var revenue = all.Sum(r => r.Revenue);
        var cogs = all.Sum(r => r.Cogs);

        return new ReportDocument
        {
            Title = "Sales Report",
            FilterSummary = FilterSummary(f),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "d MMM yyyy"),
                new ReportColumn("Channel"),
                new ReportColumn("Doc"),
                new ReportColumn("Warehouse"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Party"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Revenue", ReportAlign.Right, "N2"),
                new ReportColumn("COGS", ReportAlign.Right, "N2"),
                new ReportColumn("Gross Profit", ReportAlign.Right, "N2"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow
            {
                IsGrandTotal = true,
                Cells = ["Total", "", "", "", "", "", "", all.Sum(r => r.Quantity), revenue, cogs, revenue - cogs]
            },
        };
    }

    private static string FilterSummary(SalesFilter f)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Channel)) parts.Add($"Channel: {f.Channel}");
        if (f.From is DateTime from) parts.Add($"From: {from:d MMM yyyy}");
        if (f.To is DateTime to) parts.Add($"To: {to:d MMM yyyy}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All sales" : string.Join("  ·  ", parts);
    }
}
