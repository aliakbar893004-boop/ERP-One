# Costing Method — Tahap 2: Standard Cost + Purchase Price Variance — Design

**Tanggal:** 2026-07-21
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`
**Prasyarat:** Tahap 1 (Abstraksi Costing) selesai — `ICostingService`, `ICostingSettingService`, `CostingSetting`, seam inbound/outbound sudah terpasang.

## Ringkasan

Tahap 2 mengaktifkan **Standard Cost** sebagai metode HPP kedua yang benar-benar dapat dipilih. Persediaan dinilai pada **biaya standar tetap per varian**; mutasi masuk (GRN) **tidak** mengubah biaya standar. Selisih antara biaya beli aktual dan standar (**Purchase Price Variance / PPV**) diposting ke akun GL baru sehingga buku besar tetap koheren (double-entry balance, persediaan di GL = valuasi standar).

Inisiatif penuh: Tahap 1 Abstraksi ✅ → **Tahap 2 Standard Cost (dok ini)** → Tahap 3 Average per gudang → Tahap 4 FIFO.

## Keputusan brainstorming (2026-07-21)

1. **PPV: proper.** Persediaan selalu dinilai standar; selisih aktual−standar diposting ke akun GL "Selisih Harga Beli" baru. Menyentuh COA seed, `PostingConfiguration`, dan `JournalPostingService.PostGoodsReceiptAsync`.
2. **Storage: pakai ulang `ProductVariant.CostPrice`.** Di mode Standard, `CostPrice` = biaya standar (di-set manual di master produk, tidak diubah mutasi masuk). Semua kode valuasi/COGS/jurnal/laporan yang sudah membaca `CostPrice` jalan tanpa perubahan.
3. **Metode selectable:** `UpdateMethodAsync` kini menerima `MovingAverage` dan `StandardCost`; menolak `AveragePerWarehouse`/`Fifo` ("belum didukung"). Aturan kunci (terkunci setelah ada `StockMovement`) tidak berubah.
4. **Isolasi test:** DB test = SQLite in-memory per test class (`IClassFixture`). Test Standard membalik metode di DB-nya sendiri → tidak memengaruhi suite MA di kelas lain.

## Arsitektur

### 1. Domain

- **Tidak ada field baru.** `ProductVariant.CostPrice` menjadi biaya standar saat metode = `StandardCost`.
- `CostingMethod.StandardCost` sudah ada (enum Tahap 1). Tidak ada perubahan enum.
- `ProductVariant.ApplyMovingAverage` & `CostPrice` tetap ada (dipakai strategi MA).

### 2. Chart of Accounts + PostingConfiguration

**Akun PPV baru** — `AccountingSeeder`:
- Kode **`5150`**, nama **`Selisih Harga Beli`**, tipe **`Expense`**, parent **`5000`** (grup Harga Pokok Penjualan), **postable = true**.
- Ditambahkan dalam daftar `defs` COA standar (blok yang hanya jalan bila `Accounts` kosong). **Untuk DB yang sudah ada akun** (COA sudah ter-seed), tambahkan blok idempotent terpisah di `AccountingSeeder.SeedAsync`: bila akun kode `5150` belum ada, buat sebagai child dari akun `5000`. Ini mirror pola seed idempotent yang sudah ada.

**PostingConfiguration** (`ErpOne.Domain.Entities.PostingConfiguration`):
- Tambah properti `int? PurchasePriceVarianceAccountId { get; private set; }`.
- Perluas method `Update(...)` dengan parameter `int? purchasePriceVariance` (ditambahkan di akhir daftar param, setelah `posCash`).
- Konfigurasi EF: tambah `e.HasOne<Account>().WithMany().HasForeignKey(x => x.PurchasePriceVarianceAccountId).OnDelete(DeleteBehavior.Restrict);` di blok `modelBuilder.Entity<PostingConfiguration>`.
- Seed default: `AccountingSeeder` yang mengisi `PostingConfiguration` (mapping akun sistemik) memetakan PPV → akun kode `5150`.

**DTO & service** (`ErpOne.Application.Accounting`):
- `PostingConfigurationDto` — tambah `int? PurchasePriceVarianceAccountId` (di akhir record).
- `UpdatePostingConfigurationRequest` — tambah `int? PurchasePriceVarianceAccountId`.
- `PostingConfigurationService.GetAsync/UpdateAsync` — teruskan field baru.

> Migration: perubahan `PostingConfiguration` (kolom baru) = satu migration `AddPurchasePriceVarianceAccount`. Akun COA `5150` di-seed via `AccountingSeeder` (runtime + test factory), **bukan** `HasData`, karena COA memang di-seed lewat seeder idempotent (bukan HasData).

### 3. CostingSettingService

`UpdateMethodAsync(CostingMethod method, ct)`:
- Ganti guard Tahap 1 (`method != MovingAverage → "belum didukung"`) menjadi: **izinkan** `MovingAverage` dan `StandardCost`; tolak `AveragePerWarehouse`/`Fifo` dengan `ValidationException("Metode belum didukung.")`.
- Guard lock (`db.StockMovements.AnyAsync()` → `"Metode HPP terkunci: sudah ada transaksi stok."`) **tidak berubah**.
- `GetMethodAsync`/`GetAsync` tidak berubah.

### 4. CostingService (strategi)

- **`OnInboundAsync`** — tambah cabang:
  ```
  case CostingMethod.StandardCost:
      return; // no-op: biaya standar tetap, mutasi masuk tak mengubah CostPrice
  ```
  (cabang `MovingAverage` tetap; `AveragePerWarehouse`/`Fifo` tetap `NotSupportedException`.)
- **`GetOutboundUnitCostAsync`** — tambah cabang `StandardCost => await CurrentCostPriceAsync(variantId, ct)` (identik hasilnya dengan MA: kembalikan `CostPrice`). Praktis MA & Standard berbagi jalur outbound; hanya inbound yang beda.

### 5. Auto-posting GRN (jantung PPV)

`JournalPostingService` — inject `ICostingSettingService settings`.

`PostGoodsReceiptAsync(GoodsReceipt grn, ct)`:
- Baca `method = await settings.GetMethodAsync(ct)`.
- **Bila `method != StandardCost`** (MA): **perilaku persis seperti sekarang** —
  `value = Σ Round(qty × line.UnitCost)`; `Dr Inventory value / Cr GR-IR value`.
- **Bila `method == StandardCost`:**
  1. `grValue = Σ Round(qty × line.UnitCost)` (aktual) → nilai Cr GR-IR (tetap aktual; cocok dengan Supplier Invoice yang menutup GR-IR di nilai net).
  2. Untuk tiap baris, ambil biaya standar = `CostPrice` varian (query `db.ProductVariants` untuk varian2 pada baris GRN). `invValue = Σ Round(qty × standardCost)` → nilai Dr Inventory (standar).
  3. `d = grValue − invValue`.
  4. Baris jurnal:
     - `(inventory, invValue, 0, "Inventory received @ standard")`
     - `(grIr, 0, grValue, "Goods received not invoiced")`
     - `(ppv, Math.Max(d, 0m), Math.Max(-d, 0m), "Purchase price variance")` — di-skip otomatis oleh `PostBalancedAsync` bila kedua sisi 0 (d = 0).
  5. `ppv = RequireAccount(cfg.PurchasePriceVarianceAccountId, "Purchase Price Variance")`.
- `Round` memakai helper `Round` yang sudah ada (`Math.Round(v, 2, MidpointRounding.AwayFromZero)`).

**Tanda variance:** aktual > standar → `d > 0` → **Dr PPV** (unfavorable, beban). aktual < standar → `d < 0` → **Cr PPV** (favorable). Debit = kredit tetap terjaga: `Dr(invValue) + Dr PPV(max(d,0))` vs `Cr GR-IR(grValue) + Cr PPV(max(−d,0))` → keduanya = `max(grValue, invValue)`.

> Urutan pemanggilan (sudah ada di `GoodsReceiptService.PostAsync`): loop upsert + `OnInboundAsync` per baris, lalu `PostGoodsReceiptAsync`. Di mode Standard `OnInboundAsync` no-op sehingga `CostPrice` = standar saat jurnal dibaca. Aman.

### 6. Web

- **Settings → Costing** (`CostingSettingIndex.razor`): dari read-only jadi **selector editable**.
  - Bila **tidak terkunci** (`Locked == false`): dropdown pilih `Moving Average` / `Standard Cost` + tombol Save → `UpdateMethodAsync`. Sukses → tampilkan konfirmasi.
  - Bila **terkunci**: dropdown disabled, tampilkan metode aktif + indikator terkunci (seperti Tahap 1).
  - AppMenus resource `settings.costing` action set jadi `[ActIndex, ActEdit]` (dari `ViewOnly`). Permission `settings.costing.edit` ter-seed otomatis.
- **Settings → Posting Configuration** (`PostingConfigForm.razor`): tambah field akun **`Purchase Price Variance (Selisih Harga Beli)`** di grid mapping, di-wire ke `PurchasePriceVarianceAccountId`.

### 7. Tests

**Kelas baru `StandardCostingTests`** (DB terisolasi; set metode = StandardCost langsung di DB-nya, mem-bypass lock untuk setup):
1. **Inbound no-op:** buat varian standar `CostPrice = 1000`, GRN 10 @ aktual 1300 → setelah post, `CostPrice` **tetap 1000** (bukti mutasi masuk tak mengubah standar).
2. **PPV unfavorable (aktual > standar):** GRN 10 @ 1300, standar 1000 → jurnal GRN punya `Dr Inventory 10000`, `Cr GR-IR 13000`, `Dr PPV 3000`. Verifikasi baris jurnal.
3. **PPV favorable (aktual < standar):** GRN 10 @ 800, standar 1000 → `Dr Inventory 10000`, `Cr GR-IR 8000`, `Cr PPV 2000`.
4. **Outbound COGS = standar:** setelah stok ada, DO/POS keluar → `UnitCost`/COGS baris = `CostPrice` standar.
5. **Method switch diterima:** pada DB tanpa `StockMovement`, `UpdateMethodAsync(StandardCost)` sukses; `GetMethodAsync` = StandardCost.

**Regresi (wajib):** seluruh suite MA existing tetap hijau tanpa perubahan angka (mode default tetap MovingAverage; cabang Standard hanya aktif bila metode = StandardCost).

**PostingConfiguration:** test `GetAsync` menyertakan `PurchasePriceVarianceAccountId` (ter-seed ke `5150`); `UpdateAsync` menyimpan field baru.

## Non-Goals (Tahap 2)

- **Cost roll / revaluasi** saat nilai standar diubah setelah ada transaksi (tidak ada jurnal revaluasi). Tahap lanjutan.
- Average per gudang & FIFO (Tahap 3–4).
- Alokasi PPV ke COGS (variance diakui penuh saat GRN).
- Migrasi basis biaya saat ganti metode (metode tetap terkunci setelah transaksi pertama).
- Standard cost per gudang (standar tetap global per varian).

## Batasan yang diketahui

- Nilai standar (`CostPrice`) dapat diedit di master produk kapan saja; mengubahnya **setelah** ada transaksi tidak membuat jurnal revaluasi → GL persediaan (nilai lama) bisa melenceng dari valuasi (qty × standar baru). Didokumentasikan; cost-roll di tahap berikutnya.
- Metode dipilih saat go-live (terkunci setelah `StockMovement` pertama) — konsisten dengan Tahap 1.
- Retur Pembelian (Fase 2a, belum ada) saat diimplementasi harus mengikuti pola: keluar @ standar, tanpa mengubah `CostPrice`.
