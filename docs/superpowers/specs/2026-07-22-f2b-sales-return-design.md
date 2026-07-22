# Fase 2b — Retur Penjualan (Sales Return / Credit Note) — Design

**Tanggal:** 2026-07-22
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`
**Prasyarat:** Fase 2a Purchase Return selesai (pola mirror), costing seam Tahap 1–4 selesai (stok masuk lewat `ICostingService.OnInboundAsync`), AR (CustomerInvoice/CustomerReceipt) Fase 3 selesai.

## Ringkasan

Dokumen retur barang **dari customer** dengan alur approval (`Draft → PendingApproval → Posted`). Cermin dari Purchase Return, terbalik: barang **masuk kembali** ke stok dan (jalur invoice) menerbitkan **credit note** yang mengurangi piutang. Mendukung **dua jalur sumber**:
- **Jalur Delivery Order** — barang sudah dikirim tapi **belum di-invoice**: retur membalik COGS (`Dr Inventory / Cr COGS`), tanpa menyentuh AR/PPN.
- **Jalur Customer Invoice** — barang sudah di-invoice: credit note mengurangi Outstanding invoice (`Dr Inventory / Cr COGS` untuk barang + `Dr Sales / Dr Output Tax / Cr AR` untuk kredit).

Stok masuk kembali **lewat seam costing** (`OnInboundAsync`) pada **snapshot COGS baris DO** (biaya saat barang keluar) — konsisten & benar untuk keempat metode HPP. Mendukung retur **sebagian & beberapa kali** dengan pagar sisa-qty di tingkat baris DO.

## Keputusan brainstorming (2026-07-22)

1. **Cakupan:** dua jalur — barang sudah dikirim, baik **belum** maupun **sudah** di-invoice (mirror Purchase Return).
2. **Biaya inbound:** stok masuk pada **`DeliveryOrderLine.UnitCost`** (snapshot COGS saat pengiriman) via `OnInboundAsync` — bukan HPP varian terkini. FIFO buat layer baru di biaya itu; MA hitung ulang; per-gudang update.
3. **Anchor fisik = `DeliveryOrderLine`** (punya `UnitCost` COGS, `QuantityDelivered`, variant; warehouse via `DO→SO.WarehouseId`). Jalur invoice menautkan `CustomerInvoiceLineId` (lewat `SalesOrderLineId`) untuk credit note + AR.
4. **Sisi piutang:** DO → balik COGS saja; Invoice → credit note kurangi Outstanding invoice + reverse Output Tax.
5. **Approval:** `Draft → PendingApproval → Posted`, reuse `IApprovalService` + separation-of-duties. Efek stok/AR/GL baru saat fully-approved.
6. **Sisa qty:** dilacak di tingkat **baris DO** lintas kedua jalur (cegah retur ganda atas unit sama).

## Asimetri kunci vs Purchase Return

Pada pembelian, `SupplierInvoiceLine` menjangkar langsung ke `GoodsReceiptLine` (fisik). Pada penjualan, **`CustomerInvoiceLine` diturunkan dari `SalesOrderLine`** (pricing), sedangkan biaya/qty fisik ada di **`DeliveryOrderLine`**. Keduanya bertemu di `SalesOrderLineId`. Karena itu Sales Return menjangkar pada `DeliveryOrderLine` (fisik/biaya) dan, untuk jalur invoice, menautkan ke invoice via SO line: `DeliveryOrderLine.SalesOrderLineId ↔ CustomerInvoiceLine.SalesOrderLineId`. Warehouse tunggal per SO (`SalesOrder.WarehouseId`) sehingga tak ambigu.

## Arsitektur

### 1. Domain (`src/ErpOne.Domain/Entities/Transactions/`)

```
SalesReturnStatus { Draft, PendingApproval, Posted }
SalesReturnSource { DeliveryOrder, CustomerInvoice }

SalesReturnLine
  - Id, SalesReturnId (private set)
  - DeliveryOrderLineId          // jangkar fisik → stok + sisa qty + WarehouseId + COGS
  - CustomerInvoiceLineId (int?) // hanya jalur Invoice (credit + link AR)
  - ProductVariantId, WarehouseId, VariantSku, ProductName
  - Quantity
  - UnitCost                    // COGS snapshot dari DO line (utk Dr Inventory / Cr COGS)
  - UnitPrice, DiscountPercent, TaxRateSnapshot   // jalur Invoice (pricing invoice line); jalur DO: 0
  - LineSubtotal, LineDiscount, LineTax, LineTotal // nilai tertagih utk credit note; jalur DO: 0
  - ctor(...) → Recompute() (pola SupplierInvoiceLine/PurchaseReturnLine)
  - SetUnitCost(decimal)        // biasanya tetap = DO snapshot; disimpan utk simetri & kejelasan

SalesReturn : AuditableEntity
  - Id, ReturnNumber, CustomerId, SourceType (SalesReturnSource)
  - DeliveryOrderId (int?), CustomerInvoiceId (int?)   // salah satu terisi sesuai SourceType
  - ReturnDate, Notes, Status, RejectionNote
  - Subtotal, DiscountTotal, TaxTotal, GrandTotal, InventoryTotal
  - IReadOnlyCollection<SalesReturnLine> Lines
  - ctor(returnNumber, customerId, sourceType, deliveryOrderId?, customerInvoiceId?, returnDate, notes)
  - SetLines(IEnumerable<SalesReturnLine>) → RecomputeTotals()   // Subtotal/Disc/Tax/Grand + InventoryTotal = Σ round(qty×UnitCost)
  - UpdateHeader(returnDate, notes)   // EnsureDraft
  - RecomputeInventoryTotal()          // dipakai bila biaya di-refresh saat post
  - Submit() / MarkPosted() / ReturnToDraft(reason) / private EnsureDraft()
```

Pola entity: `private set`, ctor privat `// EF Core`, backing `List<>` sbg `IReadOnlyCollection`, invariant `throw`, namespace flat `ErpOne.Domain.Entities`, extends `AuditableEntity`. `Round = Math.Round(v, 2, MidpointRounding.AwayFromZero)`.

**CustomerInvoice (tambah konsep kredit):**
```
+ decimal CreditedAmount { get; private set; }
  Outstanding => GrandTotal - PaidAmount - CreditedAmount   // ubah formula existing
+ void ApplyCredit(decimal amount)     // guard: PaidAmount + CreditedAmount + amount <= GrandTotal; status transition
+ void ReverseCredit(decimal amount)   // simetri, tak dipakai v1
  ApplyPayment guard → PaidAmount + CreditedAmount + amount <= GrandTotal   // perketat
```
> `Outstanding` dipakai di AR aging, dashboard, credit-limit, `CustomerReceiptService`. Perubahan hanya **mengurangi** Outstanding saat ada kredit; invoice tanpa retur `CreditedAmount=0` → perilaku tak berubah. **Perbarui `CustomerReceiptService`** guard outstanding agar memperhitungkan `CreditedAmount` (mirror perubahan `SupplierPaymentService` pada Fase 2a).

### 2. Enums / konstanta

- `ApprovalDocumentType` += `SalesReturn`.
- `DocumentTypes` += `public const string SalesReturn = "SalesReturn";` (namespace `ErpOne.Application.Numbering`).
- NumberSequence `HasData` **Id=17** Code="SalesReturn" Prefix="CN" DateFormat="yyyyMM" Padding=4 ResetPeriod.Monthly Separator="-". Migration `AddSalesReturn`.
- `tablePrefixes`: `[nameof(SalesReturn)]="T_"`, `[nameof(SalesReturnLine)]="T_"`.
- EF: CustomerInvoice `CreditedAmount` kolom baru `HasPrecision(18,2)` default 0 (migration yang sama); pastikan `Ignore(Outstanding)` tetap.

### 3. Application (`src/ErpOne.Application/Sales/SalesReturns/`)

- `SalesReturnDtos.cs` (mirror PurchaseReturnDtos):
  - `ReturnableLineDto(int DeliveryOrderLineId, int? CustomerInvoiceLineId, int ProductVariantId, string Sku, string ProductName, int WarehouseId, string WarehouseName, int SourceQty, int AlreadyReturnedQty, int RemainingQty, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot)`.
  - `ReturnableSourceDto(string SourceType, int? DeliveryOrderId, int? CustomerInvoiceId, string SourceNumber, int CustomerId, string CustomerName, IReadOnlyList<ReturnableLineDto> Lines)`.
  - `ReturnableSourceOptionDto(string SourceType, int DocId, string DocNumber, DateTime DocDate, string CustomerName)`.
  - `SalesReturnLineInput(int DeliveryOrderLineId, int? CustomerInvoiceLineId, int Quantity)`.
  - `CreateSalesReturnRequest(string SourceType, int? DeliveryOrderId, int? CustomerInvoiceId, DateTime ReturnDate, string? Notes, IReadOnlyList<SalesReturnLineInput> Lines)`.
  - `UpdateSalesReturnRequest(DateTime ReturnDate, string? Notes, IReadOnlyList<SalesReturnLineInput> Lines)`.
  - `SalesReturnLineDto(...)`, `SalesReturnDto(...)` (incl. DoNumber/InvoiceNumber, CustomerName, totals, Lines, ApprovalSteps), `SalesReturnListItemDto(...)`.
- `ISalesReturnService`:
  - `GetReturnableDeliveryOrdersAsync(search?)` & `GetReturnableInvoicesAsync(search?)` → opsi sumber (DO posted masih ada sisa; Invoice ber-Outstanding & ada sisa terkirim).
  - `GetReturnableSourceAsync(sourceType, docId)` → baris + sisa qty.
  - `GetByIdAsync(id)`, `GetPagedAsync(page, pageSize, search?, SalesReturnStatus? status)`.
  - `CreateAsync`, `UpdateAsync` (Draft), `DeleteAsync` (Draft).
  - `SubmitAsync`, `ApproveAsync(id, actingUserName, isInRole)`, `RejectAsync(id, actingUserName, isInRole, reason)`.
- `SalesReturnValidators.cs`: SourceType ∈ {DeliveryOrder, CustomerInvoice}; tepat satu dari DeliveryOrderId/CustomerInvoiceId terisi; Lines NotEmpty; tiap Quantity>0 & DeliveryOrderLineId>0; jalur Invoice → CustomerInvoiceLineId wajib.

### 4. Infrastructure (`src/ErpOne.Infrastructure/Services/Sales/SalesReturnService.cs`)

Primary-ctor DI: `AppDbContext db, IApprovalService approval, IStockService stock, ICostingService costing, IValidator<CreateSalesReturnRequest> validator, IDocumentNumberService docNumbers, IJournalPostingService journalPoster`. `private const ApprovalDocumentType DocType = ApprovalDocumentType.SalesReturn;`. Pola lifecycle & `Fail(string)` mirror `PurchaseReturnService`/`StockTransferService`.

**Sisa qty per baris DO** (`ReturnedQtyByDoLineAsync`):
```
returned = Σ SalesReturnLine.Quantity
           join parent SalesReturn WHERE Status ∈ {PendingApproval, Posted}
           group by DeliveryOrderLineId
remaining(doLine) = DeliveryOrderLine.QuantityDelivered − returned
```
Jalur Invoice tambahan dibatasi: qty tertagih pada SO line terkait (`min(remaining(doLine), invoicedQty(SO line) − Σ returnedViaInvoiceLine)`).

**GetReturnableSourceAsync:**
- **Jalur DO:** muat DeliveryOrder posted + lines (**Include(Lines)**); per baris hitung remaining; variant/UnitCost/qty dari DO line; WarehouseId dari `DO→SO.WarehouseId`; pricing 0.
- **Jalur Invoice:** muat CustomerInvoice + lines (**Include(Lines)**); tiap invoice line (SO line) → cari DO line yang mengirim SO line itu (untuk COGS/qty/warehouse); remaining = min(sisa DO line, sisa invoiced); UnitPrice/Disc/Tax dari invoice line; UnitCost dari DO line.

**CreateAsync / UpdateAsync (Draft):** validate → tx → resolusi customer + baris kandidat (via GetReturnableSource, Update mengecualikan dokumen ini sendiri) → validasi tiap input `Quantity <= remaining` → generate nomor `CN` → bangun `SalesReturn` + SetLines → save → commit.

**PostAsync(SalesReturn r, ct)** (saat fully-approved, ikut tx caller):
```
foreach line:
    // stok MASUK kembali pada COGS snapshot DO line
    db.StockMovements.Add(new StockMovement(variantId, warehouseId, MovementType.In,
        +qty, line.UnitCost, r.ReturnDate, "SalesReturn", r.Id, r.ReturnNumber))
    await db.UpsertStockAsync(variantId, warehouseId, +qty, ct)
    await costing.OnInboundAsync(variantId, warehouseId, qty, line.UnitCost, ct)  // FIFO layer / MA / per-gudang
r.RecomputeInventoryTotal()
if r.SourceType == CustomerInvoice:
    inv = load CustomerInvoice(r.CustomerInvoiceId)
    if r.GrandTotal > inv.Outstanding: throw Fail("Retur melebihi Outstanding invoice.")
    inv.ApplyCredit(r.GrandTotal)
await journalPoster.PostSalesReturnAsync(r, ct)
r.MarkPosted()
```
> Berbeda dari Purchase Return (outbound → butuh guard on-hand), Sales Return adalah **inbound** — tak perlu guard stok. Biaya inbound = `line.UnitCost` (snapshot DO), eksplisit ke `OnInboundAsync`, jadi tak perlu memanggil `GetOutboundUnitCostAsync`. Cek `MovementType` masuk yang benar saat implementasi (mis. `MovementType.In`).

DI: `services.AddScoped<ISalesReturnService, SalesReturnService>();`.

### 5. GL — `IJournalPostingService.PostSalesReturnAsync(SalesReturn r)`

Metode baru. SourceType `"SalesReturn"`, SourceId `r.Id`, **idempoten** via `PostBalancedAsync`. Akun dari `PostingConfiguration`.
- **SourceType == DeliveryOrder:**
  - `Dr Inventory (r.InventoryTotal)` / `Cr COGS (r.InventoryTotal)`. (kebalikan posting DO)
- **SourceType == CustomerInvoice:**
  - `Dr Inventory (r.InventoryTotal)` / `Cr COGS (r.InventoryTotal)` — barang kembali
  - `Dr Sales (net)` `Dr Output Tax (tax bila > 0)` / `Cr AR (r.GrandTotal)` — credit note
  - `net = Subtotal − DiscountTotal`. Balanced: `Dr(inv + net + tax) = Cr(inv + GrandTotal)` dengan `GrandTotal = net + tax`. ✓
  - Akun: `InventoryAccountId`, `CogsAccountId`, `SalesAccountId`, `OutputTaxAccountId`, `ArAccountId`.

### 6. Web (`src/ErpOne.Web/`)

- Menu `AppMenus.cs`: grup Transaksi, resource `transactions.sales-returns` dgn `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost]` (icon mis. `bi-arrow-return-right`).
- `BootstrapSeeder.cs`: seed default chain `ApprovalChainStep(SalesReturn, 1, roleName)` (idempotent), setelah blok Purchase Return.
- Halaman `Components/Pages/Transactions/SalesReturns/` (mirror PurchaseReturns):
  - `SalesReturnIndex.razor` (`/transactions/sales-returns`) — `.pi` + chips status + tabel.
  - `SalesReturnForm.razor` (`.../new` & `.../{id}/edit`) — `.cf`: pilih jalur (DO/Invoice) → dropdown sumber → muat baris (Produk · Gudang · Sisa · Qty retur) → tanggal + catatan → Save draft.
  - `SalesReturnDetail.razor` (`.../{id}`) — `.pf pf-detail`; header + baris + ringkasan + Approval; Submit/Approve/Reject + Edit/Delete (Draft) — reuse plumbing PurchaseReturnDetail.

### 7. Tests (`tests/ErpOne.IntegrationTests/SalesReturnServiceTests.cs`)

Pola PurchaseReturnServiceTests (SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`, seed chain `SalesReturn` manual sebelum Submit; `AccountingSeeder` sudah jalan). Helper seed: SO→DO posted (stok keluar + COGS), opsional CustomerInvoice. Unit tests: `CustomerInvoiceCreditTests`, `SalesReturnTests` (mirror Fase 2a).
1. **Jalur DO full return:** DO qty 10 → retur 10 → approve → on-hand +10, StockMovement masuk, JE `Dr Inventory/Cr COGS`, AR tak tersentuh.
2. **Jalur Invoice full return:** DO→Invoice → retur semua → approve → on-hand naik, `CustomerInvoice.Outstanding` berkurang (via CreditedAmount), JE balanced (`Dr Inventory/Cr COGS` + `Dr Sales/Dr Output Tax/Cr AR`).
3. **Partial + sisa qty:** retur sebagian → remaining berkurang; retur kedua atas sisa → remaining 0; retur ketiga → ditolak.
4. **Retur > Outstanding invoice ditolak.**
5. **Costing:** FIFO → retur buat layer baru di COGS snapshot; outbound berikutnya memakainya (opsional, verifikasi seam).
- Bump `NumberSequenceServiceTests` assert 16→17.

## Non-Goals (YAGNI)

- Void/undo retur yang sudah Posted.
- Refund tunai ke customer atas invoice lunas penuh (retur dibatasi ≤ Outstanding).
- Retur barang yang belum pernah dikirim (tak ada baris DO).
- Penyesuaian qty yang bisa di-invoice akibat retur pra-invoice.

## Batasan yang diketahui

- Retur pra-invoice (jalur DO) tidak otomatis mengurangi qty yang bisa di-invoice; pagar `returnable` per baris DO mencegah retur ganda atas unit sama. Rekomendasi operasional: pilih satu jalur per unit.
- Satu SO line bisa dikirim lewat beberapa DO line (partial). Anchor per DO line menangani ini secara eksak (biaya & qty per pengiriman). Jalur invoice memetakan invoice line (SO line) → DO line via `SalesOrderLineId`; bila satu SO line punya banyak DO line, retur dipilih per DO line dengan pagar qty tertagih SO line.
- `CustomerReceipt` guard bayar: setelah ada `CreditedAmount`, gunakan `Outstanding` baru agar penerimaan tak melebihi sisa.
