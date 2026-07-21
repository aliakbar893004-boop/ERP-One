# Fase 2a — Retur Pembelian (Purchase Return / Debit Note) — Design

**Tanggal:** 2026-07-20
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`

## Ringkasan

Dokumen retur barang ke supplier dengan alur approval. Mendukung **dua jalur sumber**:
- **Jalur GRN** — barang sudah diterima tapi **belum di-invoice**: retur membalik GR-IR (`Dr GR-IR / Cr Inventory`), tanpa menyentuh AP/PPN.
- **Jalur Supplier Invoice** — barang sudah di-invoice: debit note mengurangi Outstanding invoice (`Dr AP / Cr Input Tax / Cr Inventory (+ variance GR-IR)`).

Retur mengeluarkan stok pada HPP baris GRN (moving-average **tidak** diubah). Mendukung retur **sebagian & beberapa kali** dengan pagar sisa-qty di tingkat baris GRN.

## Keputusan brainstorming (2026-07-20)

1. **Cakupan:** dua jalur — barang sudah diterima, baik **belum** maupun **sudah** di-invoice.
2. **Anchor:** jalur GRN → satu GoodsReceipt (posted); jalur Invoice → satu SupplierInvoice (punya Outstanding).
3. **HPP:** stok keluar `MovementType.Out` pada **UnitCost baris GRN**; moving-average **TIDAK** dihitung ulang (konsisten dgn semua keluaran lain).
4. **Sisi hutang:** GRN → reverse GR-IR; Invoice → debit note kurangi Outstanding invoice + reverse Input Tax.
5. **Approval:** `Draft → PendingApproval → Posted`, reuse `IApprovalService` + separation-of-duties (pembuat ≠ approver). Efek stok/AP/GL baru terjadi saat fully-approved.
6. **Sisa qty:** dilacak di tingkat **baris GRN** lintas kedua jalur (cegah retur ganda atas unit sama).

## Arsitektur

### 1. Domain (`src/ErpOne.Domain/Entities/Transactions/`)

```
PurchaseReturnStatus { Draft, PendingApproval, Posted }
PurchaseReturnSource { GoodsReceipt, SupplierInvoice }

PurchaseReturnLine
  - Id, PurchaseReturnId (private set)
  - GoodsReceiptLineId          // jangkar fisik → stok + sisa qty + WarehouseId + HPP
  - SupplierInvoiceLineId (int?) // hanya jalur Invoice
  - ProductVariantId, WarehouseId, VariantSku, ProductName
  - Quantity
  - UnitCost                    // HPP baris GRN (utk Cr Inventory)
  - UnitPrice, DiscountPercent, TaxRateSnapshot   // jalur Invoice; jalur GRN: UnitPrice=UnitCost, Disc=0, Tax=0
  - LineSubtotal, LineDiscount, LineTax, LineTotal
  - ctor(...) → Recompute() (pola SupplierInvoiceLine)

PurchaseReturn : AuditableEntity
  - Id, ReturnNumber, SupplierId, SourceType (PurchaseReturnSource)
  - GoodsReceiptId (int?), SupplierInvoiceId (int?)   // salah satu terisi sesuai SourceType
  - ReturnDate, Notes, Status, RejectionNote
  - Subtotal, DiscountTotal, TaxTotal, GrandTotal, InventoryTotal
  - IReadOnlyCollection<PurchaseReturnLine> Lines
  - ctor(returnNumber, supplierId, sourceType, goodsReceiptId?, supplierInvoiceId?, returnDate, notes)
  - AddLine(grnLineId, invLineId?, variantId, warehouseId, sku, name, qty, unitCost, unitPrice, discountPercent, taxRate)
  - SetTotals(subtotal, discountTotal, taxTotal, grandTotal, inventoryTotal)
  - UpdateHeader(returnDate, notes)    // EnsureDraft — TANPA ubah sumber/anchor
  - ClearLines()                        // EnsureDraft (rebuild saat Update)
  - Submit()     // EnsureDraft, Lines>0 → PendingApproval
  - MarkPosted() // PendingApproval → Posted
  - ReturnToDraft(reason) // PendingApproval → Draft + RejectionNote
  - private EnsureDraft()
```

Pola entity: `private set`, ctor privat `// EF Core`, backing `List<>` sbg `IReadOnlyCollection`, invariant `throw`. Namespace flat `ErpOne.Domain.Entities`.

**SupplierInvoice (tambah konsep kredit/debit-note):**
```
+ decimal CreditedAmount { get; private set; }
  Outstanding => GrandTotal - PaidAmount - CreditedAmount   // ubah formula existing
+ void ApplyCredit(decimal amount)
    - amount > 0; Status != Cancelled; PaidAmount + CreditedAmount + amount <= GrandTotal (else throw)
    - CreditedAmount += amount
    - Status = (PaidAmount + CreditedAmount >= GrandTotal) ? Paid : (PaidAmount + CreditedAmount > 0 ? PartiallyPaid : Open)
+ void ReverseCredit(decimal amount)   // simetri, utk kelengkapan (tak dipakai v1 — no void retur)
```
> `Outstanding` sudah dipakai luas (AR/AP aging, dashboard). Perubahan formula hanya **mengurangi** Outstanding saat ada kredit; invoice tanpa retur `CreditedAmount=0` → perilaku tak berubah. Cek pemakaian `Outstanding`/`PaidAmount` di aging & SupplierPayment tetap valid (pembayaran dibatasi `PaidAmount + amount <= GrandTotal` — pertimbangkan apakah perlu juga memperhitungkan CreditedAmount; lihat §catatan).

### 2. Enums / konstanta

- `ApprovalDocumentType` += `PurchaseReturn`.
- `DocumentTypes` += `public const string PurchaseReturn = "PurchaseReturn";`.
- NumberSequence `HasData` **Id=16** Code="PurchaseReturn" Prefix="DN" DateFormat="yyyyMM" Padding=4 ResetPeriod.Monthly Separator="-". Migration `AddPurchaseReturn`.
- `tablePrefixes`: `[nameof(PurchaseReturn)]="T_"`, `[nameof(PurchaseReturnLine)]="T_"`.
- EF: SupplierInvoice `CreditedAmount` kolom baru `HasPrecision(18,2)` default 0 (migration yang sama).

### 3. Application (`src/ErpOne.Application/Purchasing/PurchaseReturns/`)

- `PurchaseReturnDtos.cs`:
  - `ReturnableLineDto(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId, string Sku, string ProductName, int WarehouseId, string WarehouseName, int SourceQty, int AlreadyReturnedQty, int RemainingQty, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot)`.
  - `ReturnableSourceDto(string SourceType, int GoodsReceiptId?, int SupplierInvoiceId?, string SourceNumber, int SupplierId, string SupplierName, IReadOnlyList<ReturnableLineDto> Lines)`.
  - `PurchaseReturnLineInput(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int Quantity)`.
  - `CreatePurchaseReturnRequest(string SourceType, int? GoodsReceiptId, int? SupplierInvoiceId, DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines)`.
  - `UpdatePurchaseReturnRequest(DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines)`.
  - `PurchaseReturnLineDto(int Id, int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId, string Sku, string ProductName, string WarehouseName, int Quantity, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineTotal)`.
  - `PurchaseReturnDto(int Id, string ReturnNumber, string SourceType, int? GoodsReceiptId, string? GrnNumber, int? SupplierInvoiceId, string? InvoiceNumber, int SupplierId, string SupplierName, DateTime ReturnDate, string? Notes, string Status, string? RejectionNote, string? CreatedBy, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, decimal InventoryTotal, IReadOnlyList<PurchaseReturnLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps)`.
  - `PurchaseReturnListItemDto(int Id, string ReturnNumber, DateTime ReturnDate, string SourceType, string SupplierName, int LineCount, decimal GrandTotal, string Status)`.
  - `ReturnableSourceOptionDto(string SourceType, int DocId, string DocNumber, DateTime DocDate, string SupplierName)` — utk dropdown pilih sumber di Form.
- `IPurchaseReturnService`:
  - `GetReturnableGrnsAsync(search?)` & `GetReturnableInvoicesAsync(search?)` → `ReturnableSourceOptionDto[]` (GRN posted yang masih ada sisa; Invoice ber-Outstanding & ada sisa).
  - `GetReturnableSourceAsync(string sourceType, int docId)` → `ReturnableSourceDto?` (baris + sisa qty).
  - `GetByIdAsync(id)`, `GetPagedAsync(page, pageSize, search?, PurchaseReturnStatus? status)`.
  - `CreateAsync(CreatePurchaseReturnRequest)`, `UpdateAsync(id, UpdatePurchaseReturnRequest)` (Draft), `DeleteAsync(id)` (Draft).
  - `SubmitAsync(id)`, `ApproveAsync(id, actingUserName, isInRole)`, `RejectAsync(id, actingUserName, isInRole, reason)`.
- `PurchaseReturnValidators.cs`: `CreatePurchaseReturnValidator` — SourceType ∈ {GoodsReceipt, SupplierInvoice}; tepat satu dari GoodsReceiptId/SupplierInvoiceId terisi sesuai SourceType; Lines NotEmpty; tiap Quantity>0 & GoodsReceiptLineId>0; jalur Invoice → SupplierInvoiceLineId wajib.

### 4. Infrastructure (`src/ErpOne.Infrastructure/Services/Purchasing/PurchaseReturnService.cs`)

Primary-ctor DI: `AppDbContext db, IApprovalService approval, IStockService stock, IValidator<CreatePurchaseReturnRequest> validator, IDocumentNumberService docNumbers, IJournalPostingService journalPoster`.

**Sisa qty per baris GRN** (`ReturnedQtyByGrnLineAsync`):
```
returned = Σ PurchaseReturnLine.Quantity
           join parent PurchaseReturn WHERE Status ∈ {PendingApproval, Posted}
           group by GoodsReceiptLineId
remaining(grnLine) = GoodsReceiptLine.QuantityReceived − returned
```
Jalur Invoice tambahan dibatasi: `min(remaining(grnLine), invoiceLine.Quantity − Σ returnedViaInvoiceLine)`.

**GetReturnableSourceAsync:**
- Jalur GRN: muat GRN posted + lines; per baris hitung remaining; WarehouseId dari `PO.WarehouseId` (via `GRN.PurchaseOrderId`). `UnitPrice=UnitCost, Disc=0, Tax=0`.
- Jalur Invoice: muat SupplierInvoice + lines (punya GoodsReceiptLineId, UnitPrice, DiscountPercent, TaxRateSnapshot); per baris remaining = min(GRN-line-remaining, invLine.Qty − returnedViaInvLine); UnitCost = GoodsReceiptLine.UnitCost; WarehouseId dari PO gudang GRN itu.

**CreateAsync:** validate → `tx` → resolusi supplier + baris kandidat (via GetReturnableSource) → validasi tiap input `Quantity <= remaining` → generate nomor `DN` → bangun `PurchaseReturn` + AddLine per input (hitung Line* pola SupplierInvoiceLine; jalur GRN tanpa pajak) → hitung & `SetTotals(Subtotal, DiscountTotal, TaxTotal, GrandTotal, InventoryTotal)` → save → commit. (Draft; belum ada efek stok/AP/GL.)

**UpdateAsync (Draft):** revalidasi remaining (kecualikan dokumen ini sendiri) → `UpdateHeader` + `ClearLines` + AddLine ulang + SetTotals.

**PostAsync(PurchaseReturn r, ct)** (dipanggil saat fully-approved, ikut tx caller):
```
// 1. Stok keluar (guard on-hand cukup dulu)
foreach line:
    onHand = await stock.GetOnHandAsync(line.ProductVariantId, line.WarehouseId, ct)
    if onHand < line.Quantity: throw Fail("Stok tidak cukup untuk retur ...")
foreach line:
    db.StockMovements.Add(new StockMovement(line.ProductVariantId, line.WarehouseId, MovementType.Out,
        -line.Quantity, line.UnitCost, r.ReturnDate, "PurchaseReturn", r.Id, r.ReturnNumber))
    await db.UpsertStockAsync(line.ProductVariantId, line.WarehouseId, -line.Quantity, ct)
    // MA TIDAK diubah
// 2. AP (jalur Invoice)
if r.SourceType == SupplierInvoice:
    inv = load SupplierInvoice(r.SupplierInvoiceId)
    if r.GrandTotal > inv.Outstanding: throw Fail("Retur melebihi Outstanding invoice.")
    inv.ApplyCredit(r.GrandTotal)
// 3. GL
await journalPoster.PostPurchaseReturnAsync(r, ct)
r.MarkPosted()
```

**SubmitAsync/ApproveAsync/RejectAsync:** persis pola `StockTransferService` (Submit→ResetAsync+SubmitAsync→fully? PostAsync; Approve→fully? PostAsync; Reject→ReturnToDraft+ResetAsync). `Fail(string)`→`ValidationException`.
DI: `services.AddScoped<IPurchaseReturnService, PurchaseReturnService>();`

### 5. GL — `IJournalPostingService.PostPurchaseReturnAsync(PurchaseReturn r)`

Metode baru di engine 5b. SourceType `"PurchaseReturn"`, SourceId `r.Id`, **idempoten**. Akun dari `PostingConfiguration` (fail-hard bila hilang).
- **SourceType == GoodsReceipt:**
  - `Dr GR-IR (r.InventoryTotal)` / `Cr Inventory (r.InventoryTotal)`.
- **SourceType == SupplierInvoice:**
  - `Dr AP (r.GrandTotal)`
  - `Cr Input Tax (r.TaxTotal)` bila > 0
  - `Cr Inventory (r.InventoryTotal)`
  - baris **GR-IR** penyeimbang = `net − InventoryTotal` (`net = Subtotal − DiscountTotal`); bila ≠ 0 taruh di sisi yang menyeimbangkan (Cr bila net>Inventory, Dr bila net<Inventory). Biasanya 0 saat harga tagih = HPP.
  - Selalu balanced: `Dr AP(net+tax) = Cr Tax(tax) + Cr Inventory(inv) + Cr GR-IR(net−inv)`.

### 6. Web (`src/ErpOne.Web/`)

- Menu `AppMenus.cs`: grup Transactions, resource `transactions.purchase-returns` dgn `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost]` (icon mis. `bi-arrow-return-left`).
- `BootstrapSeeder.cs`: seed default chain `ApprovalChainStep(PurchaseReturn, 1, roleName)` (idempotent), setelah blok POS Void.
- Halaman `Components/Pages/Transactions/PurchaseReturns/`:
  - `PurchaseReturnIndex.razor` (`/transactions/purchase-returns`) — `.pi` + chips status + tabel (No, Tgl, Sumber, Supplier, #Baris, Grand Total, Status). Pola StockTransferIndex.
  - `PurchaseReturnForm.razor` (`/transactions/purchase-returns/new` & `/{id}/edit`) — `.cf`: pilih **jalur** (GRN/Invoice) → dropdown sumber (`GetReturnableGrns/Invoices`) → muat baris (`GetReturnableSourceAsync`) → tabel Produk · Gudang · Sisa · Qty retur (input) → tanggal + catatan → Save draft.
  - `PurchaseReturnDetail.razor` (`/transactions/purchase-returns/{id}`) — `.pf pf-detail`; header + tabel baris + ringkasan (Subtotal/Disc/Tax/Grand/Inventory) + Approval; tombol Submit/Approve/Reject — reuse plumbing `StockTransferDetail` (CascadingParameter AuthStateTask, EvaluateCanApproveAsync creator-exclusion, inline reject, RunAsync).

### 7. Tests (`tests/ErpOne.IntegrationTests/PurchaseReturnServiceTests.cs`)

Pola `StockTransferServiceTests` (SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`, seed chain `PurchaseReturn` manual sebelum Submit; `AccountingSeeder` sudah jalan → GL mapping ada). Helper seed: PO→GRN posted (stok + MA), opsional SupplierInvoice.
1. **Jalur GRN full return:** GRN posted qty 10 → retur 10 → approve → on-hand −10, 1 StockMovement Out, JE `Dr GR-IR/Cr Inventory` (SourceType PurchaseReturn), AP tak tersentuh.
2. **Jalur Invoice full return:** GRN→Invoice → retur semua → approve → on-hand turun, `SupplierInvoice.Outstanding` berkurang sebesar GrandTotal (via CreditedAmount), JE `Dr AP/Cr Inventory(+Cr Input Tax)`.
3. **Partial + sisa qty:** retur sebagian → remaining berkurang; retur kedua atas sisa → remaining 0; retur ketiga → ditolak (over-return).
4. **On-hand kurang:** jual/keluarkan stok dulu shg < qty retur → approve → ValidationException; stok & AP tak berubah.
5. **Retur > Outstanding invoice ditolak:** bayar invoice hampir penuh → retur > sisa Outstanding → approve → ValidationException.
- Bump `NumberSequenceServiceTests` assert 15→16.

Target: **+~5 test** (baseline 323 → ~328).

## Non-Goals (YAGNI)

- Void/undo retur yang sudah Posted.
- Refund tunai dari supplier atas invoice yang sudah lunas penuh (retur dibatasi ≤ Outstanding).
- Reverse moving-average.
- Retur barang yang belum pernah diterima (tak ada baris GRN).
- Menyesuaikan qty yang bisa di-invoice akibat retur pra-invoice (lihat batasan).

## Batasan yang diketahui

- Retur pra-invoice (jalur GRN) **tidak** otomatis mengurangi qty yang bisa di-invoice; alur "buat invoice dari GRN" masih menagih qty diterima. Pagar `returnable` per baris GRN mencegah retur ganda atas unit sama, tapi bila retur pra-invoice lalu buat invoice, invoice tetap menagih penuh — perlu penyesuaian manual. Rekomendasi operasional: pilih satu jalur per unit.
- Variance harga (net billed ≠ HPP GRN) diserap ke GR-IR (konsisten dgn perilaku dasar sistem).
- **Catatan `SupplierPayment`:** guard bayar saat ini `PaidAmount + amount <= GrandTotal`. Setelah ada `CreditedAmount`, idealnya `PaidAmount + CreditedAmount + amount <= GrandTotal`. Plan harus memperbarui guard `ApplyPayment` agar tak bisa membayar melebihi Outstanding baru. (Perubahan kecil, sertakan test.)
