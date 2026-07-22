# Costing Method — Tahap 3: Average per Gudang — Design

**Tanggal:** 2026-07-21
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`
**Prasyarat:** Tahap 1 (Abstraksi) & Tahap 2 (Standard Cost) selesai — `ICostingService`, `ICostingSettingService`, seam inbound/outbound (`OnInboundAsync`/`GetOutboundUnitCostAsync` sudah bawa `warehouseId`), `CostingSetting`.

## Ringkasan

Tahap 3 mengaktifkan **Average per Gudang** sebagai metode HPP: biaya rata-rata bergerak dipelihara **per (varian × gudang)** alih-alih global per varian. Biaya per-gudang disimpan di `ProductStock.CostPrice` dan menjadi sumber kebenaran untuk COGS keluar & valuasi. `ProductVariant.CostPrice` tetap ada sebagai **headline** (rata-rata tertimbang lintas gudang, di-refresh tiap inbound) untuk tampilan produk, suggested price PO, dan seed biaya awal.

Inisiatif penuh: Tahap 1 Abstraksi ✅ → Tahap 2 Standard ✅ → **Tahap 3 Average per Gudang (dok ini)** → Tahap 4 FIFO.

## Keputusan brainstorming (2026-07-21)

1. **Storage:** tambah `ProductStock.CostPrice` (per varian×gudang) sebagai sumber kebenaran COGS/valuasi. `ProductVariant.CostPrice` = headline weighted-average lintas gudang, di-maintain tiap inbound.
2. **StockTransfer disatukan:** leg tujuan selalu memanggil `OnInboundAsync`. Untuk MA & Standard ini **no-op terbukti** (MA: `inUnitCost = CostPrice` global → hasil = `CostPrice`; Standard: OnInbound no-op). Untuk per-gudang: biaya pindah & rata-rata gudang tujuan dihitung ulang. StockTransferService tetap method-agnostic.
3. **GL tidak berubah:** metode average → tidak ada variance (beda dengan Standard). Transfer value-preserving pada akun Persediaan tunggal → tanpa jurnal. Tidak ada akun/PostingConfiguration baru.
4. **Lock tidak berubah:** metode dipilih saat go-live (terkunci setelah `StockMovement` pertama).

## Arsitektur

### 1. Domain / EF

- **`ProductStock`** (`ErpOne.Domain.Entities`): tambah properti `decimal CostPrice { get; private set; }`.
  - Konstruktor tetap menerima qty; tambah method domain `void SetCost(decimal cost)` (validasi `>= 0`) yang dipakai strategi per-gudang. Konstruktor set `CostPrice = 0` default.
- **EF config** (`AppDbContext`, blok `ProductStock`): `e.Property(x => x.CostPrice).HasPrecision(18, 2);`
- **Migration** `AddProductStockCost` — kolom `CostPrice` (default 0) di tabel `ProductStocks`.
- `ProductVariant.CostPrice` & `ApplyMovingAverage` tetap (dipakai MA & sebagai headline).

### 2. Seam per-gudang (`CostingService`)

Tambah cabang `CostingMethod.AveragePerWarehouse`:

- **`OnInboundAsync(variantId, warehouseId, quantity, unitCost, ct)`** (dipanggil SETELAH `UpsertStockAsync`):
  1. Muat baris `ProductStock` untuk (variantId, warehouseId) — sudah ter-track/ada karena upsert barusan.
  2. `rowQtyBefore = row.Quantity - quantity` (qty sebelum inbound ini).
  3. Hitung moving average baris: `newRowCost = rowQtyBefore <= 0 ? unitCost : Round((rowQtyBefore * row.CostPrice + quantity * unitCost) / (rowQtyBefore + quantity))`. `row.SetCost(newRowCost)`.
  4. Refresh headline: muat semua `ProductStock` varian itu (Local-aware), hitung `totalQty = Σ qty`, `weighted = totalQty <= 0 ? unitCost : Round(Σ(qty × cost) / totalQty)`; set `variant.CostPrice` via `ApplyMovingAverageHeadline` — **atau** langsung lewat setter khusus. Karena `ProductVariant` tidak punya setter publik untuk CostPrice selain `ApplyMovingAverage`/`Update`, tambah method domain `void SetHeadlineCost(decimal cost)` (validasi `>= 0`) di `ProductVariant` untuk dipakai strategi ini.
- **`GetOutboundUnitCostAsync(variantId, warehouseId, quantity, ct)`**: kembalikan `ProductStock[variantId, warehouseId].CostPrice`; bila baris tidak ada / `Quantity == 0` dan `CostPrice == 0`, fallback ke `variant.CostPrice` (headline). (Local-aware: cek `db.ProductStocks.Local` dulu, lalu DB — konsisten dengan pola CostingService yang ada.)

> Rounding pakai helper yang sama: `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.

Cabang `MovingAverage` & `StandardCost` **tidak berubah**. `Fifo` tetap `NotSupportedException`.

### 3. Kontrak seeding baris & UpsertStock

`UpsertStockAsync` saat ini membuat `ProductStock` baru hanya dengan qty (CostPrice default 0). Untuk per-gudang, `OnInboundAsync` men-set `CostPrice` baris sesudahnya (langkah §2.3), jadi baris baru langsung dapat biaya = `unitCost` (karena `rowQtyBefore <= 0`). Tidak perlu ubah `UpsertStockAsync`.

### 4. StockTransfer (unified inbound leg)

`StockTransferService.PostAsync` (loop per baris) — tambah pemanggilan inbound di gudang tujuan **setelah** kedua upsert:

```
var cost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, t.SourceWarehouseId, line.Quantity, ct);
// (StockMovement out@source + in@dest pakai `cost`, seperti sekarang)
await db.UpsertStockAsync(line.ProductVariantId, t.SourceWarehouseId, -line.Quantity, ct);
await db.UpsertStockAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, ct);
await costing.OnInboundAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, cost, ct); // BARU
```

- **MA:** OnInbound global; `inUnitCost = cost = variant.CostPrice` → weighted average = `CostPrice` tak berubah (bit-identical).
- **Standard:** OnInbound no-op.
- **Per-gudang:** rata-rata gudang tujuan dihitung ulang dari `cost` sumber; gudang asal tak berubah (outbound). Total nilai kekal.

### 5. GL / auto-posting

Tidak ada perubahan. GRN tetap `Dr Persediaan (aktual) / Cr GR-IR (aktual)` (untuk average, biaya inbound = aktual → tak ada variance). Transfer tetap tanpa jurnal (value-preserving, akun Persediaan tunggal). `JournalPostingService` tidak disentuh.

### 6. Read / valuasi (bercabang per metode)

`ProductStock.CostPrice` **hanya dipelihara di mode Average-per-gudang**. Di MA & Standard kolom itu tidak dipakai (tetap 0), dan biaya yang benar tetap `variant.CostPrice` (global). Karena itu read sites memilih sumber sesuai metode aktif — **tidak** boleh selalu membaca `s.CostPrice` (akan salah untuk MA: baris per-gudang jadi basi vs global MA berjalan).

Yang **berubah** hanya tampilan biaya per baris gudang di **`StockService`** — `StockLevelDto` (`GetLevelsPagedAsync`, `BuildLevelQuery`): service ambil `method = await settings.GetMethodAsync(ct)`, lalu `perWh = method == AveragePerWarehouse`, dan proyeksi `select ... (perWh ? s.CostPrice : v.CostPrice)` (EF menerjemahkan jadi `CASE`).

Yang **TIDAK berubah** (dan alasannya):
- **Dashboard total nilai persediaan** (`ProductService.GetDashboardAsync`, `Σ s.Quantity × v.CostPrice`): tetap benar. Karena `v.CostPrice` = weighted-avg, `Σ_w qty_w × headline = totalQty × headline = totalValue`. Total tepat tanpa perubahan.
- **Inventory Valuation report** (`InventoryValuationReportService`): movement-based (`Σ qty × m.UnitCost` s.d. tanggal), method-agnostic — mutasi keluar sudah ter-stamp biaya per-gudang, jadi otomatis benar.

> Konsekuensi: **tidak perlu backfill** kolom baru (MA/Standard tak membaca `s.CostPrice`; per-gudang dipilih saat go-live → baris terbentuk dengan biaya benar sejak inbound pertama).

### 7. Pilihan metode (Settings)

- `CostingSettingService.UpdateMethodAsync`: perluas guard agar menerima `MovingAverage`, `StandardCost`, **`AveragePerWarehouse`**; tolak `Fifo`.
- `CostingSettingIndex.razor`: tambah opsi dropdown **"Average per Warehouse"**.

### 8. Tests

**Kelas baru `AveragePerWarehouseTests`** (DB terisolasi; set metode = AveragePerWarehouse via entity):
1. **Biaya independen per gudang:** varian sama, GRN 10 @ 1000 ke WH-A dan GRN 10 @ 1400 ke WH-B → `ProductStock[A].CostPrice == 1000`, `ProductStock[B].CostPrice == 1400`.
2. **Outbound pakai biaya gudangnya:** keluar dari WH-B → COGS = 1400; dari WH-A → 1000.
3. **Moving average per gudang:** WH-A terima 10@1000 lalu 10@1200 → `ProductStock[A].CostPrice == 1100`.
4. **Headline weighted-avg:** setelah #1, `variant.CostPrice == Round((10×1000 + 10×1400)/20) == 1200`.
5. **Transfer memindah biaya:** WH-A(10@1000) → transfer 5 ke WH-B(kosong) → `ProductStock[B].CostPrice == 1000`; WH-A cost tetap 1000; total nilai kekal.

**Regresi (wajib):** seluruh suite MA & Standard tetap hijau tanpa perubahan angka (termasuk transfer no-op, GRN, POS, DO). Read sites bercabang per metode → di MA/Standard valuasi & StockLevel tetap memakai `v.CostPrice` (perilaku lama, bit-identical).

## Non-Goals (Tahap 3)

- FIFO (Tahap 4).
- Standard cost per gudang (Standard tetap global).
- Migrasi basis biaya saat ganti metode (terkunci setelah transaksi).
- Jurnal transfer antar-gudang (tetap tanpa jurnal; akun Persediaan tunggal).

## Batasan yang diketahui

- Headline `variant.CostPrice` derivatif; edit manual di master saat mode per-gudang tidak menggerakkan COGS (baris per-gudang yang menggerakkan).
- Metode dipilih saat go-live (konsisten Tahap 1–2); karena per-gudang dipilih sebelum ada transaksi, `ProductStock.CostPrice` selalu terbentuk dari inbound pertama — tak perlu backfill.
- Read sites bercabang per metode (satu fetch `GetMethodAsync` + proyeksi bersyarat); bukan cabang yang mahal, tapi menambah satu query metode di StockLevel & valuasi.
