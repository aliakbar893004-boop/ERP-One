# Fase 4d — Cashier Shift Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Laporan read-only rekap shift kasir yang sudah ditutup dalam rentang tanggal, dikelompokkan per kasir dengan rincian per metode pembayaran + rekonsiliasi kas (expected/counted/variance), subtotal per kasir + grand total, dan export Excel/PDF.

**Architecture:** Satu service `ICashierShiftReportService` (Application) + implementasi (Infrastructure) yang query `CashierShift` (Status=Closed, Include Totals) dalam rentang → project ke DTO group-by-cashier → resolve nama warehouse/metode via dictionary. Satu halaman Blazor memanggil `GetShiftReportAsync` (tampilan) + `GetCashiersAsync` (dropdown) + `BuildShiftReportAsync` (export). Mengikuti pola `AgingReportService` / `InventoryValuationReportService`.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core (`AppDbContext`), xUnit integration tests (SQLite `EnsureCreated` via `CustomWebApplicationFactory`). Solution: `ErpOne.slnx`. Export via existing `IReportExporter`.

## Global Constraints

- Solution file `ErpOne.slnx`. Build/test: `dotnet test ErpOne.slnx`.
- Service = query read-only; **TIDAK ada entity/migration baru**. Reuse `ReportDocument`/`IReportExporter`.
- `from`/`to` parameter eksplisit; service TIDAK baca `DateTime.Today`. Rentang: `OpenedAt >= from.Date && OpenedAt < to.Date.AddDays(1)`.
- Hanya shift `Status == CashierShiftStatus.Closed`.
- Service file `namespace ErpOne.Infrastructure.Services;` (walau di folder `Services/Reports`), konsisten report service lain.
- Commit MANUAL oleh user — langkah "Commit" hanya penanda batas task; JANGAN `git commit`/`merge`/`push`. Boleh `git add`. Git identity repo-local `aliakbar893004-boop`.
- Nilai enum & tipe yang dipakai (sudah dikonfirmasi): `CashierShiftStatus.Closed`, `PaymentType.Tunai`, entity `CashierShift` (`ShiftNumber/WarehouseId/CashierUserId/CashierName/Status/OpenedAt/OpeningFloat/CashSalesTotal/ClosedAt/CountedCash/CashVariance`, computed `ExpectedCash/TotalSalesAmount/TransactionCount`, nav `Totals`), `CashierShiftTotal(PaymentMethodId/TotalAmount/TransactionCount)`.

---

## File Structure

- Create `src/ErpOne.Application/Reports/CashierShiftReportDtos.cs` — DTO.
- Create `src/ErpOne.Application/Reports/ICashierShiftReportService.cs` — interface.
- Create `src/ErpOne.Infrastructure/Services/Reports/CashierShiftReportService.cs` — implementasi.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar service (setelah `IAgingReportService`).
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — resource `reports.cashier-shifts` (setelah `reports.ap-aging`).
- Create `src/ErpOne.Web/Components/Pages/Reports/CashierShift/CashierShiftReportIndex.razor` → `/reports/cashier-shifts`.
- Create `tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs`.

---

## Task 1: DTOs + service interface

**Files:**
- Create: `src/ErpOne.Application/Reports/CashierShiftReportDtos.cs`
- Create: `src/ErpOne.Application/Reports/ICashierShiftReportService.cs`

**Interfaces:**
- Produces: `ShiftMethodDto`, `ShiftRowDto`, `ShiftCashierDto`, `ShiftReportResultDto`, `CashierOptionDto`, dan `ICashierShiftReportService` (3 metode). Dipakai identik di Task 3 (impl), Task 2/4 (test), Task 6 (page).
- Consumes: `ReportDocument` (`ErpOne.Application.Reports`).

- [ ] **Step 1: Buat file DTO**

Create `src/ErpOne.Application/Reports/CashierShiftReportDtos.cs`:

```csharp
namespace ErpOne.Application.Reports;

public record ShiftMethodDto(int PaymentMethodId, string PaymentMethodName, decimal Amount, int TransactionCount);

public record ShiftRowDto(
    int ShiftId, string ShiftNumber, int WarehouseId, string WarehouseName,
    DateTime OpenedAt, DateTime? ClosedAt,
    decimal OpeningFloat, decimal CashSales, decimal TotalSales, int TransactionCount,
    decimal ExpectedCash, decimal? CountedCash, decimal? CashVariance,
    IReadOnlyList<ShiftMethodDto> Methods);

public record ShiftCashierDto(
    string CashierUserId, string CashierName, IReadOnlyList<ShiftRowDto> Shifts,
    decimal TotalSales, int TransactionCount, decimal TotalVariance);

public record ShiftReportResultDto(
    DateTime From, DateTime To, IReadOnlyList<ShiftCashierDto> Cashiers,
    decimal GrandTotalSales, int GrandTransactionCount, decimal GrandVariance,
    int ShiftCount, int CashierCount);

public record CashierOptionDto(string UserId, string Name);
```

- [ ] **Step 2: Buat interface**

Create `src/ErpOne.Application/Reports/ICashierShiftReportService.cs`:

```csharp
namespace ErpOne.Application.Reports;

public interface ICashierShiftReportService
{
    Task<ShiftReportResultDto> GetShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<ReportDocument> BuildShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CashierOptionDto>> GetCashiersAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Application/ErpOne.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Application/Reports/CashierShiftReportDtos.cs src/ErpOne.Application/Reports/ICashierShiftReportService.cs
```

---

## Task 2: Failing test — grouping, method breakdown, variance, exclude open

**Files:**
- Create: `tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs`

**Interfaces:**
- Consumes: `ICashierShiftReportService.GetShiftReportAsync`, `IStockService.RecordOpeningAsync`, `ICashierShiftService.OpenAsync/CloseAsync`, `IPosSaleService.CreateSaleAsync`. Pola seed dari `DashboardServiceTests`.
- Produces: helper `SeedMastersAsync` + test `Groups_by_cashier_with_method_breakdown_and_variance`.

- [ ] **Step 1: Tulis test yang gagal**

Create `tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs`:

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

public class CashierShiftReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CashierShiftReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Uid() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // warehouse + product/variant (price 2000) + opening stock 100@1000 + cash & transfer payment methods.
    private static async Task<(int wh, int variant, int cashPm, int transferPm)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var cash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        var transfer = new PaymentMethod($"TRF{id}", "Transfer", PaymentType.Transfer, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(cash); db.PaymentMethods.Add(transfer);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 1000m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);
        return (wh.Id, variant.Id, cash.Id, transfer.Id);
    }

    [Fact]
    public async Task Groups_by_cashier_with_method_breakdown_and_variance()
    {
        var day = new DateTime(2026, 7, 10);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variant, cashPm, transferPm) = await SeedMastersAsync(sp);

        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var pos = sp.GetRequiredService<IPosSaleService>();

        // Cashier Rani: open (float 100000), 1 cash sale (2×2000=4000) + 1 transfer sale (1×2000=2000), close counted 103500 → var -500.
        var rani = Uid();
        var raniShift = await shifts.OpenAsync(rani, "Rani", new OpenShiftRequest(wh, 100_000m));
        await pos.CreateSaleAsync(rani, "Rani", raniShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await pos.CreateSaleAsync(rani, "Rani", raniShift.Id,
            new CreatePosSaleRequest(transferPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(raniShift.Id, rani, new CloseShiftRequest(103_500m, null)); // expected 104000 → var -500

        // Cashier Budi: open (float 50000), 1 cash sale (1×2000), close counted 52000 → var 0.
        var budi = Uid();
        var budiShift = await shifts.OpenAsync(budi, "Budi", new OpenShiftRequest(wh, 50_000m));
        await pos.CreateSaleAsync(budi, "Budi", budiShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(budiShift.Id, budi, new CloseShiftRequest(52_000m, null)); // expected 52000 → var 0

        // An OPEN shift (never closed) must be excluded.
        await shifts.OpenAsync(Uid(), "Ghost", new OpenShiftRequest(wh, 0m));

        var svc = sp.GetRequiredService<ICashierShiftReportService>();
        var r = await svc.GetShiftReportAsync(day, day, null, null);

        Assert.Equal(2, r.CashierCount);
        Assert.Equal(2, r.ShiftCount);                      // 2 closed; open excluded
        Assert.Equal(8_000m, r.GrandTotalSales);            // 6000 + 2000
        Assert.Equal(3, r.GrandTransactionCount);           // 2 + 1
        Assert.Equal(-500m, r.GrandVariance);               // -500 + 0

        var raniC = Assert.Single(r.Cashiers, c => c.CashierName == "Rani");
        Assert.Equal(6_000m, raniC.TotalSales);
        Assert.Equal(-500m, raniC.TotalVariance);
        var raniS = Assert.Single(raniC.Shifts);
        Assert.Equal(104_000m, raniS.ExpectedCash);
        Assert.Equal(103_500m, raniS.CountedCash);
        Assert.Equal(-500m, raniS.CashVariance);
        Assert.Equal(4_000m, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Tunai").Amount);
        Assert.Equal(1, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Tunai").TransactionCount);
        Assert.Equal(2_000m, Assert.Single(raniS.Methods, m => m.PaymentMethodName == "Transfer").Amount);
        // Methods ordered by amount desc → Tunai first.
        Assert.Equal("Tunai", raniS.Methods[0].PaymentMethodName);
    }
}
```

- [ ] **Step 2: Jalankan test — pastikan gagal**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~CashierShiftReportServiceTests"`
Expected: FAIL — `ICashierShiftReportService` belum terdaftar / tak ada implementasi.

---

## Task 3: Service implementation + DI registration

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Reports/CashierShiftReportService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `AppDbContext` (`CashierShifts` + nav `Totals`, `Warehouses`, `PaymentMethods`), enum `CashierShiftStatus`, `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign`.
- Produces: `CashierShiftReportService : ICashierShiftReportService` yang memenuhi Task 2 & 4.

- [ ] **Step 1: Tulis implementasi**

Create `src/ErpOne.Infrastructure/Services/Reports/CashierShiftReportService.cs`:

```csharp
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
```

- [ ] **Step 2: Daftarkan di DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — tambah tepat setelah baris `services.AddScoped<IAgingReportService, AgingReportService>();`:

```csharp
        services.AddScoped<ICashierShiftReportService, CashierShiftReportService>();
```

- [ ] **Step 3: Jalankan test Task 2 — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~CashierShiftReportServiceTests"`
Expected: PASS (`Groups_by_cashier_with_method_breakdown_and_variance`).

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Infrastructure/Services/Reports/CashierShiftReportService.cs src/ErpOne.Infrastructure/DependencyInjection.cs
```

---

## Task 4: Tests — warehouse & cashier filters + GetCashiers

**Files:**
- Modify: `tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs`

**Interfaces:**
- Consumes: `GetShiftReportAsync` (filter args), `GetCashiersAsync`.
- Produces: test `Cashier_filter_and_get_cashiers`.

- [ ] **Step 1: Tulis test filter**

Tambahkan ke `CashierShiftReportServiceTests.cs` (sebelum `}` penutup kelas):

```csharp
    [Fact]
    public async Task Cashier_filter_and_get_cashiers()
    {
        var day = new DateTime(2026, 7, 11);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variant, cashPm, _) = await SeedMastersAsync(sp);
        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var pos = sp.GetRequiredService<IPosSaleService>();

        var ani = Uid();
        var aniShift = await shifts.OpenAsync(ani, "Ani", new OpenShiftRequest(wh, 0m));
        await pos.CreateSaleAsync(ani, "Ani", aniShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));
        await shifts.CloseAsync(aniShift.Id, ani, new CloseShiftRequest(2000m, null));

        var edo = Uid();
        var edoShift = await shifts.OpenAsync(edo, "Edo", new OpenShiftRequest(wh, 0m));
        await pos.CreateSaleAsync(edo, "Edo", edoShift.Id,
            new CreatePosSaleRequest(cashPm, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await shifts.CloseAsync(edoShift.Id, edo, new CloseShiftRequest(4000m, null));

        var svc = sp.GetRequiredService<ICashierShiftReportService>();

        var onlyEdo = await svc.GetShiftReportAsync(day, day, null, edo);
        Assert.Single(onlyEdo.Cashiers);
        Assert.Equal("Edo", onlyEdo.Cashiers[0].CashierName);
        Assert.Equal(4000m, onlyEdo.GrandTotalSales);

        var cashiers = await svc.GetCashiersAsync();
        Assert.Contains(cashiers, c => c.UserId == ani && c.Name == "Ani");
        Assert.Contains(cashiers, c => c.UserId == edo && c.Name == "Edo");
    }
```

- [ ] **Step 2: Jalankan semua test — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~CashierShiftReportServiceTests"`
Expected: PASS semua (2 test).

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs
```

---

## Task 5: Menu resource `reports.cashier-shifts`

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (grup Reports, setelah `reports.ap-aging`)

**Interfaces:**
- Consumes: `ReportActions` (= `[ActIndex, ActExport]`).
- Produces: permission `reports.cashier-shifts.index`/`.export` (auto ke `AllPermissions`, di-seed admin). Route `/reports/cashier-shifts` via konvensi key→href.

- [ ] **Step 1: Tambah resource**

Modify grup Reports di `AppMenus.cs` — tambah baris setelah `reports.ap-aging`:

```csharp
            new("reports.ap-aging", "AP Aging", "bi-hourglass-bottom", ReportActions),
            new("reports.cashier-shifts", "Cashier Shifts", "bi-cash-stack", ReportActions),
        ]),
```

- [ ] **Step 2: Build Web project**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 6: Cashier Shift report page + full suite + verify

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Reports/CashierShift/CashierShiftReportIndex.razor`

**Interfaces:**
- Consumes: `ICashierShiftReportService` (`GetShiftReportAsync`/`GetCashiersAsync`/`BuildShiftReportAsync`), `ShiftReportResultDto`/`CashierOptionDto`, `IWarehouseService.GetAllAsync()`→`WarehouseDto` (ns `ErpOne.Application.Warehouses`), `IReportExporter`, `IJSRuntime` (`saveAsFile`).
- Produces: halaman route `/reports/cashier-shifts`.

- [ ] **Step 1: Tulis halaman**

Create `src/ErpOne.Web/Components/Pages/Reports/CashierShift/CashierShiftReportIndex.razor`:

```razor
@page "/reports/cashier-shifts"
@attribute [Authorize(Policy = "reports.cashier-shifts.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using ErpOne.Application.Warehouses
@using Microsoft.JSInterop
@inject ICashierShiftReportService Report
@inject IWarehouseService WarehouseService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Cashier Shift Report</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Cashier Shifts</span>
            </nav>
            <h1>Cashier Shift Report</h1>
            <p>Closed cashier shifts by cashier, with per-method breakdown and cash reconciliation.</p>
        </div>
        <AuthorizeView Policy="reports.cashier-shifts.export">
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
            <div class="kpi accent">
                <div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div>
                <div class="kpi-tx"><div class="v">Rp @_result.GrandTotalSales.ToString("N0")</div><div class="l">Total sales</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-amb"><i class="bi bi-scale"></i></div>
                <div class="kpi-tx"><div class="v">Rp @_result.GrandVariance.ToString("N0")</div><div class="l">Cash variance</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-clock-history"></i></div>
                <div class="kpi-tx"><div class="v">@_result.ShiftCount.ToString("N0")</div><div class="l">Shifts</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-receipt"></i></div>
                <div class="kpi-tx"><div class="v">@_result.GrandTransactionCount.ToString("N0")</div><div class="l">Transactions</div></div>
            </div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
        <select @bind="_cashierUserId" @bind:after="ReloadAsync">
            <option value="">All cashiers</option>
            @foreach (var c in _cashiers) { <option value="@c.UserId">@c.Name</option> }
        </select>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.ShiftCount == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cash-stack"></i></div><p>No closed shifts for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>Shift / Method</th>
                            <th style="width:140px">Opened</th><th style="width:140px">Closed</th>
                            <th>Warehouse</th>
                            <th class="r" style="width:110px">Opening</th>
                            <th class="r" style="width:120px">Sales</th>
                            <th class="r" style="width:70px">Txns</th>
                            <th class="r" style="width:120px">Expected</th>
                            <th class="r" style="width:120px">Counted</th>
                            <th class="r" style="width:110px">Variance</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var c in _result.Cashiers)
                        {
                            <tr class="fw-bold table-light"><td colspan="10">@c.CashierName (@c.CashierUserId)</td></tr>
                            @foreach (var s in c.Shifts)
                            {
                                <tr>
                                    <td class="code mono">@s.ShiftNumber</td>
                                    <td class="mono">@s.OpenedAt.ToString("yyyy-MM-dd HH:mm")</td>
                                    <td class="mono">@(s.ClosedAt?.ToString("yyyy-MM-dd HH:mm"))</td>
                                    <td>@s.WarehouseName</td>
                                    <td class="r mono">@s.OpeningFloat.ToString("N0")</td>
                                    <td class="r mono">@s.TotalSales.ToString("N0")</td>
                                    <td class="r mono">@s.TransactionCount</td>
                                    <td class="r mono">@s.ExpectedCash.ToString("N0")</td>
                                    <td class="r mono">@(s.CountedCash?.ToString("N0"))</td>
                                    <td class="r mono">@(s.CashVariance?.ToString("N0"))</td>
                                </tr>
                                @foreach (var m in s.Methods)
                                {
                                    <tr class="text-muted">
                                        <td class="ps-4">@m.PaymentMethodName</td>
                                        <td colspan="4"></td>
                                        <td class="r mono">@m.Amount.ToString("N0")</td>
                                        <td class="r mono">@m.TransactionCount</td>
                                        <td colspan="3"></td>
                                    </tr>
                                }
                            }
                            <tr class="fw-bold">
                                <td colspan="5">@c.CashierName subtotal</td>
                                <td class="r mono">@c.TotalSales.ToString("N0")</td>
                                <td class="r mono">@c.TransactionCount</td>
                                <td colspan="2"></td>
                                <td class="r mono">@c.TotalVariance.ToString("N0")</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold">
                            <td colspan="5">Grand total</td>
                            <td class="r mono">@_result.GrandTotalSales.ToString("N0")</td>
                            <td class="r mono">@_result.GrandTransactionCount</td>
                            <td colspan="2"></td>
                            <td class="r mono">@_result.GrandVariance.ToString("N0")</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private ShiftReportResultDto? _result;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private IReadOnlyList<CashierOptionDto> _cashiers = [];
    private DateTime _from = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _to = DateTime.Today;
    private int _warehouseId;
    private string _cashierUserId = "";

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        _cashiers = await Report.GetCashiersAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() => _result = await Report.GetShiftReportAsync(
        _from, _to, _warehouseId == 0 ? null : _warehouseId,
        string.IsNullOrEmpty(_cashierUserId) ? null : _cashierUserId);

    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Report.BuildShiftReportAsync(_from, _to, _warehouseId == 0 ? null : _warehouseId,
            string.IsNullOrEmpty(_cashierUserId) ? null : _cashierUserId);
        await DownloadAsync(Exporter.ToExcel(doc), "cashier-shifts.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Report.BuildShiftReportAsync(_from, _to, _warehouseId == 0 ? null : _warehouseId,
            string.IsNullOrEmpty(_cashierUserId) ? null : _cashierUserId);
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "cashier-shifts.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 2: Build + jalankan SELURUH test suite**

Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS (289 sebelumnya + 2 test cashier-shift baru = 291). Tak ada test yang meng-assert jumlah menu resource.

- [ ] **Step 3: Verifikasi manual (skill `run`/`verify`)**

Jalankan app, sign out/in (agar admin dapat `reports.cashier-shifts.*`), buka `/reports/cashier-shifts`. Cek: KPI, tabel per kasir dgn baris shift + baris metode indented + subtotal + grand total, ganti rentang tanggal & filter warehouse/cashier, Export Excel/PDF berisi kolom + Grand total. (Smoke-launch headless: route harus 302→login seperti report lain.)

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Reports/CashierShift/CashierShiftReportIndex.razor
```

---

## Self-Review (untuk penulis plan)

**Spec coverage:**
- Grouped by cashier + subtotal + grand total → Task 3 `GetShiftReportAsync` group-by + Task 6 table. ✓
- Rincian per metode per shift (nested visible rows) → Task 3 `ToRow` Methods + Build method rows + Task 6 inner `@foreach (m in s.Methods)`. ✓
- Closed shifts only + exclude open → Task 3 `Where(Status==Closed)`; diuji Task 2 (Ghost open shift excluded). ✓
- Rekonsiliasi kas (opening/expected/counted/variance) → ShiftRowDto + kolom tabel + export. ✓
- Date range on OpenedAt (`>= from.Date && < to.Date+1`) → Task 3. ✓
- Filter warehouse & cashier → Task 3 Where + Task 4 test + Task 6 dropdown. ✓
- Cashier dropdown via `GetCashiersAsync` (`CashierOptionDto`) → Task 1/3 + Task 4 test + Task 6. ✓
- Export ReportDocument (cashier header + shift rows + method rows + subtotal + grand) → Task 3 `BuildShiftReportAsync`. ✓
- Permission view+export + seeding → Task 5 (`ReportActions`). ✓
- KPI Total Sales/Cash Variance/Shifts/Transactions → Task 6. ✓
- Testing grouping/method/variance/filters → Task 2, 4. ✓

**Placeholder scan:** Tidak ada TBD/TODO. Semua kode service, page, test lengkap.

**Type consistency:** `ShiftReportResultDto`/`ShiftCashierDto`/`ShiftRowDto`/`ShiftMethodDto`/`CashierOptionDto` identik di Task 1/3/6. Metode `GetShiftReportAsync(DateTime, DateTime, int?, string?, CancellationToken)`, `BuildShiftReportAsync(...)`, `GetCashiersAsync(CancellationToken)` konsisten interface↔impl↔test↔page. Properti dipakai seragam: `TotalSales/TransactionCount/CashVariance/ExpectedCash/CountedCash/OpeningFloat/Methods{Amount,TransactionCount,PaymentMethodName}`. Ctor/param yang diverifikasi dari kode: `OpenShiftRequest(WarehouseId, OpeningFloat)`, `CloseShiftRequest(CountedCash, ClosingNote)`, `CreatePosSaleRequest(PaymentMethodId, TaxId?, TransactionDiscount, AmountTendered, Lines)`, `PosSaleLineRequest(ProductVariantId, Quantity, UnitPrice, DiscountPercent)`, `PaymentMethod(code, name, PaymentType, isActive)`, `IStockService.RecordOpeningAsync(variantId, warehouseId, qty, unitCost)`, entity `CashierShift`/`CashierShiftTotal` fields, `CashierShiftStatus.Closed`, `PaymentType.Tunai`.
```
