# Fase 4c — AR/AP Aging Reports — Design

**Date:** 2026-07-15
**Module:** Reports · Aging Piutang (AR) & Hutang (AP)
**Depends on:** Fase 3b/3a Finance (CustomerInvoice/Receipt, SupplierInvoice/Payment), Fase 4 Reports foundation (`ReportDocument`, `IReportExporter`).

## Goal

Dua laporan aging berdiri sendiri (drill-down per faktur) untuk piutang customer dan
hutang supplier, dihitung **point-in-time** pada tanggal "as of" pilihan, dikelompokkan
per party dengan subtotal + grand total, dan bisa diekspor Excel/PDF. Melengkapi
mini-aging agregat yang sudah ada di KPI Dashboard (Fase 4e) — angka harus berdamai.

## Decisions (locked)

1. **Dua halaman terpisah** — `/reports/ar-aging` (Piutang) dan `/reports/ap-aging` (Hutang),
   sejajar dengan pemisahan Sales vs Purchase report. Satu service bersama, dua sisi.
2. **5 bucket** — `Not Yet Due | 1–30 | 31–60 | 61–90 | 90+`, umur = hari **lewat DueDate**.
3. **True point-in-time outstanding** — outstanding = GrandTotal − pembayaran/penerimaan yang
   **bertanggal ≤ as-of**; pembayaran setelah as-of diabaikan.

## Architecture

Satu service bersama (`IAgingReportService`) dengan metode AR & AP simetris, dua halaman
Blazor. Mengikuti pola Inventory Valuation report (as-of date + grouping + `ReportDocument`).

### New files
- `src/ErpOne.Application/Reports/AgingDtos.cs` — DTO bersama.
- `src/ErpOne.Application/Reports/IAgingReportService.cs` — interface (AR + AP).
- `src/ErpOne.Infrastructure/Services/Reports/AgingReportService.cs` — implementasi.
- `src/ErpOne.Web/Components/Pages/Reports/Aging/ArAgingIndex.razor` → `/reports/ar-aging`.
- `src/ErpOne.Web/Components/Pages/Reports/Aging/ApAgingIndex.razor` → `/reports/ap-aging`.
- `tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs`.

### Modified files
- `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `IAgingReportService`
  (setelah baris 85, `IGrossProfitReportService`).
- `src/ErpOne.Web/Authorization/AppMenus.cs` — dua resource baru di grup **Reports**
  (setelah `reports.gross-profit`, baris 91) memakai `ReportActions` (= `[ActIndex, ActExport]`),
  jadi permission `reports.ar-aging.index` / `.export` dan `reports.ap-aging.index` / `.export`
  otomatis ter-seed ke admin oleh `BootstrapSeeder`.

## DTOs

```csharp
namespace ErpOne.Application.Reports;

public enum AgingSide { Receivable, Payable }

// Outstanding satu faktur seluruhnya jatuh ke TEPAT satu bucket.
public record AgingBucketSet(
    decimal NotDue, decimal D1_30, decimal D31_60, decimal D61_90, decimal D90Plus, decimal Total);

public record AgingInvoiceDto(
    int InvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate,
    int DaysPastDue, decimal GrandTotal, decimal Outstanding, AgingBucketSet Buckets);

public record AgingPartyDto(
    int PartyId, string PartyCode, string PartyName,
    IReadOnlyList<AgingInvoiceDto> Invoices, AgingBucketSet Subtotals);

public record AgingResultDto(
    DateTime AsOf, AgingSide Side, IReadOnlyList<AgingPartyDto> Parties,
    AgingBucketSet GrandTotals, int InvoiceCount, int PartyCount);
```

`AgingBucketSet` untuk satu faktur hanya berisi nilai di satu slot (sisanya 0), `Total` =
`Outstanding`. Subtotal party = penjumlahan bucket faktur-fakturnya; grand total = penjumlahan
subtotal party.

## Interface

```csharp
public interface IAgingReportService
{
    Task<AgingResultDto> GetArAgingAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<AgingResultDto> GetApAgingAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
    Task<ReportDocument> BuildArAgingReportAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<ReportDocument> BuildApAgingReportAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
}
```

`asOf` adalah parameter eksplisit — service TIDAK membaca `DateTime.Today` (deterministik untuk
test); halaman mengirim `DateTime.Today` default.

## Point-in-time logic (AR; AP simetris)

Untuk AR pakai `CustomerInvoice` + `CustomerReceiptAllocation`→`CustomerReceipt`
(`Status == Posted`, `ReceiptDate`). Untuk AP pakai `SupplierInvoice` +
`SupplierPaymentAllocation`→`SupplierPayment` (`Status == Posted`, `PaymentDate`).

1. **Faktur kandidat:** `InvoiceDate.Date <= asOf` **dan** `Status != Cancelled`
   (opsional difilter `customerId`/`supplierId`).
2. **paidAsOf per faktur:** Σ `Allocation.Amount` di mana parent receipt/payment
   `Status == Posted` **dan** tanggalnya `<= asOf`. Satu query grouped by invoiceId
   (join alokasi → receipt/payment).
3. **outstanding** = `GrandTotal − paidAsOf`; **simpan hanya `outstanding > 0`**.
4. **daysPastDue** = `(asOf.Date − DueDate.Date).Days`. Bucket:
   `<= 0` → NotDue · `1–30` · `31–60` · `61–90` · `> 90` → 90+.
   Seluruh `outstanding` masuk ke satu bucket itu.
5. **Grouping:** per party, party diurut nama, faktur dalam party diurut DueDate; subtotal per
   party; grand total lintas party. `PartyCount` = jumlah party ber-outstanding, `InvoiceCount`
   = jumlah faktur ber-outstanding.

### Simplification (sengaja, dicatat)

Status void/cancel dibaca **saat ini** (bukan point-in-time): receipt/payment yang kini
`Voided` dan faktur yang kini `Cancelled` dianggap tak pernah ada. Akurat untuk kasus umum;
rekonstruksi "as-of sebelum void terjadi kemudian" di luar lingkup (tanggal void tak dilacak).

## ReportDocument (export Excel/PDF)

`BuildArAgingReportAsync`/`BuildApAgingReportAsync` merakit `ReportDocument` dari
`AgingResultDto`:

- **Title:** "AR Aging" / "AP Aging". **Subtitle:** `As of {asOf:yyyy-MM-dd}`.
- **FilterSummary:** party terpilih atau "All customers"/"All suppliers".
- **Columns:** `Party` · `Invoice #` · `Invoice Date`(yyyy-MM-dd) · `Due Date`(yyyy-MM-dd) ·
  `Days`(N0, Right) · `Not Due`(N0, R) · `1–30`(N0, R) · `31–60`(N0, R) · `61–90`(N0, R) ·
  `90+`(N0, R) · `Outstanding`(N0, R).
- **Rows:** per party — baris faktur, lalu satu `ReportRow { IsSubtotal = true }` ("{Party}
  subtotal", kolom party/#/date kosong, bucket & outstanding terisi).
- **TotalsRow:** `IsGrandTotal` — "Grand total" + penjumlahan bucket & outstanding.

## Pages

Kedua halaman mengikuti pola `InventoryValuationIndex.razor` (`.pi` + `.kpis` + `.toolbar` +
`.card`/table + `tfoot`):

- `@page` + `@attribute [Authorize(Policy = "reports.ar-aging.index")]` (AP: `reports.ap-aging.index`),
  `@rendermode InteractiveServer`.
- **Header** `.pi-head`: breadcrumbs Home · Reports · AR/AP Aging, judul + deskripsi singkat,
  tombol Export Excel/PDF di `AuthorizeView Policy="reports.ar-aging.export"` (AP: `.ap-aging.export`).
- **KPI** `.kpis`: Total outstanding (accent), Overdue (= Total − NotDue), #Invoices, #Parties.
- **Toolbar:** `input type=date` (as-of, `@bind:after="ReloadAsync"`) + `select` party
  ("All customers/suppliers" + daftar). Daftar customer via `ICustomerService`, supplier via
  `ISupplierService` (verifikasi nama metode `GetAll…` di plan).
- **Table:** kolom = Party · Invoice # · Inv Date · Due · Days · Not Due · 1–30 · 31–60 · 61–90 ·
  90+ · Outstanding. Baris header grup per party (`fw-bold table-light`), baris faktur, baris
  subtotal party (`fw-bold`), `tfoot` grand total. Empty-state bila `InvoiceCount == 0`.
- **Export** identik pola Valuation: `Exporter.ToExcel(doc)` / `await Exporter.ToPdfAsync(doc)`
  + `JS "saveAsFile"`; nama file `ar-aging.xlsx`/`.pdf`, `ap-aging.xlsx`/`.pdf`.

Menu icon: AR `bi-hourglass-split`, AP `bi-hourglass-bottom` (final di plan).

## Testing

`tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs` (SQLite `CustomWebApplicationFactory`,
pola `SalesReportServiceTests`/`CustomerInvoiceServiceTests`):

- **AR point-in-time & buckets:** seed 1 customer + beberapa `CustomerInvoice` dengan DueDate
  variatif relatif `asOf` (belum jatuh tempo → NotDue; overdue ~15/45/75/120 hari → 1–30/31–60/
  61–90/90+) + `CustomerReceipt` sebagian, satu bertanggal **sebelum** as-of (dihitung) dan satu
  **sesudah** (diabaikan). Assert bucket, outstanding, `GrandTotals.Total`, exclude receipt
  pasca-as-of, dan faktur lunas-as-of tidak muncul.
- **AP mirror:** 1 supplier + `SupplierInvoice` + `SupplierPayment` (Posted).
- **Filter party:** `customerId`/`supplierId` mempersempit hasil.
- Seed langsung via `AppDbContext` bila lebih ringkas (atur `InvoiceDate`/`DueDate`/`ReceiptDate`
  eksplisit); verifikasi ctor line entity di plan.

## Konvensi & batasan

- Solution `ErpOne.slnx`. Build/test: `dotnet test ErpOne.slnx`.
- Service = query read-only; **tidak ada entity/migration baru**.
- Reuse `ReportDocument`/`IReportExporter` — jangan bikin exporter baru.
- Commit MANUAL oleh user; git identity repo-local `aliakbar893004-boop`. Langkah "commit" di
  plan hanya penanda batas task.

## Out of scope

- Rekonstruksi void/cancel per tanggal (lihat Simplification).
- Aging berbasis InvoiceDate (kita pakai DueDate).
- Multi-currency konversi (kolom Currency IDR-dominan; nilai ditampilkan apa adanya).
- Grafik/chart aging (cukup tabel + KPI); dashboard sudah punya bar mini.
