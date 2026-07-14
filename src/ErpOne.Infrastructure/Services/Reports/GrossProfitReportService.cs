using ErpOne.Application.Reports;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpOne.Infrastructure.Services;

public class GrossProfitReportService(SalesFactProvider facts, AppDbContext db) : IGrossProfitReportService
{
    public async Task<GrossProfitResultDto> GetGrossProfitAsync(GrossProfitFilter f, CancellationToken ct = default)
    {
        var salesFilter = new SalesFilter(f.From, f.To, f.Channel, null, null, null, null);
        var rows = await facts.GetAsync(salesFilter, ct);

        Dictionary<int, string>? categoryNames = null;
        if (f.GroupBy == GrossProfitGroupBy.Category)
            categoryNames = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        string KeyOf(SalesFactRow r) => f.GroupBy switch
        {
            GrossProfitGroupBy.Product => $"{r.Sku} — {r.ProductName}",
            GrossProfitGroupBy.Category => r.CategoryId is int c && categoryNames!.TryGetValue(c, out var n) ? n : "Uncategorized",
            GrossProfitGroupBy.Month => r.Date.ToString("MMM yyyy"),
            _ => "",
        };

        var groups = rows
            .GroupBy(KeyOf)
            .Select(g =>
            {
                var revenue = g.Sum(r => r.Revenue);
                var cogs = g.Sum(r => r.Cogs);
                var gp = revenue - cogs;
                return new GrossProfitGroupDto(
                    g.Key, g.Sum(r => r.Quantity), revenue, cogs, gp,
                    revenue == 0 ? 0m : gp / revenue * 100m);
            })
            .OrderBy(g => g.GroupName)
            .ToList();

        var totalRevenue = rows.Sum(r => r.Revenue);
        var totalCogs = rows.Sum(r => r.Cogs);
        var totalGp = totalRevenue - totalCogs;
        return new GrossProfitResultDto(
            f.GroupBy, groups, rows.Sum(r => r.Quantity),
            totalRevenue, totalCogs, totalGp,
            totalRevenue == 0 ? 0m : totalGp / totalRevenue * 100m);
    }

    public async Task<ReportDocument> BuildGrossProfitReportAsync(GrossProfitFilter f, CancellationToken ct = default)
    {
        var result = await GetGrossProfitAsync(f, ct);
        var groupHeader = f.GroupBy switch
        {
            GrossProfitGroupBy.Product => "Product",
            GrossProfitGroupBy.Category => "Category",
            GrossProfitGroupBy.Month => "Month",
            _ => "Group",
        };

        var rows = result.Groups.Select(g => new ReportRow
        {
            Cells = [g.GroupName, g.Qty, g.Revenue, g.Cogs, g.GrossProfit, g.MarginPercent]
        }).ToList();

        return new ReportDocument
        {
            Title = "Gross Profit Report",
            Subtitle = $"Grouped by {groupHeader}" + (string.IsNullOrEmpty(f.Channel) ? "" : $"  ·  Channel: {f.Channel}"),
            FilterSummary = FilterSummary(f),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn(groupHeader),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Revenue", ReportAlign.Right, "N2"),
                new ReportColumn("COGS", ReportAlign.Right, "N2"),
                new ReportColumn("Gross Profit", ReportAlign.Right, "N2"),
                new ReportColumn("Margin %", ReportAlign.Right, "N1"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow
            {
                IsGrandTotal = true,
                Cells = ["Total", result.TotalQty, result.TotalRevenue, result.TotalCogs, result.TotalGrossProfit, result.TotalMarginPercent]
            },
        };
    }

    private static string FilterSummary(GrossProfitFilter f)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Channel)) parts.Add($"Channel: {f.Channel}");
        if (f.From is DateTime from) parts.Add($"From: {from:d MMM yyyy}");
        if (f.To is DateTime to) parts.Add($"To: {to:d MMM yyyy}");
        return parts.Count == 0 ? "All sales" : string.Join("  ·  ", parts);
    }
}
