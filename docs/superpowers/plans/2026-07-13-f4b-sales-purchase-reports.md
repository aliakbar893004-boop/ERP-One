# Fase 4b — Sales & Purchase Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three reports on top of the 4a Reports foundation — a combined POS+B2B Sales report (with COGS/gross-profit columns), a GRN-based Purchase report, and an aggregated Gross Profit report (group by Product/Category/Month).

**Architecture:** Read-only query services in `ErpOne.Application/Reports` (interfaces) + `ErpOne.Infrastructure/Services` (EF implementations). A shared `SalesFactProvider` builds a normalized "sales fact" row list from two sources (PosSale lines + posted DeliveryOrder lines) and is reused by both the Sales report and Gross Profit report. Reports render via the existing `.pi`/`.kpis`/`Pager` UI and export through the existing `IReportExporter` (Excel/PDF) + `saveAsFile` JS helper.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server; SQLite in tests), Blazor Server, ClosedXML, QuestPDF, xUnit.

## Global Constraints

- Depends on Fase 4a being present (`ReportDocument`, `IReportExporter`, `ActExport`, Reports menu group, `saveAsFile` in `app-interop.js`). Build on the `feat/f4a-inventory-reports` branch (4b reuses 4a's foundation).
- **Commits are performed manually by the user.** Do NOT run `git commit`/`merge`/`push`. Each task ends by building + testing and leaving changes staged (`git add`) for the user to review and commit.
- Target `net10.0`; nullable + implicit usings enabled. Solution file is `ErpOne.slnx`.
- UI copy in English. Pages use `.pi` + `.kpis` + `Pager` (mirror `StockLedgerIndex.razor` / `InventoryValuationIndex.razor` from 4a).
- Every page guarded `@attribute [Authorize(Policy = "<resource>.<action>")]`. New permissions auto-seed via `AppMenus.AllPermissions`.
- Services: constructor-injected `AppDbContext`, `AsNoTracking()`, filter on entity columns before projecting to records.
- Money `N2`, quantities `N0`, percentages `N1`, dates `yyyy-MM-dd`.
- Revenue definition (ex-tax, net of discount) is consistent across channels: POS = `PosSaleLine.LineTotal`; B2B = `(SalesOrderLine.LineSubtotal − SalesOrderLine.LineDiscount) / SalesOrderLine.Quantity × DeliveryOrderLine.QuantityDelivered`.
- Build: `dotnet build ErpOne.slnx`. Test: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj`.

## File Structure

**Task 1 — Sales report + SalesFactProvider**
- Create `src/ErpOne.Application/Reports/SalesReportDtos.cs`
- Create `src/ErpOne.Application/Reports/ISalesReportService.cs`
- Create `src/ErpOne.Infrastructure/Services/SalesFactProvider.cs`
- Create `src/ErpOne.Infrastructure/Services/SalesReportService.cs`
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` (add 3 report resources — done once, here)
- Create `src/ErpOne.Web/Components/Pages/Reports/Sales/SalesReportIndex.razor`
- Test `tests/ErpOne.IntegrationTests/SalesReportServiceTests.cs`

**Task 2 — Purchase report**
- Create `src/ErpOne.Application/Reports/PurchaseReportDtos.cs`
- Create `src/ErpOne.Application/Reports/IPurchaseReportService.cs`
- Create `src/ErpOne.Infrastructure/Services/PurchaseReportService.cs`
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create `src/ErpOne.Web/Components/Pages/Reports/Purchases/PurchaseReportIndex.razor`
- Test `tests/ErpOne.IntegrationTests/PurchaseReportServiceTests.cs`

**Task 3 — Gross Profit report**
- Create `src/ErpOne.Application/Reports/GrossProfitDtos.cs`
- Create `src/ErpOne.Application/Reports/IGrossProfitReportService.cs`
- Create `src/ErpOne.Infrastructure/Services/GrossProfitReportService.cs`
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create `src/ErpOne.Web/Components/Pages/Reports/GrossProfit/GrossProfitIndex.razor`
- Test `tests/ErpOne.IntegrationTests/GrossProfitReportServiceTests.cs`

---

## Task 1: Sales Report + SalesFactProvider

**Files:** see File Structure (Task 1).

**Interfaces:**
- Consumes: `AppDbContext` (`PosSales`, `PosSaleLines`, `DeliveryOrders`, `DeliveryOrderLines`, `SalesOrders`, `SalesOrderLines`, `Customers`, `ProductVariants`, `Products`, `Warehouses`); `IReportExporter`, `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign`; `PagedResult<T>`; `IWarehouseService.GetAllAsync()`; `ICustomerService` (customer dropdown — verify method, e.g. `GetAllAsync()`); `DeliveryOrderStatus.Posted`.
- Produces:
  - `record SalesFilter(DateTime? From, DateTime? To, string? Channel, int? WarehouseId, int? CustomerId, string? CashierUserId, string? Search)`
  - `record SalesFactRow(DateTime Date, string Channel, string DocNumber, int WarehouseId, string WarehouseName, int VariantId, string Sku, string ProductName, int? CategoryId, string Party, int Quantity, decimal Revenue, decimal Cogs)` with `decimal GrossProfit => Revenue - Cogs;`
  - `record SalesSummaryDto(int Lines, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent)`
  - `class SalesFactProvider(AppDbContext db)` with `Task<List<SalesFactRow>> GetAsync(SalesFilter f, CancellationToken ct = default)`
  - `interface ISalesReportService`:
    - `Task<PagedResult<SalesFactRow>> GetSalesPagedAsync(SalesFilter f, int page, int pageSize, CancellationToken ct = default)`
    - `Task<SalesSummaryDto> GetSalesSummaryAsync(SalesFilter f, CancellationToken ct = default)`
    - `Task<ReportDocument> BuildSalesReportAsync(SalesFilter f, CancellationToken ct = default)`

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/Reports/SalesReportDtos.cs`:
```csharp
namespace ErpOne.Application.Reports;

public record SalesFilter(
    DateTime? From, DateTime? To, string? Channel, int? WarehouseId,
    int? CustomerId, string? CashierUserId, string? Search);

public record SalesFactRow(
    DateTime Date, string Channel, string DocNumber,
    int WarehouseId, string WarehouseName,
    int VariantId, string Sku, string ProductName, int? CategoryId,
    string Party, int Quantity, decimal Revenue, decimal Cogs)
{
    public decimal GrossProfit => Revenue - Cogs;
}

public record SalesSummaryDto(
    int Lines, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/Reports/ISalesReportService.cs`:
```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface ISalesReportService
{
    Task<PagedResult<SalesFactRow>> GetSalesPagedAsync(
        SalesFilter f, int page, int pageSize, CancellationToken ct = default);

    Task<SalesSummaryDto> GetSalesSummaryAsync(SalesFilter f, CancellationToken ct = default);

    Task<ReportDocument> BuildSalesReportAsync(SalesFilter f, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing test**

First confirm master service method names used by the page/tests: run
`grep -n "Task" src/ErpOne.Application/Customers/ICustomerService.cs` and note the read-all method returning items with `.Id`/`.Name` (expected `GetAllAsync()`). The test below only uses `ISalesReportService` + real POS/DO services, so it does not depend on `ICustomerService`.

Create `tests/ErpOne.IntegrationTests/SalesReportServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Reports;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.DeliveryOrders;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seed a product/variant/warehouse/customer with stock on hand (via opening) so sales can draw stock.
    private static async Task<(int variant, int wh, int customer)> SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var cust = new Customer($"CU{id}", $"Cust {id}", null, null, null, null, null, 0m, "IDR", true);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Customers.Add(cust); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        await db.SaveChangesAsync();

        // Opening stock 100 @ 1000 so both POS and B2B can issue.
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);
        return (variant.Id, wh.Id, cust.Id);
    }

    [Fact]
    public async Task Pos_sale_appears_with_revenue_and_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh, _) = await SeedAsync(sp);

        // Open a shift and record one POS sale of 2 @ 2000 (cost 1000).
        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shifts.OpenAsync(new OpenShiftRequest(wh, 0m));
        var pos = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var pm = db.PaymentMethods.First();
        await pos.CreateAsync(new CreatePosSaleRequest(
            shift.Id, pm.Id, null, 0m,
            [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var filter = new SalesFilter(null, null, "POS", wh, null, null, null);
        var summary = await svc.GetSalesSummaryAsync(filter);

        Assert.Equal(4000m, summary.Revenue);   // 2 * 2000
        Assert.Equal(2000m, summary.Cogs);       // 2 * 1000
        Assert.Equal(2000m, summary.GrossProfit);
        Assert.True(summary.Qty >= 2);
    }

    [Fact]
    public async Task Channel_filter_excludes_other_channel()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh, _) = await SeedAsync(sp);

        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shifts.OpenAsync(new OpenShiftRequest(wh, 0m));
        var pos = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var pm = db.PaymentMethods.First();
        await pos.CreateAsync(new CreatePosSaleRequest(
            shift.Id, pm.Id, null, 0m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var b2b = await svc.GetSalesSummaryAsync(new SalesFilter(null, null, "B2B", wh, null, null, null));

        Assert.Equal(0, b2b.Lines);  // only a POS sale exists
    }

    [Fact]
    public async Task Paged_returns_pos_row_fields()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh, _) = await SeedAsync(sp);

        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shifts.OpenAsync(new OpenShiftRequest(wh, 0m));
        var pos = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var pm = db.PaymentMethods.First();
        await pos.CreateAsync(new CreatePosSaleRequest(
            shift.Id, pm.Id, null, 0m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var svc = sp.GetRequiredService<ISalesReportService>();
        var page = await svc.GetSalesPagedAsync(new SalesFilter(null, null, null, wh, null, null, null), 1, 50);

        var row = Assert.Single(page.Items);
        Assert.Equal("POS", row.Channel);
        Assert.Equal(2000m, row.Revenue);
        Assert.Equal(1000m, row.Cogs);
        Assert.Equal(1000m, row.GrossProfit);
    }
}
```
Note: the exact constructor signatures for `Warehouse`, `Customer`, `Product.AddVariant`, `OpenShiftRequest`, `CreatePosSaleRequest`, and `PosSaleLineRequest` must match the current code. Verify against `PosSaleServiceTests.cs` and `CashierShiftServiceTests.cs` and adjust argument lists if they differ — do NOT invent parameters. If `Customer`'s constructor differs, copy the shape used in `CustomerServiceTests.cs`.

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~SalesReportServiceTests"`
Expected: FAIL — `ISalesReportService` not registered (or compile error until Step 5–7 done).

- [ ] **Step 5: Implement `SalesFactProvider`**

Create `src/ErpOne.Infrastructure/Services/SalesFactProvider.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

/// <summary>Builds the normalized POS+B2B "sales fact" list. Shared by Sales and Gross Profit reports.</summary>
public class SalesFactProvider(AppDbContext db)
{
    public async Task<List<SalesFactRow>> GetAsync(SalesFilter f, CancellationToken ct = default)
    {
        var rows = new List<SalesFactRow>();
        var wantPos = f.Channel is null || f.Channel == "POS";
        var wantB2b = f.Channel is null || f.Channel == "B2B";
        var toExclusive = f.To?.Date.AddDays(1);

        if (wantPos)
        {
            var q =
                from l in db.PosSaleLines.AsNoTracking()
                join s in db.PosSales.AsNoTracking() on l.PosSaleId equals s.Id
                join v in db.ProductVariants.AsNoTracking() on l.ProductVariantId equals v.Id
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
                select new { l, s, v, p, w };

            if (f.From is DateTime from) q = q.Where(x => x.s.SaleDate >= from);
            if (toExclusive is DateTime toEx) q = q.Where(x => x.s.SaleDate < toEx);
            if (f.WarehouseId is int wid) q = q.Where(x => x.s.WarehouseId == wid);
            if (!string.IsNullOrWhiteSpace(f.CashierUserId)) q = q.Where(x => x.s.CashierUserId == f.CashierUserId);
            if (f.CustomerId is not null) q = q.Where(_ => false); // POS has no customer link
            if (!string.IsNullOrWhiteSpace(f.Search))
                q = q.Where(x => x.v.Sku.Contains(f.Search) || x.p.Name.Contains(f.Search));

            rows.AddRange(await q.Select(x => new SalesFactRow(
                x.s.SaleDate, "POS", x.s.SaleNumber, x.w.Id, x.w.Name,
                x.v.Id, x.v.Sku, x.p.Name, x.p.CategoryId,
                x.s.CashierName, x.l.Quantity, x.l.LineTotal, x.l.UnitCost * x.l.Quantity))
                .ToListAsync(ct));
        }

        if (wantB2b)
        {
            var q =
                from dol in db.DeliveryOrderLines.AsNoTracking()
                join dObj in db.DeliveryOrders.AsNoTracking() on dol.DeliveryOrderId equals dObj.Id
                join so in db.SalesOrders.AsNoTracking() on dObj.SalesOrderId equals so.Id
                join sol in db.SalesOrderLines.AsNoTracking() on dol.SalesOrderLineId equals sol.Id
                join c in db.Customers.AsNoTracking() on so.CustomerId equals c.Id
                join v in db.ProductVariants.AsNoTracking() on dol.ProductVariantId equals v.Id
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                join w in db.Warehouses.AsNoTracking() on so.WarehouseId equals w.Id
                where dObj.Status == DeliveryOrderStatus.Posted
                select new { dol, dObj, so, sol, c, v, p, w };

            if (f.From is DateTime from) q = q.Where(x => x.dObj.DeliveryDate >= from);
            if (toExclusive is DateTime toEx) q = q.Where(x => x.dObj.DeliveryDate < toEx);
            if (f.WarehouseId is int wid) q = q.Where(x => x.so.WarehouseId == wid);
            if (f.CustomerId is int cid) q = q.Where(x => x.so.CustomerId == cid);
            if (!string.IsNullOrWhiteSpace(f.CashierUserId)) q = q.Where(_ => false); // B2B has no cashier
            if (!string.IsNullOrWhiteSpace(f.Search))
                q = q.Where(x => x.v.Sku.Contains(f.Search) || x.p.Name.Contains(f.Search));

            rows.AddRange(await q.Select(x => new SalesFactRow(
                x.dObj.DeliveryDate, "B2B", x.dObj.DoNumber, x.w.Id, x.w.Name,
                x.v.Id, x.v.Sku, x.p.Name, x.p.CategoryId,
                x.c.Name, x.dol.QuantityDelivered,
                x.sol.Quantity == 0 ? 0m : (x.sol.LineSubtotal - x.sol.LineDiscount) / x.sol.Quantity * x.dol.QuantityDelivered,
                x.dol.UnitCost * x.dol.QuantityDelivered))
                .ToListAsync(ct));
        }

        return rows;
    }
}
```
Note: verify `Customer` exposes `.Name` (adjust if it is `CompanyName` etc.). If EF cannot translate the B2B revenue division inside `Select`, compute revenue in a second pass in memory after materializing the raw fields (project the raw columns, then map to `SalesFactRow` with the arithmetic in LINQ-to-Objects).

- [ ] **Step 6: Implement `SalesReportService`**

Create `src/ErpOne.Infrastructure/Services/SalesReportService.cs`:
```csharp
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
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
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
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All sales" : string.Join("  ·  ", parts);
    }
}
```

- [ ] **Step 7: Register services + add the 3 report menu resources**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — under the existing report registrations add:
```csharp
services.AddScoped<SalesFactProvider>();
services.AddScoped<ISalesReportService, SalesReportService>();
```

Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — extend the `Reports` group (added in 4a) with three resources:
```csharp
        new("Reports",
        [
            new("reports.stock-ledger", "Stock Ledger", "bi-journal-text", ReportActions),
            new("reports.inventory-valuation", "Inventory Valuation", "bi-cash-stack", ReportActions),
            new("reports.sales", "Sales Report", "bi-graph-up-arrow", ReportActions),
            new("reports.purchases", "Purchase Report", "bi-cart-check", ReportActions),
            new("reports.gross-profit", "Gross Profit", "bi-coin", ReportActions),
        ]),
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~SalesReportServiceTests"`
Expected: PASS (3 tests). If a POS/DO seeding helper signature was wrong, fix it against the real service tests and re-run.

- [ ] **Step 9: Create the Sales report page**

First confirm the customer dropdown API: `grep -n "Task" src/ErpOne.Application/Customers/ICustomerService.cs` and the `CustomerDto` shape. Adjust `@using`/type/method names below if they differ.

Create `src/ErpOne.Web/Components/Pages/Reports/Sales/SalesReportIndex.razor`:
```razor
@page "/reports/sales"
@attribute [Authorize(Policy = "reports.sales.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.Reports
@using ErpOne.Application.Warehouses
@using ErpOne.Application.Customers
@using Microsoft.JSInterop
@inject ISalesReportService Sales
@inject IWarehouseService WarehouseService
@inject ICustomerService CustomerService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Sales Report</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Sales</span></nav>
            <h1>Sales Report</h1>
            <p>Combined POS &amp; B2B sales with cost and gross profit.</p>
        </div>
        <AuthorizeView Policy="reports.sales.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_summary is not null)
    {
        <div class="kpis">
            <div class="kpi accent"><div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div><div class="kpi-tx"><div class="v">Rp @_summary.Revenue.ToString("N0")</div><div class="l">Revenue</div></div></div>
            <div class="kpi"><div class="ic ic-amb"><i class="bi bi-box-seam"></i></div><div class="kpi-tx"><div class="v">Rp @_summary.Cogs.ToString("N0")</div><div class="l">COGS</div></div></div>
            <div class="kpi"><div class="ic ic-blu"><i class="bi bi-graph-up-arrow"></i></div><div class="kpi-tx"><div class="v">Rp @_summary.GrossProfit.ToString("N0")</div><div class="l">Gross profit</div></div></div>
            <div class="kpi"><div class="ic ic-grn"><i class="bi bi-percent"></i></div><div class="kpi-tx"><div class="v">@_summary.MarginPercent.ToString("N1")%</div><div class="l">Margin</div></div></div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
        <select @bind="_channel" @bind:after="ReloadAsync">
            <option value="">All channels</option>
            <option value="POS">POS</option>
            <option value="B2B">B2B</option>
        </select>
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
        <select @bind="_customerId" @bind:after="ReloadAsync">
            <option value="0">All customers</option>
            @foreach (var c in _customers) { <option value="@c.Id">@c.Name</option> }
        </select>
        <div class="search">
            <i class="bi bi-search"></i>
            <input placeholder="Search SKU or product…" @bind="_search" @bind:event="oninput" @onkeyup="ReloadAsync" />
        </div>
    </div>

    @if (_page is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_page.Total == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-graph-up-arrow"></i></div><p>No sales match your filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="card-top"><span class="n">Showing <b>@((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total)</b> of <b>@_page.Total.ToString("N0")</b></span></div>
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:100px">Date</th>
                            <th style="width:70px">Channel</th>
                            <th style="width:130px">Doc</th>
                            <th>Product</th>
                            <th>Party</th>
                            <th class="r" style="width:70px">Qty</th>
                            <th class="r" style="width:120px">Revenue</th>
                            <th class="r" style="width:120px">COGS</th>
                            <th class="r" style="width:120px">Gross Profit</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var r in _page.Items)
                        {
                            <tr>
                                <td class="mono">@r.Date.ToString("yyyy-MM-dd")</td>
                                <td>@r.Channel</td>
                                <td class="code">@r.DocNumber</td>
                                <td class="nm">@r.Sku — @r.ProductName</td>
                                <td>@r.Party</td>
                                <td class="r mono">@r.Quantity.ToString("N0")</td>
                                <td class="r mono">@r.Revenue.ToString("N2")</td>
                                <td class="r mono">@r.Cogs.ToString("N2")</td>
                                <td class="r mono">@r.GrossProfit.ToString("N2")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            @if (_page.TotalPages > 1)
            {
                <div class="card-foot"><Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" /></div>
            }
        </div>
    }
</div>

@code {
    private const int PageSize = 20;
    private PagedResult<SalesFactRow>? _page;
    private SalesSummaryDto? _summary;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private IReadOnlyList<CustomerDto> _customers = [];
    private int _currentPage = 1;
    private DateTime? _from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _to = DateTime.Today;
    private string? _channel;
    private int _warehouseId;
    private int _customerId;
    private string? _search;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        _customers = await CustomerService.GetAllAsync();
        await LoadAsync();
    }

    private SalesFilter Filter() => new(
        _from, _to,
        string.IsNullOrEmpty(_channel) ? null : _channel,
        _warehouseId == 0 ? null : _warehouseId,
        _customerId == 0 ? null : _customerId,
        null, _search);

    private async Task LoadAsync()
    {
        var f = Filter();
        _summary = await Sales.GetSalesSummaryAsync(f);
        _page = await Sales.GetSalesPagedAsync(f, _currentPage, PageSize);
    }

    private async Task ReloadAsync() { _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }

    private async Task ExportExcel()
    {
        var doc = await Sales.BuildSalesReportAsync(Filter());
        await DownloadAsync(Exporter.ToExcel(doc), "sales-report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Sales.BuildSalesReportAsync(Filter());
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "sales-report.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 10: Build, then stage for manual commit**

Run: `dotnet build ErpOne.slnx` (expect 0 errors), then run the full suite `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj` (expect all pass), then:
```bash
git add src/ErpOne.Application/Reports/SalesReportDtos.cs src/ErpOne.Application/Reports/ISalesReportService.cs \
  src/ErpOne.Infrastructure/Services/SalesFactProvider.cs src/ErpOne.Infrastructure/Services/SalesReportService.cs \
  src/ErpOne.Infrastructure/DependencyInjection.cs src/ErpOne.Web/Authorization/AppMenus.cs \
  src/ErpOne.Web/Components/Pages/Reports/Sales tests/ErpOne.IntegrationTests/SalesReportServiceTests.cs
```
Report to the user that Task 1 is staged and ready to commit (suggested message: `feat: Sales report — combined POS+B2B sales fact + service + page + Excel/PDF export`). Do not commit.

---

## Task 2: Purchase Report

**Files:** see File Structure (Task 2).

**Interfaces:**
- Consumes: `AppDbContext` (`GoodsReceipts`, `GoodsReceiptLines`, `PurchaseOrders`, `Suppliers`, `ProductVariants`, `Products`, `Warehouses`); `GoodsReceiptStatus.Posted`; `PagedResult<T>`; `IReportExporter`; `ISupplierService`/`IWarehouseService` for dropdowns.
- Produces:
  - `record PurchaseFilter(DateTime? From, DateTime? To, int? SupplierId, int? WarehouseId, string? Search)`
  - `record PurchaseRowDto(DateTime Date, string GrnNumber, int SupplierId, string SupplierName, int WarehouseId, string WarehouseName, int VariantId, string Sku, string ProductName, int Quantity, decimal UnitCost, decimal Value)`
  - `record PurchaseSummaryDto(int Lines, int Qty, decimal TotalCost, int Receipts)`
  - `interface IPurchaseReportService`:
    - `Task<PagedResult<PurchaseRowDto>> GetPurchasesPagedAsync(PurchaseFilter f, int page, int pageSize, CancellationToken ct = default)`
    - `Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(PurchaseFilter f, CancellationToken ct = default)`
    - `Task<ReportDocument> BuildPurchaseReportAsync(PurchaseFilter f, CancellationToken ct = default)`

- [ ] **Step 1: Confirm GRN warehouse + status source**

The warehouse of a GRN comes from its PO (`PurchaseOrder.WarehouseId`) — GRN has no own warehouse field. Run `grep -nE 'WarehouseId|public' src/ErpOne.Domain/Entities/PurchaseOrder.cs | head` and `grep -n "" src/ErpOne.Domain/Entities/GoodsReceiptStatus.cs` to confirm `PurchaseOrder.WarehouseId`, `PurchaseOrder.SupplierId`, and `GoodsReceiptStatus.Posted`. Use these exact names in Step 4.

- [ ] **Step 2: Create the DTOs**

Create `src/ErpOne.Application/Reports/PurchaseReportDtos.cs`:
```csharp
namespace ErpOne.Application.Reports;

public record PurchaseFilter(
    DateTime? From, DateTime? To, int? SupplierId, int? WarehouseId, string? Search);

public record PurchaseRowDto(
    DateTime Date, string GrnNumber, int SupplierId, string SupplierName,
    int WarehouseId, string WarehouseName, int VariantId, string Sku, string ProductName,
    int Quantity, decimal UnitCost, decimal Value);

public record PurchaseSummaryDto(int Lines, int Qty, decimal TotalCost, int Receipts);
```

- [ ] **Step 3: Create the service interface**

Create `src/ErpOne.Application/Reports/IPurchaseReportService.cs`:
```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface IPurchaseReportService
{
    Task<PagedResult<PurchaseRowDto>> GetPurchasesPagedAsync(
        PurchaseFilter f, int page, int pageSize, CancellationToken ct = default);

    Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(PurchaseFilter f, CancellationToken ct = default);

    Task<ReportDocument> BuildPurchaseReportAsync(PurchaseFilter f, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the failing test**

Create `tests/ErpOne.IntegrationTests/PurchaseReportServiceTests.cs`. Reuse the GRN seeding pattern from `GoodsReceiptServiceTests.cs` (the `SeedMastersAsync` + `ConfirmedPoAsync` helpers) to create a Posted GRN, then assert the report:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PurchaseReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var sup = new Supplier($"SP{id}", $"PT GRN {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(sup); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    private static async Task PostGrnAsync(IServiceProvider sp, int sup, int wh, int variant, int qty, decimal cost)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var po = await poSvc.CreateAsync(new CreatePurchaseOrderRequest(
            sup, wh, new DateTime(2026, 7, 1), null, "po",
            [new PurchaseOrderLineRequest(variant, qty, cost, 0m, null)]));
        await poSvc.SubmitAsync(po.Id);
        po = (await poSvc.GetByIdAsync(po.Id))!;
        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 7, 1), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, qty, cost)]));
        await grnSvc.PostAsync(grn.Id);
    }

    [Fact]
    public async Task Posted_grn_appears_in_report_with_value()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await PostGrnAsync(sp, sup, wh, variant, 5, 1000m);

        var svc = sp.GetRequiredService<IPurchaseReportService>();
        var summary = await svc.GetPurchaseSummaryAsync(new PurchaseFilter(null, null, sup, null, null));

        Assert.Equal(5, summary.Qty);
        Assert.Equal(5000m, summary.TotalCost);
        Assert.Equal(1, summary.Receipts);

        var page = await svc.GetPurchasesPagedAsync(new PurchaseFilter(null, null, sup, null, null), 1, 50);
        var row = Assert.Single(page.Items);
        Assert.Equal(5000m, row.Value);
        Assert.Equal(sup, row.SupplierId);
    }

    [Fact]
    public async Task Supplier_filter_excludes_other_suppliers()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (supA, whA, varA) = await SeedMastersAsync(sp);
        var (supB, whB, varB) = await SeedMastersAsync(sp);
        await PostGrnAsync(sp, supA, whA, varA, 2, 500m);
        await PostGrnAsync(sp, supB, whB, varB, 3, 700m);

        var svc = sp.GetRequiredService<IPurchaseReportService>();
        var onlyA = await svc.GetPurchasesPagedAsync(new PurchaseFilter(null, null, supA, null, null), 1, 50);

        Assert.All(onlyA.Items, r => Assert.Equal(supA, r.SupplierId));
    }
}
```
Note: verify `Supplier`/`Warehouse`/`Product.AddVariant` constructor argument lists against `GoodsReceiptServiceTests.cs` (they were copied from there) and the PO/GRN request records; adjust if the code has changed.

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~PurchaseReportServiceTests"`
Expected: FAIL — `IPurchaseReportService` not registered.

- [ ] **Step 6: Implement `PurchaseReportService`**

Create `src/ErpOne.Infrastructure/Services/PurchaseReportService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PurchaseReportService(AppDbContext db) : IPurchaseReportService
{
    private IQueryable<GrnJoin> FilteredQuery(PurchaseFilter f)
    {
        var q =
            from gl in db.GoodsReceiptLines.AsNoTracking()
            join g in db.GoodsReceipts.AsNoTracking() on gl.GoodsReceiptId equals g.Id
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            join sup in db.Suppliers.AsNoTracking() on po.SupplierId equals sup.Id
            join v in db.ProductVariants.AsNoTracking() on gl.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on po.WarehouseId equals w.Id
            where g.Status == GoodsReceiptStatus.Posted
            select new GrnJoin { GL = gl, G = g, Sup = sup, V = v, P = p, W = w };

        if (f.From is DateTime from) q = q.Where(x => x.G.ReceiptDate >= from);
        if (f.To is DateTime to) q = q.Where(x => x.G.ReceiptDate < to.Date.AddDays(1));
        if (f.SupplierId is int sid) q = q.Where(x => x.Sup.Id == sid);
        if (f.WarehouseId is int wid) q = q.Where(x => x.W.Id == wid);
        if (!string.IsNullOrWhiteSpace(f.Search))
            q = q.Where(x => x.V.Sku.Contains(f.Search) || x.P.Name.Contains(f.Search));
        return q;
    }

    public async Task<PagedResult<PurchaseRowDto>> GetPurchasesPagedAsync(
        PurchaseFilter f, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200_000);
        var q = FilteredQuery(f);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.G.ReceiptDate).ThenBy(x => x.G.GrnNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PurchaseRowDto(
                x.G.ReceiptDate, x.G.GrnNumber, x.Sup.Id, x.Sup.Name, x.W.Id, x.W.Name,
                x.V.Id, x.V.Sku, x.P.Name, x.GL.QuantityReceived, x.GL.UnitCost,
                x.GL.UnitCost * x.GL.QuantityReceived))
            .ToListAsync(ct);
        return new PagedResult<PurchaseRowDto>(items, total, page, pageSize);
    }

    public async Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(PurchaseFilter f, CancellationToken ct = default)
    {
        var q = FilteredQuery(f);
        var lines = await q.CountAsync(ct);
        var qty = await q.SumAsync(x => (int?)x.GL.QuantityReceived, ct) ?? 0;
        var totalCost = await q.SumAsync(x => (decimal?)(x.GL.UnitCost * x.GL.QuantityReceived), ct) ?? 0m;
        var receipts = await q.Select(x => x.G.Id).Distinct().CountAsync(ct);
        return new PurchaseSummaryDto(lines, qty, totalCost, receipts);
    }

    public async Task<ReportDocument> BuildPurchaseReportAsync(PurchaseFilter f, CancellationToken ct = default)
    {
        var all = await GetPurchasesPagedAsync(f, 1, 200_000, ct);
        var rows = all.Items.Select(r => new ReportRow
        {
            Cells = [r.Date, r.GrnNumber, r.SupplierName, r.WarehouseName, r.Sku, r.ProductName,
                     r.Quantity, r.UnitCost, r.Value]
        }).ToList();

        return new ReportDocument
        {
            Title = "Purchase Report",
            FilterSummary = FilterSummary(f),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("GRN"),
                new ReportColumn("Supplier"),
                new ReportColumn("Warehouse"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Value", ReportAlign.Right, "N2"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow
            {
                IsGrandTotal = true,
                Cells = ["Total", "", "", "", "", "", all.Items.Sum(r => r.Quantity), "", all.Items.Sum(r => r.Value)]
            },
        };
    }

    private static string FilterSummary(PurchaseFilter f)
    {
        var parts = new List<string>();
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All purchases (posted GRN)" : string.Join("  ·  ", parts);
    }

    private sealed class GrnJoin
    {
        public required GoodsReceiptLine GL { get; init; }
        public required GoodsReceipt G { get; init; }
        public required Supplier Sup { get; init; }
        public required ProductVariant V { get; init; }
        public required Product P { get; init; }
        public required Warehouse W { get; init; }
    }
}
```

- [ ] **Step 7: Register the service**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — add:
```csharp
services.AddScoped<IPurchaseReportService, PurchaseReportService>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~PurchaseReportServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 9: Create the Purchase report page**

Confirm the supplier dropdown API: `grep -n "Task" src/ErpOne.Application/Suppliers/ISupplierService.cs` and `SupplierDto` shape. Adjust below if names differ.

Create `src/ErpOne.Web/Components/Pages/Reports/Purchases/PurchaseReportIndex.razor`:
```razor
@page "/reports/purchases"
@attribute [Authorize(Policy = "reports.purchases.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.Reports
@using ErpOne.Application.Warehouses
@using ErpOne.Application.Suppliers
@using Microsoft.JSInterop
@inject IPurchaseReportService Purchases
@inject IWarehouseService WarehouseService
@inject ISupplierService SupplierService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Purchase Report</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Purchases</span></nav>
            <h1>Purchase Report</h1>
            <p>Goods received (posted GRN) by supplier and product.</p>
        </div>
        <AuthorizeView Policy="reports.purchases.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_summary is not null)
    {
        <div class="kpis">
            <div class="kpi accent"><div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div><div class="kpi-tx"><div class="v">Rp @_summary.TotalCost.ToString("N0")</div><div class="l">Total purchases</div></div></div>
            <div class="kpi"><div class="ic ic-blu"><i class="bi bi-stack"></i></div><div class="kpi-tx"><div class="v">@_summary.Qty.ToString("N0")</div><div class="l">Total qty</div></div></div>
            <div class="kpi"><div class="ic ic-amb"><i class="bi bi-receipt"></i></div><div class="kpi-tx"><div class="v">@_summary.Receipts.ToString("N0")</div><div class="l">Receipts</div></div></div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
        <select @bind="_supplierId" @bind:after="ReloadAsync">
            <option value="0">All suppliers</option>
            @foreach (var s in _suppliers) { <option value="@s.Id">@s.Name</option> }
        </select>
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
        <div class="search">
            <i class="bi bi-search"></i>
            <input placeholder="Search SKU or product…" @bind="_search" @bind:event="oninput" @onkeyup="ReloadAsync" />
        </div>
    </div>

    @if (_page is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_page.Total == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cart-check"></i></div><p>No purchases match your filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="card-top"><span class="n">Showing <b>@((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total)</b> of <b>@_page.Total.ToString("N0")</b></span></div>
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:100px">Date</th>
                            <th style="width:140px">GRN</th>
                            <th>Supplier</th>
                            <th>Product</th>
                            <th>Warehouse</th>
                            <th class="r" style="width:80px">Qty</th>
                            <th class="r" style="width:120px">Unit Cost</th>
                            <th class="r" style="width:130px">Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var r in _page.Items)
                        {
                            <tr>
                                <td class="mono">@r.Date.ToString("yyyy-MM-dd")</td>
                                <td class="code">@r.GrnNumber</td>
                                <td>@r.SupplierName</td>
                                <td class="nm">@r.Sku — @r.ProductName</td>
                                <td class="code">@r.WarehouseName</td>
                                <td class="r mono">@r.Quantity.ToString("N0")</td>
                                <td class="r mono">@r.UnitCost.ToString("N2")</td>
                                <td class="r mono">@r.Value.ToString("N2")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            @if (_page.TotalPages > 1)
            {
                <div class="card-foot"><Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" /></div>
            }
        </div>
    }
</div>

@code {
    private const int PageSize = 20;
    private PagedResult<PurchaseRowDto>? _page;
    private PurchaseSummaryDto? _summary;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private IReadOnlyList<SupplierDto> _suppliers = [];
    private int _currentPage = 1;
    private DateTime? _from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _to = DateTime.Today;
    private int _supplierId;
    private int _warehouseId;
    private string? _search;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        _suppliers = await SupplierService.GetAllAsync();
        await LoadAsync();
    }

    private PurchaseFilter Filter() => new(
        _from, _to,
        _supplierId == 0 ? null : _supplierId,
        _warehouseId == 0 ? null : _warehouseId,
        _search);

    private async Task LoadAsync()
    {
        var f = Filter();
        _summary = await Purchases.GetPurchaseSummaryAsync(f);
        _page = await Purchases.GetPurchasesPagedAsync(f, _currentPage, PageSize);
    }

    private async Task ReloadAsync() { _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }

    private async Task ExportExcel()
    {
        var doc = await Purchases.BuildPurchaseReportAsync(Filter());
        await DownloadAsync(Exporter.ToExcel(doc), "purchase-report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Purchases.BuildPurchaseReportAsync(Filter());
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "purchase-report.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 10: Build, then stage for manual commit**

`dotnet build ErpOne.slnx` (0 errors) → full test suite (all pass) → `git add` the Task 2 files. Report Task 2 staged (suggested message: `feat: Purchase report — posted-GRN service + page + Excel/PDF export`). Do not commit.

---

## Task 3: Gross Profit Report

**Files:** see File Structure (Task 3).

**Interfaces:**
- Consumes: `SalesFactProvider` (Task 1); `IReportExporter`; `SalesFilter`/`SalesFactRow` (Task 1).
- Produces:
  - `enum GrossProfitGroupBy { Product, Category, Month }`
  - `record GrossProfitFilter(DateTime? From, DateTime? To, string? Channel, GrossProfitGroupBy GroupBy)`
  - `record GrossProfitGroupDto(string GroupName, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent)`
  - `record GrossProfitResultDto(GrossProfitGroupBy GroupBy, IReadOnlyList<GrossProfitGroupDto> Groups, int TotalQty, decimal TotalRevenue, decimal TotalCogs, decimal TotalGrossProfit, decimal TotalMarginPercent)`
  - `interface IGrossProfitReportService`:
    - `Task<GrossProfitResultDto> GetGrossProfitAsync(GrossProfitFilter f, CancellationToken ct = default)`
    - `Task<ReportDocument> BuildGrossProfitReportAsync(GrossProfitFilter f, CancellationToken ct = default)`

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/Reports/GrossProfitDtos.cs`:
```csharp
namespace ErpOne.Application.Reports;

public enum GrossProfitGroupBy { Product, Category, Month }

public record GrossProfitFilter(DateTime? From, DateTime? To, string? Channel, GrossProfitGroupBy GroupBy);

public record GrossProfitGroupDto(
    string GroupName, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent);

public record GrossProfitResultDto(
    GrossProfitGroupBy GroupBy, IReadOnlyList<GrossProfitGroupDto> Groups,
    int TotalQty, decimal TotalRevenue, decimal TotalCogs, decimal TotalGrossProfit, decimal TotalMarginPercent);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/Reports/IGrossProfitReportService.cs`:
```csharp
namespace ErpOne.Application.Reports;

public interface IGrossProfitReportService
{
    Task<GrossProfitResultDto> GetGrossProfitAsync(GrossProfitFilter f, CancellationToken ct = default);
    Task<ReportDocument> BuildGrossProfitReportAsync(GrossProfitFilter f, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/ErpOne.IntegrationTests/GrossProfitReportServiceTests.cs`. Reuse the POS seeding approach from `SalesReportServiceTests` (Task 1) to create one POS sale, then assert gross profit and the cross-report invariant:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class GrossProfitReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public GrossProfitReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> SeedPosSaleAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);

        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(new OpenShiftRequest(wh.Id, 0m));
        var pm = db.PaymentMethods.First();
        await sp.GetRequiredService<IPosSaleService>().CreateAsync(new CreatePosSaleRequest(
            shift.Id, pm.Id, null, 0m, [new PosSaleLineRequest(variant.Id, 3, 2000m, 0m)]));
        return wh.Id;
    }

    [Fact]
    public async Task GrossProfit_by_product_matches_revenue_minus_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedPosSaleAsync(sp);

        var svc = sp.GetRequiredService<IGrossProfitReportService>();
        var result = await svc.GetGrossProfitAsync(
            new GrossProfitFilter(null, null, "POS", GrossProfitGroupBy.Product));

        // 3 @ 2000 revenue = 6000; cogs 3 @ 1000 = 3000; GP = 3000; margin 50%.
        Assert.Equal(6000m, result.TotalRevenue);
        Assert.Equal(3000m, result.TotalCogs);
        Assert.Equal(3000m, result.TotalGrossProfit);
        Assert.Equal(50m, result.TotalMarginPercent);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task GrossProfit_total_matches_sales_summary()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await SeedPosSaleAsync(sp);

        var gp = await sp.GetRequiredService<IGrossProfitReportService>()
            .GetGrossProfitAsync(new GrossProfitFilter(null, null, "POS", GrossProfitGroupBy.Category));
        var sales = await sp.GetRequiredService<ISalesReportService>()
            .GetSalesSummaryAsync(new SalesFilter(null, null, "POS", null, null, null, null));

        Assert.Equal(sales.GrossProfit, gp.TotalGrossProfit);
        Assert.Equal(sales.Revenue, gp.TotalRevenue);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~GrossProfitReportServiceTests"`
Expected: FAIL — `IGrossProfitReportService` not registered.

- [ ] **Step 5: Implement `GrossProfitReportService`**

Create `src/ErpOne.Infrastructure/Services/GrossProfitReportService.cs`:
```csharp
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
            GrossProfitGroupBy.Month => r.Date.ToString("yyyy-MM"),
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
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        return parts.Count == 0 ? "All sales" : string.Join("  ·  ", parts);
    }
}
```

- [ ] **Step 6: Register the service**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — add:
```csharp
services.AddScoped<IGrossProfitReportService, GrossProfitReportService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~GrossProfitReportServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 8: Create the Gross Profit page**

Create `src/ErpOne.Web/Components/Pages/Reports/GrossProfit/GrossProfitIndex.razor`:
```razor
@page "/reports/gross-profit"
@attribute [Authorize(Policy = "reports.gross-profit.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using Microsoft.JSInterop
@inject IGrossProfitReportService GrossProfit
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Gross Profit</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Gross Profit</span></nav>
            <h1>Gross Profit</h1>
            <p>Revenue − COGS with margin, grouped by product, category, or month.</p>
        </div>
        <AuthorizeView Policy="reports.gross-profit.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_result is not null)
    {
        <div class="kpis">
            <div class="kpi accent"><div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div><div class="kpi-tx"><div class="v">Rp @_result.TotalRevenue.ToString("N0")</div><div class="l">Revenue</div></div></div>
            <div class="kpi"><div class="ic ic-amb"><i class="bi bi-box-seam"></i></div><div class="kpi-tx"><div class="v">Rp @_result.TotalCogs.ToString("N0")</div><div class="l">COGS</div></div></div>
            <div class="kpi"><div class="ic ic-blu"><i class="bi bi-coin"></i></div><div class="kpi-tx"><div class="v">Rp @_result.TotalGrossProfit.ToString("N0")</div><div class="l">Gross profit</div></div></div>
            <div class="kpi"><div class="ic ic-grn"><i class="bi bi-percent"></i></div><div class="kpi-tx"><div class="v">@_result.TotalMarginPercent.ToString("N1")%</div><div class="l">Margin</div></div></div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
        <select @bind="_channel" @bind:after="ReloadAsync">
            <option value="">All channels</option>
            <option value="POS">POS</option>
            <option value="B2B">B2B</option>
        </select>
        <select @bind="_groupBy" @bind:after="ReloadAsync">
            <option value="@GrossProfitGroupBy.Product">Group by Product</option>
            <option value="@GrossProfitGroupBy.Category">Group by Category</option>
            <option value="@GrossProfitGroupBy.Month">Group by Month</option>
        </select>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.Groups.Count == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-coin"></i></div><p>No sales to analyze for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>@GroupHeader</th>
                            <th class="r" style="width:90px">Qty</th>
                            <th class="r" style="width:140px">Revenue</th>
                            <th class="r" style="width:140px">COGS</th>
                            <th class="r" style="width:140px">Gross Profit</th>
                            <th class="r" style="width:100px">Margin %</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var g in _result.Groups)
                        {
                            <tr>
                                <td class="nm">@g.GroupName</td>
                                <td class="r mono">@g.Qty.ToString("N0")</td>
                                <td class="r mono">@g.Revenue.ToString("N2")</td>
                                <td class="r mono">@g.Cogs.ToString("N2")</td>
                                <td class="r mono">@g.GrossProfit.ToString("N2")</td>
                                <td class="r mono">@g.MarginPercent.ToString("N1")%</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold">
                            <td>Total</td>
                            <td class="r mono">@_result.TotalQty.ToString("N0")</td>
                            <td class="r mono">@_result.TotalRevenue.ToString("N2")</td>
                            <td class="r mono">@_result.TotalCogs.ToString("N2")</td>
                            <td class="r mono">@_result.TotalGrossProfit.ToString("N2")</td>
                            <td class="r mono">@_result.TotalMarginPercent.ToString("N1")%</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private GrossProfitResultDto? _result;
    private DateTime? _from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _to = DateTime.Today;
    private string? _channel;
    private GrossProfitGroupBy _groupBy = GrossProfitGroupBy.Product;

    private string GroupHeader => _groupBy switch
    {
        GrossProfitGroupBy.Product => "Product",
        GrossProfitGroupBy.Category => "Category",
        GrossProfitGroupBy.Month => "Month",
        _ => "Group",
    };

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        _result = await GrossProfit.GetGrossProfitAsync(new GrossProfitFilter(
            _from, _to, string.IsNullOrEmpty(_channel) ? null : _channel, _groupBy));

    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await GrossProfit.BuildGrossProfitReportAsync(new GrossProfitFilter(_from, _to, string.IsNullOrEmpty(_channel) ? null : _channel, _groupBy));
        await DownloadAsync(Exporter.ToExcel(doc), "gross-profit.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await GrossProfit.BuildGrossProfitReportAsync(new GrossProfitFilter(_from, _to, string.IsNullOrEmpty(_channel) ? null : _channel, _groupBy));
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "gross-profit.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 9: Build + full test suite, then stage for manual commit**

`dotnet build ErpOne.slnx` (0 errors) → `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj` (all pass, incl. the new report classes) → `git add` the Task 3 files. Report Task 3 staged (suggested message: `feat: Gross Profit report — aggregated by product/category/month, reuses SalesFactProvider`). Do not commit.

---

## Verification checklist (drive the app after merge)

- Sales report shows POS and B2B rows; channel/warehouse/customer/date filters narrow results; KPIs (revenue/COGS/GP/margin) update; export reflects filters.
- Purchase report lists posted GRNs; supplier/warehouse/date filters work; totals correct.
- Gross Profit totals equal the Sales report GP for the same filter; group-by Product/Category/Month all render; margin % correct.
- Excel + PDF downloads produce valid files for all three reports.

## Post-implementation manual steps (user)

- Restart app + sign out/in so `BootstrapSeeder` grants `reports.sales`, `reports.purchases`, `reports.gross-profit` to the admin role.
- Reports group now lists five entries (Stock Ledger, Inventory Valuation, Sales, Purchases, Gross Profit).
