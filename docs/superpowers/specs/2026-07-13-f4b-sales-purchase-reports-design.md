# Fase 4b — Laporan Penjualan & Pembelian + Laba Kotor (Design Spec)

**Tanggal:** 2026-07-13
**Fase:** 4 (Laporan & Dashboard) — sub-proyek **4b**
**Isi:** Laporan Penjualan (POS + B2B gabungan), Laporan Pembelian (GRN), Laporan Laba Kotor (agregat).
**Prasyarat:** Fase 4a selesai (fondasi Reports: `ReportDocument`, `IReportExporter`, `saveAsFile`, menu group Reports, `ActExport`). Referensi: `docs/DEVELOPMENT-PLAN.md` Fase 4 + `2026-07-13-f4a-inventory-reports-design.md`.

---

## 1. Tujuan & Ruang Lingkup

Tiga laporan penjualan/pembelian di atas fondasi Reports yang sudah ada:

1. **Laporan Penjualan** — gabungan penjualan **POS + B2B** dalam satu daftar ternormalisasi, dengan revenue, HPP, dan laba kotor per baris.
2. **Laporan Pembelian** — dari Goods Receipt (GRN) yang sudah *Posted*.
3. **Laporan Laba Kotor** — agregasi laba kotor (revenue − HPP) + margin %, dikelompokkan per Produk / Kategori / Bulan.

Semua **read-only**, **tanpa entity/migration baru**. Data dari `PosSale(+Line)`, `DeliveryOrder(+Line)` ⋈ `SalesOrder(+Line)`, `GoodsReceipt(+Line)`, `StockMovement`, dan master terkait.

**Di luar lingkup 4b:** aging AR/AP (4c), laporan shift kasir (4d), dashboard KPI (4e).

---

## 2. Keputusan Desain (hasil brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Cakupan penjualan | **POS + B2B digabung** dalam satu "sales fact". |
| Dasar pengakuan B2B | **Delivery Order** (event barang keluar): revenue = harga SO-line × qty terkirim; HPP = `DeliveryOrderLine.UnitCost` × qty terkirim. |
| Dasar penjualan POS | `PosSale`/`PosSaleLine`: revenue = `LineTotal`; HPP = `UnitCost` × `Quantity`. |
| Dasar pembelian | **Goods Receipt** yang sudah *Posted*. |
| Laba Kotor | **Laporan agregat terpisah** (bukan sekadar kolom); Laporan Penjualan tetap detail transaksi dengan kolom HPP & laba. |
| Group by Laba Kotor | Toggle **Produk / Kategori / Bulan**. |

---

## 3. Arsitektur

Reuse penuh fondasi 4a:
- `Application/Reports/ReportDocument`, `IReportExporter` (Excel ClosedXML + PDF QuestPDF), helper JS `saveAsFile`.
- Pola halaman `.pi` + `.kpis` + `Pager`; export = build `ReportDocument` → exporter → JS download (seluruh set terfilter).

Tambahan:
- **Application/Reports** — DTO + interface: `ISalesReportService`, `IPurchaseReportService`, `IGrossProfitReportService`.
- **Infrastructure/Services** — implementasi + **`SalesFactProvider`** (helper internal) yang membangun query "sales fact" gabungan, dipakai bersama oleh `SalesReportService` dan `GrossProfitReportService` (DRY — satu sumber kebenaran definisi penjualan).
- **AppMenus.cs** — 3 resource baru di grup Reports: `reports.sales`, `reports.purchases`, `reports.gross-profit`, masing-masing `ReportActions` (`View` + `Export`). Seed otomatis via `AppMenus.AllPermissions`.
- **DI** — daftarkan 3 service (+ `SalesFactProvider`) di `DependencyInjection.cs`.

---

## 4. Model "Sales Fact" Gabungan

`SalesFactProvider` menghasilkan baris ternormalisasi `SalesFactRow`:

```
SalesFactRow(
    DateTime Date, string Channel /* "POS" | "B2B" */, string DocNumber,
    int WarehouseId, string WarehouseName,
    int VariantId, string Sku, string ProductName, int? CategoryId,
    string Party /* cashier name (POS) | customer name (B2B) */,
    int Quantity, decimal Revenue, decimal Cogs)
```
`GrossProfit = Revenue − Cogs` (dihitung di konsumen, bukan disimpan).

Sumber:
- **POS** — `PosSaleLine` ⋈ `PosSale` ⋈ `ProductVariant`/`Product` ⋈ `Warehouse`.
  Channel="POS", Date=`SaleDate`, DocNumber=`SaleNumber`, Party=`CashierName`,
  Quantity=`Quantity`, Revenue=`LineTotal`, Cogs=`UnitCost × Quantity`, Warehouse=`PosSale.WarehouseId`.
- **B2B** — `DeliveryOrderLine` ⋈ `DeliveryOrder` ⋈ `SalesOrder`/`SalesOrderLine` ⋈ `Customer` ⋈ `ProductVariant`/`Product` ⋈ `Warehouse`, **hanya DO ter-post**.
  Channel="B2B", Date=`DeliveryDate`, DocNumber=`DoNumber`, Party=nama customer,
  Quantity=`QuantityDelivered`, Revenue=hargaNetSOLine × `QuantityDelivered`,
  Cogs=`DeliveryOrderLine.UnitCost × QuantityDelivered`, Warehouse=gudang SO.

**Verifikasi saat implementasi:**
- Field harga di `SalesOrderLine` (UnitPrice + diskon/pajak) untuk menghitung "hargaNetSOLine" — samakan definisi net dengan yang dipakai modul SO.
- Nilai `DeliveryOrderStatus` yang berarti "ter-post/terkirim" (mana yang menggerakkan stok).
- `PosSale` tanpa customer → Party diisi `CashierName`; sesuaikan bila ada field customer.

Filter yang didukung provider: rentang tanggal, channel, gudang, customer (B2B), cashier (POS), cari SKU/produk.

---

## 5. Service (read-only)

### 5.1 `ISalesReportService`
- `GetSalesPagedAsync(SalesFilter, page, pageSize, ct)` → `PagedResult<SalesFactRow>` (order by Date desc).
- `GetSalesSummaryAsync(SalesFilter, ct)` → `SalesSummaryDto(int Lines, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent)`.
- `BuildSalesReportAsync(SalesFilter, ct)` → `ReportDocument` (seluruh set terfilter).

`SalesFilter(DateTime? From, DateTime? To, string? Channel, int? WarehouseId, int? CustomerId, string? CashierUserId, string? Search)`.

### 5.2 `IPurchaseReportService`
Dari GRN *Posted* (`GoodsReceiptLine` ⋈ `GoodsReceipt` ⋈ PO→Supplier ⋈ Variant/Product ⋈ Warehouse).
- `GetPurchasesPagedAsync(PurchaseFilter, page, pageSize, ct)` → baris (tanggal terima, GRN number, supplier, gudang, SKU, produk, qty, unit cost, nilai=qty×unitCost).
- `GetPurchaseSummaryAsync(PurchaseFilter, ct)` → `PurchaseSummaryDto(int Lines, int Qty, decimal TotalCost, int Receipts)`.
- `BuildPurchaseReportAsync(PurchaseFilter, ct)` → `ReportDocument`.

`PurchaseFilter(DateTime? From, DateTime? To, int? SupplierId, int? WarehouseId, string? Search)`.

**Verifikasi saat implementasi:** field HPP di `GoodsReceiptLine`, tanggal terima, dan cara link GRN→PO→Supplier.

### 5.3 `IGrossProfitReportService`
Reuse `SalesFactProvider` (definisi penjualan identik dgn Laporan Penjualan).
- `GetGrossProfitAsync(GrossProfitFilter, ct)` → `GrossProfitResultDto` berisi grup (`GroupName`, Revenue, Cogs, GrossProfit, MarginPercent, Qty) + grand total.
- `BuildGrossProfitReportAsync(GrossProfitFilter, ct)` → `ReportDocument`.

`GrossProfitFilter(DateTime? From, DateTime? To, string? Channel, GrossProfitGroupBy GroupBy)`
dengan `enum GrossProfitGroupBy { Product, Category, Month }`.
`MarginPercent = Revenue == 0 ? 0 : GrossProfit / Revenue * 100`.

---

## 6. Halaman Web

Reuse `.pi` + `.kpis` + toolbar filter + `Pager`. Tombol Export Excel/PDF (guard `*.export`).

- `/reports/sales` → **SalesReportIndex.razor**
  Toolbar: rentang tanggal (default bulan berjalan), channel (All/POS/B2B), gudang, customer, cari produk.
  KPI: Revenue, HPP (COGS), Laba Kotor, Margin %.
  Tabel: tanggal, channel, no dok, gudang, SKU, produk, pihak, qty, revenue, HPP, laba. Paginasi.
- `/reports/purchases` → **PurchaseReportIndex.razor**
  Toolbar: rentang tanggal, supplier, gudang, cari produk.
  KPI: Total Pembelian (nilai), Total Qty, Jumlah Penerimaan.
  Tabel: tanggal, GRN, supplier, gudang, SKU, produk, qty, unit cost, nilai. Paginasi.
- `/reports/gross-profit` → **GrossProfitIndex.razor**
  Toolbar: rentang tanggal, channel, toggle Group by (Produk/Kategori/Bulan).
  KPI: Revenue, HPP, Laba Kotor, Margin %.
  Tabel berkelompok: baris grup + Revenue, HPP, Laba, Margin % + grand total.

---

## 7. Penanganan Error & Edge Case
- Read-only → empty-state ramah.
- Validasi rentang tanggal (`from ≤ to`); default aman.
- Hasil besar: list ter-paginasi; export ambil seluruh set terfilter.
- B2B: hanya DO ter-post yang dihitung (DO draft diabaikan).
- Margin % saat revenue 0 → 0 (hindari bagi nol).
- Otorisasi Export dicek terpisah dari View.

---

## 8. Pengujian (integration, SQLite EnsureCreated)

- **Sales**: POS sale (via `IPosSaleService`) → baris revenue=`LineTotal`, HPP & laba benar; B2B (SO→DO posted) → baris dengan revenue & HPP benar; summary menjumlah revenue/HPP/laba benar; filter channel POS vs B2B mempersempit hasil.
- **Purchase**: GRN posted → baris dengan qty & nilai benar; filter supplier; summary (total cost, receipts) benar; GRN draft tidak muncul.
- **Gross profit**: laba = revenue − HPP; group by Produk menjumlah benar; margin % benar; **grand total laba kotor = `GetSalesSummaryAsync().GrossProfit` untuk filter setara** (invariant lintas-laporan).

---

## 9. Urutan Build (≈3 task; commit manual oleh user)

1. **Sales report** — DTO + `ISalesReportService` + `SalesFactProvider` + tests → `SalesReportIndex.razor` + menu `reports.sales`.
2. **Purchase report** — DTO + `IPurchaseReportService` + tests → `PurchaseReportIndex.razor` + menu `reports.purchases`.
3. **Gross Profit report** — DTO + `IGrossProfitReportService` (reuse `SalesFactProvider`) + tests → `GrossProfitIndex.razor` + menu `reports.gross-profit`.

---

## 10. Langkah Manual User Setelah Pull
- Restart app + sign out/in (BootstrapSeeder memberi permission `reports.sales/purchases/gross-profit` ke admin).
