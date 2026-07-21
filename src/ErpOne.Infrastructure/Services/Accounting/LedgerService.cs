using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class LedgerService(AppDbContext db) : ILedgerService
{
    // Natural sign: debit-normal accounts count debit as +; credit-normal accounts count credit as +.
    private static int Sign(AccountType type) =>
        type is AccountType.Asset or AccountType.Expense ? 1 : -1;

    public async Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var fromDate = from.Date;
        var toExclusive = to.Date.AddDays(1);

        var raw = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            join a in db.Accounts.AsNoTracking() on l.AccountId equals a.Id
            where e.Status != JournalEntryStatus.Draft && e.EntryDate >= fromDate && e.EntryDate < toExclusive
            group new { l.Debit, l.Credit } by new { a.Id, a.Code, a.Name, a.Type } into g
            select new { g.Key.Id, g.Key.Code, g.Key.Name, g.Key.Type,
                         Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(ct);

        var rows = raw
            .Select(x =>
            {
                var net = x.Debit - x.Credit;                     // + => debit balance, - => credit balance
                return new TrialBalanceRowDto(x.Id, x.Code, x.Name, x.Type,
                    net >= 0 ? net : 0m, net < 0 ? -net : 0m);
            })
            .Where(r => r.Debit != 0m || r.Credit != 0m)
            .OrderBy(r => r.Code)
            .ToList();

        return new TrialBalanceDto(fromDate, to.Date, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public async Task<GeneralLedgerDto?> GetGeneralLedgerAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return null;
        var sign = Sign(account.Type);
        var fromDate = from.Date;
        var toExclusive = to.Date.AddDays(1);

        var opening = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId && e.Status != JournalEntryStatus.Draft && e.EntryDate < fromDate
            select (decimal?)(l.Debit - l.Credit)).SumAsync(ct) ?? 0m;
        var openingBalance = sign * opening;

        var inRange = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId && e.Status != JournalEntryStatus.Draft
                  && e.EntryDate >= fromDate && e.EntryDate < toExclusive
            orderby e.EntryDate, e.Id
            select new { e.EntryDate, e.EntryNumber, e.Description, l.Debit, l.Credit })
            .ToListAsync(ct);

        var running = openingBalance;
        var lines = new List<GeneralLedgerLineDto>();
        foreach (var r in inRange)
        {
            running += sign * (r.Debit - r.Credit);
            lines.Add(new GeneralLedgerLineDto(r.EntryDate, r.EntryNumber, r.Description, r.Debit, r.Credit, running));
        }

        return new GeneralLedgerDto(account.Id, account.Code, account.Name, account.Type,
            fromDate, to.Date, openingBalance, lines, running);
    }

    public async Task<ReportDocument> BuildTrialBalanceReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var tb = await GetTrialBalanceAsync(from, to, ct);
        var rows = tb.Rows
            .Select(r => new ReportRow { Cells = [r.Code, r.Name, r.Type.ToString(), r.Debit, r.Credit] })
            .ToList();
        return new ReportDocument
        {
            Title = "Trial Balance",
            Subtitle = $"{tb.From:d MMM yyyy} – {tb.To:d MMM yyyy}",
            FilterSummary = null,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Code"),
                new ReportColumn("Account"),
                new ReportColumn("Type"),
                new ReportColumn("Debit", ReportAlign.Right, "N0"),
                new ReportColumn("Credit", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["", "Grand total", "", tb.TotalDebit, tb.TotalCredit] },
        };
    }

    public async Task<ReportDocument> BuildGeneralLedgerReportAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var gl = await GetGeneralLedgerAsync(accountId, from, to, ct);
        if (gl is null)
            return new ReportDocument { Title = "General Ledger", GeneratedAt = DateTime.Now,
                Columns = [new ReportColumn("Date")], Rows = [] };

        var rows = new List<ReportRow>
        {
            new() { IsSubtotal = true, Cells = ["", "", "Opening balance", "", "", gl.OpeningBalance] }
        };
        rows.AddRange(gl.Lines.Select(l => new ReportRow
        {
            Cells = [l.EntryDate, l.EntryNumber, l.Description, l.Debit, l.Credit, l.RunningBalance]
        }));

        return new ReportDocument
        {
            Title = $"General Ledger — {gl.Code} {gl.Name}",
            Subtitle = $"{gl.From:d MMM yyyy} – {gl.To:d MMM yyyy}",
            FilterSummary = null,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Entry #"),
                new ReportColumn("Description"),
                new ReportColumn("Debit", ReportAlign.Right, "N0"),
                new ReportColumn("Credit", ReportAlign.Right, "N0"),
                new ReportColumn("Balance", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["", "", "Closing balance", "", "", gl.ClosingBalance] },
        };
    }
}
