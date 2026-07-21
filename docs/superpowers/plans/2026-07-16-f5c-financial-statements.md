# Fase 5c — Financial Statements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Balance Sheet (as-of) + Income Statement (period) built from the General Ledger, structured by the COA hierarchy, with Excel/PDF export.

**Architecture:** A read-only `FinancialStatementService` aggregates `JournalEntryLine` (parent `Status != Draft`) into per-account signed balances, rolls them up the COA parent/child tree, and converts to natural sign per account type. Balance Sheet adds a synthetic "Laba Tahun Berjalan" line (= Revenue − Expense as-of) into Equity so Assets = Liabilities + Equity. No new entities/migrations — pure read-model over 5a/5b data. Reuses `ReportDocument`/`IReportExporter`.

**Tech Stack:** .NET 10, Blazor Server, EF Core (tests SQLite `EnsureCreated` + `AccountingSeeder`), xUnit. Builds on 5a (`Account`, `JournalEntry`, `AccountType`, `NormalBalanceSide`) and the `ReportDocument` pattern. Solution `ErpOne.slnx`.

## Global Constraints

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`. **App di VS di-stop** sebelum build/test.
- Namespace flat: Application `ErpOne.Application.Accounting`; infra service `ErpOne.Infrastructure.Services`.
- Web project: entity `Account` bentrok namespace `ErpOne.Web.Components.Account` — pakai fully-qualified bila perlu di Web C#.
- Saldo: `signed = Σ(Debit − Credit)`; `Natural(type,signed)` = `signed` untuk Asset/Expense, `−signed` untuk Liability/Equity/Revenue. Roll-up: header = own + Σ anak.
- Filter: Neraca `EntryDate < asOf.AddDays(1)`; L/R `from.Date <= EntryDate < to.AddDays(1)`. Selalu `Status != JournalEntryStatus.Draft` (Posted & Reversed dua-duanya dihitung — konsisten `LedgerService`).
- Baris `rolledSigned == 0` di-skip.
- `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign` signature persis existing (`LedgerService`/`AgingReportService`): `ReportColumn(header, align=Left, format=null)`, `ReportRow { Cells, IsSubtotal, IsGrandTotal }`, format money `"N0"`.
- Commit MANUAL — step "Commit" penanda; JANGAN `git commit/merge/push`. Boleh `git add`. Branch `Development`.

---

## File Structure
- Create: `src/ErpOne.Application/Accounting/FinancialStatementDtos.cs`, `IFinancialStatementService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/FinancialStatementService.cs`
- Create: `src/ErpOne.Web/Components/Pages/Reports/BalanceSheet/BalanceSheetIndex.razor`, `Reports/IncomeStatement/IncomeStatementIndex.razor`
- Create: `tests/ErpOne.IntegrationTests/FinancialStatementServiceTests.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`, `src/ErpOne.Web/Authorization/AppMenus.cs`

---

## Task 1: DTOs + interface

**Files:**
- Create: `src/ErpOne.Application/Accounting/FinancialStatementDtos.cs`, `IFinancialStatementService.cs`

**Interfaces:**
- Produces: `StatementLineDto`, `StatementSectionDto`, `BalanceSheetDto`, `IncomeStatementDto`, `IFinancialStatementService`.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/Accounting/FinancialStatementDtos.cs`:
```csharp
namespace ErpOne.Application.Accounting;

public record StatementLineDto(int AccountId, string Code, string Name, int Level, bool IsHeader, decimal Amount);

public record StatementSectionDto(string Title, IReadOnlyList<StatementLineDto> Lines, decimal Total);

public record BalanceSheetDto(DateTime AsOf, StatementSectionDto Assets, StatementSectionDto Liabilities,
    StatementSectionDto Equity, decimal CurrentEarnings, decimal TotalAssets, decimal TotalLiabilitiesAndEquity, bool IsBalanced);

public record IncomeStatementDto(DateTime From, DateTime To, StatementSectionDto Revenue, StatementSectionDto Expense,
    decimal TotalRevenue, decimal TotalExpense, decimal NetIncome);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/Accounting/IFinancialStatementService.cs`:
```csharp
using ErpOne.Application.Reports;

namespace ErpOne.Application.Accounting;

public interface IFinancialStatementService
{
    Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOf, CancellationToken ct = default);
    Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildBalanceSheetReportAsync(DateTime asOf, CancellationToken ct = default);
    Task<ReportDocument> BuildIncomeStatementReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Application/ErpOne.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Application/Accounting/FinancialStatementDtos.cs src/ErpOne.Application/Accounting/IFinancialStatementService.cs
```

---

## Task 2: FinancialStatementService + DI — TDD

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Accounting/FinancialStatementService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/FinancialStatementServiceTests.cs`

**Interfaces:**
- Consumes: `db.Accounts`, `db.JournalEntries`, `db.JournalEntryLines`, `AccountType`, `JournalEntryStatus`, `IJournalEntryService` (test), `ReportDocument`.
- Produces: `IFinancialStatementService` impl.

- [ ] **Step 1: Write failing tests**

Create `tests/ErpOne.IntegrationTests/FinancialStatementServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class FinancialStatementServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FinancialStatementServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> Acc(AppDbContext db, string code) =>
        await db.Accounts.Where(a => a.Code == code).Select(a => a.Id).FirstAsync();

    // Post a balanced 2-line journal via the service so it lands in the GL.
    private static async Task PostAsync(IServiceProvider sp, string desc, int drAcc, int crAcc, decimal amount)
    {
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, desc,
            [new JournalEntryLineInput(drAcc, amount, 0m, null), new JournalEntryLineInput(crAcc, 0m, amount, null)]));
        await je.PostAsync(created.Id);
    }

    [Fact]
    public async Task Balance_sheet_balances_and_income_statement_nets()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var kas = await Acc(db, "1110");
        var modal = await Acc(db, "3100");
        var penjualan = await Acc(db, "4100");
        var beban = await Acc(db, "6100");

        // Opening capital 10,000,000; cash sale 3,000,000; expense 500,000.
        await PostAsync(sp, "Opening capital", kas, modal, 10_000_000m);
        await PostAsync(sp, "Cash sale", kas, penjualan, 3_000_000m);
        await PostAsync(sp, "Salary", beban, kas, 500_000m);

        var svc = sp.GetRequiredService<IFinancialStatementService>();

        var pl = await svc.GetIncomeStatementAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.Equal(3_000_000m, pl.TotalRevenue);
        Assert.Equal(500_000m, pl.TotalExpense);
        Assert.Equal(2_500_000m, pl.NetIncome);

        var bs = await svc.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(bs.IsBalanced);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilitiesAndEquity);
        // Current earnings = revenue - expense as-of.
        Assert.Equal(2_500_000m, bs.CurrentEarnings);
        // Cash = 10,000,000 + 3,000,000 - 500,000 = 12,500,000 sits in Assets.
        Assert.Contains(bs.Assets.Lines, l => l.AccountId == kas && l.Amount == 12_500_000m);
        // Current earnings line present in equity.
        Assert.Contains(bs.Equity.Lines, l => !l.IsHeader && l.Amount == 2_500_000m && l.AccountId == 0);
    }

    [Fact]
    public async Task Zero_balance_accounts_are_omitted()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var kas = await Acc(db, "1110");
        var modal = await Acc(db, "3100");
        await PostAsync(sp, "Cap", kas, modal, 1_000_000m);

        var bs = await sp.GetRequiredService<IFinancialStatementService>().GetBalanceSheetAsync(DateTime.Today);
        // "Bank" (1120) never used → must not appear.
        var bank = await Acc(db, "1120");
        Assert.DoesNotContain(bs.Assets.Lines, l => l.AccountId == bank);
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~FinancialStatementServiceTests"`
Expected: FAIL (`IFinancialStatementService` not registered).

- [ ] **Step 3: Implementation**

Create `src/ErpOne.Infrastructure/Services/Accounting/FinancialStatementService.cs`:
```csharp
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
```

- [ ] **Step 4: DI**

In `DependencyInjection.cs`, after `services.AddScoped<IJournalPostingService, JournalPostingService>();`:
```csharp
        services.AddScoped<IFinancialStatementService, FinancialStatementService>();
```

- [ ] **Step 5: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~FinancialStatementServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Accounting/FinancialStatementService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/FinancialStatementServiceTests.cs
```

---

## Task 3: Menu + Balance Sheet & Income Statement pages

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Create: `src/ErpOne.Web/Components/Pages/Reports/BalanceSheet/BalanceSheetIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Reports/IncomeStatement/IncomeStatementIndex.razor`

- [ ] **Step 1: Menu resources**

In `AppMenus.cs`, in the `Reports` group after `reports.trial-balance`:
```csharp
            new("reports.balance-sheet", "Balance Sheet", "bi-clipboard-data", ReportActions),
            new("reports.income-statement", "Income Statement", "bi-graph-up", ReportActions),
```

- [ ] **Step 2: Balance Sheet page**

Create `src/ErpOne.Web/Components/Pages/Reports/BalanceSheet/BalanceSheetIndex.razor`:
```razor
@page "/reports/balance-sheet"
@attribute [Authorize(Policy = "reports.balance-sheet.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Application.Reports
@using Microsoft.JSInterop
@inject IFinancialStatementService Statements
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Balance Sheet</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Balance Sheet</span></nav>
            <h1>Balance Sheet</h1>
            <p>Assets, liabilities and equity as of a chosen date.</p>
        </div>
        <AuthorizeView Policy="reports.balance-sheet.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <input type="date" @bind="_asOf" @bind:after="ReloadAsync" />
        @if (_bs is not null)
        {
            <span class="badge @(_bs.IsBalanced ? "bg-success" : "bg-danger")">
                @(_bs.IsBalanced ? "Balanced" : "Not balanced")
            </span>
        }
    </div>

    @if (_bs is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th>Account</th><th class="text-end" style="width:200px">Amount</th></tr></thead>
                    <tbody>
                        @Section(_bs.Assets)
                        <tr class="fw-bold"><td>Total Assets</td><td class="text-end mono">@_bs.TotalAssets.ToString("N0")</td></tr>
                        @Section(_bs.Liabilities)
                        @Section(_bs.Equity)
                        <tr class="fw-bold"><td>Total Liabilities &amp; Equity</td><td class="text-end mono">@_bs.TotalLiabilitiesAndEquity.ToString("N0")</td></tr>
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private BalanceSheetDto? _bs;
    private DateTime _asOf = DateTime.Today;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync() => _bs = await Statements.GetBalanceSheetAsync(_asOf);
    private async Task ReloadAsync() => await LoadAsync();

    private RenderFragment Section(StatementSectionDto s) => __builder =>
    {
        <tr class="table-light fw-bold"><td colspan="2">@s.Title</td></tr>
        @foreach (var l in s.Lines)
        {
            <tr class="@(l.IsHeader ? "fw-semibold" : "")">
                <td style="padding-left:@(12 + l.Level * 20)px">@(string.IsNullOrEmpty(l.Code) ? l.Name : $"{l.Code} — {l.Name}")</td>
                <td class="text-end mono">@l.Amount.ToString("N0")</td>
            </tr>
        }
        <tr class="fw-bold"><td class="text-end">Total @s.Title</td><td class="text-end mono">@s.Total.ToString("N0")</td></tr>
    };

    private async Task ExportExcel()
    {
        var doc = await Statements.BuildBalanceSheetReportAsync(_asOf);
        await Download(Exporter.ToExcel(doc), "balance-sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    private async Task ExportPdf()
    {
        var doc = await Statements.BuildBalanceSheetReportAsync(_asOf);
        await Download(await Exporter.ToPdfAsync(doc), "balance-sheet.pdf", "application/pdf");
    }
    private async Task Download(byte[] bytes, string name, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", name, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 3: Income Statement page**

Create `src/ErpOne.Web/Components/Pages/Reports/IncomeStatement/IncomeStatementIndex.razor`:
```razor
@page "/reports/income-statement"
@attribute [Authorize(Policy = "reports.income-statement.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Application.Reports
@using Microsoft.JSInterop
@inject IFinancialStatementService Statements
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Income Statement</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Income Statement</span></nav>
            <h1>Income Statement</h1>
            <p>Revenue minus expenses for the selected period.</p>
        </div>
        <AuthorizeView Policy="reports.income-statement.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
    </div>

    @if (_pl is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th>Account</th><th class="text-end" style="width:200px">Amount</th></tr></thead>
                    <tbody>
                        @Section(_pl.Revenue)
                        @Section(_pl.Expense)
                        <tr class="fw-bold"><td>Net Income</td><td class="text-end mono">@_pl.NetIncome.ToString("N0")</td></tr>
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private IncomeStatementDto? _pl;
    private DateTime _from = new(DateTime.Today.Year, 1, 1);
    private DateTime _to = DateTime.Today;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync() => _pl = await Statements.GetIncomeStatementAsync(_from, _to);
    private async Task ReloadAsync() => await LoadAsync();

    private RenderFragment Section(StatementSectionDto s) => __builder =>
    {
        <tr class="table-light fw-bold"><td colspan="2">@s.Title</td></tr>
        @foreach (var l in s.Lines)
        {
            <tr class="@(l.IsHeader ? "fw-semibold" : "")">
                <td style="padding-left:@(12 + l.Level * 20)px">@(string.IsNullOrEmpty(l.Code) ? l.Name : $"{l.Code} — {l.Name}")</td>
                <td class="text-end mono">@l.Amount.ToString("N0")</td>
            </tr>
        }
        <tr class="fw-bold"><td class="text-end">Total @s.Title</td><td class="text-end mono">@s.Total.ToString("N0")</td></tr>
    };

    private async Task ExportExcel()
    {
        var doc = await Statements.BuildIncomeStatementReportAsync(_from, _to);
        await Download(Exporter.ToExcel(doc), "income-statement.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    private async Task ExportPdf()
    {
        var doc = await Statements.BuildIncomeStatementReportAsync(_from, _to);
        await Download(await Exporter.ToPdfAsync(doc), "income-statement.pdf", "application/pdf");
    }
    private async Task Download(byte[] bytes, string name, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", name, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 4: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/Pages/Reports/BalanceSheet/ src/ErpOne.Web/Components/Pages/Reports/IncomeStatement/
```

---

## Task 4: Full suite + verification

- [ ] **Step 1: Full test suite**

App di VS di-stop. Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA PASS. Baseline 309 + 2 baru = **311**.

- [ ] **Step 2: Manual verification (skill `run`/`verify`)**

Run app; sign out/in (permission `reports.balance-sheet.*`, `reports.income-statement.*`). Buat beberapa transaksi (POS sale, expense) supaya auto-post 5b mengisi GL. Verifikasi:
1. `/reports/income-statement` (rentang tahun berjalan) — Pendapatan, Beban (COGS grup & Beban Operasional terpisah), Net Income.
2. `/reports/balance-sheet` (as-of hari ini) — Aset = Kewajiban + Ekuitas, badge **Balanced**, baris **Laba Tahun Berjalan** = Net Income periode berjalan.
3. Export Excel & PDF kedua laporan terunduh.
4. Bandingkan Net Income (L/R) dengan Laba Tahun Berjalan (Neraca) untuk rentang sejak awal — harus sama.

- [ ] **Step 3: Done marker**

Beritahu user Fase 5c (dan Fase 5 seluruhnya) selesai, siap commit manual.

---

## Self-Review

**Spec coverage:** Balance Sheet as-of + hierarki + Laba Tahun Berjalan → Task 2 (`GetBalanceSheetAsync`). Income Statement periode + hierarki + Net Income → Task 2 (`GetIncomeStatementAsync`). Export → Task 2 report builders. Pages + menu → Task 3. Zero-balance skip → `BuildSection` (`if signed==0 return`) + test. Testing balance + net + omit-zero → Task 2 tests. Arus Kas di luar scope. ✓

**Placeholder scan:** Tak ada TBD. Semua kode lengkap.

**Type consistency:** `StatementSectionDto`/`StatementLineDto`/`BalanceSheetDto`/`IncomeStatementDto` konsisten Task 1↔2↔3. `IFinancialStatementService` 4 method konsisten Task 2↔3. `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign` sesuai signature existing. `AccountType`/`JournalEntryStatus` dari 5a. `Natural()` + roll-up konsisten dgn `LedgerService`. Current earnings dihitung dari root Revenue/Expense (hindari double-count karena roll-up sudah menjumlahkan anak ke root).

**Catatan:** `SumNatural` menjumlahkan hanya akun `ParentId == null` (root) memakai `rolled` (yang sudah roll-up anak) → tak double-count. `BuildSection.total` juga pakai roots saja. Konsisten.
