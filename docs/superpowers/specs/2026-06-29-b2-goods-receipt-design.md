# Tahap B2 — Goods Receipt (GRN / Penerimaan Barang)

**Tanggal:** 2026-06-29
**Status:** Disetujui (lanjut ke implementasi)
**Program besar:** Transaksi (lihat `2026-06-23-transactions-foundation-design.md`).
Tahap B = Pembelian. **B1 (Purchase Order) selesai.** Dokumen ini = **B2 (Goods Receipt)**.

## Konteks

GRN menerima barang fisik atas sebuah **Purchase Order yang sudah Confirmed**, mendukung
**penerimaan parsial** (beberapa GRN per PO), menambah **stok masuk** lewat ledger
`StockMovement`/`ProductStock`, dan memperbarui **HPP rata-rata bergerak** lewat
`ProductVariant.ApplyMovingAverage(...)`.

Semua mengikuti pola existing (Clean Architecture, Blazor Server + Bootstrap 5,
Application service-based + FluentValidation, `AppDbContext : IdentityDbContext`,
audit otomatis, otorisasi permission via `AppMenus.cs`, UI list `Pager`+`SwalService`,
UI form `fs-card` + validasi inline + spinner).

## Keputusan desain (hasil brainstorming)

1. **Sumber HPP per unit:** default **netto setelah diskon, tanpa PPN** =
   `Round(UnitPrice × (1 − DiscountPercent/100), 2)`. **Bisa di-override** per baris
   saat penerimaan (harga faktur aktual beda dari PO).
2. **Over-receipt:** **boleh melebihi qty PO dengan toleransi %**. Default **10%**,
   dibaca dari `appsettings` (`GoodsReceipt:OverReceiptTolerancePercent`), berlaku global.
   Batas per baris = `floor(Quantity × (1 + tol/100))`.
3. **Siklus hidup GRN:** **Draft → Posted**. Draft belum menggerakkan stok, bisa
   diedit/dihapus. **Post** menggerakkan stok + HPP, lalu GRN menjadi **immutable**.
   Tidak ada reversal/cancel setelah Post (koreksi via stock opname/adjustment yang ada).
4. **Approval GRN:** **tidak ada**. Penerimaan murni operasional gudang (PO sudah di-approve).
5. **Kardinalitas:** **satu GRN = satu PO.** Baris GRN diisi otomatis dari sisa qty PO.
6. **Tutup PO manual:** ada aksi **Tutup PO** untuk PO `PartiallyReceived` →
   `Closed` (mengunci penerimaan lanjutan saat sisa tak akan dikirim supplier).

## Scope B2

### 1. Domain (`src/MyApp.Domain`)

#### 1a. `Enums/PurchaseOrderStatus.cs` — tambah nilai
`PartiallyReceived`, `Received`, `Closed`. (Disimpan sebagai string di DB → tak ada
perubahan skema untuk enum ini.)

#### 1b. `Entities/PurchaseOrderLine.cs` — tambah tracking penerimaan
- `int ReceivedQuantity` (private set, default 0).
- `decimal DefaultUnitCost => Round(UnitPrice × (1 − DiscountPercent/100), 2,
  MidpointRounding.AwayFromZero)` — default HPP per unit (read-only computed).
- `void ApplyReceipt(int qty, int tolerancePercent)`:
  - guard `qty > 0`;
  - hitung `maxAllowed = floor(Quantity × (1 + tolerancePercent/100))`;
  - guard `ReceivedQuantity + qty ≤ maxAllowed` (else `InvalidOperationException`);
  - `ReceivedQuantity += qty`.
- `bool IsFullyReceived => ReceivedQuantity >= Quantity`.

#### 1c. `Entities/PurchaseOrder.cs` — transisi penerimaan
- `void MarkReceived()` / `void MarkPartiallyReceived()` — hanya dari
  `Confirmed`/`PartiallyReceived`. Dipanggil service setelah agregasi line.
- `void Close()` — `PartiallyReceived` → `Closed`; selain itu throw.
- Penerimaan hanya boleh dari status `Confirmed` atau `PartiallyReceived`
  (helper `bool CanReceive`).

#### 1d. `Enums/GoodsReceiptStatus.cs` (baru)
`Draft`, `Posted`.

#### 1e. `Entities/GoodsReceipt.cs` (baru, `AuditableEntity`, private setters)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| GrnNumber | string | unik, auto `GRN-YYYYMM-####`, di-set saat create |
| PurchaseOrderId | int | FK PO (required) |
| ReceiptDate | DateTime | required |
| Notes | string? | ≤500 |
| Status | GoodsReceiptStatus | default `Draft` |
| Lines | IReadOnlyCollection\<GoodsReceiptLine\> | child, akses field |

Method: ctor (validasi), `UpdateHeader(receiptDate, notes)` & `SetLines(...)`
(hanya saat `Draft`), `Post()` (`Draft` → `Posted`; guard ≥1 line; selain Draft throw).

#### 1f. `Entities/GoodsReceiptLine.cs` (baru)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| GoodsReceiptId | int | FK |
| PurchaseOrderLineId | int | FK ke PO line yang diterima |
| ProductVariantId | int | snapshot dari PO line |
| QuantityReceived | int | > 0 |
| UnitCost | decimal | ≥ 0, (18,2) |

### 2. Application (`src/MyApp.Application/GoodsReceipts/`)

- **`IGoodsReceiptService`**:
  - `Task<PagedResult<GoodsReceiptListItemDto>> GetPagedAsync(int page, int pageSize, string? search, GoodsReceiptStatus? status, CancellationToken)`
  - `Task<GoodsReceiptDto?> GetByIdAsync(int id, CancellationToken)`
  - `Task<IReadOnlyList<ReceivablePoDto>> GetReceivablePosAsync(CancellationToken)` — PO `Confirmed`/`PartiallyReceived`
  - `Task<PoForReceiptDto?> GetPoForReceiptAsync(int poId, CancellationToken)` — PO + sisa qty per line + default unit cost
  - `Task<GoodsReceiptDto> CreateDraftAsync(CreateGoodsReceiptRequest, CancellationToken)`
  - `Task<bool> UpdateDraftAsync(int id, UpdateGoodsReceiptRequest, CancellationToken)`
  - `Task<bool> DeleteDraftAsync(int id, CancellationToken)`
  - `Task<bool> PostAsync(int id, CancellationToken)`
- **DTO records** (`GoodsReceiptDtos.cs`): `GoodsReceiptListItemDto`, `GoodsReceiptDto`
  (+ `IReadOnlyList<GoodsReceiptLineDto> Lines`), `GoodsReceiptLineDto`,
  `ReceivablePoDto`, `PoForReceiptDto` (+ baris dgn `RemainingQuantity`, `DefaultUnitCost`),
  `CreateGoodsReceiptRequest(int PurchaseOrderId, DateTime ReceiptDate, string? Notes,
  IReadOnlyList<GoodsReceiptLineRequest> Lines)`,
  `GoodsReceiptLineRequest(int PurchaseOrderLineId, int QuantityReceived, decimal UnitCost)`,
  `UpdateGoodsReceiptRequest(DateTime ReceiptDate, string? Notes, IReadOnlyList<GoodsReceiptLineRequest> Lines)`.
- **Validators** (`GoodsReceiptValidators.cs`): header (`PurchaseOrderId>0`, `ReceiptDate`
  not empty, `Lines` not empty), line (`QuantityReceived>0`, `UnitCost≥0`). Validasi
  toleransi over-receipt **di service** (butuh state PO terkini).
- **`IPurchaseOrderService`** — tambah `Task<bool> CloseAsync(int id, CancellationToken)`.

### 3. Infrastructure (`src/MyApp.Infrastructure`)

- **`Services/GoodsReceiptService.cs`** (pola `StockService` + `PurchaseOrderService`):
  - ctor inject `AppDbContext`, validators, dan opsi toleransi.
  - `CreateDraftAsync`: validate; load PO (`CanReceive`); generate `GrnNumber`;
    untuk tiap line cek `QuantityReceived ≤ remainingTolerance` di mana
    `remainingTolerance = floor(ordered×(1+tol)) − ReceivedQuantity(terposting)`;
    simpan GRN Draft.
  - `UpdateDraftAsync`/`DeleteDraftAsync`: hanya bila `Status == Draft`.
  - `PostAsync`: `BeginTransaction`; load GRN(Draft)+lines, PO(`CanReceive`).
    Untuk tiap GRN line:
    1. load `ProductVariant`;
    2. `totalBefore = Σ ProductStock.Quantity` (variant tsb, **lintas gudang** — HPP global);
    3. `db.StockMovements.Add(new StockMovement(variantId, PO.WarehouseId, MovementType.In,
       +qty, unitCost, ReceiptDate, refType:"GRN", refId:grnId))`;
    4. upsert `ProductStock(variant, PO.WarehouseId).ApplyDelta(+qty)`;
    5. `variant.ApplyMovingAverage(totalBefore, qty, unitCost)`;
    6. `poLine.ApplyReceipt(qty, tol)`.
    Lalu: jika semua PO line `IsFullyReceived` → `po.MarkReceived()` else
    `po.MarkPartiallyReceived()`; `grn.Post()`; `SaveChanges`; `Commit`.
  - Toleransi: `GoodsReceiptOptions { int OverReceiptTolerancePercent = 10; }` di-bind
    dari section `GoodsReceipt` (`appsettings.json`).
- **`Services/PurchaseOrderService.cs`** — `CloseAsync` → load PO; `po.Close()`; save.
- **`DependencyInjection.cs`** — `AddScoped<IGoodsReceiptService, GoodsReceiptService>()`;
  bind `GoodsReceiptOptions`.
- **`Persistence/AppDbContext.cs`**:
  - `DbSet<GoodsReceipt> GoodsReceipts`, `DbSet<GoodsReceiptLine> GoodsReceiptLines`.
  - Fluent: `GrnNumber` unique; `Status` enum→string(20); `UnitCost (18,2)`;
    FK `PurchaseOrder` `Restrict`, lines `Cascade`, `ProductVariant` `Restrict`,
    `PurchaseOrderLine` `Restrict`; nav `Lines` `PropertyAccessMode.Field`.
  - Properti baru `PurchaseOrderLine.ReceivedQuantity` (default 0).
- **Migration**: satu migration baru — 2 tabel (`GoodsReceipts`, `GoodsReceiptLines`)
  + kolom `ReceivedQuantity` di `PurchaseOrderLines`. `Down()` membuang keduanya + kolom.
- **`appsettings.json`** (Web) — section `"GoodsReceipt": { "OverReceiptTolerancePercent": 10 }`.

### 4. Web (`src/MyApp.Web/Components/Pages/Transactions/GoodsReceipts/`)

- **`GrnIndex.razor`** (`/transactions/goods-receipts`, policy `transactions.goods-receipts.index`)
  — tabel (GRN#, PO#, Supplier, Tanggal, Status badge) + search + filter status +
  `Pager` (15/hal). Hapus hanya untuk Draft via `SwalService`. Tombol "Penerimaan Baru".
- **`GrnForm.razor`** (`/transactions/goods-receipts/new`, `?poId={id}` opsional,
  dan `/transactions/goods-receipts/{Id:int}/edit`) — pilih PO (dropdown
  `GetReceivablePosAsync`; bila `poId` query → preselect), auto-isi baris dari
  `GetPoForReceiptAsync` (produk, sisa qty, default unit cost). Kolom editable: qty
  diterima + unit cost. `fs-card`, validasi inline, spinner. Simpan sebagai Draft.
- **`GrnDetail.razor`** (`/transactions/goods-receipts/{Id:int}`) — header + lines;
  tombol **Post** (bila Draft, policy `...post`) dgn konfirmasi `SwalService`;
  setelah Post tampil read-only.
- **`PoDetail.razor`** (existing, edit) — tampilkan kolom progres diterima per line
  (`ReceivedQuantity / Quantity`); tombol **Buat GRN** (bila `Confirmed`/`PartiallyReceived`,
  policy `goods-receipts.create`) → `GrnForm?poId=`; tombol **Tutup PO** (bila
  `PartiallyReceived`, policy `purchase-orders.close`) dgn konfirmasi.

### 5. Menu & otorisasi (`src/MyApp.Web/Authorization/AppMenus.cs`)

- Tambah `AppAction ActPost = new("post", "Post", "bi-box-arrow-in-down")`.
- Grup **Transaksi**: resource baru
  `new("transactions.goods-receipts", "Goods Receipt", "bi-box-seam",
  [ActIndex, ActCreate, ActEdit, ActDelete, ActPost])`.
- Resource `transactions.purchase-orders`: tambah `ActClose = new("close","Close","bi-lock-fill")`.
- Permission baru auto-granted ke admin via `AllPermissions`. Entri `NavMenu.razor`.

### 6. Testing

- **Unit** (`MyApp.UnitTests`):
  - `PurchaseOrderLine.ApplyReceipt`: akumulasi, tolak qty>0 gagal, tolak melebihi
    toleransi, lolos tepat di batas toleransi; `DefaultUnitCost` (diskon, pembulatan);
    `IsFullyReceived`.
  - `PurchaseOrder`: `Close` (hanya dari PartiallyReceived), `MarkReceived`/`MarkPartiallyReceived`
    guard status.
  - Validator GRN (header & line).
- **Integration** (`MyApp.IntegrationTests`):
  - Post menambah `ProductStock` & menulis `StockMovement` (Type=In, RefType="GRN", RefId).
  - `ProductVariant.CostPrice` ter-update sesuai moving average (termasuk unit cost override).
  - Penerimaan parsial → PO `PartiallyReceived`; penerimaan penuh → `Received`.
  - Over-receipt dalam toleransi lolos; di atas toleransi ditolak.
  - Draft tidak menggerakkan stok; hapus Draft berhasil; Post tak bisa diulang.
  - `PurchaseOrderService.CloseAsync` (PartiallyReceived → Closed).

## Di luar scope B2
- Retur barang ke supplier / reversal GRN setelah Post (koreksi via stock opname/adjustment).
- Satu GRN mencakup banyak PO.
- Multi-currency / konversi kurs.
- Tahap C (Sales Order + Delivery Order).

## Catatan / edge case
- Toleransi over-receipt divalidasi terhadap qty yang **sudah diposting** (draft tak
  dihitung). Dua draft paralel bisa lolos validasi draft tetapi yang kedua gagal saat
  Post — diterima sebagai edge case (Post bersifat otoritatif dalam transaksi).
- HPP (`ProductVariant.CostPrice`) bersifat global per varian; `totalBefore` dihitung
  lintas gudang, konsisten dengan `StockService` existing. Pergerakan stok & `ProductStock`
  tetap per `PO.WarehouseId`.
