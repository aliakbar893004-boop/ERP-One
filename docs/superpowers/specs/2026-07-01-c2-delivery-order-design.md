# Tahap C2 — Penjualan: Delivery Order

**Tanggal:** 2026-07-01
**Status:** Disetujui (lanjut ke rencana implementasi)
**Program besar:** Transaksi (lihat `2026-06-23-transactions-foundation-design.md`).
Tahap C = Penjualan. **C1 (Sales Order) selesai.** Dokumen ini = **C2 (Delivery Order)**.

## Konteks

Delivery Order (DO) mengirim barang fisik atas sebuah **Sales Order yang sudah Confirmed**,
mendukung **pengiriman parsial** (beberapa DO per SO), mengurangi **stok keluar** lewat ledger
`StockMovement`/`ProductStock`, dan mencatat **COGS** (harga pokok) per baris.

C2 adalah **cermin GRN (B2) untuk sisi jual**. Perbedaan kunci dari GRN:
1. **UnitCost = COGS diambil otomatis** dari `ProductVariant.CostPrice` saat Post (bukan diinput
   user seperti harga faktur di GRN).
2. **HPP rata-rata (MA) TIDAK berubah** — DO adalah mutasi **keluar** (sama seperti stock-out
   adjustment di `StockService.RecordAdjustmentAsync`, yang memakai `variant.CostPrice` dan tidak
   memanggil `ApplyMovingAverage`).

Semua mengikuti pola existing (Clean Architecture, Blazor Server + Bootstrap 5, Application
service-based + FluentValidation, `AppDbContext : IdentityDbContext`, audit otomatis, otorisasi
permission via `AppMenus.cs`, UI list `Pager`+`SwalService`, UI form `fs-card`/Atlas + validasi
inline + spinner).

## Keputusan desain (hasil brainstorming)

1. **Over-delivery:** **STRICT — tidak boleh melebihi qty yang dipesan.** Batas per baris =
   `Quantity − DeliveredQuantity(terposting)`. Tidak ada toleransi/config (beda dari GRN).
2. **Stok kurang saat Post:** **Draft tetap boleh disimpan** (stok bisa masuk belakangan via GRN).
   Saat **Post**, cek on-hand di gudang sumber (`SO.WarehouseId`); bila kurang, **tolak Post**
   dengan pesan jelas menyebut item + qty tersedia, tanpa mutasi apa pun. Post otoritatif dalam
   transaksi (pola sama seperti toleransi GRN divalidasi saat Post). Backorder/stok negatif tidak
   dimungkinkan (domain `ProductStock` melarang negatif).
3. **Sumber COGS:** `ProductVariant.CostPrice` (HPP MA saat ini, global per varian) di-snapshot ke
   `DeliveryOrderLine.UnitCost` saat Post. **MA tidak diperbarui** (mutasi keluar).
4. **Approval DO:** **tidak ada** (SO sudah di-approve; pengiriman murni operasional gudang).
5. **Kardinalitas:** **satu DO = satu SO.** Baris DO diisi otomatis dari sisa qty SO.
6. **Siklus hidup DO:** **Draft → Posted.** Draft belum menggerakkan stok, bisa diedit/dihapus.
   **Post** menggerakkan stok + set COGS, lalu DO **immutable**. Tidak ada reversal/cancel setelah
   Post (koreksi via stock opname/adjustment yang ada).
7. **Tutup SO manual:** aksi **Tutup SO** untuk SO `PartiallyDelivered` → `Closed` (mengunci
   pengiriman lanjutan saat sisa tak akan dikirim).
8. **Gudang:** stok dikurangi & dicek di **`SalesOrder.WarehouseId`** (gudang sumber).

## Pola Existing yang Diikuti

- Clean Architecture: Domain (entitas `AuditableEntity`, private setters, invariant via
  ctor/method) → Application (DTO record + interface service + FluentValidation) → Infrastructure
  (`AppDbContext` mapping + service + DI) → Web (Blazor Server, permission via `AppMenus`).
- Enum disimpan sebagai string: `.HasConversion<string>().HasMaxLength(20)`.
- Decimal `(18,2)` untuk uang/cost via `.HasPrecision(18, 2)`.
- Pembulatan `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- Service melempar `FluentValidation.ValidationException` untuk error validasi/duplikasi.
- **Helper stok bersama** `AppDbContext.UpsertStockAsync(variantId, warehouseId, delta, ct)`
  (`src/MyApp.Infrastructure/Persistence/StockWriteExtensions.cs`) — `.Local`-aware, menolak hasil
  negatif dengan `InvalidOperationException`. Dipakai DO untuk delta negatif.
- **Moving average global per varian**: DO tidak mengubahnya. `StockMovement`/`ProductStock` per
  `SalesOrder.WarehouseId`.

## Scope C2

### 1. Domain (`src/MyApp.Domain/Entities`)

#### 1a. `SalesOrderStatus.cs` — tambah nilai
`PartiallyDelivered`, `Delivered`, `Closed`. (String → tak ada perubahan skema enum.)

#### 1b. `SalesOrderLine.cs` — tambah tracking pengiriman
- `int DeliveredQuantity` (private set, default 0).
- `bool IsFullyDelivered => DeliveredQuantity >= Quantity`.
- `void ApplyDelivery(int qty)`:
  - guard `qty > 0` (`ArgumentException`);
  - guard `DeliveredQuantity + qty <= Quantity` (strict, tanpa toleransi) else
    `InvalidOperationException`;
  - `DeliveredQuantity += qty`.

#### 1c. `SalesOrder.cs` — transisi pengiriman
- `bool CanDeliver => Status is Confirmed or PartiallyDelivered`.
- `void MarkPartiallyDelivered()` / `void MarkDelivered()` — hanya bila `CanDeliver`, else
  `InvalidOperationException`. Dipanggil service setelah agregasi line.
- `void Close()` — `PartiallyDelivered` → `Closed`; selain itu throw.

#### 1d. `DeliveryOrderStatus.cs` (baru)
`Draft`, `Posted`.

#### 1e. `DeliveryOrder.cs` (baru, `AuditableEntity`, private setters)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| DoNumber | string | unik, auto `DO-YYYYMM-####`, di-set saat create |
| SalesOrderId | int | FK SO (required) |
| DeliveryDate | DateTime | required |
| Notes | string? | ≤500 |
| Status | DeliveryOrderStatus | default `Draft` |
| Lines | IReadOnlyCollection\<DeliveryOrderLine\> | child, akses field |

Method: ctor (validasi), `UpdateHeader(deliveryDate, notes)` & `SetLines(...)` (hanya `Draft`),
`Post()` (`Draft` → `Posted`; guard ≥1 line; selain Draft throw).

#### 1f. `DeliveryOrderLine.cs` (baru)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| DeliveryOrderId | int | FK |
| SalesOrderLineId | int | FK ke SO line yang dikirim |
| ProductVariantId | int | snapshot dari SO line |
| QuantityDelivered | int | > 0 |
| UnitCost | decimal | ≥ 0, (18,2) — COGS snapshot saat Post |

### 2. Application (`src/MyApp.Application/DeliveryOrders/`)

- **`IDeliveryOrderService`**:
  - `Task<PagedResult<DeliveryOrderListItemDto>> GetPagedAsync(int page, int pageSize, string? search, DeliveryOrderStatus? status, CancellationToken)`
  - `Task<DeliveryOrderDto?> GetByIdAsync(int id, CancellationToken)`
  - `Task<DeliveryOrderDashboardDto> GetDashboardAsync(CancellationToken)`
  - `Task<IReadOnlyList<DeliverableSoDto>> GetDeliverableSosAsync(CancellationToken)` — SO `Confirmed`/`PartiallyDelivered`
  - `Task<SoForDeliveryDto?> GetSoForDeliveryAsync(int soId, CancellationToken)` — SO + sisa qty per line
  - `Task<DeliveryOrderDto> CreateDraftAsync(CreateDeliveryOrderRequest, CancellationToken)`
  - `Task<bool> UpdateDraftAsync(int id, UpdateDeliveryOrderRequest, CancellationToken)`
  - `Task<bool> DeleteDraftAsync(int id, CancellationToken)`
  - `Task<bool> PostAsync(int id, CancellationToken)`
- **DTO records** (`DeliveryOrderDtos.cs`): `DeliveryOrderListItemDto` (Id, DoNumber, SalesOrderId,
  SoNumber, CustomerName, DeliveryDate, Status, TotalQuantity), `DeliveryOrderDto`
  (+ `IReadOnlyList<DeliveryOrderLineDto> Lines`; header: DoNumber, SO#, CustomerName,
  WarehouseName, DeliveryDate, Notes, Status, CreatedAt, CreatedBy), `DeliveryOrderLineDto`
  (Id, SalesOrderLineId, ProductVariantId, VariantSku, ProductName, OrderedQuantity,
  QuantityDelivered, UnitCost, LineCost), `DeliverableSoDto` (Id, SoNumber, CustomerName,
  OrderDate, Status), `SoForDeliveryDto` (+ `SoForDeliveryLineDto` dgn OrderedQuantity,
  AlreadyDeliveredQuantity, RemainingQuantity), `DeliveryOrderLineRequest(int SalesOrderLineId,
  int QuantityDelivered)` — **tanpa UnitCost**, `CreateDeliveryOrderRequest(int SalesOrderId,
  DateTime DeliveryDate, string? Notes, IReadOnlyList<DeliveryOrderLineRequest> Lines)`,
  `UpdateDeliveryOrderRequest(DateTime DeliveryDate, string? Notes,
  IReadOnlyList<DeliveryOrderLineRequest> Lines)`.
- **Validators** (`DeliveryOrderValidators.cs`): header (`SalesOrderId>0`, `DeliveryDate` not empty,
  `Lines` not empty, Notes ≤500), line (`SalesOrderLineId>0`, `QuantityDelivered>0`). Validasi
  sisa-qty (strict) **dan** ketersediaan stok **di service** (butuh state SO + stok terkini).
- **`ISalesOrderService`** — tambah `Task<bool> CloseAsync(int id, CancellationToken)`.
  Juga: **`GetCreditInfoAsync` diperluas** — `EstimatedOutstanding` kini menjumlahkan SO berstatus
  `Confirmed`, `PartiallyDelivered`, **dan** `Delivered` (sesuai catatan di spec C1). `SalesOrderService`
  diperbarui + test credit info disesuaikan.

### 3. Infrastructure (`src/MyApp.Infrastructure`)

- **`Services/DeliveryOrderService.cs`** (pola `GoodsReceiptService`):
  - ctor inject `AppDbContext` + validators.
  - `CreateDraftAsync`: validate; load SO (`CanDeliver`) else Fail; generate `DoNumber`; untuk tiap
    line cek `QuantityDelivered ≤ remaining` di mana `remaining = ordered − DeliveredQuantity(terposting)`;
    simpan DO Draft (baris menyimpan `ProductVariantId` snapshot; `UnitCost` = 0 sampai Post).
  - `UpdateDraftAsync`/`DeleteDraftAsync`: hanya bila `Status == Draft`.
  - `PostAsync`: `BeginTransaction`; load DO(Draft)+lines, SO(`CanDeliver`).
    Akumulasi qty keluar per varian dalam post ini (`Dictionary<int,int>`) untuk cek stok multi-baris
    varian sama yang belum di-flush. Untuk tiap DO line:
    1. load `SalesOrderLine` (validasi milik SO) + `ProductVariant`;
    2. `available = Σ ProductStock.Quantity(variant, SO.WarehouseId) − akumulasiKeluar`;
       bila `qty > available` → `Fail("... melebihi stok tersedia (available) ...")`;
    3. `db.StockMovements.Add(new StockMovement(variantId, SO.WarehouseId, MovementType.Out,
       qty, variant.CostPrice, DeliveryDate, refType:"DO", refId:doId, note:DoNumber))`;
    4. `await db.UpsertStockAsync(variantId, SO.WarehouseId, −qty, ct)` (helper bersama, guard negatif);
    5. `line.SetUnitCost(variant.CostPrice)` (COGS snapshot; MA **tidak** diubah);
    6. `soLine.ApplyDelivery(qty)`;
    7. akumulasiKeluar[variantId] += qty.
    Lalu: jika semua SO line `IsFullyDelivered` → `so.MarkDelivered()` else
    `so.MarkPartiallyDelivered()`; `do.Post()`; `SaveChanges`; `Commit`.
    > Catatan: `DeliveryOrderLine.UnitCost` di-set via method domain (mis. `SetUnitCost(decimal)`
    > yang hanya boleh saat masih Draft/belum ada nilai) agar COGS ter-snapshot saat Post.
  - `GetSoForDeliveryAsync`: kembalikan SO + baris dgn `RemainingQuantity = Quantity − DeliveredQuantity`
    (>0 saja relevan; default pre-fill = remaining).
- **`Services/SalesOrderService.cs`** — tambah `CloseAsync` (load SO; `so.Close()`; save) dan
  perbarui `GetCreditInfoAsync` (himpunan status komitmen: Confirmed + PartiallyDelivered + Delivered).
- **`DependencyInjection.cs`** — `AddScoped<IDeliveryOrderService, DeliveryOrderService>()`.
- **`Persistence/AppDbContext.cs`**:
  - `DbSet<DeliveryOrder> DeliveryOrders`, `DbSet<DeliveryOrderLine> DeliveryOrderLines`.
  - Fluent: `DoNumber` unique; `Status` enum→string(20); `UnitCost (18,2)`; FK `SalesOrder`
    `Restrict`, lines `Cascade`, `ProductVariant` `Restrict`, `SalesOrderLine` `Restrict`; nav
    `Lines` `PropertyAccessMode.Field`.
  - Properti baru `SalesOrderLine.DeliveredQuantity` (default 0).
- **Migration**: satu migration baru — 2 tabel (`DeliveryOrders`, `DeliveryOrderLines`) + kolom
  `DeliveredQuantity` di `SalesOrderLines`. `Down()` membuang keduanya + kolom.
- **appsettings**: **tidak ada** section baru (tanpa toleransi).

### 4. Web (`src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/`)

- **`DoIndex.razor`** (`/transactions/delivery-orders`, policy `transactions.delivery-orders.index`)
  — tabel (DO#, SO#, Customer, Tanggal, Status badge) + search + filter status + `Pager` (15/hal).
  Hapus hanya untuk Draft via `SwalService`. Tombol "Pengiriman Baru". (Mirror `GrnIndex`.)
- **`DoForm.razor`** (`/transactions/delivery-orders/new`, `?soId={id}` opsional, dan
  `/transactions/delivery-orders/{Id:int}/edit`) — pilih SO (dropdown `GetDeliverableSosAsync`;
  bila `soId` query → preselect), auto-isi baris dari `GetSoForDeliveryAsync` (produk, sisa qty).
  Kolom editable: **qty terkirim saja** (tanpa harga — COGS otomatis saat Post). Atlas layout,
  validasi inline, spinner. Simpan sebagai Draft. (Mirror `GrnForm`.)
- **`DoDetail.razor`** (`/transactions/delivery-orders/{Id:int}`) — header + lines (termasuk COGS
  per baris setelah Post); tombol **Post** (bila Draft, policy `...post`) dgn konfirmasi
  `SwalService`; setelah Post read-only. (Mirror `GrnDetail`.)
- **`SoDetail.razor`** (existing, edit) — tampilkan kolom progres terkirim per line
  (`DeliveredQuantity / Quantity`); tombol **Buat DO** (bila `Confirmed`/`PartiallyDelivered`,
  policy `delivery-orders.create`) → `DoForm?soId=`; tombol **Tutup SO** (bila `PartiallyDelivered`,
  policy `sales-orders.close`) dgn konfirmasi. (Mirror perubahan `PoDetail` di B2.)

### 5. Menu & otorisasi (`src/MyApp.Web/Authorization/AppMenus.cs`)

- `ActPost` & `ActClose` sudah ada dari B2 — dipakai ulang.
- Grup **Transaksi**: resource baru `new("transactions.delivery-orders", "Delivery Order",
  "bi-truck", [ActIndex, ActCreate, ActEdit, ActDelete, ActPost])`.
- Resource `transactions.sales-orders`: tambah `ActClose` ke set action-nya (jadi
  `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActClose]`).
- Permission baru auto-granted ke admin via `AllPermissions`/`BootstrapSeeder`.
- Entri `NavMenu.razor` untuk Delivery Order (hardcoded, pola sama seperti GRN entry di B2).

### 6. Testing

- **Unit** (`MyApp.UnitTests`):
  - `SalesOrderLine.ApplyDelivery`: akumulasi, tolak `qty≤0`, tolak melebihi qty dipesan (strict),
    lolos tepat di batas; `IsFullyDelivered`.
  - `SalesOrder`: `Close` (hanya dari PartiallyDelivered), `MarkDelivered`/`MarkPartiallyDelivered`
    guard status (`CanDeliver`).
  - `DeliveryOrder`/`DeliveryOrderLine`: ctor validasi, `Post` guard (≥1 line, hanya dari Draft),
    tak bisa modifikasi setelah Post.
  - Validator DO (header & line).
- **Integration** (`MyApp.IntegrationTests`):
  - Post mengurangi `ProductStock` & menulis `StockMovement` (Type=Out, RefType="DO", RefId).
  - **`ProductVariant.CostPrice` TIDAK berubah** setelah Post (MA tak terpengaruh mutasi keluar).
  - `DeliveryOrderLine.UnitCost` = `variant.CostPrice` saat Post (COGS snapshot).
  - Pengiriman parsial → SO `PartiallyDelivered`; pengiriman penuh → `Delivered`.
  - Stok kurang → Post ditolak (`ValidationException` dgn pesan), tanpa mutasi stok/DO tetap Draft.
  - Over-delivery (qty > sisa) ditolak saat CreateDraft.
  - Draft tidak menggerakkan stok; hapus Draft berhasil; Post tak bisa diulang.
  - `SalesOrderService.CloseAsync` (PartiallyDelivered → Closed).
  - `GetCreditInfoAsync` memasukkan SO PartiallyDelivered/Delivered ke outstanding.

## Di luar scope C2
- Retur barang dari customer / reversal DO setelah Post (koreksi via stock opname/adjustment).
- Satu DO mencakup banyak SO.
- Multi-currency / konversi kurs.
- Modul AR/faktur/pembayaran.
