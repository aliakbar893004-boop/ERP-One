# Costing Method — Tahap 4: FIFO (Layer-based) — Design

**Tanggal:** 2026-07-21
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`
**Prasyarat:** Tahap 1 (Abstraksi), Tahap 2 (Standard), Tahap 3 (Average per gudang) selesai — seam `ICostingService` (`OnInboundAsync`/`GetOutboundUnitCostAsync` bawa `warehouseId`), `CostingSetting`, `ProductStock.CostPrice`, display per-gudang.

## Ringkasan

Tahap 4 (terakhir) menambah **FIFO** berbasis **layer**: tiap mutasi masuk membuat satu *cost layer* (qty, biaya, urutan) per (varian × gudang); mutasi keluar mengonsumsi layer **tertua dulu** dan menghasilkan biaya **tertimbang** dari layer yang terkonsumsi. Konsumsi terjadi **di dalam `GetOutboundUnitCostAsync`** (mutasi), sesuai antisipasi Tahap 1 — nol perubahan callsite outbound.

Inisiatif penuh selesai: Tahap 1 ✅ → 2 ✅ → 3 ✅ → **4 FIFO (dok ini)**.

## Keputusan brainstorming (2026-07-21)

1. **Konsumsi di `GetOutboundUnitCostAsync`:** cabang FIFO mengurangi sisa layer & mengembalikan biaya tertimbang. Nol perubahan callsite (kelima pemanggil outbound sudah memanggil tepat sekali per baris, persis sebelum mencatat mutasi). Asimetri semantik (read murni untuk 3 metode lain, bermutasi untuk FIFO) diterima & didokumentasikan.
2. **Layer per (varian × gudang):** konsisten dengan model per-gudang Tahap 3 & dengan outbound yang selalu dari satu gudang. Transfer: konsumsi layer gudang asal, buat layer baru di gudang tujuan.
3. **GL tidak berubah:** FIFO = biaya aktual → tanpa variance. GRN Dr Persediaan = aktual, transfer value-preserving, tanpa akun/PostingConfiguration baru.
4. **Urutan FIFO = `Id` ascending** (urutan insert), tanpa kolom Sequence terpisah.

## Arsitektur

### 1. Domain / EF

- Entity baru **`CostLayer`** (`ErpOne.Domain.Entities`, folder `Entities/Inventory`):
  ```
  int Id
  int ProductVariantId
  int WarehouseId
  decimal UnitCost      // biaya layer (18,2)
  int OriginalQty
  int RemainingQty
  private ctor // EF
  ctor(int productVariantId, int warehouseId, decimal unitCost, int quantity)  // RemainingQty = OriginalQty = quantity; validasi qty>0, unitCost>=0
  int Consume(int qty)  // ambil min(qty, RemainingQty); kurangi RemainingQty; kembalikan jumlah yang diambil
  ```
- **EF config** (`AppDbContext`): `HasKey(Id)`; `Property(UnitCost).HasPrecision(18,2)`; FK ke `ProductVariant` & `Warehouse` (`OnDelete Restrict`); index `(ProductVariantId, WarehouseId, Id)` untuk query konsumsi FIFO.
- **`tablePrefixes`**: daftarkan `[nameof(CostLayer)]` dengan prefix yang sama seperti tabel transaksional lain (cek prefix `StockMovement`/`ProductStock` saat implementasi — samakan; kemungkinan tanpa prefix master `M_`).
- **Migration** `AddCostLayer`.

### 2. Seam FIFO (`CostingService`)

Tambah cabang `CostingMethod.Fifo`:

- **`OnInboundAsync(variantId, warehouseId, quantity, unitCost, ct)`** (dipanggil SETELAH `UpsertStockAsync`):
  1. `if (quantity <= 0) return;`
  2. `db.CostLayers.Add(new CostLayer(variantId, warehouseId, unitCost, quantity));`
  3. Refresh biaya tampilan (§3).
- **`GetOutboundUnitCostAsync(variantId, warehouseId, quantity, ct)`** (dipanggil persis sebelum mencatat mutasi keluar):
  1. Muat layer (v,w) dengan `RemainingQty > 0`, **Local-aware** (gabung `db.CostLayers.Local` + DB yang belum dilacak), urut `Id` ascending.
  2. Konsumsi: iterasi layer, `take = Math.Min(need, layer.RemainingQty)`, `layer.Consume(take)`, `acc += take * layer.UnitCost`, `need -= take`, sampai `need == 0` atau layer habis.
  3. `var consumedQty = quantity - need;` `unit = consumedQty <= 0 ? await CurrentCostPriceAsync(...) : Round(acc / consumedQty);`
     (Fallback headline bila tak ada layer — mestinya tak terjadi karena stok divalidasi upstream; jaga-jaga.)
  4. Refresh biaya tampilan (§3).
  5. `return unit;`

Cabang `MovingAverage`, `StandardCost`, `AveragePerWarehouse` **tidak berubah**.

> Local-aware wajib: dalam satu transaksi (mis. GRN multi-baris lalu penjualan, atau transfer) layer yang baru dibuat/dikonsumsi belum di-flush.

### 3. Biaya tampilan (`ProductStock.CostPrice` + headline)

Helper `RefreshFifoDisplayAsync(variantId, warehouseId, ct)` dipanggil di akhir OnInbound & GetOutbound FIFO:
- `ProductStock[v,w].CostPrice` = rata-rata tertimbang **sisa layer** gudang itu: `Σ(remaining × unitCost) / Σ remaining` (Round 2dp), `0` bila tak ada sisa. (`row.SetCost(...)`.)
- `variant.CostPrice` (headline) = weighted lintas **semua** gudang atas sisa layer varian: `Σ(remaining × unitCost) / Σ remaining` (Round), via `SetHeadlineCost`. Bila total 0, biarkan nilai terakhir (jangan set 0 agar suggested-price/PO tak jadi 0) — set hanya bila total > 0.

Kedua sumber ini Local-aware (baca `db.CostLayers.Local` + DB).

### 4. Read / display

- **`StockService`** — perluas flag: `perWh = method is AveragePerWarehouse or Fifo`. FIFO ikut menampilkan biaya per-gudang (`s.CostPrice`) di Stock Levels (di `GetLevelsPagedAsync` & `BuildLevelQuery`).
- **Dashboard total** & **Inventory Valuation report**: tetap benar tanpa perubahan (headline weighted → `Σ qty×headline` = total sisa; report movement-based).

### 5. StockTransfer — tanpa perubahan

Leg tujuan sudah memanggil `OnInboundAsync` (Tahap 3). FIFO: `GetOutbound(sumber)` mengonsumsi layer gudang asal → biaya tertimbang; `OnInbound(tujuan)` membuat layer baru di gudang tujuan pada biaya itu. Otomatis benar; StockTransferService tidak disentuh di Tahap 4.

### 6. GL / auto-posting — tanpa perubahan

FIFO tak menimbulkan variance; GRN, POS, DO, transfer memakai jalur yang sama. `JournalPostingService` tidak disentuh.

### 7. Pilihan metode (Settings)

- `CostingSettingService.UpdateMethodAsync`: perluas guard agar menerima **keempat** metode (`MovingAverage`, `StandardCost`, `AveragePerWarehouse`, `Fifo`). Guard tetap menolak nilai enum **tak dikenal** (`(CostingMethod)n` di luar 0..3) dengan "Metode belum didukung." sebagai jaring pengaman.
- `CostingSettingIndex.razor`: tambah opsi dropdown **"FIFO"**.
- **Dampak test existing (WAJIB diperbarui):** `CostingSettingServiceTests.UpdateMethodAsync_rejects_unsupported_method` (Tahap 1) memakai `CostingMethod.Fifo` sebagai kasus "belum didukung". Karena Fifo kini **diterima**, ubah test itu agar memakai nilai enum tak valid, mis. `(CostingMethod)999`, untuk tetap memverifikasi penolakan.

### 8. Tests

**Kelas baru `FifoCostingTests`** (DB terisolasi; set metode = Fifo via entity):
1. **Tertua-dulu lintas layer:** WH terima 10@1000 lalu 10@1200; outbound 15 → unit COGS = `Round((10×1000 + 5×1200)/15)` = **1066,67**; sisa layer = 5@1200.
2. **Outbound berikutnya:** keluar 5 lagi → unit COGS = 1200; sisa 0.
3. **Independen per gudang:** WH-A(10@1000) & WH-B(10@1400); outbound WH-B → 1400.
4. **Display:** setelah #1, `ProductStock[WH].CostPrice` = 1200 (sisa 5@1200 → weighted 1200); Stock Levels menampilkan angka per-gudang.
5. **Transfer:** WH-A(10@1000) → transfer 5 ke WH-B kosong → layer WH-B 5@1000; `GetOutbound(WH-B)` berikutnya = 1000.

**Regresi (wajib):** seluruh suite MA/Standard/Average tetap hijau tanpa perubahan angka.

## Non-Goals (Tahap 4)

- Pruning layer habis (RemainingQty=0) — disimpan untuk audit.
- LIFO / specific-identification.
- Migrasi basis biaya saat ganti metode (terkunci setelah transaksi).
- Jurnal transfer antar-gudang (tetap tanpa jurnal).

## Batasan yang diketahui

- Pembulatan: unit-cost FIFO tertimbang dibulatkan 2dp; `unit×qty` bisa berbeda beberapa sen dari jumlah biaya layer eksak. Konsisten dengan pembulatan HPP lain; dapat diterima.
- `GetOutboundUnitCostAsync` **bermutasi** untuk FIFO (mengonsumsi layer) — asimetri dengan metode lain yang read murni. Aman karena tiap callsite memanggil tepat sekali per baris outbound, di dalam transaksi dokumen.
- Tabel `CostLayer` bertambah seiring pembelian; layer habis tidak dihapus (audit). Pruning tahap lanjutan bila perlu.
- FIFO dipilih saat go-live (terkunci setelah `StockMovement` pertama), konsisten Tahap 1–3.
