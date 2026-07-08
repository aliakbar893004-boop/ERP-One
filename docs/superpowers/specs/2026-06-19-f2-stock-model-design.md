# Desain F2 — Model Stok + Opname + Moving Average

- **Tanggal**: 2026-06-19
- **Status**: Disetujui (desain) — menunggu rencana implementasi
- **Fase**: F2 (lanjutan dari F0 master data & F1 product-variant)
- **Spec induk**: `docs/superpowers/specs/2026-06-18-master-data-design.md` (Bagian 5 & 8)
- **Lingkup**: Model stok per gudang (`ProductStock` + `StockMovement`), penyesuaian/opname stok manual, valuasi Moving Average, migrasi `ProductVariant.Stock` → ledger.

## 1. Konteks & Keputusan

F1 memindahkan SKU/harga/stok ke `ProductVariant` dengan kolom `Stock` (int) yang **sementara**. F2
mengganti kolom itu dengan model stok berbasis ledger per gudang dan menghapusnya. Transaksi
Pembelian/Penjualan baru ada di F4, sehingga **di F2 satu-satunya cara stok berubah adalah input
manual** (saldo awal + opname/penyesuaian).

Keputusan yang disepakati saat brainstorming:

| Topik | Keputusan |
|-------|-----------|
| Cakupan F2 | Model stok **+ halaman Opname/Penyesuaian manual** + tampilan stok per gudang |
| Moving Average | **Dihitung sejak F2**: mutasi masuk membawa `UnitCost`; `CostPrice` varian dihitung ulang |
| Lingkup MA | **Global per varian** (1 angka HPP/varian di `ProductVariant.CostPrice`), bukan per gudang |
| Stok di ProductForm | Input "Stok awal + HPP awal" per varian (saat create) → `StockMovement` opening ke gudang default; **read-only saat edit** |
| `ReservedQty` | **Ditunda** (YAGNI — belum ada reservasi sampai Penjualan/F4) |
| Sumber kebenaran | `StockMovement` (ledger, immutable); `ProductStock` = cache saldo |
| Testing | Mengikuti F1: test dijaga **compile**, tidak dijadikan gate; verifikasi = build 0 error + migrasi apply bersih |

## 2. Konvensi Arsitektur (wajib diikuti)

Mengikuti `docs/superpowers/specs/2026-06-18-master-data-design.md` Bagian 2: rich domain entity
(private setter, factory constructor, validasi di entity), pola service-based, EF Core dengan konfig
inline di `AppDbContext.OnModelCreating`, halaman Blazor mengikuti pola Products/Units, otorisasi via
`AppMenus.cs`.

## 3. Domain

### 3.1 `StockMovement` (buku besar — append-only, IMMUTABLE)

```
StockMovement : AuditableEntity
 ├─ Id (int, private set)
 ├─ ProductVariantId (int)
 ├─ WarehouseId (int)
 ├─ Type : MovementType (In | Out | Transfer | Adjustment)
 ├─ Quantity (int, BERTANDA: + masuk / − keluar)   // tidak boleh 0
 ├─ UnitCost (decimal >= 0)                         // HPP per unit pada mutasi masuk; untuk keluar = HPP saat itu (COGS)
 ├─ MovementDate (DateTime)
 ├─ RefType (string?, max 50)                       // mis. "Opening", "Opname"
 ├─ RefId (int?)
 └─ Note (string?, max 500)
```

- Factory constructor saja; **tidak ada `Update()`/`Delete()`** — koreksi dilakukan dengan mutasi
  baru (jejak audit lengkap).
- Validasi di entity: `Quantity != 0`, `UnitCost >= 0`, `WarehouseId`/`ProductVariantId` > 0.
- `MovementType` adalah enum baru di `MyApp.Domain.Entities`.

### 3.2 `ProductStock` (saldo materialized — cache)

```
ProductStock : AuditableEntity
 ├─ Id (int, private set)
 ├─ ProductVariantId (int)
 ├─ WarehouseId (int)
 ├─ Quantity (int >= 0)
 └─ unique index (ProductVariantId, WarehouseId)
```

- Method `ApplyDelta(int delta)`: `Quantity += delta`; lempar exception bila hasil < 0.
- Dibuat lazily: bila baris (varian+gudang) belum ada saat mutasi pertama, service membuatnya.

### 3.3 `ProductVariant` (perubahan)

- **Hapus** kolom/property `Stock` (int) beserta `SetStock` dan parameternya di constructor/`Update`.
- Tambah method MA:
  `void ApplyMovingAverage(int totalQtyBefore, int inQty, decimal inUnitCost)`
  dengan `totalQtyBefore` = total saldo varian **lintas semua gudang** sebelum mutasi; menghitung
  `CostPrice = (totalQtyBefore * CostPrice + inQty * inUnitCost) / (totalQtyBefore + inQty)`
  bila `totalQtyBefore + inQty > 0`, selain itu `CostPrice` tidak berubah.
- `CostPrice` tetap di varian dan **menjadi nilai HPP Moving Average** (global lintas gudang).

## 4. Application

### 4.1 `IStockService`

```csharp
Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default);
Task<int> GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct = default);
Task<IReadOnlyList<StockMovementDto>> GetMovementsByVariantAsync(int variantId, CancellationToken ct = default);
Task<PagedResult<StockLevelDto>> GetLevelsPagedAsync(int page, int pageSize, int? warehouseId, string? search, CancellationToken ct = default);
Task RecordAdjustmentAsync(StockAdjustmentRequest request, CancellationToken ct = default);
Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);
```

### 4.2 Aturan transaksional (inti)

Setiap perubahan stok berjalan dalam **satu transaksi DB** dengan urutan:
1. Hitung `qtyOnHandBefore` (saldo varian saat ini — total lintas gudang untuk MA, dan per gudang untuk `ProductStock`).
2. Tulis `StockMovement` (immutable).
3. Update/`buat` `ProductStock` untuk (varian+gudang) via `ApplyDelta`.
4. Bila mutasi **masuk** (Quantity > 0): panggil `variant.ApplyMovingAverage(totalQtyBefore, inQty, unitCost)`.
5. `SaveChanges` sekali. Tidak ada perubahan stok tanpa `StockMovement`.

Penyesuaian (opname) memakai **selisih**: line membawa target/selisih qty per varian+gudang; service
mengubahnya menjadi `StockMovement(Adjustment)` bertanda (+/−). Untuk selisih **negatif**, `UnitCost`
diisi `CostPrice` saat itu (COGS) dan MA **tidak** diubah; untuk selisih **positif**, `UnitCost` dari
input dipakai untuk MA.

### 4.3 DTO & Validator

- `StockLevelDto(int VariantId, string Sku, string ProductName, int WarehouseId, string WarehouseName, int Quantity, decimal CostPrice)`
- `StockMovementDto(int Id, int VariantId, int WarehouseId, string WarehouseName, MovementType Type, int Quantity, decimal UnitCost, DateTime MovementDate, string? RefType, string? Note)`
- `StockAdjustmentRequest(int WarehouseId, DateTime Date, string? Note, IReadOnlyList<StockAdjustmentLine> Lines)`
- `StockAdjustmentLine(int VariantId, int DeltaQuantity, decimal UnitCost, string? Reason)`
- Validator FluentValidation: `WarehouseId` valid, minimal 1 line, `DeltaQuantity != 0`, `UnitCost >= 0`, saldo hasil tidak negatif (dicek di service terhadap data).

## 5. Infrastructure + Migrasi

- Tambah `DbSet<StockMovement>` & `DbSet<ProductStock>`; konfigurasi inline di
  `AppDbContext.OnModelCreating` (unique index `ProductStock(ProductVariantId, WarehouseId)`, FK ke
  `ProductVariants`/`Warehouses` dengan `DeleteBehavior.Restrict`, presisi `decimal` untuk `UnitCost`).
- `StockService` di `Infrastructure/Services/`.
- Migrasi EF Core baru, **hand-edit & idempotent**, urutan:
  1. `CreateTable` `ProductStocks` & `StockMovements` + indeks/FK.
  2. **Backfill** (SQL): ambil `Id` gudang default (`Warehouses WHERE IsDefault = 1`). Untuk tiap
     `ProductVariant` dengan `Stock > 0`: `INSERT StockMovements(Adjustment, RefType='Opening',
     Note='Saldo awal migrasi F2', Quantity=Stock, UnitCost=CostPrice, MovementDate=SYSUTCDATETIME())`
     ke gudang default, dan `INSERT ProductStocks(VariantId, defaultWhId, Stock)`.
  3. `DropColumn` `ProductVariant.Stock`.
  - `Down()`: `AddColumn` `Stock`, backfill balik dari `ProductStocks` (jumlah saldo per varian),
    lalu `DropTable` kedua tabel. (Multi-gudang saat down digabung jadi total — didokumentasikan.)
- Catatan: migrasi mengasumsikan **tepat satu** gudang `IsDefault = 1` (di-seed di F0). Bila tidak
  ada, backfill harus gagal eksplisit (raiserror) agar tidak menulis stok ke gudang yang salah.

## 6. Web (UI + Otorisasi)

### 6.1 Otorisasi (`AppMenus.cs`)

Grup baru **"Inventory"**:
- `inventory.stock-levels` — aksi: View.
- `inventory.adjustments` — aksi: View, Create.

### 6.2 Halaman Blazor

- `Components/Pages/Inventory/StockAdjustments/` — **List** (riwayat opname/penyesuaian) + **Create**
  (form opname: pilih gudang & tanggal → tambah baris varian dengan qty selisih/target + `UnitCost` +
  alasan).
- `Components/Pages/Inventory/StockLevels/` — **List** saldo per varian per gudang (filter gudang +
  search SKU/nama), mengikuti pola `ProductIndex`.
- `ProductForm`:
  - **Create**: input "Stok awal" + "HPP awal" per varian → `RecordOpeningAsync` ke gudang default.
  - **Edit**: field stok **read-only**; tampilkan ringkasan saldo per gudang (read-only).

### 6.3 Dashboard & ProductService

- `ProductService.GetDashboardAsync`: `TotalStock`, `outOfStock`, `lowStock`, `ByCategory`,
  `LowStock`, dan **nilai inventori** diagregasi dari `ProductStocks` (bukan `variant.Stock`).
  Nilai inventori = `Σ (ProductStock.Quantity × variant.CostPrice)`.
- `ProductDto.Stock` (total per produk) dihitung dari `Σ ProductStock.Quantity` lintas
  varian+gudang; `ProductVariantDto.Stock` = `Σ ProductStock.Quantity` lintas gudang untuk varian itu.
- Hapus seluruh referensi `variant.Stock`; import produk: kolom stok awal → `RecordOpeningAsync`.

## 7. Pemetaan Sentuhan Kode (referensi implementasi)

| Berkas | Perubahan |
|--------|-----------|
| `Domain/Entities/StockMovement.cs`, `MovementType.cs`, `ProductStock.cs` | **Baru** |
| `Domain/Entities/ProductVariant.cs` | Hapus `Stock`/`SetStock`; tambah `ApplyMovingAverage` |
| `Application/Stock/IStockService.cs`, `StockDtos.cs`, `StockValidators.cs` | **Baru** |
| `Infrastructure/Services/StockService.cs` | **Baru** |
| `Infrastructure/Persistence/AppDbContext.cs` | 2 DbSet + konfig |
| `Infrastructure/Persistence/Migrations/*_AddStockModel.cs` | **Baru** (hand-edit) |
| `Infrastructure/Services/ProductService.cs` | Agregasi dari `ProductStock`; opening movement di create/import; hapus `v.Stock` |
| `Application/Products/ProductDtos.cs`, `ProductDashboardDtos.cs`, `CreateProductValidator.cs` | Sesuaikan field stok |
| `Web/Authorization/AppMenus.cs` | Grup "Inventory" + 2 resource |
| `Web/Components/Pages/Inventory/...` | Halaman baru (StockLevels, StockAdjustments) |
| `Web/Components/Pages/.../ProductForm.razor` | Stok/HPP awal (create), read-only (edit) |
| `Web/Components/Pages/Home.razor` | Konsumsi dashboard dari sumber baru (tanpa perubahan signature bila DTO dipertahankan) |
| Tests | Dijaga compile (bukan gate) |

## 8. Di Luar Lingkup (YAGNI)

- Transaksi Pembelian/Penjualan/Mutasi antar gudang (`StockTransfer`) — F4.
- `ReservedQty`/reservasi stok.
- MA per gudang (pakai MA global per varian).
- Valuasi FIFO/LIFO.
- Multi-varian import per gudang lanjutan, gambar per varian.

## 9. Verifikasi

- `dotnet build MyApp.slnx` = 0 error.
- `dotnet ef database update` = migrasi apply bersih pada DB yang sudah berisi data F1.
- Cek manual: opname menambah/mengurangi saldo lewat ledger; MA `CostPrice` berubah benar pada mutasi
  masuk; dashboard total stok & nilai inventori konsisten dengan `ProductStock`.
