using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class AgingReportService(AppDbContext db) : IAgingReportService
{
    public async Task<AgingResultDto> GetArAgingAsync(DateTime asOf, int? customerId, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        var invoices = await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate < toExclusive && i.Status != CustomerInvoiceStatus.Cancelled)
            .Where(i => customerId == null || i.CustomerId == customerId)
            .Select(i => new InvoiceRow(i.Id, i.InvoiceNumber, i.CustomerId, i.InvoiceDate, i.DueDate, i.GrandTotal))
            .ToListAsync(ct);

        var paid = await (
            from a in db.CustomerReceiptAllocations.AsNoTracking()
            join r in db.CustomerReceipts.AsNoTracking() on a.CustomerReceiptId equals r.Id
            where r.Status == CustomerReceiptStatus.Posted && r.ReceiptDate < toExclusive
            group a.Amount by a.CustomerInvoiceId into g
            select new { InvoiceId = g.Key, Paid = g.Sum() })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        var partyIds = invoices.Select(i => i.PartyId).Distinct().ToList();
        var parties = await db.Customers.AsNoTracking()
            .Where(c => partyIds.Contains(c.Id))
            .Select(c => new PartyInfo(c.Id, c.Code, c.Name))
            .ToListAsync(ct);

        return Build(asOf.Date, AgingSide.Receivable, invoices, paid, parties);
    }

    public async Task<AgingResultDto> GetApAgingAsync(DateTime asOf, int? supplierId, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        var invoices = await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate < toExclusive && i.Status != SupplierInvoiceStatus.Cancelled)
            .Where(i => supplierId == null || i.SupplierId == supplierId)
            .Select(i => new InvoiceRow(i.Id, i.InvoiceNumber, i.SupplierId, i.InvoiceDate, i.DueDate, i.GrandTotal))
            .ToListAsync(ct);

        var paid = await (
            from a in db.SupplierPaymentAllocations.AsNoTracking()
            join p in db.SupplierPayments.AsNoTracking() on a.SupplierPaymentId equals p.Id
            where p.Status == SupplierPaymentStatus.Posted && p.PaymentDate < toExclusive
            group a.Amount by a.SupplierInvoiceId into g
            select new { InvoiceId = g.Key, Paid = g.Sum() })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        var partyIds = invoices.Select(i => i.PartyId).Distinct().ToList();
        var parties = await db.Suppliers.AsNoTracking()
            .Where(s => partyIds.Contains(s.Id))
            .Select(s => new PartyInfo(s.Id, s.Code, s.Name))
            .ToListAsync(ct);

        return Build(asOf.Date, AgingSide.Payable, invoices, paid, parties);
    }

    public async Task<ReportDocument> BuildArAgingReportAsync(DateTime asOf, int? customerId, CancellationToken ct = default)
    {
        var result = await GetArAgingAsync(asOf, customerId, ct);
        var filter = customerId is null ? "All customers" : $"Customer: {result.Parties.FirstOrDefault()?.PartyName ?? $"#{customerId}"}";
        return ToReportDocument(result, "AR Aging", filter);
    }

    public async Task<ReportDocument> BuildApAgingReportAsync(DateTime asOf, int? supplierId, CancellationToken ct = default)
    {
        var result = await GetApAgingAsync(asOf, supplierId, ct);
        var filter = supplierId is null ? "All suppliers" : $"Supplier: {result.Parties.FirstOrDefault()?.PartyName ?? $"#{supplierId}"}";
        return ToReportDocument(result, "AP Aging", filter);
    }

    private static AgingResultDto Build(DateTime day, AgingSide side,
        List<InvoiceRow> invoices, Dictionary<int, decimal> paidByInvoice, List<PartyInfo> parties)
    {
        var partyById = parties.ToDictionary(p => p.Id);

        var aged = new List<(int PartyId, AgingInvoiceDto Dto)>();
        foreach (var inv in invoices)
        {
            var paid = paidByInvoice.TryGetValue(inv.Id, out var p) ? p : 0m;
            var outstanding = inv.GrandTotal - paid;
            if (outstanding <= 0m) continue;
            var days = (day - inv.DueDate.Date).Days;
            aged.Add((inv.PartyId, new AgingInvoiceDto(
                inv.Id, inv.Number, inv.InvoiceDate, inv.DueDate, days, inv.GrandTotal, outstanding, BucketOf(days, outstanding))));
        }

        var partyDtos = aged
            .GroupBy(x => x.PartyId)
            .Select(g =>
            {
                var info = partyById.TryGetValue(g.Key, out var pi) ? pi : new PartyInfo(g.Key, "?", "(unknown)");
                var invs = g.Select(x => x.Dto).OrderBy(d => d.DueDate).ToList();
                return new AgingPartyDto(info.Id, info.Code, info.Name, invs, Sum(invs.Select(d => d.Buckets)));
            })
            .OrderBy(p => p.PartyName)
            .ToList();

        return new AgingResultDto(day, side, partyDtos, Sum(partyDtos.Select(p => p.Subtotals)), aged.Count, partyDtos.Count);
    }

    private static AgingBucketSet BucketOf(int daysPastDue, decimal amount) => daysPastDue switch
    {
        <= 0  => new(amount, 0, 0, 0, 0, amount),
        <= 30 => new(0, amount, 0, 0, 0, amount),
        <= 60 => new(0, 0, amount, 0, 0, amount),
        <= 90 => new(0, 0, 0, amount, 0, amount),
        _     => new(0, 0, 0, 0, amount, amount),
    };

    private static AgingBucketSet Sum(IEnumerable<AgingBucketSet> sets) =>
        sets.Aggregate(new AgingBucketSet(0, 0, 0, 0, 0, 0),
            (a, b) => new(a.NotDue + b.NotDue, a.D1_30 + b.D1_30, a.D31_60 + b.D31_60,
                a.D61_90 + b.D61_90, a.D90Plus + b.D90Plus, a.Total + b.Total));

    private static ReportDocument ToReportDocument(AgingResultDto r, string title, string filterSummary)
    {
        var rows = new List<ReportRow>();
        foreach (var p in r.Parties)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"▸ {p.PartyCode} — {p.PartyName}", "", "", "", "", "", "", "", "", "", ""] });
            foreach (var i in p.Invoices)
                rows.Add(new ReportRow { Cells = [p.PartyName, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.DaysPastDue,
                    i.Buckets.NotDue, i.Buckets.D1_30, i.Buckets.D31_60, i.Buckets.D61_90, i.Buckets.D90Plus, i.Outstanding] });
            var s = p.Subtotals;
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"{p.PartyName} subtotal", "", "", "", "",
                s.NotDue, s.D1_30, s.D31_60, s.D61_90, s.D90Plus, s.Total] });
        }

        var g = r.GrandTotals;
        return new ReportDocument
        {
            Title = title,
            Subtitle = $"As of {r.AsOf:d MMM yyyy}",
            FilterSummary = filterSummary,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Party"),
                new ReportColumn("Invoice #"),
                new ReportColumn("Invoice Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Due Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Days", ReportAlign.Right, "N0"),
                new ReportColumn("Not Due", ReportAlign.Right, "N0"),
                new ReportColumn("1-30", ReportAlign.Right, "N0"),
                new ReportColumn("31-60", ReportAlign.Right, "N0"),
                new ReportColumn("61-90", ReportAlign.Right, "N0"),
                new ReportColumn("90+", ReportAlign.Right, "N0"),
                new ReportColumn("Outstanding", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Grand total", "", "", "", "",
                g.NotDue, g.D1_30, g.D31_60, g.D61_90, g.D90Plus, g.Total] },
        };
    }

    private sealed record InvoiceRow(int Id, string Number, int PartyId, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal);
    private sealed record PartyInfo(int Id, string Code, string Name);
}
