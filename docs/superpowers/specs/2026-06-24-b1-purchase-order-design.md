# Tahap B1 — Pembelian: Purchase Order + Engine Approval

**Tanggal:** 2026-06-24
**Status:** Disetujui (lanjut ke rencana implementasi)
**Bagian dari:** Program Transaksi (lihat `2026-06-23-transactions-foundation-design.md`)

## Konteks

Tahap A (Supplier, Customer, hub Transaksi) sudah selesai. Tahap B (Pembelian) di roadmap
mencakup PO + Goods Receipt + engine approval. Karena terlalu besar untuk satu spec, Tahap B
dipecah:

| Sub-tahap | Isi |
|---|---|
| **B1** (dokumen ini) | Engine approval multi-level (rantai role tetap, reusable) + modul **Purchase Order** (Draft → approval → Confirmed) |
| **B2** (spec terpisah) | **Goods Receipt (GRN)**: penerimaan parsial, stok masuk, HPP rata-rata bergerak |

Engine approval dibangun di B1 sebagai modul **document-agnostic** agar dipakai ulang verbatim
untuk Sales Order di Tahap C.

## Keputusan Desain (hasil brainstorming)

- **Slicing:** B1 = PO + approval; B2 = GRN.
- **Model approval:** rantai role **tetap** (fixed chain), dikonfigurasi sekali per tipe dokumen.
- **Konfigurasi rantai:** tabel `ApprovalChainStep` di DB, di-seed default, + halaman Settings untuk mengelola.
- **Aksi reject:** PO kembali ke **Draft** (bisa direvisi & submit ulang); alasan reject disimpan; rantai di-reset.
- **Aturan approver:** user mana pun yang memegang role pada step boleh approve; **pembuat PO tidak boleh approve PO-nya sendiri** (segregation of duties); satu user boleh approve beberapa level bila punya banyak role.
- **Nomor PO:** auto-generate `PO-YYYYMM-####` (urut per bulan), unik.
- **Baris PO:** ProductVariant, Qty, UnitPrice (harga beli), DiscountPercent opsional, Tax opsional (entitas `Tax`).
- **Lifecycle:** `Draft → PendingApproval → Confirmed`; `Rejected` (transient, balik ke Draft); `Cancelled` (dari Draft atau PendingApproval). PO `Confirmed` tidak bisa diedit/cancel di B1.
- **Rantai kosong:** PO yang disubmit tanpa rantai terkonfigurasi langsung **Confirmed** (0 step).

## Pola Existing yang Diikuti

- Clean Architecture: Domain (entitas `AuditableEntity`, private setters, invariant via ctor/`Update`/method) → Application (DTO record + interface service + FluentValidation) → Infrastructure (`AppDbContext` mapping + service + DI) → Web (Blazor Server, Bootstrap 5, permission via `AppMenus`).
- Enum disimpan sebagai string: `e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20)`.
- Service melempar `FluentValidation.ValidationException` untuk error validasi/duplikasi.
- UI list: tabel + search + `Pager` (15/hal) + `SwalService`. UI form: `fs-card`, validasi inline, spinner.
- Roles = `ApplicationRole : IdentityRole<string>`. Engine approval merujuk role **by RoleName** (cek via `User.IsInRole` / claim) — Domain tidak ber-FK ke tabel Identity.
- Audit otomatis via `AppDbContext.StampAudit()`.

## Arsitektur

### 1. Engine Approval (modul `Approvals/`, document-agnostic)

Tidak tahu apa pun tentang isi PO/SO. Hanya bekerja dengan `(DocumentType, DocumentId)`.

**Enum**
- `ApprovalDocumentType { PurchaseOrder, SalesOrder }`
- `ApprovalStepStatus { Pending, Approved, Rejected }`

**Entitas konfigurasi — `ApprovalChainStep : AuditableEntity`** (dikelola di Settings)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| DocumentType | enum (string) | wajib |
| StepOrder | int | ≥1, unik per `(DocumentType, StepOrder)` |
| RoleName | string | ≤256, wajib, harus role yang ada |

Di-seed default: `PurchaseOrder` → satu step `StepOrder=1, RoleName="Manager"` (atau role admin yang ada bila "Manager" belum ada — ditentukan saat seeding mengikuti role existing).

**Entitas instance — `ApprovalStep : AuditableEntity`** (dibuat saat submit; snapshot dari config)
| Field | Tipe | Catatan |
|---|---|---|
| Id | int | PK |
| DocumentType | enum (string) | |
| DocumentId | int | id dokumen (PO) |
| StepOrder | int | |
| RoleName | string | snapshot dari config |
| Status | enum (string) | default `Pending` |
| ActedByUserId | string? | diisi saat approve/reject |
| ActedByName | string? | nama/email untuk tampilan |
| ActedAt | DateTime? | |
| Note | string? | ≤500, alasan reject |

Index: `(DocumentType, DocumentId, StepOrder)`.

**`IApprovalChainService`** (config CRUD) — `GetByDocumentTypeAsync`, `ReplaceChainAsync(docType, IReadOnlyList<(order, roleName)>)`. Operasi "replace" mengganti seluruh rantai untuk satu tipe dokumen secara atomik (hapus lama, tulis baru) agar urutan selalu konsisten.

**`IApprovalService`** (runtime, document-agnostic)
- `SubmitAsync(docType, docId, ct)` → menyalin rantai config ke `ApprovalStep`. **Rantai kosong ⇒ 0 step ⇒ fully approved.** Return `ApprovalSubmitResult(bool FullyApproved)`.
- `ApproveAsync(docType, docId, actingUserId, Func<string,bool> isInRole, creatorUserId, actingDisplayName, ct)` → ambil step `Pending` ber-`StepOrder` terkecil; validasi `isInRole(step.RoleName)` & `actingUserId != creatorUserId`; set step `Approved`. Return `ApprovalActionResult(bool FullyApproved)`. Lempar `ValidationException` bila tak berwenang / bukan giliran / pembuat sendiri.
- `RejectAsync(docType, docId, actingUserId, actingDisplayName, reason, ct)` → step `Pending` terkecil → `Rejected` + simpan `Note`. (Validasi role sama seperti approve.)
- `ResetAsync(docType, docId, ct)` → hapus semua `ApprovalStep` untuk dokumen (dipakai saat reject→Draft & saat submit ulang untuk membersihkan sisa lama).
- `GetStepsAsync(docType, docId, ct)` → `IReadOnlyList<ApprovalStepDto>` (untuk timeline).

Engine **tidak** menyentuh status PO. Pemanggil (PO service) yang menafsirkan `FullyApproved`.

### 2. Purchase Order (Domain)

**`PurchaseOrderStatus { Draft, PendingApproval, Confirmed, Rejected, Cancelled }`** (string).

**`PurchaseOrder : AuditableEntity`**
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| PoNumber | string | unik, ≤30, di-set sistem |
| SupplierId | int | wajib (FK Supplier) |
| WarehouseId | int | wajib (FK Warehouse, gudang tujuan) |
| OrderDate | DateTime | wajib |
| ExpectedDate | DateTime? | opsional, ≥ OrderDate bila diisi |
| Currency | string | ≤3, snapshot dari `Supplier.DefaultCurrency` saat dibuat |
| Notes | string? | ≤500 |
| Status | enum | default `Draft` |
| RejectionNote | string? | ≤500, alasan reject terakhir |
| Subtotal, DiscountTotal, TaxTotal, GrandTotal | decimal(18,2) | dihitung dari baris |
| Lines | `ICollection<PurchaseOrderLine>` | ≥1 saat submit |

Method: ctor membuat header `Draft`; `SetLines(...)`/`UpdateHeader(...)` hanya saat `Draft` (lempar bila bukan Draft); `RecomputeTotals()` (privat, dipanggil saat baris berubah); transisi `Submit()` (Draft→PendingApproval, butuh ≥1 baris), `MarkConfirmed()` (PendingApproval→Confirmed), `ReturnToDraft(reason)` (PendingApproval→Draft, simpan `RejectionNote`), `Cancel()` (Draft|PendingApproval→Cancelled). Semua transisi tak valid melempar `InvalidOperationException`.

**`PurchaseOrderLine`** (owned/child, bukan `AuditableEntity`)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| PurchaseOrderId | int | FK |
| ProductVariantId | int | wajib |
| Quantity | int | >0 |
| UnitPrice | decimal(18,2) | ≥0 |
| DiscountPercent | decimal(5,2) | 0..100, default 0 |
| TaxId | int? | FK Tax opsional |
| TaxRateSnapshot | decimal(5,2) | snapshot `Tax.Rate` saat baris dibuat (0 bila tanpa pajak) |
| LineSubtotal | decimal(18,2) | `Quantity * UnitPrice` |
| LineDiscount | decimal(18,2) | `LineSubtotal * DiscountPercent/100` |
| LineTax | decimal(18,2) | `(LineSubtotal - LineDiscount) * TaxRateSnapshot/100` (pajak diperlakukan exclusive di PO) |
| LineTotal | decimal(18,2) | `LineSubtotal - LineDiscount + LineTax` |

Amount dihitung di domain (ctor/update), dibulatkan `(18,2)` `MidpointRounding.AwayFromZero`. PO totals = Σ baris.

> Catatan: `Tax.IsInclusive` diabaikan di B1 — pajak PO diperlakukan exclusive (ditambahkan ke total). Penyederhanaan yang dicatat; bisa ditinjau bila kebutuhan inclusive muncul.

### 3. Application / Infrastructure

- `Application/Approvals/` — `ApprovalDtos.cs` (`ApprovalStepDto`, `ApprovalChainStepDto`, result records), `IApprovalService.cs`, `IApprovalChainService.cs`, validator config rantai (RoleName wajib, StepOrder unik & berurutan).
- `Application/PurchaseOrders/` — `PurchaseOrderDtos.cs` (`PurchaseOrderDto`, `PurchaseOrderLineDto`, `PurchaseOrderListItemDto`, `CreatePurchaseOrderRequest`+`PurchaseOrderLineRequest`, `UpdatePurchaseOrderRequest`), `IPurchaseOrderService.cs`, `PurchaseOrderValidators.cs` (header + ≥1 baris, qty>0, harga≥0, diskon 0..100).
- `Infrastructure/Services/ApprovalService.cs`, `ApprovalChainService.cs`.
- `Infrastructure/Services/PurchaseOrderService.cs` — orkestrasi dalam **satu transaksi**:
  - `CreateAsync` / `UpdateAsync` (hanya Draft) — generate `PoNumber` saat create (query max urut bulan berjalan di dalam transaksi), simpan baris, snapshot tax rate & currency.
  - `SubmitAsync(id, ct)` — `po.Submit()`; `approval.ResetAsync` lalu `approval.SubmitAsync`; bila `FullyApproved` → `po.MarkConfirmed()`.
  - `ApproveAsync(id, actingUserId, isInRole, displayName, ct)` — panggil `approval.ApproveAsync` (creatorUserId = `po.CreatedBy`); bila `FullyApproved` → `po.MarkConfirmed()`.
  - `RejectAsync(id, actingUserId, displayName, reason, ct)` — `approval.RejectAsync` + `po.ReturnToDraft(reason)` + `approval.ResetAsync`.
  - `CancelAsync(id, ct)` — `po.Cancel()` (+ reset step bila ada).
  - Query: `GetPagedAsync(page,size,search,status?)`, `GetByIdAsync` (header+lines+steps), `GetAllSuppliers/Variants` via service existing untuk dropdown.
- DI registration semua service baru.
- `AppDbContext`: `DbSet` untuk `PurchaseOrders`, `PurchaseOrderLines`, `ApprovalChainSteps`, `ApprovalSteps`; konfigurasi fluent (enum string, decimal presisi, unique index `PoNumber`, unique `(DocumentType,StepOrder)` config, index `(DocumentType,DocumentId,StepOrder)` instance, cascade delete lines saat PO dihapus draft).
- Satu EF migration baru. **Tidak ada** perubahan tabel stok/produk.

### 4. Web (Blazor Server)

- `Components/Pages/Transactions/PurchaseOrders/`:
  - `PoIndex.razor` (`/transactions/purchase-orders`, policy `transactions.purchase-orders.index`) — **menggantikan** `PurchaseOrderPlaceholder.razor`. Tabel: No PO, Supplier, Tanggal, Total, Status (badge berwarna per status); filter status + search + `Pager`. Tombol "Buat PO" (policy create).
  - `PoForm.razor` (`/transactions/purchase-orders/new`, `/{id}/edit`) — header (Supplier, Warehouse, OrderDate, ExpectedDate, Notes) + editor baris dinamis (tambah/hapus baris, pilih variant, qty, harga, diskon%, pajak) dengan total hidup. Hanya untuk PO `Draft`.
  - `PoDetail.razor` (`/transactions/purchase-orders/{id}`) — tampilan read-only PO + ringkasan total + **timeline approval** (daftar step: order, role, status, oleh siapa, kapan, catatan). Tombol kontekstual: **Submit** (Draft, policy edit/create pembuat), **Approve/Reject** (PendingApproval, hanya bila user di role step berjalan & bukan pembuat, policy `approve`), **Cancel** (Draft/PendingApproval), **Edit** (Draft). Reject membuka prompt alasan.
- `Components/Pages/Settings/ApprovalChains/`:
  - `ApprovalChainsIndex.razor` (`/settings/approval-chains`, policy `settings.approval-chains.index`) — daftar tipe dokumen + ringkasan rantai.
  - `ApprovalChainForm.razor` — editor urutan role per tipe dokumen (tambah/hapus/urutkan step, pilih RoleName dari daftar role). Simpan via `ReplaceChainAsync`.
- `NavMenu.razor`: entri Purchase Order (sudah ada placeholder, tetap), tambah entri Settings → Approval Chains.
- `AppMenus.cs`:
  - `transactions.purchase-orders` dinaikkan dari `ViewOnly` → `CRUD` + action baru `approve` (set kustom `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove]`).
  - Grup Settings: tambah `settings.approval-chains` (CRUD).
  - Action konstanta baru `ActApprove = new("approve","Approve","bi-check2-circle")`.
  - Permission baru auto-grant ke admin via `BootstrapSeeder`.

### 5. Testing

- **Unit (`MyApp.UnitTests`):**
  - `PurchaseOrderLine`: math total (subtotal, diskon, pajak, pembulatan), qty>0, diskon 0..100, harga≥0.
  - `PurchaseOrder`: `RecomputeTotals`, guard transisi (`Submit` butuh baris & hanya dari Draft; `MarkConfirmed` hanya dari PendingApproval; `Cancel` dari Draft/PendingApproval; edit baris ditolak bila bukan Draft).
  - Validator PO (header, ≥1 baris) & validator config rantai (StepOrder unik/berurutan, RoleName wajib).
- **Integration (`MyApp.IntegrationTests`):**
  - Engine approval: submit→approve seluruh rantai→FullyApproved; reject→step Rejected; rantai kosong→auto FullyApproved; pembuat tak boleh approve (ValidationException); user tanpa role step ditolak.
  - PO service: create (PoNumber ter-generate & unik per bulan) → submit → approve → status `Confirmed`; reject → status `Draft` + RejectionNote; cancel.
  - PoNumber unik (dua PO bulan sama berurutan).

## Di luar scope B1 (→ B2 / C)

- Goods Receipt, penerimaan parsial, pergerakan stok, HPP rata-rata bergerak — **Tahap B2**.
- Pelacakan penerimaan pada PO setelah Confirmed (PartiallyReceived/Received) — **Tahap B2**.
- Sales Order & Delivery Order — **Tahap C** (engine approval B1 dipakai ulang).
- Konversi multi-currency (YAGNI). Pajak inclusive di PO (disederhanakan exclusive).
- Tidak ada perubahan tabel/logika stok di B1.
