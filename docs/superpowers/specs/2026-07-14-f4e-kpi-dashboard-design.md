# Fase 4e — KPI Dashboard (design spec)

Date: 2026-07-14
Branch: `feat/f4a-inventory-reports`
Status: approved, ready for implementation plan

## Goal

Ganti isi `Home.razor` (`/`) yang sekarang berupa dashboard produk/stok menjadi:

1. **`/` (Home)** — landing/welcome sederhana (bukan dashboard lagi).
2. **`/dashboard` (baru)** — Dashboard KPI operasional lintas modul, sebagai **menu tersendiri**. Menggabungkan KPI operasional baru **+** semua widget produk/stok lama.

Ini menyelesaikan item Fase 4e di `docs/DEVELOPMENT-PLAN.md` (baris 147: "Dashboard KPI: omzet hari ini, transaksi, stok menipis, PO/SO pending approval, hutang/piutang jatuh tempo").

## Scope keputusan (hasil brainstorming)

- Semua widget di-include (KPI headline + PO/SO pending + stok menipis + chart stok lama + mini aging AR/AP).
- **Mini aging harus ringan**: satu query agregat GROUP BY per sisi (AR & AP), hanya total per-bucket — **bukan** report aging penuh (itu Fase 4c). Tidak ada per-invoice loop.
- Home `/` jadi landing kosong/menu; seluruh isi dashboard pindah ke `/dashboard`.

## Arsitektur (Opsi A — satu service pengomposisi)

Ikuti pola report yang sudah ada (read-only query service di `Application` + implementasi di `Infrastructure/Services`).

### Interface & DTO — `src/ErpOne.Application/Dashboard/`

`IDashboardService`:

```csharp
Task<OperationalDashboardDto> GetAsync(DateTime asOf, CancellationToken ct = default);
```

`asOf` = tanggal acuan (page kirim `DateTime.Today`). Dipakai untuk "hari ini" (omzet/transaksi) dan umur aging, agar deterministik & dapat di-test (pola sama dgn Inventory Valuation as-of-date).

`DashboardDtos.cs`:

```csharp
public record OperationalDashboardDto(
    DashboardKpis Kpis,
    PendingApprovalsDto Pending,
    AgingBuckets ArAging,
    AgingBuckets ApAging,
    ProductDashboardDto Stock);          // reuse apa adanya dari ProductService

public record DashboardKpis(
    decimal TodayRevenue,                 // omzet hari ini (POS + B2B)
    int TodayTxnCount,                    // jumlah transaksi hari ini (distinct DocNumber)
    decimal ArDue,                        // AR outstanding dgn DueDate overdue ATAU <= hari+7
    decimal ApDue);                       // AP idem

public record PendingApprovalsDto(
    int PoPendingCount, IReadOnlyList<PendingDocRow> PoPending,
    int SoPendingCount, IReadOnlyList<PendingDocRow> SoPending);

public record PendingDocRow(int Id, string Number, string Party, decimal Total, DateTime Date);

public record AgingBuckets(
    decimal Current,   // 0-30 hari (belum/awal)
    decimal D31_60,
    decimal D61_90,
    decimal D90Plus,
    decimal Total);
```

### Implementasi — `src/ErpOne.Infrastructure/Services/DashboardService.cs`

Konstruktor DI: `AppDbContext db`, `SalesFactProvider sales`, `IProductService products`.

- **KPI omzet & transaksi hari ini** — reuse `SalesFactProvider.GetAsync(new SalesFilter(From: today, To: today, ...))`.
  - `TodayRevenue = Σ row.Revenue`.
  - `TodayTxnCount = rows.Select(r => r.DocNumber).Distinct().Count()`.
  - "today" = `asOf.Date`; filter `SalesFilter(From: asOf.Date, To: asOf.Date, ...)`.
- **AR/AP outstanding & aging** — query langsung `CustomerInvoice` / `SupplierInvoice`:
  - Outstanding = `GrandTotal - PaidAmount` untuk `Status is Open or PartiallyPaid` (kedua enum sama: `Open, PartiallyPaid, Paid, Cancelled`).
  - `ArDue`/`ApDue` (KPI) = Σ outstanding dgn `DueDate <= asOf.AddDays(7)` (mencakup overdue + akan jatuh tempo ≤7 hari).
  - `AgingBuckets` = **satu** query GROUP BY umur (`asOf - DueDate` hari): `<=30 / 31-60 / 61-90 / >90`, Σ outstanding tiap bucket + Total. Yang belum jatuh tempo (DueDate > asOf) masuk bucket `Current`.
- **PO/SO pending** — `PurchaseOrder.Status == PendingApproval`, `SalesOrder.Status == PendingApproval`. Ambil count + top ~5 (Number, party name, total, date), order by date desc.
- **Stock** — panggil `products.GetDashboardAsync()`, taruh utuh di `OperationalDashboardDto.Stock`.

Registrasi DI di `Infrastructure` service registration (mirip service report lain). `SalesFactProvider` sudah terdaftar (dipakai 4b).

## UI

### Home `/` — `src/ErpOne.Web/Components/Pages/Home.razor`

Rombak jadi landing sederhana: hero sambutan (nama app / user) + beberapa tombol/kartu tautan cepat ke Dashboard & modul utama. Hapus injeksi `IProductService` dan seluruh widget stok (pindah ke Dashboard). CSS lama di `Home.razor.css` yang masih relevan (bar, h-card, dll.) dipindah ke `Dashboard.razor.css`.

### Dashboard `/dashboard` — `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor`

`@page "/dashboard"`, `@rendermode InteractiveServer`, `@inject IDashboardService`, load di `OnInitializedAsync`.

Layout (atas → bawah):

1. **Hero** `dash-hero` — judul "Dashboard" + tanggal.
2. **Baris KPI** `cr-kpis` — 4 `cr-kpi`, tiap kartu klik → tujuan terkait:
   - Omzet hari ini → `/reports/sales`
   - Transaksi hari ini → `/reports/sales`
   - AR jatuh tempo → `/finance/ar-invoices`
   - AP jatuh tempo → `/finance/ap-invoices`
3. **PO & SO pending approval** — 2 `h-card` (atau 1 baris): jumlah + daftar ringkas (top 5), tiap baris klik ke dokumennya; header klik ke modul PO/SO. Kosong → empty state.
4. **Mini aging AR & AP** — 2 `h-card` ringan: 4 bar/angka bucket (`0-30 / 31-60 / 61-90 / 90+`) + total. Bar pakai gaya `.bar/.bar-fill` yang sudah ada.
5. **Bagian stok lama (dipindah utuh)** — Products by Status, Stock by Category, tabel Low/Out of Stock. Kode & CSS diangkat dari `Home.razor`/`Home.razor.css` apa adanya.

CSS: `Dashboard.razor.css` (reuse `.cr-kpis/.cr-kpi` global + gaya `h-card/bar/mini-alert/thumb` yang dipindah dari Home).

### Menu — `src/ErpOne.Web/Authorization/AppMenus.cs`

Tambah resource baru di grup teratas (`GroupLabel = null`), di atas/berdampingan `home`:

```csharp
new("dashboard", "Dashboard", "bi-speedometer2", ViewOnly),
```

Menu data-driven (per `navmenu-data-driven-design`) memetakan key → route. `home` tetap → `/`; `dashboard` → `/dashboard`. Pastikan mapping route menyertakan entri `dashboard`.

Permission baru `dashboard.index` otomatis masuk `AllPermissions`. BootstrapSeeder memberi permission baru ke admin saat startup (pola sama seperti finance). Catatan manual: user restart app + sign out/in agar admin dapat permission `dashboard.index`.

## Testing

`tests/ErpOne.IntegrationTests/DashboardServiceTests.cs` (SQLite `EnsureCreated`):

- Seed penjualan hari ini (POS + B2B) → assert `TodayRevenue` & `TodayTxnCount`.
- Seed `CustomerInvoice`/`SupplierInvoice` dgn `DueDate` beragam (overdue, ≤7 hari, jauh, lunas, cancelled) → assert `ArDue`/`ApDue` dan tiap bucket `AgingBuckets` benar; invoice `Paid`/`Cancelled` tidak dihitung.
- Seed PO & SO `PendingApproval` + status lain → assert count & isi list hanya yang pending.
- Assert bagian `Stock` ter-populate (delegasi ke `ProductService`).

Bila `NumberSequenceServiceTests` atau test yang meng-assert jumlah menu resource ada, naikkan angkanya karena menambah resource `dashboard`.

## Build/test pattern

Solution `ErpOne.slnx`. Jalankan `dotnet test ErpOne.slnx`. Target: seluruh test hijau (sebelumnya 149).

## Manual steps (setelah pull)

Restart app + sign out/in agar admin mendapat permission `dashboard.index`. Tidak ada migrasi DB (murni read-only, tanpa entity baru).

## Out of scope

- Report AR/AP aging penuh (drill-down per invoice) = Fase 4c.
- Report Shift Kasir = Fase 4d.
- Grafik time-series / tren historis (cukup KPI hari ini + snapshot).
