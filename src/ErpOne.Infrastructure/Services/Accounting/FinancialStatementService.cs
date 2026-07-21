using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class FinancialStatementService(AppDbContext db) : IFinancialStatementService
{
    private sealed record Acc(int Id, string Code, string Name, AccountType Type, int? ParentId);

    private static decimal Natural(AccountType type, decimal signed) =>
        type is AccountType.Asset or AccountType.Expense ? signed : -signed;

    private async Task<(List<Acc> accounts, Dictionary<int, decimal> rolled)> LoadAsync(
        DateTime? fromInclusive, DateTime toExclusive, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Select(a => new Acc(a.Id, a.Code, a.Name, a.Type, a.ParentId))
            .ToListAsync(ct);

        var q = from l in db.JournalEntryLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.Status != JournalEntryStatus.Draft && e.EntryDate < toExclusive
                select new { l.AccountId, l.Debit, l.Credit, e.EntryDate };
        if (fromInclusive is DateTime f) q = q.Where(x => x.EntryDate >= f);

        var own = (await q.GroupBy(x => x.AccountId)
                .Select(g => new { AccountId = g.Key, Signed = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
                .ToListAsync(ct))
            .ToDictionary(x => x.AccountId, x => x.Signed);

        var childrenByParent = accounts.Where(a => a.ParentId != null)
            .ToLookup(a => a.ParentId!.Value);
        var rolled = new Dictionary<int, decimal>();
        decimal Roll(int id)
        {
            if (rolled.TryGetValue(id, out var cached)) return cached;
            var sum = own.TryGetValue(id, out var o) ? o : 0m;
            foreach (var child in childrenByParent[id]) sum += Roll(child.Id);
            rolled[id] = sum;
            return sum;
        }
        foreach (var a in accounts) Roll(a.Id);
        return (accounts, rolled);
    }

    private static StatementSectionDto BuildSection(string title, IEnumerable<AccountType> types,
        List<Acc> accounts, Dictionary<int, decimal> rolled)
    {
        var typeSet = types.ToHashSet();
        var inScope = accounts.Where(a => typeSet.Contains(a.Type)).ToList();
        var idSet = inScope.Select(a => a.Id).ToHashSet();
        var childrenByParent = inScope.Where(a => a.ParentId != null && idSet.Contains(a.ParentId!.Value))
            .ToLookup(a => a.ParentId!.Value);
        var roots = inScope.Where(a => a.ParentId == null || !idSet.Contains(a.ParentId!.Value))
            .OrderBy(a => a.Code).ToList();

        var lines = new List<StatementLineDto>();
        void Walk(Acc a, int level)
        {
            var signed = rolled.TryGetValue(a.Id, out var s) ? s : 0m;
            if (signed == 0m) return; // skip empty subtree
            var kids = childrenByParent[a.Id].OrderBy(x => x.Code).ToList();
            lines.Add(new StatementLineDto(a.Id, a.Code, a.Name, level, kids.Count > 0, Natural(a.Type, signed)));
            foreach (var k in kids) Walk(k, level + 1);
        }
        foreach (var r in roots) Walk(r, 0);

        var total = roots.Sum(r => Natural(r.Type, rolled.TryGetValue(r.Id, out var s) ? s : 0m));
        return new StatementSectionDto(title, lines, total);
    }

    public async Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOf, CancellationToken ct = default)
    {
        var (accounts, rolled) = await LoadAsync(null, asOf.Date.AddDays(1), ct);

        var assets = BuildSection("Assets", [AccountType.Asset], accounts, rolled);
        var liabilities = BuildSection("Liabilities", [AccountType.Liability], accounts, rolled);
        var equityBase = BuildSection("Equity", [AccountType.Equity], accounts, rolled);

        // Current earnings = revenue - expense (natural) as-of.
        decimal SumNatural(AccountType type) => accounts.Where(a => a.Type == type)
            .Where(a => a.ParentId == null)                        // roots only to avoid double count
            .Sum(a => Natural(type, rolled.TryGetValue(a.Id, out var s) ? s : 0m));
        var currentEarnings = SumNatural(AccountType.Revenue) - SumNatural(AccountType.Expense);

        var equityLines = equityBase.Lines.ToList();
        equityLines.Add(new StatementLineDto(0, "", "Laba Tahun Berjalan", 0, false, currentEarnings));
        var equity = new StatementSectionDto("Equity", equityLines, equityBase.Total + currentEarnings);

        var totalLiabEq = liabilities.Total + equity.Total;
        return new BalanceSheetDto(asOf.Date, assets, liabilities, equity, currentEarnings,
            assets.Total, totalLiabEq, assets.Total == totalLiabEq);
    }

    public async Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (accounts, rolled) = await LoadAsync(from.Date, to.Date.AddDays(1), ct);
        var revenue = BuildSection("Revenue", [AccountType.Revenue], accounts, rolled);
        var expense = BuildSection("Expenses", [AccountType.Expense], accounts, rolled);
        return new IncomeStatementDto(from.Date, to.Date, revenue, expense,
            revenue.Total, expense.Total, revenue.Total - expense.Total);
    }

    public async Task<ReportDocument> BuildBalanceSheetReportAsync(DateTime asOf, CancellationToken ct = default)
    {
        var bs = await GetBalanceSheetAsync(asOf, ct);
        var rows = new List<ReportRow>();
        void AddSection(StatementSectionDto s)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [s.Title, ""] });
            foreach (var l in s.Lines)
                rows.Add(new ReportRow { IsSubtotal = l.IsHeader, Cells = [Indent(l), l.Amount] });
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"Total {s.Title}", s.Total] });
        }
        AddSection(bs.Assets);
        AddSection(bs.Liabilities);
        AddSection(bs.Equity);
        return new ReportDocument
        {
            Title = "Balance Sheet",
            Subtitle = $"As of {bs.AsOf:d MMM yyyy}",
            GeneratedAt = DateTime.Now,
            Columns = [new ReportColumn("Account"), new ReportColumn("Amount", ReportAlign.Right, "N0")],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Total Liabilities & Equity", bs.TotalLiabilitiesAndEquity] },
        };
    }

    public async Task<ReportDocument> BuildIncomeStatementReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var pl = await GetIncomeStatementAsync(from, to, ct);
        var rows = new List<ReportRow>();
        void AddSection(StatementSectionDto s)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [s.Title, ""] });
            foreach (var l in s.Lines)
                rows.Add(new ReportRow { IsSubtotal = l.IsHeader, Cells = [Indent(l), l.Amount] });
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"Total {s.Title}", s.Total] });
        }
        AddSection(pl.Revenue);
        AddSection(pl.Expense);
        return new ReportDocument
        {
            Title = "Income Statement",
            Subtitle = $"{pl.From:d MMM yyyy} – {pl.To:d MMM yyyy}",
            GeneratedAt = DateTime.Now,
            Columns = [new ReportColumn("Account"), new ReportColumn("Amount", ReportAlign.Right, "N0")],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Net Income", pl.NetIncome] },
        };
    }

    private static string Indent(StatementLineDto l) =>
        (l.Level > 0 ? new string(' ', l.Level * 4) : "") + (string.IsNullOrEmpty(l.Code) ? l.Name : $"{l.Code} {l.Name}");
}
