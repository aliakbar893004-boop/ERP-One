# Desain F1 — Refactor Product → Variant (struktural + UI multi-varian)

- **Tanggal**: 2026-06-18
- **Status**: Disetujui (desain) — menunggu rencana implementasi
- **Prasyarat**: F0 selesai & ter-deploy (master Brand, Warehouse, Tax, PaymentMethod, Attribute+AttributeValue ada di DB).
- **Spec induk**: `2026-06-18-master-data-design.md` (§3 Restrukturisasi Produk).

## 1. Tujuan & Keputusan

Memecah `Product` yang "flat" menjadi induk + varian, di mana SKU, harga, dan stok pindah ke
`ProductVariant`. Mendukung produk multi-varian (mis. Ukuran × Warna) sekaligus tetap mulus untuk
produk sederhana (1 varian).

| Topik | Keputusan |
|-------|-----------|
| Cakupan F1 | Sekaligus: refactor struktural **dan** UI multi-varian |
| Stok selama F1 | Field `Stock` (int) **sementara** di `ProductVariant`; dimigrasi ke model stok per-gudang di F2 lalu dihapus |
| SKU | `Product.Code` (base, auto per kategori, dikunci) + `Variant.Sku` = Code + sufiks kode atribut |
| Atribut produk | Diturunkan dari nilai varian — tanpa tabel link Product↔Attribute terpisah |
| Import | Tetap 1 varian default per baris; import multi-varian ditunda (YAGNI) |
| Valuasi | `CostPrice` per varian (untuk Moving Average di F2); di F1 di-set 0 saat backfill |

## 2. Model Data

### 2.1 `Product` (induk) — perubahan

- **Hapus** (pindah ke varian): `Sku`, `Price`, `DiscountPrice`, `Stock`, `Weight`, `Dimensions`.
- **Pertahankan**: `Id`, `Name`, `Description`, `CategoryId`/`Category`, `Status`, `Images`.
- **Tambah**:
  - `Code` — base SKU, `"{KodeKategori}/{urut:0000}"`, auto, dikunci sejak dibuat, unik.
  - `BrandId?` (FK `Brand`, `OnDelete SetNull`).
  - `BaseUnitId?` (FK `Unit`, `OnDelete SetNull`).
  - `TaxId?` (FK `Tax`, `OnDelete SetNull`) — pajak default.
  - `Variants` — koleksi `ProductVariant` (field-backed, minimal 1), cascade delete.

### 2.2 `ProductVariant` (baru)

```
ProductVariant : AuditableEntity
 ├─ Id, ProductId (FK, cascade)
 ├─ Sku (dikunci, unik)
 ├─ Barcode? (maxlen 50)
 ├─ Price (>= 0), DiscountPrice? (>= 0, <= Price), CostPrice (>= 0)
 ├─ Weight? (>= 0), Dimensions? (maxlen 100)
 ├─ Stock (int >= 0, SEMENTARA — dihapus di F2)
 ├─ IsActive
 └─ AttributeValues : koleksi ProductVariantAttribute (field-backed)
```
Method: factory ctor, `Update(...)`, validasi `SetXxx` di entity, helper untuk menambah/menghapus
kaitan AttributeValue.

### 2.3 `ProductVariantAttribute` (join, baru)

```
ProductVariantAttribute
 ├─ Id
 ├─ ProductVariantId (FK ProductVariant, cascade)
 └─ AttributeValueId (FK AttributeValue dari master F0, restrict)
```
Satu varian = kumpulan nilai atribut (mis. {Ukuran:M, Warna:Merah}).

## 3. SKU & Penomoran

- `Product.Code` = `"{KodeKategori}/{urut:0000}"` (skema lama, kini di induk). Urut = maks yang ada + 1 per kode kategori. Dikunci setelah dibuat.
- `Variant.Sku` = `Code` + sufiks; sufiks = `"-" + join("-", attributeValue.Code)` urut menurut nama atribut. Contoh: `ELK/0001-M-RED`.
- Produk tanpa varian nyata → 1 varian dengan `Sku = Code` (tanpa sufiks).
- Keduanya unik (index unik pada `Product.Code` dan `ProductVariant.Sku`). SKU varian dikunci setelah dibuat.

## 4. Layanan (`ProductService` dirombak)

### 4.1 DTO
- `ProductVariantDto(int Id, string Sku, string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice, decimal? Weight, string? Dimensions, int Stock, bool IsActive, IReadOnlyList<AttributeValueRefDto> Attributes)`.
- `AttributeValueRefDto(int AttributeValueId, string AttributeName, string ValueCode, string Value)`.
- `ProductDto` — info induk (Name, CategoryId/Name, BrandId/Name, BaseUnitId/Name, TaxId/Name, Status, Images, Code) + `IReadOnlyList<ProductVariantDto> Variants` + turunan `decimal MinPrice`, `decimal MaxPrice`, `int TotalStock`, `int VariantCount`.
- `CreateProductRequest` / `UpdateProductRequest` — field induk + `IReadOnlyList<VariantInput> Variants`.
- `VariantInput(string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice, decimal? Weight, string? Dimensions, int Stock, bool IsActive, IReadOnlyList<int> AttributeValueIds)` — SKU TIDAK di input (auto-generate).

### 4.2 Operasi
- **Create**: validasi; pastikan ≥1 varian; resolve Category; generate `Product.Code`; untuk tiap varian generate `Sku` = Code + sufiks (dari kode AttributeValue yang dipilih); pastikan SKU unik antar varian dalam request & terhadap DB; simpan.
- **Update**: SKU & Code dikunci; varian bisa ditambah/diubah/dihapus (varian baru dapat SKU baru; minimal 1 tersisa).
- **Validasi**: ≥1 varian; tiap kombinasi atribut unik dalam satu produk; `DiscountPrice ≤ Price`; nilai `≥ 0`.
- **Dashboard**: `TotalStock = Σ variant.Stock`; `InventoryValue = Σ variant.Price × variant.Stock`; out-of-stock/low-stock dihitung per varian; agregasi kategori dari induk.
- **Import**: tiap baris = 1 produk + 1 varian default (Price/Discount/Cost/Stock/Weight/Dim dari kolom). Multi-varian via import ditunda.

## 5. UI (Blazor)

### 5.1 `ProductForm` — 2 mode
- **Sederhana** (default, untuk produk tanpa varian): satu varian; field Price/Discount/Cost/Stock/Weight/Dimensions inline (mirip form lama).
- **Multi-varian**: pilih Atribut (dropdown dari master) + centang nilai yang berlaku → tombol **Generate** membuat semua kombinasi → tabel varian: kolom SKU (auto, readonly) | Price | Discount | Cost | Stock | Aktif | hapus baris. Bisa hapus kombinasi yang tak diinginkan.
- Field induk: Name, Category, Brand, BaseUnit, Tax, Status, Description, Images (Images tetap di level produk).

### 5.2 Halaman lain
- `ProductIndex`: kolom Code, Name, Category, rentang harga (Min–Max), total stok, jumlah varian.
- `ProductImport`: tidak berubah fungsional (1 varian default per baris).
- `Dashboard`: tampilan sama; sumber agregasi pindah ke varian.

## 6. Migrasi Data ⚠️

Migrasi EF `RefactorProductToVariant`, SQL kustom di `Up()`, **urut**:
1. `CREATE TABLE ProductVariants` dan `ProductVariantAttributes` (+ index, FK).
2. `ALTER TABLE Products ADD Code, BrandId, BaseUnitId, TaxId`.
3. Backfill: `INSERT INTO ProductVariants` 1 baris/produk — salin `Sku→Sku`, `Price`, `DiscountPrice`, `Stock`, `Weight`, `Dimensions`; `CostPrice = 0`; `IsActive = 1`; stempel audit.
4. Backfill: `UPDATE Products SET Code = Sku`.
5. `ALTER TABLE Products DROP COLUMN Sku, Price, DiscountPrice, Stock, Weight, Dimensions` (drop index Sku lama dulu).
6. `CREATE UNIQUE INDEX` pada `ProductVariants.Sku` dan `Products.Code`.

`Down()`: re-add kolom lama ke `Products`, salin balik dari varian default (varian pertama per produk), drop tabel varian & kolom baru. Catatan: produk multi-varian tidak dapat dibalik sempurna (hanya varian pertama dipulihkan) — didokumentasikan; `Down()` ditujukan untuk rollback sebelum data multi-varian dibuat.

## 7. Dekomposisi (rencana: 5 task berurutan)

| Task | Isi | Deliverable teruji |
|------|-----|--------------------|
| 1 | Entity `ProductVariant`, `ProductVariantAttribute`; ubah `Product` | Unit test domain (SKU base, suffix, validasi harga, ≥1 varian) |
| 2 | DbContext config + migrasi `RefactorProductToVariant` | Test migrasi: seed produk gaya-lama (SQLite) → migrasi → tiap produk 1 varian benar |
| 3 | DTO + validator + `ProductService` (CRUD, SKU-gen, dashboard, import) | Integration test: CRUD multi-varian, SKU unik, dashboard, import 1-varian |
| 4 | UI `ProductIndex` + `ProductForm` mode sederhana | Build hijau; render & simpan 1-varian |
| 5 | UI `ProductForm` mode multi-varian (generator) + penyesuaian `ProductImport`/`Dashboard` | Build hijau; generate kombinasi & simpan |

## 8. Testing
- **Unit** (`ProductTests` ditulis ulang): pembuatan induk+varian, generate SKU & sufiks, validasi (`DiscountPrice ≤ Price`, `≥0`, minimal 1 varian, kombinasi atribut unik).
- **Integration**: CRUD multi-varian; SKU unik lintas produk; dashboard agregasi dari varian; import 1-varian; update menambah/menghapus varian.
- **Migrasi**: backfill benar (jumlah varian = jumlah produk lama, data tersalin), `Products.Code` terisi.

## 9. Di Luar Lingkup F1 (ditunda)
- Model stok per-gudang & buku mutasi (F2) — `Variant.Stock` sementara dihapus di sana.
- Import multi-varian.
- Gambar per-varian (gambar tetap di level produk).
- Supplier/Customer (F3), transaksi (F4).

## 10. Risiko & Mitigasi
- **Migrasi data merusak data produk lama** → SQL backfill idempotent-aware + test migrasi pada SQLite sebelum apply ke SQL Server; backup DB sebelum `database update` di prod.
- **`ProductService` luas berubah** → dipecah; integration test menutup CRUD/dashboard/import.
- **Form multi-varian kompleks** → dipisah jadi Task 4 (sederhana) lalu Task 5 (multi-varian) agar bisa direview bertahap.
