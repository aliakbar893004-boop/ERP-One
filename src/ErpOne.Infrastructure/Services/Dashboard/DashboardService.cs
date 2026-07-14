using ErpOne.Application.Dashboard;
using ErpOne.Application.Products;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpOne.Infrastructure.Services;

public class DashboardService(
    AppDbContext db,
    SalesFactProvider sales,
    IProductService products) : IDashboardService
{
    private const int PendingListSize = 5;

    public async Task<OperationalDashboardDto> GetAsync(DateTime asOf, CancellationToken ct = default)
    {
        var day = asOf.Date;

        // KPI omzet & transaksi (POS + B2B) via shared fact provider — satu query mencakup
        // rentang bulan-berjalan + jendela 7 hari (mana yang lebih awal).
        var weekStart = day.AddDays(-6);
        var monthStart = new DateTime(day.Year, day.Month, 1);
        var rangeStart = weekStart < monthStart ? weekStart : monthStart;
        var rows = await sales.GetAsync(new SalesFilter(rangeStart, day, null, null, null, null, null), ct);

        var byDayRevenue = rows.GroupBy(r => r.Date.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));
        var byDayTxn = rows.GroupBy(r => r.Date.Date).ToDictionary(g => g.Key, g => g.Select(x => x.DocNumber).Distinct().Count());
        var revenueTrend = new List<decimal>(7);
        var txnTrend = new List<int>(7);
        for (var i = 0; i < 7; i++)
        {
            var d = weekStart.AddDays(i);
            revenueTrend.Add(byDayRevenue.GetValueOrDefault(d, 0m));
            txnTrend.Add(byDayTxn.GetValueOrDefault(d, 0));
        }
        var todayRevenue = revenueTrend[6];
        var todayTxnCount = txnTrend[6];
        var yesterdayRevenue = revenueTrend[5];
        var yesterdayTxnCount = txnTrend[5];

        var monthRows = rows.Where(r => r.Date.Date >= monthStart).ToList();
        var monthRevenue = monthRows.Sum(r => r.Revenue);
        var monthTxnCount = monthRows.Select(r => r.DocNumber).Distinct().Count();

        // AR / AP aging (portable date-threshold comparisons; SQLite + SQL Server).
        var arAging = await ArAgingAsync(day, ct);
        var apAging = await ApAgingAsync(day, ct);

        // KPI due = outstanding dengan DueDate <= asOf + 7 (mencakup overdue + jatuh tempo <= 7 hari).
        var dueCutoff = day.AddDays(7);
        var arDue = await db.CustomerInvoices
            .Where(i => i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
            .Where(i => i.DueDate <= dueCutoff)
            .SumAsync(i => i.GrandTotal - i.PaidAmount, ct);
        var apDue = await db.SupplierInvoices
            .Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
            .Where(i => i.DueDate <= dueCutoff)
            .SumAsync(i => i.GrandTotal - i.PaidAmount, ct);

        // Pending approvals (join ke master untuk nama; entity tidak punya navigation).
        var poPendingCount = await db.PurchaseOrders.CountAsync(p => p.Status == PurchaseOrderStatus.PendingApproval, ct);
        var poPending = await db.PurchaseOrders
            .Where(p => p.Status == PurchaseOrderStatus.PendingApproval)
            .OrderByDescending(p => p.OrderDate).Take(PendingListSize)
            .Join(db.Suppliers, p => p.SupplierId, s => s.Id,
                (p, s) => new PendingDocRow(p.Id, p.PoNumber, s.Name, p.GrandTotal, p.OrderDate))
            .ToListAsync(ct);
        var soPendingCount = await db.SalesOrders.CountAsync(s => s.Status == SalesOrderStatus.PendingApproval, ct);
        var soPending = await db.SalesOrders
            .Where(s => s.Status == SalesOrderStatus.PendingApproval)
            .OrderByDescending(s => s.OrderDate).Take(PendingListSize)
            .Join(db.Customers, s => s.CustomerId, c => c.Id,
                (s, c) => new PendingDocRow(s.Id, s.SoNumber, c.Name, s.GrandTotal, s.OrderDate))
            .ToListAsync(ct);

        var stock = await products.GetDashboardAsync(ct);

        return new OperationalDashboardDto(
            new DashboardKpis(todayRevenue, todayTxnCount, arDue, apDue,
                yesterdayRevenue, yesterdayTxnCount, revenueTrend, txnTrend,
                monthRevenue, monthTxnCount),
            new PendingApprovalsDto(poPendingCount, poPending, soPendingCount, soPending),
            arAging, apAging, stock);
    }

    // Satu query ringan per sisi: ambil (DueDate, outstanding) untuk invoice belum lunas, lalu bucket di memori.
    private async Task<AgingBuckets> ArAgingAsync(DateTime day, CancellationToken ct)
    {
        var rows = await db.CustomerInvoices
            .Where(i => i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
            .Select(i => new AgingRow(i.DueDate, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);
        return Bucketize(rows, day);
    }

    private async Task<AgingBuckets> ApAgingAsync(DateTime day, CancellationToken ct)
    {
        var rows = await db.SupplierInvoices
            .Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
            .Select(i => new AgingRow(i.DueDate, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);
        return Bucketize(rows, day);
    }

    // Age = hari sejak DueDate (negatif = belum jatuh tempo → masuk Current).
    private static AgingBuckets Bucketize(IReadOnlyList<AgingRow> rows, DateTime day)
    {
        decimal current = 0m, d31_60 = 0m, d61_90 = 0m, d90Plus = 0m;
        foreach (var r in rows)
        {
            var age = (day.Date - r.DueDate.Date).Days;
            if (age <= 30) current += r.Amount;
            else if (age <= 60) d31_60 += r.Amount;
            else if (age <= 90) d61_90 += r.Amount;
            else d90Plus += r.Amount;
        }
        return new AgingBuckets(current, d31_60, d61_90, d90Plus, current + d31_60 + d61_90 + d90Plus);
    }

    private record AgingRow(DateTime DueDate, decimal Amount);
}
