# Fase 3a-i — Supplier Invoice (Accounts Payable) — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Sub-proyek Fase 3a bagian pertama. Mencatat tagihan hutang dari GRN. Tanpa approval (approval hanya di Payment / 3a-ii). Tanpa pembayaran (Payment + ledger kas/bank di 3a-ii).

## Tujuan
Membentuk Supplier Invoice dari 1+ GRN Posted milik supplier yang sama, dengan baris & nilai diturunkan otomatis dari pricing PO. Memberi angka **hutang outstanding** yang nyata.

## Keputusan brainstorming
- Multi-GRN per supplier; baris **auto-derived read-only** dari received qty × pricing PO line.
- **Tanpa approval** — invoice langsung `Open`.
- **Cancel (soft)**, bukan hard-delete.
- Edit **hanya header** saat Open & belum dibayar.
- Nomor via `IDocumentNumberService` (prefix `APV`).

## Domain
`enum SupplierInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }`

`SupplierInvoice : AuditableEntity`:
- `InvoiceNumber` (generated `APV-yyyyMM-0001`)
- `SupplierInvoiceNo` (opsional; no. fisik supplier)
- `SupplierId`, `Currency`
- `InvoiceDate`, `DueDate`
- `Notes`, `Status`
- `Subtotal`, `DiscountTotal`, `TaxTotal`, `GrandTotal`
- `PaidAmount` (0 awal; diubah 3a-ii)
- `Outstanding => GrandTotal - PaidAmount` (computed, tidak dipetakan ke kolom)
- `Lines : IReadOnlyCollection<SupplierInvoiceLine>`

Method domain:
- ctor `(invoiceNumber, supplierId, currency, invoiceDate, dueDate, supplierInvoiceNo, notes)` → Status=Open
- `SetLines(IEnumerable<SupplierInvoiceLine>)` → hitung totals
- `UpdateHeader(invoiceDate, dueDate, supplierInvoiceNo, notes)` → hanya bila Open & PaidAmount==0
- `Cancel()` → hanya bila PaidAmount==0 & Status!=Cancelled
- (untuk 3a-ii nanti: `ApplyPayment(amount)` / `ReversePayment(amount)` mengubah PaidAmount & Status)

`SupplierInvoiceLine`:
- `GoodsReceiptId`, `GoodsReceiptLineId`, `ProductVariantId`
- `Quantity`, `UnitPrice`, `DiscountPercent`, `TaxRateSnapshot`
- `LineSubtotal`, `LineDiscount`, `LineTax`, `LineTotal` (dihitung di domain seperti `PurchaseOrderLine`)

Totals di invoice: `Subtotal = Σ LineSubtotal`, `DiscountTotal = Σ LineDiscount`, `TaxTotal = Σ LineTax`, `GrandTotal = Σ LineTotal`.

## Aturan bisnis
- **Sumber baris**: tiap GRN line → 1 invoice line, memakai `QuantityReceived` dan pricing dari PO line terkait (`UnitPrice`, `DiscountPercent`, `TaxRateSnapshot`). Perhitungan baris = pola `PurchaseOrderLine.Recompute`.
- **Cegah dobel-invoice**: GRN yang line-nya sudah dirujuk invoice non-Cancelled tak boleh dipilih lagi. `GetUninvoicedGrnsAsync(supplierId)` mengembalikan GRN Posted milik supplier yang belum ter-invoice.
- **Konsistensi**: semua GRN terpilih harus supplier sama & currency PO sama; jika tidak → `ValidationException`.
- **Cancel**: `Open` & `PaidAmount==0` → `Cancelled`; GRN-nya otomatis kembali "uninvoiced" (karena query mengecualikan invoice Cancelled).

## Infrastructure / EF
- `DbSet<SupplierInvoice>`, `DbSet<SupplierInvoiceLine>`; config inline: `InvoiceNumber` unik, precision 18,2 untuk uang, `Status` `HasConversion<string>()`, FK ke Supplier (Restrict). Prefix tabel `T_` (transaksi).
- Migration `AddSupplierInvoice` + seed `NumberSequence` Id=7 (`SupplierInvoice`, prefix `APV`, `yyyyMM`, pad 4, Monthly).

## Application
- `ISupplierInvoiceService`:
  - `GetPagedAsync(page, size, search?, status?)`
  - `GetByIdAsync(id)` → `SupplierInvoiceDto` (+ lines)
  - `GetDashboardAsync()` → total, per-status count, total outstanding
  - `GetUninvoicedGrnsAsync(supplierId)` → daftar GRN + preview baris/total
  - `CreateAsync(CreateSupplierInvoiceRequest{ SupplierId, InvoiceDate, DueDate?, SupplierInvoiceNo?, Notes?, GrnIds[] })`
  - `UpdateHeaderAsync(id, UpdateSupplierInvoiceHeaderRequest)`
  - `CancelAsync(id)`
- DTO + FluentValidation (`GrnIds` tidak kosong; SupplierId valid; DueDate ≥ InvoiceDate bila diisi).
- Impl mengikuti pola `PurchaseOrderService` (transaksi, build lines, hitung totals, `docNumbers.NextAsync(DocumentTypes.SupplierInvoice, ...)`).
- Tambah `DocumentTypes.SupplierInvoice`.

## Web (grup Finance)
- Resource `finance.ap-invoices` ("Supplier Invoices", ikon `bi-receipt`, actions index/create/edit/delete).
- **Index `.pi`**: KPI (total outstanding, count per status) + chips filter status + tabel (No, Supplier, Tanggal, Jatuh tempo, Grand total, Outstanding, Status). Muncul otomatis di menu (NavMenu data-driven).
- **Form `.cf`** (`/finance/ap-invoices/new`): pilih Supplier → tampil daftar GRN belum-ter-invoice (checkbox) → preview baris + total → header (InvoiceDate, DueDate, SupplierInvoiceNo, Notes) → Save. Edit (`/{id}/edit`) hanya header.
- **Detail `.pf`** (`/finance/ap-invoices/{id}`): card + info-grid + tabel baris + ringkasan; tombol Cancel (bila unpaid). (Riwayat pembayaran ditambah di 3a-ii.)

## Permissions
- `finance.ap-invoices` (index/create/edit/delete) — auto-seed ke admin.

## Tests (`ErpOne.IntegrationTests`)
- Create dari 1 GRN: totals = agregasi baris, status Open, nomor `APV-`.
- Create dari multi-GRN: totals agregat benar.
- GRN ter-invoice dikeluarkan dari `GetUninvoicedGrnsAsync`; setelah Cancel, muncul lagi.
- Tolak bila GRN beda supplier / beda currency.
- Cancel invoice ber-`PaidAmount==0` sukses; status Cancelled.
- Seed master (supplier/warehouse/product/PO/GRN Posted) memakai pola `PurchaseOrderServiceTests`/`GoodsReceiptServiceTests`.

## Out of scope (3a-ii)
- Supplier Payment, alokasi, `CashBankMovement` ledger, transisi PartiallyPaid/Paid, approval, saldo kas/bank.

## Verifikasi
- Build hijau; `dotnet test` semua lulus.
- Manual: buat GRN Posted → buat Supplier Invoice dari GRN → outstanding tampil; GRN tak bisa di-invoice dua kali; Cancel membebaskan GRN.
