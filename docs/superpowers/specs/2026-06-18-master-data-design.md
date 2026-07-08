# Desain Master Data — MyApp

- **Tanggal**: 2026-06-18
- **Status**: Disetujui (desain) — menunggu rencana implementasi
- **Lingkup**: Master data lengkap + kerangka transaksi (sketsa) untuk sistem retail (Inventori + POS + Pembelian + Katalog)

## 1. Konteks & Keputusan

Aplikasi adalah sistem manajemen produk/inventori berbasis .NET 10, Blazor, Clean Architecture
(Domain / Application / Infrastructure / Web), pola **service-based** (bukan MediatR), EF Core,
dengan rich domain entity (private setter, factory constructor, `Update()`, validasi di entity)
dan otorisasi berbasis permission (`AppMenus.cs`).

Master yang sudah ada: **Product, ProductCategory, Unit** (grup Master) dan **User, Role, Error Log** (grup Settings).

Keputusan desain yang disepakati:

| Topik | Keputusan |
|-------|-----------|
| Lingkup dokumen | Master lengkap + kerangka transaksi |
| Model stok | Saldo per gudang (`ProductStock`) + buku mutasi/ledger (`StockMovement`) |
| Varian produk | Setiap produk punya ≥1 varian; SKU, harga, stok pindah ke level varian |
| Atribut varian | Master terstruktur: `Attribute` + `AttributeValue` |
| Valuasi inventori | Moving Average |
| Konversi satuan | Ditunda (YAGNI) — pengembangan opsional, tidak masuk fase ini |

## 2. Konvensi Arsitektur (wajib diikuti tiap entity baru)

1. **Domain**: rich entity mewarisi `AuditableEntity`; private setter; factory constructor; method `Update()`; validasi (`SetXxx`) di dalam entity; `Id` `private set`.
2. **Application**: interface `IXxxService`, DTO (`XxxDtos.cs`), validator FluentValidation (`CreateXxxValidator.cs`); abstraksi umum di `Common/` (`ICurrentUser`, `IFileStorage`, `PagedResult`).
3. **Infrastructure**: implementasi `XxxService` di `Services/`; `DbSet` + konfigurasi inline di `AppDbContext.OnModelCreating`; migrasi EF Core.
4. **Web**: halaman Blazor di `Components/Pages/Master/<Nama>/` (List/Create/Edit) mengikuti pola Products/Units; resource otorisasi di `AppMenus.Groups` dengan aksi CRUD.
5. **Kode entity** (`Code`) di-`Trim().ToUpperInvariant()` seperti `ProductCategory`/`Unit`. SKU di-generate otomatis (per kategori) seperti yang sudah ada.

## 3. Restrukturisasi Produk (Product → Variant + Atribut)

### 3.1 Struktur

```
Product (induk/template)
 ├─ Name, Description, CategoryId, BrandId, BaseUnitId, TaxId?, Status
 ├─ Images (tetap di level produk, dipakai bersama varian)
 └─ ProductVariant (1..n)
      ├─ Sku (dikunci sejak dibuat), Barcode?
      ├─ Price, DiscountPrice?, CostPrice
      ├─ Weight?, Dimensions?, IsActive
      └─ AttributeValues (mis. {Ukuran: M, Warna: Merah})
```

### 3.2 Aturan

- **Setiap produk punya minimal 1 varian.** Produk tanpa varian nyata tetap memiliki **1 varian default** sehingga model seragam (stok & harga selalu di level varian). UI menyembunyikan kerumitan ini untuk produk sederhana.
- **SKU, Price, DiscountPrice, Stock TIDAK lagi di `Product`** — pindah ke `ProductVariant`. Field `Stock` lama di `Product` dihapus (digantikan model stok di Bagian 5).
- **`CostPrice`** (HPP) ada di varian untuk valuasi inventori dengan metode **Moving Average**.
- `Weight`/`Dimensions` pindah ke level varian (varian berbeda bisa beda berat/dimensi).

### 3.3 Atribut varian (2 master baru)

```
Attribute (master)              AttributeValue (master)
 ├─ Code, Name                   ├─ AttributeId (FK)
 └─ contoh: "Ukuran", "Warna"    ├─ Code, Value
                                 └─ contoh: S/M/L, Merah/Biru

ProductVariantAttribute (tabel jembatan)
 └─ ProductVariantId ↔ AttributeValueId
    (1 varian = kumpulan nilai, mis. {Ukuran:M, Warna:Merah})
```

## 4. Master Pendukung Baru

Semua mengikuti pola `Code` + `Name` + `Description`, plus field khusus:

| Master | Field khusus | Dipakai untuk | Link |
|--------|-------------|---------------|------|
| **Brand** (Merek) | LogoPath? | Katalog | `Product.BrandId` |
| **Supplier** (Pemasok) | Phone, Email, Address, Npwp, PaymentTermDays | Pembelian | Transaksi beli |
| **Customer** (Pelanggan) | Type (Umum/Member), Phone, Email, Address, Npwp | Penjualan/POS | Transaksi jual |
| **Warehouse** (Gudang) | Address, IsActive, IsDefault | Inventori | `ProductStock`, mutasi |
| **Tax** (Pajak) | Rate (%), IsInclusive | Harga/transaksi | `Product.TaxId` (default, bisa override di transaksi) |
| **PaymentMethod** (Metode Bayar) | Type (Tunai/Transfer/Kartu/QRIS), IsActive | POS | Transaksi jual |

**Unit**: memperbaiki gap lama — `Product.BaseUnitId` (FK ke `Unit`) ditambahkan sebagai satuan dasar. Konversi satuan ditunda.

## 5. Model Stok

```
ProductStock (saldo — untuk query cepat)
 ├─ ProductVariantId + WarehouseId  (unik)
 ├─ Quantity, ReservedQty?
 └─ = materialized balance dari agregasi mutasi

StockMovement (buku besar — sumber kebenaran)
 ├─ ProductVariantId, WarehouseId
 ├─ Type: In | Out | Transfer | Adjustment
 ├─ Quantity (bertanda), MovementDate
 ├─ RefType + RefId (mis. "Purchase"/"Sales"/"Opname")
 └─ Note
```

**Aturan:** setiap perubahan stok **selalu** lewat `StockMovement`; `ProductStock.Quantity` di-update transaksional dalam transaksi yang sama. Tidak ada perubahan stok langsung tanpa mutasi → jejak audit lengkap. Sumber kebenaran = `StockMovement`; `ProductStock` = cache saldo untuk performa.

## 6. Kerangka Transaksi (sketsa — desain detail di dokumen terpisah)

Semua transaksi = **header + lines**, dan semuanya bermuara ke `StockMovement`:

```
Purchase (Pembelian)      → saat barang diterima → StockMovement(In)
 ├─ Header: Supplier, Warehouse, Date, Status, TaxId
 └─ Lines:  Variant, Qty, CostPrice, Tax

Sales (Penjualan/POS)     → saat dikonfirmasi → StockMovement(Out)
 ├─ Header: Customer, Warehouse, Date, PaymentMethod, Status, TaxId
 └─ Lines:  Variant, Qty, Price, Discount, Tax

StockTransfer (Mutasi)    → StockMovement(Out di asal) + (In di tujuan)
 └─ FromWarehouse, ToWarehouse, Lines: Variant, Qty

StockAdjustment (Opname)  → StockMovement(Adjustment)
 └─ Warehouse, Lines: Variant, QtySelisih, Reason
```

Sketsa ini hanya memastikan struktur master sudah pas. Desain detail transaksi bukan bagian dari spec ini.

## 7. Otorisasi & UI

Mengikuti `AppMenus.cs` — tiap master jadi `AppResource` dengan aksi CRUD.

- **Grup "Master" (tambahan)**: `master.brands`, `master.suppliers`, `master.customers`, `master.warehouses`, `master.taxes`, `master.payment-methods`, `master.attributes`.
- Halaman Blazor baru di `Components/Pages/Master/<Nama>/` (List/Create/Edit) mengikuti pola Products/Units.
- Halaman Product diperluas: section **Varian** + pemilih **Brand / Unit / Pajak**.

## 8. Fase Pembangunan & Strategi Migrasi

| Fase | Isi | Catatan migrasi |
|------|-----|-----------------|
| **F0** | Master sederhana: Brand, Warehouse, Tax, PaymentMethod, Attribute + AttributeValue | Murni tambah tabel — aman, tanpa ubah data lama. Buat 1 Gudang default di sini. |
| **F1** | Refactor Product → Variant; tambah `BaseUnitId`, `BrandId`, `TaxId` di Product | ⚠️ Migrasi data: tiap Product lama → buat 1 varian default (SKU & harga lama pindah ke varian). |
| **F2** | Model stok: `ProductStock` + `StockMovement` | ⚠️ `Product.Stock` lama → seed jadi `StockMovement(Adjustment)` awal ke gudang default. |
| **F3** | Supplier & Customer | Tambah tabel — aman. |
| **F4** | Kerangka transaksi | Dokumen desain terpisah. |

**Risiko utama:** F1 & F2 mengubah data Product yang sudah ada. Strategi: migrasi EF Core + skrip data idempotent yang mem-backfill varian default dan saldo stok awal. Gudang default dibuat di F0 agar F2 punya tujuan stok.

## 9. Di Luar Lingkup (YAGNI)

- Konversi satuan (beli BOX, jual PCS).
- Varian dengan gambar sendiri (gambar tetap di level produk).
- Valuasi FIFO/LIFO (pakai Moving Average).
- Multi-mata uang, price list/tier, promo terstruktur, payment terms lanjutan.
- Desain detail transaksi (Pembelian/Penjualan/Mutasi/Opname).
