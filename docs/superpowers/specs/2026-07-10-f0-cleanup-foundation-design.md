# Fase 0 — Cleanup & Fondasi — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui (siap ke implementation plan)
**Ruang lingkup:** Satu spec mencakup 5 item Fase 0 dari `docs/DEVELOPMENT-PLAN.md`.

## Tujuan

Membereskan sisa template dan membangun fondasi yang memuluskan fase berikutnya:
Master Currency, penomoran dokumen terpusat, Company Settings, dan kerangka
dashboard KPI. Semua mengikuti pola wajib proyek (Domain → EF+migration →
Application → Infrastructure → Web `.pi`/`.cf`/`.pf` → Permissions → Tests) dan
**reuse** komponen yang sudah ada (`SwalService`, `Pager`, upload gambar produk,
CSS `.cr`, `.pi`/`.cf`).

## Keputusan brainstorming

| Topik | Keputusan |
|-------|-----------|
| Cakupan | Satu spec + satu implementation plan untuk kelima item |
| Penomoran | Retrofit **semua** modul existing ke `IDocumentNumberService` (+ test regresi) |
| Currency | Master Currency saja, **tanpa** exchange rate |
| Company Settings | Full fields **+ wire langsung ke struk POS** |
| Dashboard | Refactor `Home.razor` ke skeleton `.cr` (pertahankan data produk/stok) |

## Arsitektur & konvensi bersama

- **Grup menu baru `Settings`** di `Authorization/AppMenus.cs`; `master.currencies`
  ditambahkan ke grup **Master**.
- **Satu migration** untuk 3 entity baru: `Currency`, `NumberSequence`
  (+ `NumberSequenceCounter`), `CompanySetting`.
- **Bahasa UI:** Inggris. **Desain:** `.pi` (index), `.cf` (form) + token Atlas (emerald).
- **Permission seeding** otomatis untuk setiap resource baru di `BootstrapSeeder.cs`.

---

## Item 1 — Template Cleanup

- Hapus `src/ErpOne.Web/Components/Pages/Counter.razor` dan `Weather.razor`.
- Hapus class `WeatherForecast` bila ada.
- Sapu referensi nav/route yang menyebut halaman tersebut.
- **Kriteria selesai:** build hijau, tidak ada route/nav yang menggantung.

---

## Item 2 — Master Currency (tanpa kurs)

### Domain
Entity `Currency`:
- `Code` — kode ISO 4217, **unik** (mis. `IDR`, `USD`)
- `Name` — nama (mis. `Rupiah`)
- `Symbol` — simbol (mis. `Rp`)
- `DecimalPlaces` — int, default `2`
- `IsBase` — bool, **hanya satu** boleh true (divalidasi di service)
- `IsActive` — bool

### EF / Infrastructure
- `DbSet<Currency>` + `IEntityTypeConfiguration<Currency>` (unique index pada `Code`).

### Application
- `ICurrencyService` (CRUD standar), `CurrencyDtos.cs`, `CurrencyValidators.cs`.
- Validasi: `Code` unik & wajib; menetapkan `IsBase=true` menurunkan base lama;
  tidak boleh menghapus base currency.

### Web
- Resource `master.currencies`: Index `.pi` (list + KPI/chips), Form `.cf`.

### Wiring
- Field `DefaultCurrency` di **Supplier** & **Customer** berubah dari free-text
  menjadi **dropdown** dari currency `IsActive`.
- PO/SO tetap mewarisi currency dari supplier/customer (perilaku sekarang dipertahankan).

### Seed & permission
- Seed `IDR` sebagai base currency.
- Permission `master.currencies` (index/create/edit/delete).

---

## Item 3 — Penomoran Dokumen Terpusat (retrofit semua)

### Masalah sekarang
Setiap service punya `GenerateNumberAsync` privat sendiri dengan format
di-hardcode dan pola **read-max-then-increment** yang rawan *race condition*:

| Dokumen | Format sekarang |
|---------|-----------------|
| PurchaseOrder | `PO-{yyyyMM}-{D4}` (reset bulanan) |
| SalesOrder | `SO-{yyyyMM}-{D4}` (reset bulanan) |
| GoodsReceipt | `GRN-{yyyyMM}-{D4}` (reset bulanan) |
| DeliveryOrder | `DO-{yyyyMM}-{D4}` (reset bulanan) |
| PosSale | `POS-{yyyyMMdd}-{D4}` (reset harian) |
| CashierShift | `SHIFT-{yyyyMMdd}-{D4}` (reset harian) |

> Product code (`ProductService.GenerateCodeAsync`) berbasis kategori, bukan
> dokumen bertanggal — **di luar scope** item ini.

### Domain
Entity `NumberSequence`:
- `Code` — key dokumen: `PurchaseOrder` | `SalesOrder` | `GoodsReceipt` |
  `DeliveryOrder` | `PosSale` | `CashierShift`
- `Prefix` — mis. `PO`
- `DateFormat` — `yyyyMM` | `yyyyMMdd` | kosong
- `Padding` — int, mis. `4`
- `ResetPeriod` — enum `Never` | `Daily` | `Monthly` | `Yearly`
- `Separator` — mis. `-`

Entity `NumberSequenceCounter` (pemisah counter agar atomik):
- `SequenceCode`, `PeriodKey` (mis. `202607`), `LastValue`
- Unique index `(SequenceCode, PeriodKey)`.

### Service
`IDocumentNumberService`:
- `Task<string> NextAsync(string code, DateTime docDate, CancellationToken ct)`
  → mis. `"PO-202607-0001"`.
- **Race-safe:** increment counter dalam transaksi dengan
  `UPDATE NumberSequenceCounter SET LastValue = LastValue + 1 ... OUTPUT/SELECT`
  (row lock SQL Server) — dua panggilan paralel menghasilkan nomor berbeda.
- **Kontinuitas data lama:** bila belum ada baris counter untuk `PeriodKey`
  tertentu, inisialisasi `LastValue` sekali dari **max nomor dokumen existing**
  pada period tersebut (query backfill), lalu increment — nomor lanjut mulus,
  tidak reset ke 0001.

### Retrofit
- Ganti 6 method `GenerateNumberAsync` privat (PO/SO/GRN/DO/POS/Shift) dengan
  panggilan `IDocumentNumberService.NextAsync(...)`.
- Format hasil **dipertahankan persis** seperti tabel di atas.

### Web
- Resource `settings.document-numbering`: halaman konfigurasi prefix/padding/
  reset-period per dokumen (edit `NumberSequence`).

### Seed & permission
- Seed 6 `NumberSequence` dengan format yang ada sekarang.
- Permission `settings.document-numbering`.

### Tests
- Regresi: tiap service tetap menghasilkan format string yang sama.
- Konkurensi: 2+ `NextAsync` paralel → nomor unik (tak ada duplikat).
- Kontinuitas: dengan data dokumen existing, nomor berikutnya = max+1.

---

## Item 4 — Company Settings + wire ke struk POS

### Domain
Entity `CompanySetting` (single-row, `Id = 1`, get-or-create):
- `CompanyName`, `Address`, `Phone`, `Email`, `TaxId` (NPWP)
- `LogoUrl`
- `ReceiptHeader`, `ReceiptFooter`

### Application / Infrastructure
- `ICompanySettingService.GetAsync()` (buat baris default bila belum ada) &
  `UpdateAsync(...)`.
- Upload logo: **reuse** mekanisme upload gambar produk yang sudah ada.

### Web
- Resource `settings.company`: Form `.cf`.

### Wire struk POS
- Di `Components/Pages/Cashier/Pos/PosSaleDetail.razor` blok `.pos-receipt`:
  - Header menampilkan `CompanyName` / `Address` / `Phone` / `TaxId` + `ReceiptHeader`.
  - Footer memakai `ReceiptFooter` (menggantikan teks hardcoded
    "Thank you for your visit 🙏").
  - Nama gudang (`WarehouseName`) tetap sebagai sub-line.
  - Inject `ICompanySettingService`.

### Permission
- `settings.company` (view/edit).

---

## Item 5 — Dashboard skeleton (`.cr`)

- Refactor `Components/Pages/Home.razor` dari style lama `.stat-card`/`.d-card`
  ke pola `.cr-hero`/`.cr-kpis` (Atlas) yang sudah dipakai `ShiftDetail` &
  `PosSaleDetail`.
- **Pertahankan** data produk/stok existing (`ProductService.GetDashboardAsync`),
  disusun dalam kerangka KPI yang mudah ditambah section Fase 4 (omzet, jumlah
  transaksi, stok menipis, PO/SO pending approval, AR/AP jatuh tempo).
- Bersihkan CSS `.stat-card`/`.d-card` yang tidak terpakai lagi.

---

## Cross-cutting

- **AppMenus:** grup baru `Settings` (`settings.company`,
  `settings.document-numbering`); `master.currencies` di grup `Master`.
- **BootstrapSeeder:** seed permission resource baru + IDR currency +
  6 NumberSequence + 1 CompanySetting default (idempoten).
- **Migration:** satu migration untuk Currency, NumberSequence,
  NumberSequenceCounter, CompanySetting.

## Rencana test (ringkas)

- `CurrencyServiceTests` — CRUD, unik code, single-base invariant.
- `DocumentNumberServiceTests` — format, konkurensi, kontinuitas.
- `CompanySettingServiceTests` — get-or-create, update.
- Regresi penomoran pada service existing (format tak berubah).

## Out of scope (ditunda)

- Exchange rate / kurs (Fase 3+ bila multi-currency).
- Pengisian data KPI dashboard baru (omzet, dsb.) — Fase 4.
- Refactor product code berbasis kategori.
