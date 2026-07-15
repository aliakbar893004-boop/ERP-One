using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CashierShiftReportService(AppDbContext db) : ICashierShiftReportService
{
    public async Task<ShiftReportResultDto> GetShiftReportAsync(
        DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default)
    {
        var fromDate = from.Date;
        var toExclusive = to.Date.AddDays(1);

        var shifts = await db.CashierShifts.AsNoTracking()
            .Include(s => s.Totals)
            .Where(s => s.Status == CashierShiftStatus.Closed)
            .Where(s => s.OpenedAt >= fromDate && s.OpenedAt < toExclusive)
            .Where(s => warehouseId == null || s.WarehouseId == warehouseId)
            .Where(s => cashierUserId == null || s.CashierUserId == cashierUserId)
            .ToListAsync(ct);

        var whNames = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var pmNames = await db.PaymentMethods.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var cashiers = shifts
            .GroupBy(s => new { s.CashierUserId, s.CashierName })
            .Select(g =>
            {
                var rows = g.OrderBy(s => s.OpenedAt).Select(s => ToRow(s, whNames, pmNames)).ToList();
                return new ShiftCashierDto(
                    g.Key.CashierUserId, g.Key.CashierName, rows,
                    rows.Sum(r => r.TotalSales),
                    rows.Sum(r => r.TransactionCount),
                    rows.Sum(r => r.CashVariance ?? 0m));
            })
            .OrderBy(c => c.CashierName)
            .ToList();

        return new ShiftReportResultDto(
            fromDate, to.Date, cashiers,
            cashiers.Sum(c => c.TotalSales),
            cashiers.Sum(c => c.TransactionCount),
            cashiers.Sum(c => c.TotalVariance),
            cashiers.Sum(c => c.Shifts.Count),
            cashiers.Count);
    }

    public async Task<IReadOnlyList<CashierOptionDto>> GetCashiersAsync(CancellationToken ct = default) =>
        await db.CashierShifts.AsNoTracking()
            .Where(s => s.Status == CashierShiftStatus.Closed)
            .Select(s => new { s.CashierUserId, s.CashierName })
            .Distinct()
            .OrderBy(x => x.CashierName)
            .Select(x => new CashierOptionDto(x.CashierUserId, x.CashierName))
            .ToListAsync(ct);

    public async Task<ReportDocument> BuildShiftReportAsync(
        DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default)
    {
        var r = await GetShiftReportAsync(from, to, warehouseId, cashierUserId, ct);

        var rows = new List<ReportRow>();
        foreach (var c in r.Cashiers)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"▸ {c.CashierName} ({c.CashierUserId})", "", "", "", "", "", "", "", "", ""] });
            foreach (var s in c.Shifts)
            {
                rows.Add(new ReportRow { Cells = [s.ShiftNumber, s.OpenedAt, s.ClosedAt, s.WarehouseName,
                    s.OpeningFloat, s.TotalSales, s.TransactionCount, s.ExpectedCash, s.CountedCash, s.CashVariance] });
                foreach (var m in s.Methods)
                    rows.Add(new ReportRow { Cells = [$"    {m.PaymentMethodName}", "", "", "", "", m.Amount, m.TransactionCount, "", "", ""] });
            }
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"{c.CashierName} subtotal", "", "", "", "",
                c.TotalSales, c.TransactionCount, "", "", c.TotalVariance] });
        }

        return new ReportDocument
        {
            Title = "Cashier Shift Report",
            Subtitle = $"{r.From:d MMM yyyy} – {r.To:d MMM yyyy}",
            FilterSummary = BuildFilter(warehouseId, cashierUserId, r),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Shift / Method"),
                new ReportColumn("Opened", ReportAlign.Left, "yyyy-MM-dd HH:mm"),
                new ReportColumn("Closed", ReportAlign.Left, "yyyy-MM-dd HH:mm"),
                new ReportColumn("Warehouse"),
                new ReportColumn("Opening", ReportAlign.Right, "N0"),
                new ReportColumn("Sales", ReportAlign.Right, "N0"),
                new ReportColumn("Txns", ReportAlign.Right, "N0"),
                new ReportColumn("Expected", ReportAlign.Right, "N0"),
                new ReportColumn("Counted", ReportAlign.Right, "N0"),
                new ReportColumn("Variance", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Grand total", "", "", "", "",
                r.GrandTotalSales, r.GrandTransactionCount, "", "", r.GrandVariance] },
        };
    }

    private static string BuildFilter(int? warehouseId, string? cashierUserId, ShiftReportResultDto r)
    {
        var wh = warehouseId is null ? "All warehouses"
            : $"Warehouse: {r.Cashiers.SelectMany(c => c.Shifts).FirstOrDefault()?.WarehouseName ?? $"#{warehouseId}"}";
        var cs = cashierUserId is null ? "All cashiers"
            : $"Cashier: {r.Cashiers.FirstOrDefault()?.CashierName ?? cashierUserId}";
        return $"{wh} · {cs}";
    }

    private static ShiftRowDto ToRow(CashierShift s, Dictionary<int, string> whNames, Dictionary<int, string> pmNames) =>
        new(s.Id, s.ShiftNumber, s.WarehouseId,
            whNames.TryGetValue(s.WarehouseId, out var wn) ? wn : $"#{s.WarehouseId}",
            s.OpenedAt, s.ClosedAt, s.OpeningFloat, s.CashSalesTotal, s.TotalSalesAmount, s.TransactionCount,
            s.ExpectedCash, s.CountedCash, s.CashVariance,
            s.Totals
                .Select(t => new ShiftMethodDto(t.PaymentMethodId,
                    pmNames.TryGetValue(t.PaymentMethodId, out var pn) ? pn : $"#{t.PaymentMethodId}",
                    t.TotalAmount, t.TransactionCount))
                .OrderByDescending(m => m.Amount).ToList());
}
