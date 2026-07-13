# Fase 4a — Inventory Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Reports layer's first two reports — Stock Ledger (list + per-variant drill-down with running balance) and Inventory Valuation (as-of-date, groupable by category or warehouse) — plus the shared Reports foundation (export engine, menu group, permissions).

**Architecture:** A new read-only `Reports` slice. Query services in `ErpOne.Application/Reports` (interfaces) + `ErpOne.Infrastructure/Services` (EF implementations) read from the existing `StockMovement` append-only ledger and `ProductStock`. A neutral `ReportDocument` model + `IReportExporter` (ClosedXML for `.xlsx`, QuestPDF for PDF) turns any report into a downloadable file, delivered to the browser via a JS interop helper. No new entities or migrations.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server; SQLite in tests), Blazor Server (Interactive Server), FluentValidation, ClosedXML, QuestPDF, xUnit.

## Global Constraints

- Target framework `net10.0`; nullable enabled; implicit usings enabled.
- All UI copy in **English** (existing modules are English; some older code/comments are Indonesian — match the file you are in).
- Pages use the global `.pi` list design + `.kpis`/`.kpi` KPI cards + `Pager` component (see `StockLevelIndex.razor`).
- Every page guarded with `@attribute [Authorize(Policy = "<resource>.<action>")]`.
- New permissions must be seeded automatically — they are derived from `AppMenus.AllPermissions`, which `BootstrapSeeder` grants to the admin role. No seeder code change needed; just register the resource in `AppMenus.Groups`.
- Services: constructor-injected `AppDbContext`, `AsNoTracking()`, apply `Where`/`OrderBy` on entity columns **before** projecting to a record DTO (EF cannot translate filters over a record-constructor projection — see `StockService.GetLevelsPagedAsync`).
- Money formatted `N2` (or `N0` for whole rupiah where appropriate), quantities `N0`, dates `yyyy-MM-dd`.
- Commit after each task with the identity already configured for this repo (`aliakbar893004-boop`).
- Build command: `dotnet build ErpOne.sln`. Test command: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj`.

## File Structure

**Task 1 — Reports foundation**
- Create `src/ErpOne.Application/Reports/ReportDocument.cs` — neutral report model (columns/rows/totals).
- Create `src/ErpOne.Application/Reports/IReportExporter.cs` — export interface.
- Create `src/ErpOne.Infrastructure/Services/ReportExporter.cs` — ClosedXML + QuestPDF implementation.
- Modify `src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj` — add ClosedXML + QuestPDF packages.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — register `IReportExporter`.
- Modify `src/ErpOne.Web/Program.cs` — set QuestPDF Community license.
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — add `ActExport` + `Reports` group (2 resources).
- Modify `src/ErpOne.Web/wwwroot/js/app-interop.js` — add `saveAsFile`.
- Test `tests/ErpOne.IntegrationTests/ReportExporterTests.cs`.

**Task 2 — Stock Ledger**
- Create `src/ErpOne.Application/Reports/StockLedgerDtos.cs`.
- Create `src/ErpOne.Application/Reports/IStockLedgerReportService.cs`.
- Create `src/ErpOne.Infrastructure/Services/StockLedgerReportService.cs`.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — register service.
- Create `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockLedgerIndex.razor`.
- Create `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockCard.razor`.
- Test `tests/ErpOne.IntegrationTests/StockLedgerReportServiceTests.cs`.

**Task 3 — Inventory Valuation**
- Create `src/ErpOne.Application/Reports/InventoryValuationDtos.cs`.
- Create `src/ErpOne.Application/Reports/IInventoryValuationReportService.cs`.
- Create `src/ErpOne.Infrastructure/Services/InventoryValuationReportService.cs`.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — register service.
- Create `src/ErpOne.Web/Components/Pages/Reports/InventoryValuation/InventoryValuationIndex.razor`.
- Test `tests/ErpOne.IntegrationTests/InventoryValuationReportServiceTests.cs`.

---

## Task 1: Reports Foundation (export engine + menu + permissions)

**Files:**
- Create: `src/ErpOne.Application/Reports/ReportDocument.cs`
- Create: `src/ErpOne.Application/Reports/IReportExporter.cs`
- Create: `src/ErpOne.Infrastructure/Services/ReportExporter.cs`
- Modify: `src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Modify: `src/ErpOne.Web/Program.cs`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/wwwroot/js/app-interop.js`
- Test: `tests/ErpOne.IntegrationTests/ReportExporterTests.cs`

**Interfaces:**
- Produces:
  - `enum ReportAlign { Left, Right, Center }`
  - `record ReportColumn(string Header, ReportAlign Align = ReportAlign.Left, string? Format = null)`
  - `class ReportRow { IReadOnlyList<object?> Cells; bool IsSubtotal; bool IsGrandTotal; }`
  - `class ReportDocument { string Title; string? Subtitle; string? FilterSummary; DateTime GeneratedAt; IReadOnlyList<ReportColumn> Columns; IReadOnlyList<ReportRow> Rows; ReportRow? TotalsRow; }`
  - `interface IReportExporter { byte[] ToExcel(ReportDocument doc); Task<byte[]> ToPdfAsync(ReportDocument doc, CancellationToken ct = default); }`
- Consumes: `ICompanySettingService.GetAsync(ct)` → `CompanySettingDto(int Id, string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter)`.

- [ ] **Step 1: Add NuGet packages to Infrastructure**

Run:
```bash
cd "F:/4. My Data/Project/MyApplication"
dotnet add src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj package ClosedXML
dotnet add src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj package QuestPDF
```
Expected: both packages added; `ErpOne.Infrastructure.csproj` now has `<PackageReference Include="ClosedXML" .../>` and `<PackageReference Include="QuestPDF" .../>`. If restore fails on version resolution for net10.0, pin a known-good version, e.g. `dotnet add ... package QuestPDF --version 2024.12.3` and `... package ClosedXML --version 0.104.2`.

- [ ] **Step 2: Create the `ReportDocument` model**

Create `src/ErpOne.Application/Reports/ReportDocument.cs`:
```csharp
namespace ErpOne.Application.Reports;

public enum ReportAlign { Left, Right, Center }

/// <summary>One column: header text, alignment, and an optional .NET numeric/date format string (e.g. "N0", "N2", "yyyy-MM-dd").</summary>
public record ReportColumn(string Header, ReportAlign Align = ReportAlign.Left, string? Format = null);

/// <summary>One data row. Cell values are raw (int/decimal/DateTime/string/null); formatting comes from the column.</summary>
public class ReportRow
{
    public IReadOnlyList<object?> Cells { get; init; } = [];
    public bool IsSubtotal { get; init; }
    public bool IsGrandTotal { get; init; }
}

/// <summary>Neutral tabular report model shared by every report and both exporters (Excel + PDF).</summary>
public class ReportDocument
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public string? FilterSummary { get; init; }
    public DateTime GeneratedAt { get; init; }
    public IReadOnlyList<ReportColumn> Columns { get; init; } = [];
    public IReadOnlyList<ReportRow> Rows { get; init; } = [];
    public ReportRow? TotalsRow { get; init; }
}
```

- [ ] **Step 3: Create the `IReportExporter` interface**

Create `src/ErpOne.Application/Reports/IReportExporter.cs`:
```csharp
namespace ErpOne.Application.Reports;

public interface IReportExporter
{
    /// <summary>Render a report to an .xlsx byte array (ClosedXML).</summary>
    byte[] ToExcel(ReportDocument doc);

    /// <summary>Render a report to a PDF byte array (QuestPDF), with a company header from CompanySetting.</summary>
    Task<byte[]> ToPdfAsync(ReportDocument doc, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the failing exporter test**

Create `tests/ErpOne.IntegrationTests/ReportExporterTests.cs`:
```csharp
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ReportExporterTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ReportExporterTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static ReportDocument SampleDoc() => new()
    {
        Title = "Sample Report",
        Subtitle = "Unit test",
        FilterSummary = "All warehouses",
        GeneratedAt = new DateTime(2026, 7, 13, 10, 0, 0),
        Columns =
        [
            new ReportColumn("Name"),
            new ReportColumn("Qty", ReportAlign.Right, "N0"),
            new ReportColumn("Value", ReportAlign.Right, "N2"),
        ],
        Rows =
        [
            new ReportRow { Cells = ["Widget A", 5, 1250.50m] },
            new ReportRow { Cells = ["Widget B", 3, 900m] },
        ],
        TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Total", 8, 2150.50m] },
    };

    [Fact]
    public void ToExcel_writes_header_and_data_cells()
    {
        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IReportExporter>();

        var bytes = exporter.ToExcel(SampleDoc());

        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        // The title is somewhere in column A; the data value "Widget A" must be present.
        var used = ws.RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Sample Report", used);
        Assert.Contains("Widget A", used);
        Assert.Contains("Qty", used);
    }

    [Fact]
    public async Task ToPdf_produces_nonempty_bytes()
    {
        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IReportExporter>();

        var bytes = await exporter.ToPdfAsync(SampleDoc());

        Assert.NotEmpty(bytes);
        // PDF files start with "%PDF".
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~ReportExporterTests"`
Expected: FAIL to compile / resolve — `IReportExporter` not registered (no implementation yet).

- [ ] **Step 6: Implement `ReportExporter`**

Create `src/ErpOne.Infrastructure/Services/ReportExporter.cs`:
```csharp
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ErpOne.Application.CompanySettings;
using ErpOne.Application.Reports;

namespace ErpOne.Infrastructure.Services;

public class ReportExporter(ICompanySettingService companySettings) : IReportExporter
{
    // ── Excel (ClosedXML) ──────────────────────────────────────────────────
    public byte[] ToExcel(ReportDocument doc)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");
        var lastCol = Math.Max(1, doc.Columns.Count);
        var row = 1;

        ws.Cell(row, 1).Value = doc.Title;
        ws.Range(row, 1, row, lastCol).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        if (!string.IsNullOrWhiteSpace(doc.Subtitle))
        {
            ws.Cell(row, 1).Value = doc.Subtitle;
            ws.Range(row, 1, row, lastCol).Merge();
            row++;
        }
        if (!string.IsNullOrWhiteSpace(doc.FilterSummary))
        {
            ws.Cell(row, 1).Value = doc.FilterSummary;
            ws.Range(row, 1, row, lastCol).Merge();
            row++;
        }
        ws.Cell(row, 1).Value = $"Generated: {doc.GeneratedAt:yyyy-MM-dd HH:mm}";
        ws.Range(row, 1, row, lastCol).Merge();
        row += 2;

        // Header
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = doc.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }
        row++;

        // Data + totals
        foreach (var r in doc.Rows) WriteRow(ws, row++, doc.Columns, r);
        if (doc.TotalsRow is not null) WriteRow(ws, row, doc.Columns, doc.TotalsRow);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLWorksheet ws, int row, IReadOnlyList<ReportColumn> cols, ReportRow r)
    {
        for (var c = 0; c < cols.Count && c < r.Cells.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            SetTypedValue(cell, r.Cells[c]);
            var fmt = ExcelNumberFormat(cols[c].Format);
            if (fmt is not null) cell.Style.NumberFormat.Format = fmt;
            cell.Style.Alignment.Horizontal = cols[c].Align switch
            {
                ReportAlign.Right => XLAlignmentHorizontalValues.Right,
                ReportAlign.Center => XLAlignmentHorizontalValues.Center,
                _ => XLAlignmentHorizontalValues.Left,
            };
            if (r.IsSubtotal || r.IsGrandTotal) cell.Style.Font.Bold = true;
        }
    }

    private static void SetTypedValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Value = Blank.Value; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case decimal d: cell.Value = d; break;
            case double db: cell.Value = db; break;
            case DateTime dt: cell.Value = dt; break;
            case bool b: cell.Value = b; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    private static string? ExcelNumberFormat(string? netFormat) => netFormat switch
    {
        "N0" => "#,##0",
        "N2" => "#,##0.00",
        "yyyy-MM-dd" => "yyyy-mm-dd",
        _ => null,
    };

    // ── PDF (QuestPDF) ─────────────────────────────────────────────────────
    public async Task<byte[]> ToPdfAsync(ReportDocument doc, CancellationToken ct = default)
    {
        var company = await companySettings.GetAsync(ct);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.Size(PageSizes.A4.Landscape());
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(company.CompanyName ?? "Company").Bold().FontSize(13);
                    if (!string.IsNullOrWhiteSpace(company.Address))
                        col.Item().Text(company.Address!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).Text(doc.Title).Bold().FontSize(11);
                    if (!string.IsNullOrWhiteSpace(doc.Subtitle))
                        col.Item().Text(doc.Subtitle!).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(doc.FilterSummary))
                        col.Item().Text(doc.FilterSummary!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().Text($"Generated: {doc.GeneratedAt:yyyy-MM-dd HH:mm}").FontSize(7).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        foreach (var _ in doc.Columns) cd.RelativeColumn();
                    });

                    // Header row
                    foreach (var c in doc.Columns)
                        table.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                            .AlignedText(c.Header, c.Align, bold: true);

                    // Data rows
                    foreach (var r in doc.Rows)
                        WritePdfRow(table, doc.Columns, r);

                    if (doc.TotalsRow is not null)
                        WritePdfRow(table, doc.Columns, doc.TotalsRow);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        });

        return pdf.GeneratePdf();
    }

    private static void WritePdfRow(TableDescriptor table, IReadOnlyList<ReportColumn> cols, ReportRow r)
    {
        var bold = r.IsSubtotal || r.IsGrandTotal;
        for (var c = 0; c < cols.Count; c++)
        {
            var value = c < r.Cells.Count ? r.Cells[c] : null;
            var text = FormatCell(value, cols[c].Format);
            var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
            if (r.IsGrandTotal) cell = cell.Background(Colors.Grey.Lighten4);
            cell.AlignedText(text, cols[c].Align, bold);
        }
    }

    private static string FormatCell(object? value, string? format) => value switch
    {
        null => "",
        decimal d when format is not null => d.ToString(format),
        int i when format is not null => i.ToString(format),
        DateTime dt => dt.ToString(format ?? "yyyy-MM-dd"),
        _ => value.ToString() ?? "",
    };
}

// Small helper to keep alignment/bold logic terse in QuestPDF composition.
file static class PdfCellExtensions
{
    public static void AlignedText(this IContainer container, string text, ReportAlign align, bool bold)
    {
        var aligned = align switch
        {
            ReportAlign.Right => container.AlignRight(),
            ReportAlign.Center => container.AlignCenter(),
            _ => container.AlignLeft(),
        };
        var span = aligned.Text(text);
        if (bold) span.Bold();
    }
}
```

Note: if the installed QuestPDF version rejects the `file static` extension against `IContainer` (API drift between versions), inline the alignment directly in `WritePdfRow`/header using `cell.AlignRight().Text(...)`. Keep behavior identical.

- [ ] **Step 7: Register `IReportExporter` in DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`. Add the using with the other `ErpOne.Application.*` usings:
```csharp
using ErpOne.Application.Reports;
```
Add the registration next to `services.AddScoped<IStockService, StockService>();`:
```csharp
services.AddScoped<IReportExporter, ReportExporter>();
```

- [ ] **Step 8: Set QuestPDF Community license in Program.cs**

Modify `src/ErpOne.Web/Program.cs`. Immediately after `var builder = WebApplication.CreateBuilder(args);` (line ~18), add:
```csharp
// QuestPDF Community license (free for orgs with < USD 1M annual revenue).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

- [ ] **Step 9: Run the exporter test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~ReportExporterTests"`
Expected: PASS (2 tests). If PDF test fails to start because the license isn't set inside the test host, add the same `QuestPDF.Settings.License = ...` line at the top of `ReportExporterTests` constructor before generating (the WebApplicationFactory boots `Program`, which sets it, so this is only a fallback).

- [ ] **Step 10: Add `ActExport` and the `Reports` menu group**

Modify `src/ErpOne.Web/Authorization/AppMenus.cs`.

Add the action next to `ActVoid` (line ~18):
```csharp
public static readonly AppAction ActExport = new("export", "Export", "bi-download");
```
Add a `ReportActions` helper next to the other `private static AppAction[] ...` helpers (line ~32):
```csharp
private static AppAction[] ReportActions => [ActIndex, ActExport];
```
Add a new group to `Groups` right after the `Finance` group (before `Settings`):
```csharp
new("Reports",
[
    new("reports.stock-ledger", "Stock Ledger", "bi-journal-text", ReportActions),
    new("reports.inventory-valuation", "Inventory Valuation", "bi-cash-stack", ReportActions),
]),
```

- [ ] **Step 11: Add the `saveAsFile` JS interop helper**

Modify `src/ErpOne.Web/wwwroot/js/app-interop.js`. Append at end of file:
```javascript
// Download a byte[] (passed as base64) as a file. Used by report exports.
window.saveAsFile = (fileName, base64, mimeType) => {
    const link = document.createElement('a');
    link.download = fileName;
    link.href = `data:${mimeType || 'application/octet-stream'};base64,${base64}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
```

- [ ] **Step 12: Build the whole solution**

Run: `dotnet build ErpOne.sln`
Expected: Build succeeded, 0 errors. (Warnings about QuestPDF/ClosedXML analyzers are acceptable.)

- [ ] **Step 13: Commit**

```bash
git add src/ErpOne.Application/Reports src/ErpOne.Infrastructure/Services/ReportExporter.cs \
  src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj src/ErpOne.Infrastructure/DependencyInjection.cs \
  src/ErpOne.Web/Program.cs src/ErpOne.Web/Authorization/AppMenus.cs \
  src/ErpOne.Web/wwwroot/js/app-interop.js tests/ErpOne.IntegrationTests/ReportExporterTests.cs
git commit -m "feat: Reports foundation — ReportDocument + Excel/PDF exporter (ClosedXML+QuestPDF), Reports menu group + export permission"
```

---

## Task 2: Stock Ledger (service + list + drill-down pages)

**Files:**
- Create: `src/ErpOne.Application/Reports/StockLedgerDtos.cs`
- Create: `src/ErpOne.Application/Reports/IStockLedgerReportService.cs`
- Create: `src/ErpOne.Infrastructure/Services/StockLedgerReportService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockLedgerIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockCard.razor`
- Test: `tests/ErpOne.IntegrationTests/StockLedgerReportServiceTests.cs`

**Interfaces:**
- Consumes: `IReportExporter` (Task 1); `MovementType` enum `{ In, Out, Transfer, Adjustment }`; `PagedResult<T>(Items, Total, Page, PageSize)`; `IWarehouseService.GetAllAsync()` → items with `.Id`/`.Name`; `IStockService.RecordOpeningAsync`/`RecordAdjustmentAsync` (used by tests to seed consistent ledger + ProductStock).
- Produces:
  - `record StockLedgerFilter(string? Search, int? WarehouseId, MovementType? Type, DateTime? From, DateTime? To)`
  - `record StockMovementRowDto(int Id, DateTime MovementDate, int VariantId, string Sku, string ProductName, int WarehouseId, string WarehouseName, MovementType Type, int Quantity, decimal UnitCost, string? RefType, int? RefId)`
  - `record StockLedgerSummaryDto(int Records, int TotalIn, int TotalOut, int NetChange)`
  - `record StockCardLineDto(int Id, DateTime MovementDate, MovementType Type, int Quantity, decimal UnitCost, int RunningQty, decimal RunningValue, string? RefType, int? RefId)`
  - `record StockCardDto(int VariantId, string Sku, string ProductName, int? WarehouseId, string WarehouseName, DateTime From, DateTime To, int OpeningQty, decimal OpeningValue, int ClosingQty, decimal ClosingValue, IReadOnlyList<StockCardLineDto> Lines)`
  - `interface IStockLedgerReportService` with:
    - `Task<PagedResult<StockMovementRowDto>> GetMovementsPagedAsync(StockLedgerFilter filter, int page, int pageSize, CancellationToken ct = default)`
    - `Task<StockLedgerSummaryDto> GetSummaryAsync(StockLedgerFilter filter, CancellationToken ct = default)`
    - `Task<StockCardDto?> GetStockCardAsync(int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)`
    - `Task<ReportDocument> BuildMovementsReportAsync(StockLedgerFilter filter, CancellationToken ct = default)`
    - `Task<ReportDocument?> BuildStockCardReportAsync(int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)`

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/Reports/StockLedgerDtos.cs`:
```csharp
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Reports;

public record StockLedgerFilter(
    string? Search, int? WarehouseId, MovementType? Type, DateTime? From, DateTime? To);

public record StockMovementRowDto(
    int Id, DateTime MovementDate, int VariantId, string Sku, string ProductName,
    int WarehouseId, string WarehouseName, MovementType Type, int Quantity, decimal UnitCost,
    string? RefType, int? RefId);

public record StockLedgerSummaryDto(int Records, int TotalIn, int TotalOut, int NetChange);

public record StockCardLineDto(
    int Id, DateTime MovementDate, MovementType Type, int Quantity, decimal UnitCost,
    int RunningQty, decimal RunningValue, string? RefType, int? RefId);

public record StockCardDto(
    int VariantId, string Sku, string ProductName, int? WarehouseId, string WarehouseName,
    DateTime From, DateTime To, int OpeningQty, decimal OpeningValue,
    int ClosingQty, decimal ClosingValue, IReadOnlyList<StockCardLineDto> Lines);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/Reports/IStockLedgerReportService.cs`:
```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.Reports;

public interface IStockLedgerReportService
{
    Task<PagedResult<StockMovementRowDto>> GetMovementsPagedAsync(
        StockLedgerFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<StockLedgerSummaryDto> GetSummaryAsync(StockLedgerFilter filter, CancellationToken ct = default);

    Task<StockCardDto?> GetStockCardAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<ReportDocument> BuildMovementsReportAsync(StockLedgerFilter filter, CancellationToken ct = default);

    Task<ReportDocument?> BuildStockCardReportAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing service test**

Create `tests/ErpOne.IntegrationTests/StockLedgerReportServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockLedgerReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockLedgerReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seeds one product+variant+warehouse, then an opening IN (10 @ 1000) and an
    // adjustment OUT (-4) using the real stock service so StockMovement + ProductStock stay consistent.
    private static async Task<(int variant, int wh)> SeedLedgerAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1500m, null, 0m, null, null, true);
        await db.SaveChangesAsync();

        var stock = sp.GetRequiredService<IStockService>();
        await stock.RecordOpeningAsync(variant.Id, wh.Id, 10, 1000m);          // +10 @ 1000
        await stock.RecordAdjustmentAsync(new StockAdjustmentRequest(
            wh.Id, DateTime.UtcNow, "sell",
            [new StockAdjustmentLine(variant.Id, -4, 0m, "issue")]));           // -4 @ MA(1000)
        return (variant.Id, wh.Id);
    }

    [Fact]
    public async Task StockCard_opening_plus_movements_equals_closing_and_matches_onhand()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();
        var stock = sp.GetRequiredService<IStockService>();

        var card = await svc.GetStockCardAsync(variant, wh, DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date.AddDays(1));

        Assert.NotNull(card);
        Assert.Equal(card!.OpeningQty + card.Lines.Sum(l => l.Quantity), card.ClosingQty);
        Assert.Equal(6, card.ClosingQty); // 10 - 4
        Assert.Equal(await stock.GetOnHandAsync(variant, wh), card.ClosingQty);
        // Running qty of the last line equals closing qty.
        Assert.Equal(card.ClosingQty, card.Lines[^1].RunningQty);
    }

    [Fact]
    public async Task Summary_counts_in_out_and_net()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var summary = await svc.GetSummaryAsync(new StockLedgerFilter(null, wh, null, null, null));

        Assert.Equal(10, summary.TotalIn);
        Assert.Equal(4, summary.TotalOut);
        Assert.Equal(6, summary.NetChange);
        Assert.Equal(2, summary.Records);
    }

    [Fact]
    public async Task MovementsPaged_filters_by_type()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var all = await svc.GetMovementsPagedAsync(new StockLedgerFilter(null, wh, null, null, null), 1, 50);
        var adjustments = await svc.GetMovementsPagedAsync(
            new StockLedgerFilter(null, wh, MovementType.Adjustment, null, null), 1, 50);

        Assert.Equal(2, all.Total);
        Assert.All(adjustments.Items, m => Assert.Equal(MovementType.Adjustment, m.Type));
    }

    [Fact]
    public async Task BuildStockCardReport_returns_document_with_rows()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedLedgerAsync(sp);
        var svc = sp.GetRequiredService<IStockLedgerReportService>();

        var doc = await svc.BuildStockCardReportAsync(variant, wh, DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date.AddDays(1));

        Assert.NotNull(doc);
        Assert.NotEmpty(doc!.Columns);
        Assert.NotEmpty(doc.Rows);
    }
}
```
Note: `RecordOpeningAsync`/`RecordAdjustmentAsync` write `MovementDate` = `DateTime.UtcNow` (opening) and `request.Date` (adjustment). The stock-card date window above spans a year back to tomorrow, so both movements fall inside.

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~StockLedgerReportServiceTests"`
Expected: FAIL — `IStockLedgerReportService` not registered.

- [ ] **Step 5: Implement `StockLedgerReportService`**

Create `src/ErpOne.Infrastructure/Services/StockLedgerReportService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockLedgerReportService(AppDbContext db) : IStockLedgerReportService
{
    // Base joined query with all filters applied on entity columns (before projection).
    private IQueryable<MovementJoin> FilteredQuery(StockLedgerFilter f)
    {
        var q =
            from m in db.StockMovements.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on m.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
            select new MovementJoin { M = m, V = v, P = p, W = w };

        if (f.WarehouseId is int wid) q = q.Where(x => x.M.WarehouseId == wid);
        if (f.Type is MovementType t) q = q.Where(x => x.M.Type == t);
        if (f.From is DateTime from) q = q.Where(x => x.M.MovementDate >= from);
        if (f.To is DateTime to) q = q.Where(x => x.M.MovementDate < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(f.Search))
            q = q.Where(x => x.V.Sku.Contains(f.Search) || x.P.Name.Contains(f.Search));
        return q;
    }

    public async Task<PagedResult<StockMovementRowDto>> GetMovementsPagedAsync(
        StockLedgerFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = FilteredQuery(filter);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.M.MovementDate).ThenByDescending(x => x.M.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockMovementRowDto(
                x.M.Id, x.M.MovementDate, x.V.Id, x.V.Sku, x.P.Name,
                x.W.Id, x.W.Name, x.M.Type, x.M.Quantity, x.M.UnitCost, x.M.RefType, x.M.RefId))
            .ToListAsync(ct);

        return new PagedResult<StockMovementRowDto>(items, total, page, pageSize);
    }

    public async Task<StockLedgerSummaryDto> GetSummaryAsync(StockLedgerFilter filter, CancellationToken ct = default)
    {
        var q = FilteredQuery(filter);
        var records = await q.CountAsync(ct);
        var totalIn = await q.Where(x => x.M.Quantity > 0).SumAsync(x => (int?)x.M.Quantity, ct) ?? 0;
        var totalOut = await q.Where(x => x.M.Quantity < 0).SumAsync(x => (int?)x.M.Quantity, ct) ?? 0; // negative
        return new StockLedgerSummaryDto(records, totalIn, -totalOut, totalIn + totalOut);
    }

    public async Task<StockCardDto?> GetStockCardAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var head = await (
            from v in db.ProductVariants.AsNoTracking()
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            where v.Id == variantId
            select new { v.Id, v.Sku, ProductName = p.Name }).FirstOrDefaultAsync(ct);
        if (head is null) return null;

        var warehouseName = "All warehouses";
        if (warehouseId is int wid)
            warehouseName = await db.Warehouses.AsNoTracking()
                .Where(w => w.Id == wid).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "?";

        var toExclusive = to.Date.AddDays(1);

        IQueryable<StockMovement> baseQ = db.StockMovements.AsNoTracking().Where(m => m.ProductVariantId == variantId);
        if (warehouseId is int w2) baseQ = baseQ.Where(m => m.WarehouseId == w2);

        // Opening = everything strictly before `from`.
        var opening = await baseQ.Where(m => m.MovementDate < from)
            .Select(m => new { m.Quantity, m.UnitCost }).ToListAsync(ct);
        var openingQty = opening.Sum(o => o.Quantity);
        var openingValue = opening.Sum(o => o.Quantity * o.UnitCost);

        // In-range movements, chronological, with running balance.
        var inRange = await baseQ
            .Where(m => m.MovementDate >= from && m.MovementDate < toExclusive)
            .OrderBy(m => m.MovementDate).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.MovementDate, m.Type, m.Quantity, m.UnitCost, m.RefType, m.RefId })
            .ToListAsync(ct);

        var runningQty = openingQty;
        var runningValue = openingValue;
        var lines = new List<StockCardLineDto>(inRange.Count);
        foreach (var m in inRange)
        {
            runningQty += m.Quantity;
            runningValue += m.Quantity * m.UnitCost;
            lines.Add(new StockCardLineDto(
                m.Id, m.MovementDate, m.Type, m.Quantity, m.UnitCost, runningQty, runningValue, m.RefType, m.RefId));
        }

        return new StockCardDto(
            head.Id, head.Sku, head.ProductName, warehouseId, warehouseName,
            from, to, openingQty, openingValue, runningQty, runningValue, lines);
    }

    public async Task<ReportDocument> BuildMovementsReportAsync(StockLedgerFilter filter, CancellationToken ct = default)
    {
        // Export the full filtered set (not paged).
        var all = await GetMovementsPagedAsync(filter, 1, 200_000, ct);
        var rows = all.Items.Select(m => new ReportRow
        {
            Cells =
            [
                m.MovementDate, m.Sku, m.ProductName, m.WarehouseName, m.Type.ToString(),
                m.Quantity, m.UnitCost, Ref(m.RefType, m.RefId),
            ]
        }).ToList();

        return new ReportDocument
        {
            Title = "Stock Ledger",
            FilterSummary = FilterSummary(filter),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Warehouse"),
                new ReportColumn("Type"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Reference"),
            ],
            Rows = rows,
        };
    }

    public async Task<ReportDocument?> BuildStockCardReportAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var card = await GetStockCardAsync(variantId, warehouseId, from, to, ct);
        if (card is null) return null;

        var rows = new List<ReportRow>
        {
            new() { IsSubtotal = true, Cells = ["Opening balance", "", "", card.OpeningQty, "", card.OpeningQty, card.OpeningValue, ""] },
        };
        rows.AddRange(card.Lines.Select(l => new ReportRow
        {
            Cells = [l.MovementDate, l.Type.ToString(), Ref(l.RefType, l.RefId), l.Quantity, l.UnitCost, l.RunningQty, l.RunningValue, ""]
        }));

        return new ReportDocument
        {
            Title = "Stock Card",
            Subtitle = $"{card.Sku} — {card.ProductName}  ·  {card.WarehouseName}",
            FilterSummary = $"Period: {card.From:yyyy-MM-dd} to {card.To:yyyy-MM-dd}",
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Type"),
                new ReportColumn("Reference"),
                new ReportColumn("Qty +/-", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Balance Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Balance Value", ReportAlign.Right, "N2"),
                new ReportColumn(""),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Closing balance", "", "", "", "", card.ClosingQty, card.ClosingValue, ""] },
        };
    }

    private static string Ref(string? refType, int? refId) =>
        refType is null ? "" : refId is null ? refType : $"{refType} #{refId}";

    private static string FilterSummary(StockLedgerFilter f)
    {
        var parts = new List<string>();
        if (f.Type is MovementType t) parts.Add($"Type: {t}");
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All movements" : string.Join("  ·  ", parts);
    }

    // Non-projected join holder so EF can filter on entity columns before Select.
    private sealed class MovementJoin
    {
        public required StockMovement M { get; init; }
        public required ProductVariant V { get; init; }
        public required Product P { get; init; }
        public required Warehouse W { get; init; }
    }
}
```
Note on OUT UnitCost assumption (from spec §2): `RecordAdjustmentAsync` and the GRN/DO/POS flows record OUT movements with `UnitCost = variant.CostPrice` (the moving average at that moment). Therefore `runningValue += Quantity * UnitCost` yields the correct as-of value. If a code path is later found that records OUT with a non-MA cost, revisit `RunningValue`.

- [ ] **Step 6: Register the service in DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`. Under the `IReportExporter` registration add:
```csharp
services.AddScoped<IStockLedgerReportService, StockLedgerReportService>();
```

- [ ] **Step 7: Run the service tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~StockLedgerReportServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 8: Create the Stock Ledger index page**

Create `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockLedgerIndex.razor`:
```razor
@page "/reports/stock-ledger"
@attribute [Authorize(Policy = "reports.stock-ledger.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.Reports
@using ErpOne.Application.Warehouses
@using ErpOne.Domain.Entities
@using Microsoft.JSInterop
@inject IStockLedgerReportService Ledger
@inject IWarehouseService WarehouseService
@inject IReportExporter Exporter
@inject IJSRuntime JS
@inject NavigationManager Nav

<PageTitle>Stock Ledger</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Stock Ledger</span></nav>
            <h1>Stock Ledger</h1>
            <p>All stock movements across products and warehouses.</p>
        </div>
        <AuthorizeView Policy="reports.stock-ledger.export">
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
            <div class="kpi"><div class="ic ic-grn"><i class="bi bi-box-arrow-in-down"></i></div><div class="kpi-tx"><div class="v">@_summary.TotalIn.ToString("N0")</div><div class="l">Total in</div></div></div>
            <div class="kpi"><div class="ic ic-red"><i class="bi bi-box-arrow-up"></i></div><div class="kpi-tx"><div class="v">@_summary.TotalOut.ToString("N0")</div><div class="l">Total out</div></div></div>
            <div class="kpi"><div class="ic ic-blu"><i class="bi bi-arrow-left-right"></i></div><div class="kpi-tx"><div class="v">@_summary.NetChange.ToString("N0")</div><div class="l">Net change</div></div></div>
            <div class="kpi"><div class="ic ic-amb"><i class="bi bi-list-ol"></i></div><div class="kpi-tx"><div class="v">@_summary.Records.ToString("N0")</div><div class="l">Movements</div></div></div>
        </div>
    }

    <div class="toolbar">
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
        <select @bind="_type" @bind:after="ReloadAsync">
            <option value="">All types</option>
            @foreach (var t in Enum.GetValues<MovementType>()) { <option value="@t">@t</option> }
        </select>
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
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
        <div class="empty"><div class="empty-ic"><i class="bi bi-journal-text"></i></div><p>No movements match your filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="card-top"><span class="n">Showing <b>@((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total)</b> of <b>@_page.Total.ToString("N0")</b></span></div>
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:110px">Date</th>
                            <th style="width:150px">SKU</th>
                            <th>Product</th>
                            <th>Warehouse</th>
                            <th style="width:110px">Type</th>
                            <th class="r" style="width:90px">Qty</th>
                            <th class="r" style="width:120px">Unit Cost</th>
                            <th style="width:140px">Reference</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var m in _page.Items)
                        {
                            <tr style="cursor:pointer" @onclick="() => OpenCard(m)">
                                <td class="mono">@m.MovementDate.ToString("yyyy-MM-dd")</td>
                                <td class="code mono">@m.Sku</td>
                                <td class="nm">@m.ProductName</td>
                                <td class="code">@m.WarehouseName</td>
                                <td>@m.Type</td>
                                <td class="r mono @(m.Quantity < 0 ? "lowstock" : "")">@m.Quantity.ToString("N0")</td>
                                <td class="r mono">@m.UnitCost.ToString("N2")</td>
                                <td class="code">@RefLabel(m)</td>
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
    private PagedResult<StockMovementRowDto>? _page;
    private StockLedgerSummaryDto? _summary;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private int _currentPage = 1;
    private int _warehouseId;
    private string? _type;
    private DateTime? _from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _to = DateTime.Today;
    private string? _search;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        await LoadAsync();
    }

    private StockLedgerFilter Filter() => new(
        _search,
        _warehouseId == 0 ? null : _warehouseId,
        string.IsNullOrEmpty(_type) ? null : Enum.Parse<MovementType>(_type),
        _from, _to);

    private async Task LoadAsync()
    {
        var f = Filter();
        _summary = await Ledger.GetSummaryAsync(f);
        _page = await Ledger.GetMovementsPagedAsync(f, _currentPage, PageSize);
    }

    private async Task ReloadAsync() { _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }

    private void OpenCard(StockMovementRowDto m)
    {
        var from = (_from ?? DateTime.Today.AddMonths(-1)).ToString("yyyy-MM-dd");
        var to = (_to ?? DateTime.Today).ToString("yyyy-MM-dd");
        var wh = _warehouseId == 0 ? "" : $"&warehouse={_warehouseId}";
        Nav.NavigateTo($"/reports/stock-ledger/{m.VariantId}?from={from}&to={to}{wh}");
    }

    private static string RefLabel(StockMovementRowDto m) =>
        m.RefType is null ? "" : m.RefId is null ? m.RefType : $"{m.RefType} #{m.RefId}";

    private async Task ExportExcel()
    {
        var doc = await Ledger.BuildMovementsReportAsync(Filter());
        await DownloadAsync(Exporter.ToExcel(doc), "stock-ledger.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Ledger.BuildMovementsReportAsync(Filter());
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "stock-ledger.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 9: Create the Stock Card drill-down page**

Create `src/ErpOne.Web/Components/Pages/Reports/StockLedger/StockCard.razor`:
```razor
@page "/reports/stock-ledger/{VariantId:int}"
@attribute [Authorize(Policy = "reports.stock-ledger.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using Microsoft.JSInterop
@inject IStockLedgerReportService Ledger
@inject IReportExporter Exporter
@inject IJSRuntime JS
@inject NavigationManager Nav

<PageTitle>Stock Card</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><a href="/reports/stock-ledger">Stock Ledger</a><span class="sep">·</span><span class="here">Stock Card</span></nav>
            <h1>Stock Card</h1>
            @if (_card is not null) { <p>@_card.Sku — @_card.ProductName · @_card.WarehouseName</p> }
        </div>
        <div class="pi-actions">
            <a class="btn btn-outline-secondary" href="/reports/stock-ledger"><i class="bi bi-arrow-left"></i> Back</a>
            @if (_card is not null)
            {
                <AuthorizeView Policy="reports.stock-ledger.export">
                    <Authorized>
                        <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                        <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                    </Authorized>
                </AuthorizeView>
            }
        </div>
    </div>

    @if (_notFound)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-question-circle"></i></div><p>Variant not found.</p></div>
    }
    else if (_card is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else
    {
        <div class="card">
            <div class="card-top"><span class="n">Period <b>@_card.From.ToString("yyyy-MM-dd")</b> to <b>@_card.To.ToString("yyyy-MM-dd")</b></span></div>
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:110px">Date</th>
                            <th style="width:120px">Type</th>
                            <th style="width:150px">Reference</th>
                            <th class="r" style="width:90px">Qty +/-</th>
                            <th class="r" style="width:120px">Unit Cost</th>
                            <th class="r" style="width:120px">Balance Qty</th>
                            <th class="r" style="width:140px">Balance Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr class="fw-bold">
                            <td colspan="3">Opening balance</td>
                            <td></td><td></td>
                            <td class="r mono">@_card.OpeningQty.ToString("N0")</td>
                            <td class="r mono">@_card.OpeningValue.ToString("N2")</td>
                        </tr>
                        @foreach (var l in _card.Lines)
                        {
                            <tr>
                                <td class="mono">@l.MovementDate.ToString("yyyy-MM-dd")</td>
                                <td>@l.Type</td>
                                <td class="code">@RefLabel(l)</td>
                                <td class="r mono @(l.Quantity < 0 ? "lowstock" : "")">@l.Quantity.ToString("N0")</td>
                                <td class="r mono">@l.UnitCost.ToString("N2")</td>
                                <td class="r mono">@l.RunningQty.ToString("N0")</td>
                                <td class="r mono">@l.RunningValue.ToString("N2")</td>
                            </tr>
                        }
                        <tr class="fw-bold">
                            <td colspan="3">Closing balance</td>
                            <td></td><td></td>
                            <td class="r mono">@_card.ClosingQty.ToString("N0")</td>
                            <td class="r mono">@_card.ClosingValue.ToString("N2")</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    [Parameter] public int VariantId { get; set; }
    [SupplyParameterFromQuery] public int? Warehouse { get; set; }
    [SupplyParameterFromQuery] public string? From { get; set; }
    [SupplyParameterFromQuery] public string? To { get; set; }

    private StockCardDto? _card;
    private bool _notFound;

    protected override async Task OnInitializedAsync()
    {
        var from = DateTime.TryParse(From, out var f) ? f : DateTime.Today.AddMonths(-1);
        var to = DateTime.TryParse(To, out var t) ? t : DateTime.Today;
        _card = await Ledger.GetStockCardAsync(VariantId, Warehouse, from, to);
        _notFound = _card is null;
    }

    private static string RefLabel(StockCardLineDto l) =>
        l.RefType is null ? "" : l.RefId is null ? l.RefType : $"{l.RefType} #{l.RefId}";

    private async Task ExportExcel()
    {
        var doc = await Ledger.BuildStockCardReportAsync(VariantId, _card!.WarehouseId, _card.From, _card.To);
        if (doc is not null) await DownloadAsync(Exporter.ToExcel(doc), "stock-card.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Ledger.BuildStockCardReportAsync(VariantId, _card!.WarehouseId, _card.From, _card.To);
        if (doc is not null) await DownloadAsync(await Exporter.ToPdfAsync(doc), "stock-card.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 10: Build the solution**

Run: `dotnet build ErpOne.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Commit**

```bash
git add src/ErpOne.Application/Reports/StockLedgerDtos.cs src/ErpOne.Application/Reports/IStockLedgerReportService.cs \
  src/ErpOne.Infrastructure/Services/StockLedgerReportService.cs src/ErpOne.Infrastructure/DependencyInjection.cs \
  src/ErpOne.Web/Components/Pages/Reports/StockLedger \
  tests/ErpOne.IntegrationTests/StockLedgerReportServiceTests.cs
git commit -m "feat: Stock Ledger report — service (list + stock-card running balance) + index/drill-down pages + Excel/PDF export"
```

---

## Task 3: Inventory Valuation (service + page, group by category/warehouse)

**Files:**
- Create: `src/ErpOne.Application/Reports/InventoryValuationDtos.cs`
- Create: `src/ErpOne.Application/Reports/IInventoryValuationReportService.cs`
- Create: `src/ErpOne.Infrastructure/Services/InventoryValuationReportService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `src/ErpOne.Web/Components/Pages/Reports/InventoryValuation/InventoryValuationIndex.razor`
- Test: `tests/ErpOne.IntegrationTests/InventoryValuationReportServiceTests.cs`

**Interfaces:**
- Consumes: `IReportExporter`; `IStockService.RecordOpeningAsync`/`RecordAdjustmentAsync`; `IWarehouseService.GetAllAsync()`; `IProductCategoryService` (list categories for the filter dropdown — verify the method name, e.g. `GetAllAsync()`).
- Produces:
  - `enum ValuationGroupBy { Category, Warehouse }`
  - `record ValuationItemDto(int VariantId, string Sku, string ProductName, string GroupName, int Qty, decimal AvgCost, decimal Value)`
  - `record ValuationGroupDto(string GroupName, IReadOnlyList<ValuationItemDto> Items, int TotalQty, decimal TotalValue)`
  - `record ValuationResultDto(DateTime AsOf, ValuationGroupBy GroupBy, IReadOnlyList<ValuationGroupDto> Groups, int GrandTotalQty, decimal GrandTotalValue, int ItemCount)`
  - `interface IInventoryValuationReportService` with:
    - `Task<ValuationResultDto> GetValuationAsync(DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId, bool includeZeroQty, CancellationToken ct = default)`
    - `Task<ReportDocument> BuildValuationReportAsync(DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId, bool includeZeroQty, CancellationToken ct = default)`

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/Reports/InventoryValuationDtos.cs`:
```csharp
namespace ErpOne.Application.Reports;

public enum ValuationGroupBy { Category, Warehouse }

public record ValuationItemDto(
    int VariantId, string Sku, string ProductName, string GroupName, int Qty, decimal AvgCost, decimal Value);

public record ValuationGroupDto(
    string GroupName, IReadOnlyList<ValuationItemDto> Items, int TotalQty, decimal TotalValue);

public record ValuationResultDto(
    DateTime AsOf, ValuationGroupBy GroupBy, IReadOnlyList<ValuationGroupDto> Groups,
    int GrandTotalQty, decimal GrandTotalValue, int ItemCount);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/Reports/IInventoryValuationReportService.cs`:
```csharp
namespace ErpOne.Application.Reports;

public interface IInventoryValuationReportService
{
    Task<ValuationResultDto> GetValuationAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default);

    Task<ReportDocument> BuildValuationReportAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing service test**

Create `tests/ErpOne.IntegrationTests/InventoryValuationReportServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class InventoryValuationReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public InventoryValuationReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int variant, int wh)> SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1500m, null, 0m, null, null, true);
        await db.SaveChangesAsync();

        var stock = sp.GetRequiredService<IStockService>();
        await stock.RecordOpeningAsync(variant.Id, wh.Id, 10, 1000m); // qty 10, value 10000, MA 1000
        return (variant.Id, wh.Id);
    }

    [Fact]
    public async Task Valuation_today_matches_productstock_value()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, wh) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var result = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Category, null, null, false);

        // Compare against ProductStock.Qty * variant.CostPrice for the seeded variant.
        var expectedValue = await (
            from s in db.ProductStocks
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            where s.ProductVariantId == variant
            select s.Quantity * v.CostPrice).SumAsync();

        var line = result.Groups.SelectMany(g => g.Items).Single(i => i.VariantId == variant);
        Assert.Equal(10, line.Qty);
        Assert.Equal(expectedValue, line.Value);
        Assert.Equal(1000m, line.AvgCost);
    }

    [Fact]
    public async Task Valuation_asof_past_excludes_later_movements()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (variant, _) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();

        // Opening was recorded "now"; as-of yesterday there should be no qty for this variant.
        var result = await svc.GetValuationAsync(DateTime.Today.AddDays(-1), ValuationGroupBy.Category, null, null, false);

        Assert.DoesNotContain(result.Groups.SelectMany(g => g.Items), i => i.VariantId == variant);
    }

    [Fact]
    public async Task GroupBy_category_and_warehouse_have_same_grand_total()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        var svc = sp.GetRequiredService<IInventoryValuationReportService>();

        var byCat = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Category, null, null, false);
        var byWh = await svc.GetValuationAsync(DateTime.Today, ValuationGroupBy.Warehouse, null, null, false);

        Assert.Equal(byCat.GrandTotalValue, byWh.GrandTotalValue);
        Assert.Equal(byCat.GrandTotalQty, byWh.GrandTotalQty);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~InventoryValuationReportServiceTests"`
Expected: FAIL — `IInventoryValuationReportService` not registered.

- [ ] **Step 5: Implement `InventoryValuationReportService`**

Create `src/ErpOne.Infrastructure/Services/InventoryValuationReportService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class InventoryValuationReportService(AppDbContext db) : IInventoryValuationReportService
{
    private const string Uncategorized = "Uncategorized";

    public async Task<ValuationResultDto> GetValuationAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        // Join movements to variant/product/warehouse; filter to <= asOf and optional warehouse/category.
        var q =
            from m in db.StockMovements.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on m.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
            where m.MovementDate < toExclusive
            select new { m.Quantity, m.UnitCost, VariantId = v.Id, v.Sku, ProductName = p.Name,
                         p.CategoryId, WarehouseId = w.Id, WarehouseName = w.Name };

        if (warehouseId is int wid) q = q.Where(x => x.WarehouseId == wid);
        if (categoryId is int cid) q = q.Where(x => x.CategoryId == cid);

        var rows = await q.ToListAsync(ct);

        // Category names lookup (small table).
        var categoryNames = await db.ProductCategories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Aggregate per (variant, group-key). For Category grouping we aggregate across warehouses per variant;
        // for Warehouse grouping we keep variant split per warehouse.
        var items = rows
            .GroupBy(x => groupBy == ValuationGroupBy.Warehouse
                ? (x.VariantId, x.WarehouseId)
                : (x.VariantId, 0))
            .Select(g =>
            {
                var first = g.First();
                var qty = g.Sum(x => x.Quantity);
                var value = g.Sum(x => x.Quantity * x.UnitCost);
                var groupName = groupBy == ValuationGroupBy.Warehouse
                    ? first.WarehouseName
                    : (first.CategoryId is int c && categoryNames.TryGetValue(c, out var n) ? n : Uncategorized);
                var avg = qty == 0 ? 0m : value / qty;
                return new ValuationItemDto(first.VariantId, first.Sku, first.ProductName, groupName, qty, avg, value);
            })
            .Where(i => includeZeroQty || i.Qty != 0)
            .ToList();

        var groups = items
            .GroupBy(i => i.GroupName)
            .OrderBy(g => g.Key)
            .Select(g => new ValuationGroupDto(
                g.Key,
                g.OrderBy(i => i.ProductName).ThenBy(i => i.Sku).ToList(),
                g.Sum(i => i.Qty),
                g.Sum(i => i.Value)))
            .ToList();

        return new ValuationResultDto(
            asOf, groupBy, groups,
            items.Sum(i => i.Qty), items.Sum(i => i.Value), items.Count);
    }

    public async Task<ReportDocument> BuildValuationReportAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default)
    {
        var result = await GetValuationAsync(asOf, groupBy, warehouseId, categoryId, includeZeroQty, ct);

        var rows = new List<ReportRow>();
        foreach (var g in result.Groups)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"▸ {g.GroupName}", "", "", "", ""] });
            foreach (var i in g.Items)
                rows.Add(new ReportRow { Cells = [i.GroupName, i.Sku, i.ProductName, i.Qty, i.AvgCost, i.Value] });
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"{g.GroupName} subtotal", "", "", g.TotalQty, "", g.TotalValue] });
        }

        return new ReportDocument
        {
            Title = "Inventory Valuation",
            Subtitle = $"As of {result.AsOf:yyyy-MM-dd}  ·  Grouped by {result.GroupBy}",
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn(groupBy == ValuationGroupBy.Warehouse ? "Warehouse" : "Category"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Avg Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Value", ReportAlign.Right, "N2"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Grand total", "", "", result.GrandTotalQty, "", result.GrandTotalValue] },
        };
    }
}
```
Note: the report column count is 6; the subtotal/group-header rows pad with empty strings to stay aligned. Verify `ProductCategory` exposes `Id` and `Name` (used in the dictionary) — adjust if the name property differs.

- [ ] **Step 6: Register the service in DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs`. Under the `IStockLedgerReportService` registration add:
```csharp
services.AddScoped<IInventoryValuationReportService, InventoryValuationReportService>();
```

- [ ] **Step 7: Run the service tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj --filter "FullyQualifiedName~InventoryValuationReportServiceTests"`
Expected: PASS (3 tests).

- [ ] **Step 8: Create the Inventory Valuation page**

First confirm the category-list method: run `grep -n "Task" src/ErpOne.Application/ProductCategories/IProductCategoryService.cs` and use the read-all method (likely `GetAllAsync()` returning items with `.Id`/`.Name`). Adjust the `@inject` and call below to match.

Create `src/ErpOne.Web/Components/Pages/Reports/InventoryValuation/InventoryValuationIndex.razor`:
```razor
@page "/reports/inventory-valuation"
@attribute [Authorize(Policy = "reports.inventory-valuation.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using ErpOne.Application.Warehouses
@using ErpOne.Application.ProductCategories
@using Microsoft.JSInterop
@inject IInventoryValuationReportService Valuation
@inject IWarehouseService WarehouseService
@inject IProductCategoryService CategoryService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Inventory Valuation</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Inventory Valuation</span></nav>
            <h1>Inventory Valuation</h1>
            <p>Stock value (qty × moving-average cost) as of a chosen date.</p>
        </div>
        <AuthorizeView Policy="reports.inventory-valuation.export">
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
            <div class="kpi accent"><div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div><div class="kpi-tx"><div class="v">Rp @_result.GrandTotalValue.ToString("N0")</div><div class="l">Total value</div></div></div>
            <div class="kpi"><div class="ic ic-blu"><i class="bi bi-stack"></i></div><div class="kpi-tx"><div class="v">@_result.GrandTotalQty.ToString("N0")</div><div class="l">Total qty</div></div></div>
            <div class="kpi"><div class="ic ic-amb"><i class="bi bi-box-seam"></i></div><div class="kpi-tx"><div class="v">@_result.ItemCount.ToString("N0")</div><div class="l">Items</div></div></div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_asOf" @bind:after="ReloadAsync" />
        <select @bind="_groupBy" @bind:after="ReloadAsync">
            <option value="@ValuationGroupBy.Category">Group by Category</option>
            <option value="@ValuationGroupBy.Warehouse">Group by Warehouse</option>
        </select>
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
        <select @bind="_categoryId" @bind:after="ReloadAsync">
            <option value="0">All categories</option>
            @foreach (var c in _categories) { <option value="@c.Id">@c.Name</option> }
        </select>
        <label class="chk"><input type="checkbox" @bind="_includeZero" @bind:after="ReloadAsync" /> Show zero qty</label>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.ItemCount == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cash-stack"></i></div><p>No inventory to value for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>@(_groupBy == ValuationGroupBy.Warehouse ? "Warehouse" : "Category")</th>
                            <th style="width:150px">SKU</th>
                            <th>Product</th>
                            <th class="r" style="width:100px">Qty</th>
                            <th class="r" style="width:130px">Avg Cost</th>
                            <th class="r" style="width:150px">Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var g in _result.Groups)
                        {
                            <tr class="fw-bold table-light"><td colspan="6">@g.GroupName</td></tr>
                            @foreach (var i in g.Items)
                            {
                                <tr>
                                    <td class="code">@i.GroupName</td>
                                    <td class="code mono">@i.Sku</td>
                                    <td class="nm">@i.ProductName</td>
                                    <td class="r mono">@i.Qty.ToString("N0")</td>
                                    <td class="r mono">@i.AvgCost.ToString("N2")</td>
                                    <td class="r mono">@i.Value.ToString("N2")</td>
                                </tr>
                            }
                            <tr class="fw-bold">
                                <td colspan="3">@g.GroupName subtotal</td>
                                <td class="r mono">@g.TotalQty.ToString("N0")</td>
                                <td></td>
                                <td class="r mono">@g.TotalValue.ToString("N2")</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold">
                            <td colspan="3">Grand total</td>
                            <td class="r mono">@_result.GrandTotalQty.ToString("N0")</td>
                            <td></td>
                            <td class="r mono">@_result.GrandTotalValue.ToString("N2")</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private ValuationResultDto? _result;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private IReadOnlyList<ProductCategoryDto> _categories = [];
    private DateTime _asOf = DateTime.Today;
    private ValuationGroupBy _groupBy = ValuationGroupBy.Category;
    private int _warehouseId;
    private int _categoryId;
    private bool _includeZero;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        _categories = await CategoryService.GetAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() =>
        _result = await Valuation.GetValuationAsync(
            _asOf, _groupBy,
            _warehouseId == 0 ? null : _warehouseId,
            _categoryId == 0 ? null : _categoryId,
            _includeZero);

    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Valuation.BuildValuationReportAsync(_asOf, _groupBy, _warehouseId == 0 ? null : _warehouseId, _categoryId == 0 ? null : _categoryId, _includeZero);
        await DownloadAsync(Exporter.ToExcel(doc), "inventory-valuation.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Valuation.BuildValuationReportAsync(_asOf, _groupBy, _warehouseId == 0 ? null : _warehouseId, _categoryId == 0 ? null : _categoryId, _includeZero);
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "inventory-valuation.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```
Note: verify `ProductCategoryDto` name/shape and `IProductCategoryService.GetAllAsync()` from Step 8's grep, and `WarehouseDto`. Adjust `@using`/type names if they differ.

- [ ] **Step 9: Build the solution**

Run: `dotnet build ErpOne.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Run the full integration test suite**

Run: `dotnet test tests/ErpOne.IntegrationTests/ErpOne.IntegrationTests.csproj`
Expected: All tests pass (including the 3 new report test classes and the pre-existing suite).

- [ ] **Step 11: Commit**

```bash
git add src/ErpOne.Application/Reports/InventoryValuationDtos.cs src/ErpOne.Application/Reports/IInventoryValuationReportService.cs \
  src/ErpOne.Infrastructure/Services/InventoryValuationReportService.cs src/ErpOne.Infrastructure/DependencyInjection.cs \
  src/ErpOne.Web/Components/Pages/Reports/InventoryValuation \
  tests/ErpOne.IntegrationTests/InventoryValuationReportServiceTests.cs
git commit -m "feat: Inventory Valuation report — as-of-date service (group by category/warehouse) + page + Excel/PDF export"
```

---

## Post-implementation manual steps (user)

- Restart the app and sign out/in so `BootstrapSeeder` grants the new `reports.*` permissions to the admin role.
- The **Reports** group appears in the sidebar with **Stock Ledger** and **Inventory Valuation**.
- Verify a real export downloads a valid `.xlsx` (opens in Excel) and a valid PDF (with the company header from Settings → Company Profile).

## Verification checklist (drive the app, not just tests)

- Stock Ledger list loads, filters (warehouse/type/date/search) narrow results, KPIs update.
- Clicking a row opens the Stock Card with opening → running → closing balances that reconcile.
- Inventory Valuation totals match between "Group by Category" and "Group by Warehouse".
- Excel and PDF export buttons produce files for both reports; export reflects the active filters, not just the current page.
