# Fase 1a — Transfer Stok (Design Spec)

**Tanggal:** 2026-07-16
**Branch kerja:** `Development`
**Bagian dari:** Fase 1 (Inventory Lengkap). Quick win — reuse stock movement.

---

## 1. Tujuan & Ruang Lingkup

Memindahkan kuantitas stok antar gudang lewat dokumen transfer dengan approval chain. Alur **Draft → PendingApproval → Posted** (1 langkah gerakan stok; stok pindah saat fully-approved).

### Di luar scope
- **In-transit** (2 langkah post-out/receive) — tidak dipakai; stok pindah sekaligus saat posting.
- **Void/reverse transfer** — koreksi via transfer balik manual.
- **Jurnal GL (5b)** — transfer = perpindahan internal; nilai persediaan total & akun Inventory tak berubah → **tidak ada auto-posting**. 5b tak disentuh.

---

## 2. Keputusan Desain (brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Workflow | Draft → PendingApproval → Posted (1 langkah). Pola `SupplierPayment`: Submit → approval → fully-approved → post. |
| Approval | **Ya** — `ApprovalDocumentType.StockTransfer`, reuse `IApprovalService`; default chain (role admin) di BootstrapSeeder. |
| Costing | HPP tak berubah (`ProductVariant.CostPrice` = moving-average global per varian, bukan per gudang). Transfer hanya memindahkan qty. |
| Numbering | Prefix **TRF**, sequence Id berikutnya (13). |

---

## 3. Domain (folder Transactions, namespace `ErpOne.Domain.Entities`)

### `StockTransfer` — tabel `T_StockTransfers`
```
Id, TransferNumber (unik), TransferDate, SourceWarehouseId, DestinationWarehouseId,
Notes?, Status (StockTransferStatus), RejectionNote?, Lines (IReadOnlyCollection<StockTransferLine>)
```
- `enum StockTransferStatus { Draft, PendingApproval, Posted }`.
- Ctor `(string transferNumber, DateTime transferDate, int sourceWarehouseId, int destinationWarehouseId, string? notes)` — validasi source ≠ destination; Status = Draft.
- `SetLines(IEnumerable<(int variantId, int qty)>)` — draft-only; qty > 0.
- `UpdateHeader(...)` — draft-only.
- `Submit()` — Draft → PendingApproval.
- `MarkPosted()` — PendingApproval → Posted.
- `ReturnToDraft(string reason)` — PendingApproval → Draft (+ RejectionNote).

### `StockTransferLine` — tabel `T_StockTransferLines`
`Id, StockTransferId, ProductVariantId, Quantity (int > 0)`.

---

## 4. Approval
- Tambah `StockTransfer` ke `ApprovalDocumentType`.
- `StockTransferService` reuse `IApprovalService`: `SubmitAsync` → `ResetAsync` + `SubmitAsync` (return `fullyApproved` bila chain kosong → langsung post); `ApproveAsync` → bila fully-approved panggil post privat; `RejectAsync` → `ReturnToDraft`.
- Seed default chain di BootstrapSeeder (idempotent, role admin) mengikuti pola PO/SO/SupplierPayment.

---

## 5. Service `IStockTransferService`
`GetPagedAsync(page,size,search?,status?)`, `GetByIdAsync`, `CreateAsync(req)` (draft), `UpdateAsync(id,req)` (draft), `DeleteAsync(id)` (draft), `SubmitAsync(id)`, `ApproveAsync(id, actingUserName, isInRole)`, `RejectAsync(id, reason, ...)`.

**Post privat (fully-approved):** bungkus `BeginTransactionAsync`. Validasi: source ≠ dest; tiap qty > 0; **stok tersedia di gudang asal ≥ qty** (per varian). Per baris:
- `StockMovement` Out di source (`−qty`, `variant.CostPrice`, refType `"StockTransfer"`, refId).
- `StockMovement` In di destination (`+qty`, `variant.CostPrice`).
- `UpsertStockAsync(variantId, source, −qty)` & `UpsertStockAsync(variantId, dest, +qty)`.
- `transfer.MarkPosted()`.

Validasi request via FluentValidation (source≠dest, ≥1 baris, qty>0).

---

## 6. Infrastructure & Web
- Migration `AddStockTransfer` (2 tabel + NumberSequence TRF).
- Menu `inventory.transfers` — actions index/create/edit/delete/approve/post.
- Pages:
  - Index `/inventory/transfers` (`.pi` + status chips + Pager).
  - Form `/inventory/transfers/new` & `/{id}/edit` (`.cf`: pilih gudang asal/tujuan + baris varian; reuse pola product/variant picker Stock Adjustment).
  - Detail `/inventory/transfers/{id}` (`.pf`: info + baris + tombol Submit/Approve/Reject + tampil langkah approval).
- BootstrapSeeder: seed default chain StockTransfer.

---

## 7. Testing
Integration (SQLite EnsureCreated; isolasi via Id/kode sendiri):
- Seed 2 gudang + 1 varian + stok awal di gudang asal. Create → Submit → Approve (chain admin) → **stok asal berkurang, stok tujuan bertambah**, Status Posted.
- Stok asal kurang dari qty → post/approve ditolak (ValidationException), stok tak berubah.
- Source == destination → CreateAsync ditolak.
- Chain kosong → Submit langsung Posted.
- **Test gotcha:** stok awal via seed `ProductStock` langsung (variant, warehouse, qty) atau `IStockService`; isolasi by ProductId/warehouse sendiri (DB shared). Bump assert count `NumberSequenceServiceTests` (12 → 13).

---

## 8. Manual Steps
- Restart + sign out/in (permission `inventory.transfers.*`).
- Konfigurasi approver StockTransfer di Settings → Approval Chain bila mau role non-admin.

## 9. File Impact
**Create:** `StockTransfer.cs`, `StockTransferLine.cs`, `StockTransferStatus.cs` (Domain/Entities/Transactions); `StockTransferDtos.cs`, `IStockTransferService.cs`, `StockTransferValidators.cs` (Application/StockTransfers); `StockTransferService.cs` (Infrastructure/Services/Transactions); migration; Index/Form/Detail razor (Web/Components/Pages/Inventory/Transfers); `StockTransferServiceTests.cs`.
**Modify:** `ApprovalDocumentType.cs` (+StockTransfer); `AppDbContext.cs` (DbSet+config+tablePrefix+NumberSequence Id=13); `DependencyInjection.cs`; `AppMenus.cs`; `BootstrapSeeder.cs`; `NumberSequenceServiceTests.cs` (assert +1).
**Tidak menyentuh 5b** (tanpa jurnal).
