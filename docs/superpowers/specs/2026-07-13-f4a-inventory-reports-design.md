# Fase 4a — Laporan Inventory (Design Spec)

**Tanggal:** 2026-07-13
**Fase:** 4 (Laporan & Dashboard) — sub-proyek **4a**
**Isi:** Kartu Stok (Stock Ledger) + Nilai Persediaan (Inventory Valuation)
**Prasyarat:** Fase 0 & 3 selesai (data transaksi lengkap). Referensi: `docs/DEVELOPMENT-PLAN.md` Fase 4.

---

## 1. Tujuan & Ruang Lingkup

Membangun dua laporan inventory pertama sekaligus **fondasi bersama grup Reports**
yang akan dipakai ulang oleh sub-proyek laporan berikutnya (4b–4e):

1. **Kartu Stok (Stock Ledger)** — list mutasi filterable + drill-down kartu stok
   per varian dengan saldo berjalan.
2. **Nilai Persediaan (Inventory Valuation)** — nilai persediaan **per tanggal**
   (`as of date`) hasil rekonstruksi dari buku besar mutasi.

Reports adalah lapisan **read-only** baru — **tidak ada entity/migration baru**.
Semua data berasal dari `StockMovement` (append-only ledger) dan `ProductStock`
(saldo materialized) yang sudah ada.

**Di luar lingkup 4a:** laporan penjualan/pembelian/laba (4b), aging AR/AP (4c),
shift kasir (4d), dashboard KPI (4e).

---

## 2. Keputusan Desain (hasil brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Bentuk Kartu Stok | **List + drill-down**: halaman utama = daftar mutasi filterable; klik baris/produk → kartu stok per varian dengan saldo berjalan. |
| Nilai Persediaan | **Per tanggal (`as of date`)**, rekonstruksi dari `StockMovement`. Pengelompokan **bisa di-toggle: per kategori atau per gudang**. |
| Format export | **Excel (.xlsx) asli + PDF asli** (bukan CSV, bukan print browser). |
| Library export | **ClosedXML** (Excel, MIT) + **QuestPDF** (PDF, Community license). |
| Fondasi bersama | Dibangun sebagai bagian sub-proyek pertama ini (bukan fase scaffold terpisah). |

**Insight kunci (Nilai Persediaan):** setiap `StockMovement` menyimpan `UnitCost`
(HPP per unit saat mutasi — biaya beli untuk IN, moving-average untuk OUT). Maka
nilai persediaan per tanggal = **running sum sederhana** `Σ(Quantity × UnitCost)`
untuk semua mutasi `≤ asOf` — tanpa simulasi moving-average penuh.
> Asumsi yang wajib diverifikasi saat implementasi: `UnitCost` pada mutasi OUT
> memang menyimpan moving-average pada saat itu. Jika tidak, valuasi as-of-date
> perlu rekonstruksi moving-average berjalan.

---

## 3. Arsitektur & Fondasi Bersama

Lapisan read-only baru, terisolasi dari service transaksional.

### 3.1 Application — `ErpOne.Application/Reports/`
- Interface service: `IStockLedgerReportService`, `IInventoryValuationReportService`.
- DTO laporan (`ReportsDtos.cs`).
- **Model export netral `ReportDocument`**:
  - `Title`, `Subtitle`, `FilterSummary` (string ringkas), `GeneratedAt`.
  - `Columns`: daftar `{ Name, Align (Left/Right/Center), Format (mis. "N0", "N2", tanggal) }`.
  - `Rows`: daftar baris; tiap baris = daftar nilai sel (+ flag baris subtotal/total opsional).
  - `TotalsRow` opsional.

### 3.2 Infrastructure — `ReportExporter`
`IReportExporter` (di Application) dengan implementasi di Infrastructure:
- `byte[] ToExcel(ReportDocument doc)` — via **ClosedXML**.
- `byte[] ToPdf(ReportDocument doc)` — via **QuestPDF**, dengan kop dokumen
  (nama/alamat/logo dari `CompanySetting` yang sudah ada via `ICompanySettingService`).
- Registrasi lisensi QuestPDF sekali di `Program.cs`:
  `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;`
- Registrasi DI di `ErpOne.Infrastructure/DependencyInjection.cs`.

### 3.3 Web — download & menu
- **Helper JS interop** `saveAsFile(fileName, base64)` (pola standar Blazor Server)
  di `wwwroot`, dipakai ulang semua laporan.
- **Export mengambil seluruh hasil terfilter**, bukan hanya halaman aktif.
- `AppMenus.cs`:
  - Tambah `AppAction ActExport = new("export", "Export", "bi-download")`.
  - Grup baru **Reports** dengan resource:
    - `reports.stock-ledger` — aksi `[ActIndex, ActExport]`.
    - `reports.inventory-valuation` — aksi `[ActIndex, ActExport]`.
- Seed otomatis permission baru di `Infrastructure/BootstrapSeeder.cs` (admin dapat akses).

### 3.4 Dependency NuGet baru
- `ClosedXML` → `ErpOne.Infrastructure`.
- `QuestPDF` → `ErpOne.Infrastructure`.

---

## 4. Service Data (read-only)

### 4.1 `IStockLedgerReportService`
- `GetMovementsPagedAsync(filter, page, pageSize, ct)`
  - Filter: search produk/varian, `warehouseId?`, `MovementType?`, `from?`, `to?`.
  - Join ke nama varian/produk/gudang + resolusi nomor dokumen dari `RefType`/`RefId`.
  - Return `PagedResult<StockMovementRowDto>`.
- `GetMovementsSummaryAsync(filter, ct)` — KPI: total masuk, total keluar,
  mutasi bersih, jumlah baris (untuk seluruh set terfilter).
- `GetStockCardAsync(variantId, warehouseId?, from, to, ct)`
  - **Saldo awal**: `Σ Quantity` & `Σ(Quantity × UnitCost)` untuk mutasi `< from`.
  - Daftar mutasi dalam `[from, to]` dengan **saldo berjalan** (qty & nilai) terhitung.
  - **Saldo akhir**.
- `BuildMovementsReportAsync(filter, ct)` → `ReportDocument` (untuk export list).
- `BuildStockCardReportAsync(variantId, warehouseId?, from, to, ct)` → `ReportDocument`.

### 4.2 `IInventoryValuationReportService`
- `GetValuationAsync(asOfDate, groupBy, warehouseId?, categoryId?, includeZeroQty, ct)`
  - `groupBy`: enum `ValuationGroupBy { Category, Warehouse }` (default `Category`).
  - Per varian: `qty = Σ Quantity (MovementDate ≤ asOf)`,
    `value = Σ(Quantity × UnitCost) (≤ asOf)`, `avgCost = qty == 0 ? 0 : value/qty`.
  - Dikelompokkan sesuai `groupBy`: subtotal per grup (kategori **atau** gudang) + grand total.
    - Catatan: saat `groupBy = Warehouse`, satu varian bisa muncul di >1 grup gudang
      (nilai per gudang); saat `groupBy = Category`, agregasi lintas gudang per varian
      (kecuali difilter `warehouseId`).
  - `includeZeroQty = false` → sembunyikan item qty 0 (default).
- `GetValuationSummaryAsync(...)` — KPI: total nilai, total qty, jumlah item.
- `BuildValuationReportAsync(...)` → `ReportDocument`.

**Invariant untuk test:** `asOf = hari ini` (tanpa batas atas) → total nilai valuasi
= `Σ ProductStock.Quantity × HPP` dan qty per varian = `ProductStock.Quantity`.

---

## 5. Halaman Web & Routing

Reuse desain global `.pi` (list) + `.cr-kpis`. Toolbar filter memakai gaya ringan
bersama (`.rp-toolbar` kecil atau elemen yang sudah ada — putuskan saat implementasi,
jangan bikin sistem desain baru).

### 5.1 `/reports/stock-ledger` → `StockLedgerIndex.razor`
- Toolbar filter: search produk/varian, dropdown gudang, dropdown tipe mutasi,
  rentang tanggal (default: bulan berjalan).
- KPI (`.cr-kpis`): total masuk, total keluar, mutasi bersih, jumlah baris.
- Tabel mutasi ter-paginasi (`Pager`): tanggal, produk/varian, gudang, tipe, ref dok, qty ±, HPP.
- Klik baris/produk → drill-down.
- Tombol **Export Excel / Export PDF** (seluruh set terfilter).

### 5.2 `/reports/stock-ledger/{variantId:int}` → `StockCard.razor`
- Query string: `warehouse`, `from`, `to`.
- Header info: varian + gudang + periode.
- Baris **Saldo Awal** → mutasi dengan **saldo berjalan (qty & nilai)** → baris **Saldo Akhir**.
- Varian tak ditemukan → pesan not-found + tombol kembali.
- Tombol Export Excel / PDF (kartu stok satu varian).

### 5.3 `/reports/inventory-valuation` → `InventoryValuationIndex.razor`
- Toolbar: "As of date" (default hari ini), **toggle "Group by: Kategori / Gudang"**,
  dropdown gudang, dropdown kategori, toggle "tampilkan qty 0".
- KPI (`.cr-kpis`): total nilai persediaan, total qty, jumlah item.
- Tabel dikelompokkan sesuai toggle (kategori **atau** gudang): baris varian
  (varian, qty, HPP rata2, nilai) + subtotal per grup + grand total.
- Tombol Export Excel / PDF.

---

## 6. Penanganan Error & Edge Case

- Read-only → fokus **empty-state** ramah (belum ada mutasi / semua ter-filter habis).
- Validasi rentang tanggal (`from ≤ to`); default aman bila kosong.
- Hasil besar: list & valuasi ter-paginasi di UI; **export tetap ambil seluruh set terfilter**.
- Drill-down varian tak ditemukan → not-found + tombol kembali.
- Otorisasi: aksi `Export` dicek terpisah dari `View` (permission `reports.*.export`).

---

## 7. Pengujian

Pola integration test SQLite `EnsureCreated` (seperti `StockServiceTests`):

- **Stock card**: `saldo awal + Σ mutasi = saldo akhir`; saldo akhir (asOf=hari ini,
  tanpa filter tanggal) = `ProductStock.Quantity`.
- **Valuasi as-of-date**: `asOf = hari ini` → total nilai = `Σ ProductStock.Qty × HPP`;
  `asOf` di masa lalu memotong mutasi setelahnya dengan benar; item qty 0 tersembunyi/tampil sesuai flag.
- **Group by**: grand total identik untuk `groupBy = Category` maupun `Warehouse`
  (hanya pengelompokan/subtotal yang berbeda, bukan totalnya).
- **Filter**: gudang / tipe / rentang tanggal mempersempit hasil sesuai harapan.
- **Exporter**: `ReportDocument → .xlsx` dibaca ulang via ClosedXML (sel & header sesuai);
  `→ PDF` menghasilkan byte non-kosong.

---

## 8. Urutan Build (≈3 commit, ikut konvensi modul)

1. **Fondasi Reports**: `ReportDocument` + `IReportExporter`/`ReportExporter`
   (ClosedXML + QuestPDF) + helper download JS + `ActExport` + grup menu Reports
   + seed permission + registrasi lisensi QuestPDF + dependency NuGet.
2. **Stock Ledger**: `IStockLedgerReportService` + tests → `StockLedgerIndex.razor`
   + `StockCard.razor` (drill-down) + menu.
3. **Inventory Valuation**: `IInventoryValuationReportService` + tests →
   `InventoryValuationIndex.razor` + menu.

---

## 9. Langkah Manual User Setelah Pull

- Restart app + sign out/in (BootstrapSeeder memberi permission `reports.*` baru ke admin).
