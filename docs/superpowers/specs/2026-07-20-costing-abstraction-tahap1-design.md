# Costing Method — Tahap 1: Abstraksi Costing — Design

**Tanggal:** 2026-07-20
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`

## Ringkasan

Tahap 1 dari inisiatif **HPP metode selectable**. Membungkus logika Moving Average yang saat ini hardcoded di balik seam **`ICostingService`** + setting **`CostingSetting`** (global, terkunci setelah ada transaksi stok), **tanpa mengubah perilaku** (Moving Average tetap identik; dijamin test regresi). Ini fondasi wajib agar metode lain (Standard Cost, Average per gudang, FIFO) tinggal "colok" sebagai strategi di tahap berikutnya.

Inisiatif penuh dipecah: **Tahap 1 Abstraksi (dok ini)** → Tahap 2 Standard Cost → Tahap 3 Average per gudang → Tahap 4 FIFO. Tiap tahap spec+plan sendiri.

## Keputusan brainstorming (2026-07-20)

1. **Metode dibutuhkan (jangka panjang):** Moving Average (ada), Standard Cost, Average per gudang, FIFO. Tahap 1 hanya membangun abstraksi + Moving Average.
2. **Konfigurasi:** setting **global** company-wide; boleh diubah **hanya selama belum ada `StockMovement`**, lalu terkunci. Seed default = Moving Average.
3. **Seam:** dua tanggung jawab — `OnInboundAsync` (perbarui basis biaya) & `GetOutboundUnitCostAsync` (tentukan COGS keluar). StockMovement + UpsertStock **tetap di caller**.
4. **Kontrak MA:** `OnInboundAsync` dipanggil **setelah** `UpsertStockAsync`; total dihitung Local-aware → MA identik dengan sekarang (aman utk GRN multi-baris varian sama).

## Arsitektur

### 1. Domain / Enum

- `enum CostingMethod { MovingAverage, StandardCost, AveragePerWarehouse, Fifo }` (`ErpOne.Domain.Entities`). Tahap 1 hanya `MovingAverage` yang valid dipilih.
- Entity single-row **`CostingSetting : AuditableEntity`** (pola `PostingConfiguration`):
  ```
  Id (=1), CostingMethod Method
  private ctor // EF
  void SetMethod(CostingMethod method)
  ```
  Di-seed satu baris `Method = MovingAverage` (via `HasData` atau `AccountingSeeder`-style idempotent seeder; lihat §4).

### 2. Application (`src/ErpOne.Application/Inventory/Costing/`)

- `ICostingService`:
  ```
  Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);
  Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default);
  ```
  - `OnInboundAsync`: dipanggil SETELAH stok di-upsert; memperbarui basis biaya varian. MA: hitung ulang `CostPrice` (abaikan `warehouseId`). `warehouseId` disertakan sejak Tahap 1 agar signature stabil utk Per-gudang (Tahap 3). (Future: FIFO tambah layer; Standard: no-op; Per-gudang: perbarui biaya (varian×gudang).)
  - `GetOutboundUnitCostAsync`: kembalikan unit cost utk pengeluaran. MA: `CostPrice` berjalan (abaikan `warehouseId`/`quantity`). (Future: FIFO konsumsi layer → unit cost tertimbang; Per-gudang: biaya gudang itu.)
- `ICostingSettingService`:
  ```
  Task<CostingMethod> GetMethodAsync(CancellationToken ct = default);
  Task<CostingSettingDto> GetAsync(CancellationToken ct = default);
  Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default);
  ```
  - `UpdateMethodAsync`: **tolak** bila `db.StockMovements.Any()` (ValidationException "Metode HPP terkunci: sudah ada transaksi stok."). Tahap 1: tolak method selain `MovingAverage` ("Metode belum didukung.").
- `CostingSettingDto(CostingMethod Method, bool Locked)` — `Locked = ada StockMovement`.

### 3. Infrastructure (`src/ErpOne.Infrastructure/Services/Inventory/`)

- `CostingService(AppDbContext db, ICostingSettingService settings) : ICostingService`:
  - Baca metode aktif (`settings.GetMethodAsync`), switch strategi. Tahap 1 hanya cabang `MovingAverage`; cabang lain `throw new NotSupportedException(...)`.
  - **MovingAverage.OnInboundAsync(variantId, qty, unitCost):**
    ```
    var variant = load ProductVariant(variantId)
    var totalAfter = await db.TotalOnHandLocalAwareAsync(variantId, ct)   // Local-aware lintas gudang
    var totalBefore = totalAfter - qty
    variant.ApplyMovingAverage(totalBefore, qty, unitCost)                // domain method existing — math sama
    ```
  - **MovingAverage.GetOutboundUnitCostAsync(variantId, _, _):** `return variant.CostPrice`.
- Helper baru `StockReadExtensions.TotalOnHandLocalAwareAsync(this AppDbContext db, int variantId, ct)`:
  - Total qty varian **lintas semua gudang**, memperhitungkan `ProductStocks.Local` (Added/Modified belum flush) agar konsisten dgn `UpsertStockAsync`. Gabungkan: nilai dari entitas Local untuk baris yang dilacak + nilai DB untuk baris yang tak dilacak (hindari dobel-hitung).
- `CostingSettingService(AppDbContext db) : ICostingSettingService`.
- DI: `AddScoped<ICostingService, CostingService>()`, `AddScoped<ICostingSettingService, CostingSettingService>()`.

### 4. EF wiring

- DbSet `CostingSettings`. Config: key Id. `Method` `HasConversion<string>()` maxlen 20.
- Seed baris tunggal `Method=MovingAverage`. Pilih salah satu (plan tentukan): `HasData(new { Id=1, Method=... })` **atau** idempotent di `AccountingSeeder`/BootstrapSeeder + `CustomWebApplicationFactory.InitializeDatabase` (agar test punya setting). **Karena costing dipakai semua alur stok, setting WAJIB ada di test** → bila `HasData`, otomatis ada via EnsureCreated; bila seeder, harus dipanggil di InitializeDatabase. **Rekomendasi: `HasData`** (paling sederhana, otomatis ada di test).
- `tablePrefixes`: `[nameof(CostingSetting)] = "M_"`.
- Migration `AddCostingSetting`.

### 5. Titik refactor (ganti akses costing langsung → `ICostingService`)

Inject `ICostingService` ke service berikut; ganti pemanggilan:

**Masuk (inbound) — ganti `variant.ApplyMovingAverage(...)` → `await costing.OnInboundAsync(variantId, warehouseId, qty, unitCost, ct)` (dipanggil SETELAH `UpsertStockAsync`):**
- `GoodsReceiptService.PostAsync` — hapus perhitungan `totalBefore`/`addedPerVariant` yang manual (kini di dalam service); urutan jadi: add StockMovement → `UpsertStockAsync` → `OnInboundAsync`.
- `IStockService` impl `RecordOpeningAsync` — saldo awal (inbound).
- `IStockService` impl `RecordAdjustmentAsync` — untuk delta **positif** (yang saat ini me-recompute MA).

**Keluar (outbound) — ganti baca `variant.CostPrice` → `await costing.GetOutboundUnitCostAsync(variantId, warehouseId, qty, ct)`:**
- `PosSaleService.CreateSaleAsync` — cost per baris (snapshot COGS).
- `DeliveryOrderService.PostAsync` — set `UnitCost` baris DO.
- `StockTransferService.PostAsync` — cost mutasi.
- `StockOpnameService.PostAsync` — cost mutasi selisih.
- `IStockService` impl `RecordAdjustmentAsync` — delta **negatif**.
- (Retur Pembelian F2a — saat diimplementasi, ikut pola ini.)

> Catatan: `ProductVariant.ApplyMovingAverage` & `CostPrice` **tetap ada** (dipakai oleh strategi MA). Yang berubah: caller tak lagi memanggilnya langsung.

### 6. Web (minimal, Tahap 1)

- Halaman/kartu Settings (mis. di grup Settings) menampilkan **metode HPP aktif** + indikator terkunci. Tahap 1: hanya `Moving Average`, pemilih dinonaktifkan bila `Locked`. (Pemilih aktif penuh menyusul saat ada metode kedua.)
- Menu resource mis. `settings.costing` (`[ActIndex, ActEdit]`) + seed permission otomatis. (Opsional Tahap 1 — boleh read-only dulu.)

### 7. Tests

- **Regresi (utama, wajib):** seluruh integration test yang menyentuh costing tetap **hijau tanpa perubahan angka** — GRN (termasuk multi-baris varian sama), POS (COGS), DeliveryOrder, StockTransfer, StockOpname, StockAdjustment, opening. Ini bukti "no behavior change".
- **Baru:**
  1. `CostingSetting` default = MovingAverage; `GetMethodAsync` = MovingAverage.
  2. `UpdateMethodAsync` **ditolak** setelah ada `StockMovement` (ValidationException).
  3. `UpdateMethodAsync(StandardCost/Fifo/...)` ditolak di Tahap 1 ("belum didukung").
  4. MA via service: GRN 2 baris varian sama → `CostPrice` akhir identik dgn perhitungan manual (kunci subtlety §D).
- Bila pakai seeder (bukan HasData): pastikan `CustomWebApplicationFactory.InitializeDatabase` menyeed CostingSetting; kalau tidak, service gagal karena setting null.

## Non-Goals (Tahap 1)

- Implementasi Standard Cost / Average per gudang / FIFO (tahap 2–4).
- UI pemilih metode penuh + wizard setup.
- Migrasi basis biaya saat ganti metode (tak relevan — metode terkunci setelah ada transaksi).
- Mengubah rumus/hasil Moving Average (harus identik).

## Batasan yang diketahui

- `GetOutboundUnitCostAsync` mengembalikan **unit cost** (bukan total) agar caller tak berubah pola (kalikan qty sendiri). Untuk FIFO nanti, satu pengeluaran lintas layer → unit cost tertimbang (rata2 layer terkonsumsi); caller tetap tak berubah.
- Tahap 1 strategi MA mengabaikan `warehouseId` (MA global), tapi param sudah ada di signature `OnInboundAsync`/`GetOutboundUnitCostAsync` sejak Tahap 1 agar Per-gudang (Tahap 3) tak perlu mengubah caller lagi.
- Setting berlaku company-wide (bukan per produk/kategori) — sesuai keputusan.
