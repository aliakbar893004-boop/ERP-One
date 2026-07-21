# Fase 2c — POS Void / Refund — Design

**Tanggal:** 2026-07-20
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`

## Ringkasan

Kemampuan membatalkan / mengembalikan transaksi POS. Sebuah **refund** adalah **dokumen baru** (`PosRefund`) yang merujuk `PosSale` asli; **full void = refund seluruh qty tersisa**. Mendukung refund **sebagian per-item** dan **beberapa kali** terhadap satu sale. Saat diposting, refund membalik stok, total shift (rekonsiliasi kas), dan jurnal GL secara proporsional. `PosSale` tetap **immutable** (tidak diubah/ditandai di tabelnya sendiri; status refund diturunkan dari dokumen refund).

Keputusan brainstorming (2026-07-20):
1. **Cakupan:** full **dan** partial (per-item, bisa beberapa refund per sale).
2. **Batas waktu:** hanya untuk sale di shift yang **masih Open** (dalam shift berjalan). Refund tercatat ke shift sale itu juga.
3. **Otorisasi:** reuse pola void finance — popup username+password supervisor → verifikasi kredensial + role dari approval-chain `PosSaleVoid` (fallback permission `cashier.pos.void`).
4. **Metode refund:** mengikuti metode pembayaran asli sale (tunai → kas keluar laci; non-tunai → balik total metode itu, tak sentuh laci).

## Arsitektur

### 1. Domain (`src/ErpOne.Domain/Entities/Cashier/`)

`PosSale` **tidak berubah** (tetap immutable, tanpa field status baru). Status refund (Completed/PartiallyRefunded/Refunded) diturunkan dari agregasi `PosRefundLine`.

```
PosRefund : AuditableEntity
  - Id, RefundNumber, PosSaleId (private set), CashierShiftId
  - RefundDate, PaymentMethodId, IsCashPayment
  - Subtotal, TransactionDiscount, TaxTotal, GrandTotal, CogsTotal   // jumlah yang di-refund (proporsional)
  - Reason, AuthorizedBy, CashierUserId, CashierName
  - IReadOnlyCollection<PosRefundLine> Lines
  - ctor(refundNumber, posSaleId, cashierShiftId, refundDate, paymentMethodId, isCashPayment, reason, authorizedBy, cashierUserId, cashierName)
  - AddLine(posSaleLineId, variantId, sku, name, qty, unitPrice, discountPercent, unitCost)  // hitung LineTotal
  - SetTotals(subtotal, txnDiscount, taxTotal, grandTotal, cogsTotal)  // di-set service setelah alokasi

PosRefundLine
  - Id, PosRefundId (private set), PosSaleLineId, ProductVariantId
  - VariantSku, ProductName, Quantity, UnitPrice, DiscountPercent, UnitCost, LineTotal
  - ctor(...) → Recompute LineTotal = round(qty*unitPrice*(1-disc/100))
```

Pola entity: `private set`, ctor privat `// EF Core`, backing `List<>` sebagai `IReadOnlyCollection`, invariant `throw`. Namespace flat `ErpOne.Domain.Entities`.

**CashierShift (tambah method reversal):**
```
CashierShift.RecordRefund(int paymentMethodId, bool isCash, decimal amount)
  - guard Status == Open, amount > 0
  - kurangi CashierShiftTotal metode itu via SubtractRefund(amount)  (TransactionCount TETAP — refund bukan sale baru)
  - if isCash: CashSalesTotal -= amount   (ExpectedCash turun = kas keluar laci)

CashierShiftTotal.SubtractRefund(decimal amount)
  - guard amount > 0 && amount <= TotalAmount ; TotalAmount -= amount ; TransactionCount tak berubah
```
> Aman dari negatif karena refund hanya untuk sale di shift yang sama & masih Open → total metode itu pasti sudah memuat sale tsb.

### 2. Enums / konstanta

- `ApprovalDocumentType` += `PosSaleVoid`.
- `DocumentTypes` += `public const string PosRefund = "PosRefund";`.
- NumberSequence `HasData` **Id=15** Code="PosRefund" Prefix="RFN" DateFormat="yyyyMMdd" Padding=4 ResetPeriod.Daily Separator="-". Migration `AddPosRefund`.
- `tablePrefixes`: `[nameof(PosRefund)]="T_"`, `[nameof(PosRefundLine)]="T_"`.

### 3. Application (`src/ErpOne.Application/Cashier/PosRefunds/`)

- `PosRefundDtos.cs`:
  - `PosRefundLineInput(int PosSaleLineId, int Quantity)`.
  - `CreatePosRefundRequest(string Reason, IReadOnlyList<PosRefundLineInput> Lines)`.
  - `RefundableLineDto(int PosSaleLineId, int ProductVariantId, string Sku, string ProductName, int SoldQty, int AlreadyRefundedQty, int RemainingQty, decimal UnitPrice, decimal DiscountPercent)`.
  - `PosRefundLineDto(int Id, int PosSaleLineId, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal)`.
  - `PosRefundDto(int Id, string RefundNumber, int PosSaleId, string SaleNumber, DateTime RefundDate, int PaymentMethodId, string PaymentMethodName, bool IsCashPayment, decimal Subtotal, decimal TransactionDiscount, decimal TaxTotal, decimal GrandTotal, string Reason, string? AuthorizedBy, string CashierName, IReadOnlyList<PosRefundLineDto> Lines)`.
  - `PosRefundListItemDto(int Id, string RefundNumber, DateTime RefundDate, string SaleNumber, string PaymentMethodName, decimal GrandTotal, string CashierName)`.
- `IPosRefundService`:
  - `GetRefundableAsync(int posSaleId)` → header sale + `RefundableLineDto[]` (remaining per baris) + status refund; null bila sale tak ada.
  - `RefundAsync(int posSaleId, CreatePosRefundRequest request, string cashierUserId, string cashierName, string authorizedBy)` → `PosRefundDto`.
  - `GetBySaleAsync(int posSaleId)` → `IReadOnlyList<PosRefundDto>` (riwayat refund sale).
  - `GetPagedAsync(page, pageSize, search?, shiftId?)`.
- `PosRefundValidators.cs`: `CreatePosRefundValidator` — `Lines` NotEmpty; tiap `Quantity` > 0; `Reason` NotEmpty.

### 4. Infrastructure (`src/ErpOne.Infrastructure/Services/Cashier/PosRefundService.cs`)

Primary-ctor DI: `AppDbContext db, IValidator<CreatePosRefundRequest> validator, IDocumentNumberService docNumbers, IJournalPostingService journalPoster`.

`RefundAsync` (dalam satu `tx`):
1. Validate request.
2. Load `PosSale` + `Lines`, `CashierShift`; `Fail` bila sale tak ada.
3. Guard: `shift.Status == Open` (else Fail "Shift sudah ditutup, tidak bisa refund"); tiap input baris → cari `PosSaleLine`, hitung `remaining = SoldQty − Σ refund sebelumnya`; `Quantity <= remaining` (else Fail); minimal 1 baris ber-qty > 0.
4. Buat `PosRefund` (nomor `RFN` via `docNumbers.NextAsync(DocumentTypes.PosRefund, now)`), salin `PaymentMethodId`/`IsCashPayment` dari sale.
5. Per baris input: `refund.AddLine(saleLine.Id, variantId, sku, name, qty, saleLine.UnitPrice, saleLine.DiscountPercent, saleLine.UnitCost)`; `StockMovement(v.Id, whId, MovementType.In, +qty, saleLine.UnitCost, now, refType:"PosRefund", refId:null, note: refund.RefundNumber)` + `UpsertStockAsync(+qty)` (mirror pola POS yang pakai `refId:null, note:SaleNumber`; MA tak di-recompute).
6. **Alokasi total** (lihat §5): hitung `subtotal, allocTxnDiscount, taxTotal, grandTotal, cogsTotal` → `refund.SetTotals(...)`.
7. `shift.RecordRefund(sale.PaymentMethodId, sale.IsCashPayment, grandTotal)`.
8. `db.PosRefunds.Add(refund)` → `SaveChangesAsync` → `journalPoster.PostPosRefundAsync(refund, ct)` → `tx.Commit`.
9. Return `GetByIdAsync(refund.Id)`.

`Fail(string)` helper → `ValidationException` (pola PosSaleService).
DI: `services.AddScoped<IPosRefundService, PosRefundService>();`

### 5. Perhitungan alokasi (partial-safe)

```
ΣrefundLineTotal = Σ round(qty * UnitPrice * (1 - DiscountPercent/100))     // = subtotal refund
allocTxnDiscount = Sale.Subtotal == 0 ? 0 : round(Sale.TransactionDiscount * ΣrefundLineTotal / Sale.Subtotal)
base       = ΣrefundLineTotal - allocTxnDiscount
taxTotal   = round(base * Sale.TaxRateSnapshot / 100)
grandTotal = base + taxTotal
cogsTotal  = Σ round(qty * UnitCost)
round(x)   = Math.Round(x, 2, MidpointRounding.AwayFromZero)
```
- **Full void** (semua baris, semua qty, belum pernah refund) menghasilkan angka **persis** = sale asli (allocTxnDiscount = TransactionDiscount, taxTotal = TaxTotal, grandTotal = GrandTotal).
- **Batasan diketahui:** beberapa refund parsial berturut bisa selisih beberapa sen vs sale asli karena pembulatan per-refund. Diterima untuk v1.

### 6. GL — `IJournalPostingService.PostPosRefundAsync(PosRefund refund)`

Metode baru di engine 5b (bukan `ReverseForAsync` yang membalik satu JE penuh). Membalik jurnal POS secara proporsional, SourceType `"PosRefund"`, SourceId `refund.Id`, **idempoten**:
- Dr Sales Revenue `base`, Dr Tax Payable `taxTotal`, Cr Cash/Bank `grandTotal`.
- Dr Inventory `cogsTotal`, Cr COGS `cogsTotal`.

Akun diambil dari `PostingConfiguration` + fallback kas (pola `PostPosSaleAsync`). Konsisten dgn keputusan 5b (fail-hard bila mapping hilang). Titik panggil = setelah `SaveChanges`, sebelum `tx.Commit` (ikut tx caller).

### 7. Web (`src/ErpOne.Web/`)

- Menu `AppMenus.cs`: tambah action `void` ke resource **`cashier.pos`** (jadi `cashier.pos.void`).
- `BootstrapSeeder.cs`: seed default chain `ApprovalChainStep(PosSaleVoid, 1, roleName)` (idempotent), setelah blok Stock Opname.
- `PosSaleDetail.razor` (sudah ada): 
  - Badge status refund (Completed / Partially refunded / Refunded).
  - Tombol **Refund / Void** (policy `cashier.pos.void`) → panel: tabel baris dgn kolom Sold / Already refunded / Remaining / **Refund qty** (input, default 0; tombol "Refund all" isi semua remaining = full void) + textarea Reason.
  - Konfirmasi → **popup otorisasi** (username+password) mengikuti pola `ArReceiptDetail`: `UserManager.CheckPasswordAsync` → role di chain `PosSaleVoid` (fallback `UserHasVoidPermissionAsync` cek `cashier.pos.void`) → `authorizedBy` → `PosRefundService.RefundAsync(...)`.
  - **Riwayat refund** sale (list `PosRefundDto` + baris).
- Total shift & Cashier Shift report otomatis mencerminkan refund (net per metode, ExpectedCash turun untuk refund tunai).

### 8. Tests (`tests/ErpOne.IntegrationTests/PosRefundServiceTests.cs`)

Meniru pola `PosSaleServiceTests` (SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`; `AccountingSeeder` sudah dipanggil di `InitializeDatabase` sejak 5b → GL mapping ada). Seed chain `PosSaleVoid` manual bila perlu (tapi RefundAsync tak butuh chain — otorisasi di UI; service hanya terima `authorizedBy` string).
1. **Full void** membalik semua: buat shift+sale (mis. 2 item), refund all → stok kembali (StockMovement In +qty), `CashierShiftTotal` metode itu = 0 lagi, `ExpectedCash` kembali ke OpeningFloat (sale tunai), 1 JE reversal (SourceType PosRefund), riwayat refund = GrandTotal sale.
2. **Partial refund + remaining:** refund sebagian qty 1 baris → remaining berkurang; refund kedua atas sisa → remaining 0; total refund ≈ sale.
3. **Over-refund ditolak:** qty > remaining → ValidationException; refund kedua yang melebihi sisa → ditolak.
4. **Shift tertutup ditolak:** tutup shift → RefundAsync → ValidationException; stok & total tak berubah.
5. **Refund non-tunai tak sentuh laci:** sale metode kartu → refund → `CashSalesTotal`/`ExpectedCash` tetap, tapi `CashierShiftTotal` metode kartu berkurang.
- Bump `NumberSequenceServiceTests` assert 14→15.

Target: **+~5 test** (baseline 318 → ~323).

## Non-Goals (YAGNI)

- Refund setelah shift ditutup / lintas shift.
- Cetak struk refund khusus.
- Perubahan moving-average (refund = kembalikan pada snapshot cost).
- Tukar barang (exchange) / ganti item.
- Refund metode berbeda dari sale asli.

## Batasan yang diketahui

- Beberapa refund parsial berturut bisa selisih beberapa sen vs total sale asli (pembulatan per-refund).
- Refund hanya untuk varian/baris yang ada di sale asli; tak bisa menambah item.
- `PosSale` tetap immutable; status refund murni turunan agregasi `PosRefundLine`.
