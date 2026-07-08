# Tahap C1 — Penjualan: Sales Order

**Tanggal:** 2026-07-01
**Status:** Disetujui (lanjut ke rencana implementasi)
**Bagian dari:** Program Transaksi (lihat `2026-06-23-transactions-foundation-design.md`)

## Konteks

Tahap A (Supplier, Customer, hub Transaksi) dan Tahap B (Pembelian: B1 Purchase Order +
engine approval, B2 Goods Receipt) sudah selesai. Tahap C (Penjualan) di roadmap mencakup
Sales Order + Delivery Order. Seperti Tahap B, terlalu besar untuk satu spec sehingga dipecah:

| Sub-tahap | Isi |
|---|---|
| **C1** (dokumen ini) | Modul **Sales Order** (Draft → approval → Confirmed), memakai ulang engine approval B1 |
| **C2** (spec terpisah) | **Delivery Order (DO)**: pengiriman parsial, stok **keluar** di HPP (COGS), status pengiriman pada SO |

C1 adalah **cermin penuh dari B1 (Purchase Order)**: pola, penamaan, dan struktur sama —
hanya Supplier→Customer, harga beli→harga jual, dan gudang menjadi **sumber** pengiriman.
Engine approval B1 (document-agnostic) dipakai ulang **verbatim** lewat
`ApprovalDocumentType.SalesOrder` (nilai enum sudah ada; chain service sudah teruji untuk
tipe ini). **Tidak ada pergerakan stok di C1** — itu ditangani C2 (Delivery Order).

## Keputusan Desain (hasil brainstorming)

- **Slicing:** C1 = Sales Order + pakai ulang approval; C2 = Delivery Order (stok keluar + COGS).
- **Model approval:** identik B1 — rantai role tetap per tipe dokumen, dikonfigurasi di Settings.
  Rantai kosong ⇒ SO yang disubmit langsung **Confirmed** (0 step).
- **Aksi reject:** SO kembali ke **Draft** (bisa direvisi & submit ulang); alasan disimpan; rantai di-reset.
- **Aturan approver:** sama B1 — user mana pun yang memegang role step boleh approve;
  **pembuat SO tidak boleh approve SO-nya sendiri** (segregation of duties).
- **Nomor SO:** auto-generate `SO-YYYYMM-####` (urut per bulan), unik.
- **Baris SO:** ProductVariant, Qty, UnitPrice (harga jual), DiscountPercent opsional, Tax opsional.
  **Default `UnitPrice` = `ProductVariant.DiscountPrice ?? ProductVariant.Price`** (dapat di-edit).
- **Pajak:** diperlakukan **exclusive** (ditambahkan ke total), sama seperti B1. `Tax.IsInclusive` diabaikan.
- **Lifecycle:** `Draft → PendingApproval → Confirmed`; `Rejected` (transient, balik ke Draft);
  `Cancelled` (dari Draft atau PendingApproval). SO `Confirmed` tidak bisa diedit/cancel di C1.
- **Credit limit:** **peringatan lunak (soft warning)**. Perkiraan outstanding dihitung dari
  Σ `GrandTotal` SO customer berstatus komitmen aktif; bila SO ini membuat total melebihi
  `Customer.CreditLimit`, UI menampilkan peringatan **tanpa memblokir**. Ini proxy kasar
  (bukan piutang riil) karena belum ada modul AR/pembayaran — dicatat untuk ditinjau nanti.

## Pola Existing yang Diikuti

- Clean Architecture: Domain (entitas `AuditableEntity`, private setters, invariant via
  ctor/`Update`/method) → Application (DTO record + interface service + FluentValidation) →
  Infrastructure (`AppDbContext` mapping + service + DI) → Web (Blazor Server, Bootstrap 5,
  permission via `AppMenus`).
- Enum disimpan sebagai string: `e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20)`.
- Service melempar `FluentValidation.ValidationException` untuk error validasi/duplikasi.
- UI list: tabel + search + `Pager` (15/hal) + `SwalService`. UI form: `fs-card`, validasi inline, spinner.
- Roles = `ApplicationRole : IdentityRole<string>`. Engine approval merujuk role **by RoleName**.
- Audit otomatis via `AppDbContext.StampAudit()`.

## Arsitektur

Referensi lengkap engine approval ada di `2026-06-24-b1-purchase-order-design.md` §1.
Di C1 engine itu dipakai apa adanya; tidak ada perubahan pada `Approvals/`.

### 1. Domain (`src/MyApp.Domain`)

#### 1a. `Entities/SalesOrderStatus.cs` (baru)
`SalesOrderStatus { Draft, PendingApproval, Confirmed, Rejected, Cancelled }` (string).
> Nilai `PartiallyDelivered`, `Delivered`, `Closed` **ditambahkan di C2** (analog dengan cara
> B2 menambah status penerimaan ke `PurchaseOrderStatus`). C1 tidak menyertakannya.

#### 1b. `Entities/SalesOrder.cs` (baru, `AuditableEntity`, private setters)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| SoNumber | string | unik, ≤30, di-set sistem (`SO-YYYYMM-####`) |
| CustomerId | int | wajib (FK Customer) |
| WarehouseId | int | wajib (FK Warehouse, gudang sumber) |
| OrderDate | DateTime | wajib |
| ExpectedDate | DateTime? | opsional, ≥ OrderDate bila diisi |
| Currency | string | ≤3, snapshot dari `Customer.DefaultCurrency` saat dibuat |
| Notes | string? | ≤500 |
| Status | SalesOrderStatus | default `Draft` |
| RejectionNote | string? | ≤500, alasan reject terakhir |
| Subtotal, DiscountTotal, TaxTotal, GrandTotal | decimal(18,2) | dihitung dari baris |
| Lines | `ICollection<SalesOrderLine>` | ≥1 saat submit |

Method (mirror `PurchaseOrder`): ctor membuat header `Draft`; `SetLines(...)`/`UpdateHeader(...)`
hanya saat `Draft` (lempar bila bukan Draft); `RecomputeTotals()` (privat, dipanggil saat baris
berubah); transisi `Submit()` (Draft→PendingApproval, butuh ≥1 baris), `MarkConfirmed()`
(PendingApproval→Confirmed), `ReturnToDraft(reason)` (PendingApproval→Draft, simpan
`RejectionNote`), `Cancel()` (Draft|PendingApproval→Cancelled). Transisi tak valid melempar
`InvalidOperationException`.

#### 1c. `Entities/SalesOrderLine.cs` (baru, child, bukan `AuditableEntity`)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| SalesOrderId | int | FK |
| ProductVariantId | int | wajib |
| Quantity | int | >0 |
| UnitPrice | decimal(18,2) | ≥0 |
| DiscountPercent | decimal(5,2) | 0..100, default 0 |
| TaxId | int? | FK Tax opsional |
| TaxRateSnapshot | decimal(5,2) | snapshot `Tax.Rate` saat baris dibuat (0 bila tanpa pajak) |
| LineSubtotal | decimal(18,2) | `Quantity * UnitPrice` |
| LineDiscount | decimal(18,2) | `LineSubtotal * DiscountPercent/100` |
| LineTax | decimal(18,2) | `(LineSubtotal - LineDiscount) * TaxRateSnapshot/100` (exclusive) |
| LineTotal | decimal(18,2) | `LineSubtotal - LineDiscount + LineTax` |

Amount dihitung di domain (ctor/update), dibulatkan `(18,2)` `MidpointRounding.AwayFromZero`.
SO totals = Σ baris. Perhitungan **identik** `PurchaseOrderLine`.

### 2. Approval — pakai ulang (tanpa perubahan engine)

- `ApprovalDocumentType.SalesOrder` sudah ada. `IApprovalService`/`IApprovalChainService`
  dipakai apa adanya (submit/approve/reject/reset/getSteps + config chain).
- **Seed rantai default SalesOrder** via `BootstrapSeeder` (pola sama seperti seed PurchaseOrder
  di B1): satu step default `StepOrder=1` ber-RoleName mengikuti role yang ada (role manager/admin
  yang sama dengan default PO). Bila tidak ada rantai, submit → langsung Confirmed.
- **Settings → Approval Chains**: pastikan daftar tipe dokumen di `ApprovalChainsIndex.razor`
  mencakup `SalesOrder` sehingga rantainya bisa dikelola. Bila UI B1 sudah meng-enumerasi seluruh
  `ApprovalDocumentType`, tidak ada perubahan; bila hanya PurchaseOrder yang di-hardcode, tambahkan
  entri SalesOrder.

### 3. Application (`src/MyApp.Application/SalesOrders/`)

- **DTO records** (`SalesOrderDtos.cs`):
  - `SalesOrderListItemDto(int Id, string SoNumber, int CustomerId, string CustomerName,
    DateTime OrderDate, string Status, decimal GrandTotal)`
  - `SalesOrderLineDto(int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal)`
  - `SalesOrderDto(...header lengkap..., string CustomerName, string WarehouseName, string Status,
    string? RejectionNote, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrandTotal, IReadOnlyList<SalesOrderLineDto> Lines, IReadOnlyList<ApprovalStepDto> Steps)`
  - `SalesOrderLineRequest(int ProductVariantId, int Quantity, decimal UnitPrice,
    decimal DiscountPercent, int? TaxId)`
  - `CreateSalesOrderRequest(int CustomerId, int WarehouseId, DateTime OrderDate,
    DateTime? ExpectedDate, string? Notes, IReadOnlyList<SalesOrderLineRequest> Lines)`
  - `UpdateSalesOrderRequest(int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate,
    string? Notes, IReadOnlyList<SalesOrderLineRequest> Lines)`
  - `SalesOrderCreditInfoDto(decimal CreditLimit, decimal EstimatedOutstanding, decimal ThisOrderTotal, bool ExceedsLimit)`
- **`ISalesOrderService`**:
  - `Task<PagedResult<SalesOrderListItemDto>> GetPagedAsync(int page, int pageSize, string? search, SalesOrderStatus? status, CancellationToken)`
  - `Task<SalesOrderDto?> GetByIdAsync(int id, CancellationToken)` (header + lines + steps)
  - `Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest, CancellationToken)`
  - `Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest, CancellationToken)` — hanya Draft
  - `Task<bool> SubmitAsync(int id, CancellationToken)`
  - `Task<bool> ApproveAsync(int id, string actingUserId, Func<string,bool> isInRole, string actingDisplayName, CancellationToken)`
  - `Task<bool> RejectAsync(int id, string actingUserId, string actingDisplayName, string reason, CancellationToken)`
  - `Task<bool> CancelAsync(int id, CancellationToken)`
  - `Task<SalesOrderCreditInfoDto> GetCreditInfoAsync(int customerId, decimal thisOrderTotal, int? excludeSoId, CancellationToken)`
- **Validators** (`SalesOrderValidators.cs`): header (`CustomerId>0`, `WarehouseId>0`, `OrderDate`
  not empty, `ExpectedDate ≥ OrderDate` bila diisi, `Lines` not empty), line (`ProductVariantId>0`,
  `Quantity>0`, `UnitPrice≥0`, `DiscountPercent 0..100`).

### 4. Infrastructure (`src/MyApp.Infrastructure`)

- **`Services/SalesOrderService.cs`** (pola `PurchaseOrderService`) — orkestrasi dalam **satu transaksi**:
  - `CreateAsync`/`UpdateAsync` (hanya Draft) — generate `SoNumber` saat create (query max urut
    bulan berjalan di dalam transaksi), simpan baris, snapshot tax rate & `Customer.DefaultCurrency`.
    Default `UnitPrice` bila request mengirim 0/absen: `variant.DiscountPrice ?? variant.Price`
    (default diterapkan di UI; service tetap menerima nilai apa adanya dan hanya memvalidasi ≥0).
  - `SubmitAsync` — `so.Submit()`; `approval.ResetAsync` lalu `approval.SubmitAsync`; bila
    `FullyApproved` → `so.MarkConfirmed()`.
  - `ApproveAsync` — `approval.ApproveAsync` (creatorUserId = `so.CreatedBy`); bila `FullyApproved`
    → `so.MarkConfirmed()`.
  - `RejectAsync` — `approval.RejectAsync` + `so.ReturnToDraft(reason)` + `approval.ResetAsync`.
  - `CancelAsync` — `so.Cancel()` (+ reset step bila ada).
  - `GetCreditInfoAsync` — `EstimatedOutstanding` = Σ `GrandTotal` SO milik customer berstatus
    `Confirmed` (di C1 hanya status ini yang merupakan komitmen aktif; C2 akan menambahkan
    `PartiallyDelivered`/`Delivered` ke himpunan ini), **kecuali** SO ber-`Id == excludeSoId`.
    `ExceedsLimit = CreditLimit > 0 && (EstimatedOutstanding + thisOrderTotal) > CreditLimit`.
  - Query dropdown: reuse service existing (customers via `ICustomerService`, variants via service produk existing).
- **DI**: `AddScoped<ISalesOrderService, SalesOrderService>()`.
- **`Persistence/AppDbContext.cs`**: `DbSet<SalesOrder> SalesOrders`, `DbSet<SalesOrderLine> SalesOrderLines`;
  fluent: `SoNumber` unique; `Status` enum→string(20); decimal `(18,2)`/`(5,2)` sesuai field;
  FK `Customer` `Restrict`, `Warehouse` `Restrict`, `Tax` `Restrict`, lines `Cascade` saat SO draft dihapus;
  `ProductVariant` `Restrict`.
- **Migration**: satu migration baru — tabel `SalesOrders` + `SalesOrderLines`. `Down()` membuang keduanya.
  **Tidak ada** perubahan tabel stok/produk/approval.

### 5. Web (`src/MyApp.Web/Components/Pages/Transactions/SalesOrders/`)

- **`SoIndex.razor`** (`/transactions/sales-orders`, policy `transactions.sales-orders.index`) —
  **menggantikan** `SalesOrderPlaceholder.razor` (hapus placeholder). Tabel: No SO, Customer,
  Tanggal, Total, Status (badge berwarna per status); filter status + search + `Pager`.
  Tombol "Buat SO" (policy create). Hapus hanya untuk Draft via `SwalService`.
- **`SoForm.razor`** (`/transactions/sales-orders/new`, `/{id}/edit`) — header (Customer, Warehouse,
  OrderDate, ExpectedDate, Notes) + editor baris dinamis (tambah/hapus baris, pilih variant,
  qty, harga [default `DiscountPrice ?? Price`], diskon%, pajak) dengan total hidup. Banner
  credit-limit (via `GetCreditInfoAsync`) muncul saat total > limit — informatif, tak memblokir.
  Hanya untuk SO `Draft`.
- **`SoDetail.razor`** (`/transactions/sales-orders/{id}`) — read-only SO + ringkasan total +
  **timeline approval** (order, role, status, oleh siapa, kapan, catatan) + banner credit-limit.
  Tombol kontekstual: **Submit** (Draft), **Approve/Reject** (PendingApproval, hanya bila user di
  role step berjalan & bukan pembuat, policy `approve`), **Cancel** (Draft/PendingApproval),
  **Edit** (Draft). Reject membuka prompt alasan.
- **`AppMenus.cs`**: `transactions.sales-orders` dinaikkan dari `ViewOnly` → set kustom
  `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove]` (konstanta `ActApprove` sudah ada dari B1).
  Permission baru auto-grant ke admin via `BootstrapSeeder`/`AllPermissions`. `NavMenu.razor`
  entri SO sudah ada — tetap.

### 6. Testing

- **Unit (`MyApp.UnitTests`):**
  - `SalesOrderLine`: math total (subtotal, diskon, pajak, pembulatan), qty>0, diskon 0..100, harga≥0.
  - `SalesOrder`: `RecomputeTotals`, guard transisi (`Submit` butuh baris & hanya dari Draft;
    `MarkConfirmed` hanya dari PendingApproval; `Cancel` dari Draft/PendingApproval; edit baris
    ditolak bila bukan Draft).
  - Validator SO (header + ≥1 baris).
- **Integration (`MyApp.IntegrationTests`):**
  - SO service: create (`SoNumber` ter-generate & unik per bulan) → submit → approve seluruh rantai
    → status `Confirmed`; reject → status `Draft` + `RejectionNote`; cancel.
  - `SoNumber` unik (dua SO bulan sama berurutan).
  - Approval reuse untuk `ApprovalDocumentType.SalesOrder`: pembuat tak boleh approve; rantai kosong
    → auto Confirmed.
  - `GetCreditInfoAsync`: outstanding menjumlahkan SO Confirmed customer, mengecualikan `excludeSoId`,
    `ExceedsLimit` benar di atas/bawah/ tepat batas.

## Di luar scope C1 (→ C2 atau lanjutan)

- Delivery Order, pengiriman parsial, pergerakan stok keluar, COGS — **Tahap C2**.
- Status pengiriman pada SO (`PartiallyDelivered`/`Delivered`/`Closed`) & aksi "Tutup SO" — **Tahap C2**.
- Modul AR/piutang/pembayaran (credit limit di C1 hanya peringatan lunak berbasis proxy SO Confirmed).
- Konversi multi-currency (YAGNI). Pajak inclusive (disederhanakan exclusive, sama B1).
- Tidak ada perubahan tabel/logika stok di C1.
