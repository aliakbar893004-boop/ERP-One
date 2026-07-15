# Fase 4d — Cashier Shift Report — Design

**Date:** 2026-07-15
**Module:** Reports · Laporan Shift Kasir
**Depends on:** Fase D1/D2 (CashierShift + PosSale), Fase 4 Reports foundation (`ReportDocument`, `IReportExporter`).

## Goal

Satu laporan read-only untuk rekap **shift kasir yang sudah ditutup** dalam rentang tanggal:
dikelompokkan **per kasir**, tiap shift menampilkan rincian **per metode pembayaran** + rekonsiliasi
kas (opening float, expected, counted, variance), dengan subtotal per kasir + grand total dan
export Excel/PDF. Laporan terakhir di Fase 4.

## Decisions (locked)

1. **Grouped by cashier** — header kasir → shift-shift-nya → subtotal per kasir → grand total.
2. **Rincian per metode per shift** — tiap shift diikuti baris per metode (nama · amount · txn), ditampilkan
   nested/indented selalu terlihat (bukan collapse JS) agar konsisten dgn AR/AP aging & bersih saat export.
3. **Closed shifts only** — `Status == Closed`; CountedCash & Variance bermakna.

## Architecture

Satu service read-only (`ICashierShiftReportService`) + satu halaman. Sumber: `CashierShift` (+ koleksi
`Totals` per metode). **Tidak ada entity/migration baru.** Mengikuti pola `AgingReportService` /
`InventoryValuationReportService` (`Get…Async` untuk UI + `Build…ReportAsync`→`ReportDocument` untuk export).

### New files
- `src/ErpOne.Application/Reports/CashierShiftReportDtos.cs`
- `src/ErpOne.Application/Reports/ICashierShiftReportService.cs`
- `src/ErpOne.Infrastructure/Services/Reports/CashierShiftReportService.cs`
- `src/ErpOne.Web/Components/Pages/Reports/CashierShift/CashierShiftReportIndex.razor` → `/reports/cashier-shifts`
- `tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs`

### Modified files
- `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `ICashierShiftReportService` (setelah `IAgingReportService`).
- `src/ErpOne.Web/Authorization/AppMenus.cs` — resource `reports.cashier-shifts` di grup Reports (setelah `reports.ap-aging`) memakai `ReportActions` (view+export) → permission `.index`/`.export` auto-seed admin.

## DTOs

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

Catatan: DTO report ini terpisah dari `ErpOne.Application.CashierShifts.ShiftMethodTotalDto` yang sudah ada
(biar layer report self-contained). `TotalSales` shift = `TotalSalesAmount` (Σ MethodTotals). `Variance` = `CashVariance`
(over = +, short = −). `CountedCash`/`CashVariance` nullable secara tipe tapi selalu terisi (closed-only).

## Interface

```csharp
public interface ICashierShiftReportService
{
    Task<ShiftReportResultDto> GetShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<ReportDocument> BuildShiftReportAsync(DateTime from, DateTime to, int? warehouseId, string? cashierUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CashierOptionDto>> GetCashiersAsync(CancellationToken ct = default);
}
```

`from`/`to` parameter eksplisit; service tak baca `DateTime.Today` (deterministik). Halaman kirim default rentang.

## Logic

1. Muat `CashierShift` (`Include(s => s.Totals)`) di mana `Status == CashierShiftStatus.Closed` **dan**
   `OpenedAt < toExclusive` (`toExclusive = to.Date.AddDays(1)`) **dan** `OpenedAt >= from.Date`,
   difilter opsional `warehouseId` / `cashierUserId`.
2. Resolve nama: `Warehouses` (dict id→Name) & `PaymentMethods` (dict id→Name) via satu query masing-masing.
3. Bangun `ShiftRowDto` per shift; `Methods` diurut Amount desc. Group per `CashierUserId` → `ShiftCashierDto`
   (subtotal: Σ TotalSales, Σ TransactionCount, Σ CashVariance). Kasir diurut nama; shift diurut `OpenedAt`.
4. Grand total lintas kasir; `ShiftCount` = jumlah shift, `CashierCount` = jumlah kasir.

## ReportDocument (export)

`BuildShiftReportAsync` merakit `ReportDocument` dari hasil:
- **Title:** "Cashier Shift Report". **Subtitle:** `{from:d MMM yyyy} – {to:d MMM yyyy}`. **FilterSummary:** warehouse & cashier terpilih atau "All".
- **Columns:** `Shift / Method` · `Opened`(yyyy-MM-dd HH:mm) · `Closed`(yyyy-MM-dd HH:mm) · `Warehouse` · `Opening`(N0,R) · `Sales`(N0,R) · `Txns`(N0,R) · `Expected`(N0,R) · `Counted`(N0,R) · `Variance`(N0,R).
- **Rows:** per kasir → 1 baris header `IsSubtotal` (`▸ {CashierName} ({UserId})`); tiap shift → baris shift (ShiftNumber di kolom 1, dates, warehouse, opening, TotalSales, txns, expected, counted, variance) lalu baris-baris metode (nama metode indented di kolom 1, Amount di kolom Sales, count di kolom Txns, kolom lain kosong); lalu baris `IsSubtotal` subtotal kasir (Sales/Txns/Variance). **TotalsRow** `IsGrandTotal` = grand Sales/Txns/Variance.

## Page (`/reports/cashier-shifts`)

Pola `.pi` + `.kpis` + `.toolbar` + `.card`/table (seperti AR/AP aging):
- `@attribute [Authorize(Policy = "reports.cashier-shifts.index")]`, `@rendermode InteractiveServer`.
- Header `.pi-head`: breadcrumbs Home · Reports · Cashier Shifts, judul + deskripsi; tombol Export Excel/PDF di `AuthorizeView Policy="reports.cashier-shifts.export"`.
- KPI `.kpis`: **Total Sales** (accent) · **Cash Variance** (net over/short) · **Shifts** · **Transactions**.
- Toolbar: `input type=date` From + To (`@bind:after="ReloadAsync"`), select Warehouse (via `IWarehouseService.GetAllAsync`), select Cashier (daftar kasir distinct dari shift closed — lihat catatan).
- Table: header grup per kasir (`fw-bold table-light`); tiap shift baris ringkas + baris metode indented; baris subtotal kasir (`fw-bold`); `tfoot` grand total. Empty-state bila `ShiftCount == 0`.
- Export identik pola aging (`Exporter.ToExcel`/`ToPdfAsync` + JS `saveAsFile`), nama file `cashier-shifts.xlsx`/`.pdf`.

**Cashier dropdown:** service report expose `Task<IReadOnlyList<CashierOptionDto>> GetCashiersAsync(CancellationToken ct = default)` yang query distinct kasir dari shift closed (`db.CashierShifts.Where(s => s.Status == Closed).Select(...).Distinct()`), dgn `public record CashierOptionDto(string UserId, string Name)` di file DTO. Halaman isi dropdown dari sini saat `OnInitializedAsync`.

## Testing

`tests/ErpOne.IntegrationTests/CashierShiftReportServiceTests.cs` (SQLite `CustomWebApplicationFactory`, pola `DashboardServiceTests`/`AgingReportServiceTests`):
- Seed warehouse + product/variant + opening stock + 2 payment method (satu Tunai/cash, satu non-cash). Buka shift (`ICashierShiftService.OpenAsync(userId, name, OpenShiftRequest(whId, float))`), buat beberapa POS sale via `IPosSaleService.CreateSaleAsync` (cash & non-cash) → shift `Totals` & `CashSalesTotal` terisi otomatis (RecordSale). Tutup (`CloseAsync(shiftId, userId, CloseShiftRequest(countedCash, note))`) dgn countedCash yang menghasilkan variance diketahui.
- Assert: report group per kasir, per-metode amount/txn benar, per-shift TotalSales/Expected/Counted/Variance benar, subtotal kasir & grand total, dan shift **open** tidak muncul (buka satu shift lagi tanpa close → tak masuk).
- Test filter warehouse & cashierUserId mempersempit hasil.

## Konvensi & batasan

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`.
- Service = query read-only; tak ada entity/migration baru. Reuse `ReportDocument`/`IReportExporter`.
- Commit MANUAL oleh user; git identity repo-local `aliakbar893004-boop`.

## Out of scope

- Shift open/in-progress (closed-only).
- Drill-down per transaksi POS (grain terhalus = agregat per metode per shift).
- Grafik/chart (cukup tabel + KPI).
- Multi-currency (nilai apa adanya, IDR).
