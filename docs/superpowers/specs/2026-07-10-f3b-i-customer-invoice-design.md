# Fase 3b-i — Customer Invoice (Accounts Receivable) — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Bagian pertama Fase 3b (AR). Mencatat piutang dari Sales Order. Mirror 3a-i (Supplier Invoice) dengan sumber = SO. Tanpa approval. Receipt + ledger di 3b-ii.

## Keputusan brainstorming
- Sumber: **1+ Sales Order langsung** (bukan DO), baris = SO line Quantity × pricing SO line.
- Credit limit customer = **peringatan saja** (tampilkan limit/outstanding/sisa; boleh lampaui).
- Cancel (soft); edit header saja saat Open & belum dibayar.
- Nomor via `IDocumentNumberService` (prefix `ARV`).

## Domain
`enum CustomerInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }`

`CustomerInvoice : AuditableEntity`:
- `InvoiceNumber` (`ARV-yyyyMM-0001`), `CustomerRef` (no. PO customer, opsional), `CustomerId`, `Currency`, `InvoiceDate`, `DueDate`, `Notes`, `Status`
- `Subtotal`, `DiscountTotal`, `TaxTotal`, `GrandTotal`, `PaidAmount`
- `Outstanding => GrandTotal - PaidAmount` (computed, `Ignore`d)
- `Lines`
- Methods: ctor(→Open), `SetLines`, `UpdateHeader(invoiceDate, dueDate, customerRef, notes)` (Open & PaidAmount==0), `Cancel()` (PaidAmount==0), plus `ApplyPayment(amount)`/`ReversePayment(amount)` (untuk 3b-ii; sama seperti SupplierInvoice).

`CustomerInvoiceLine`: `CustomerInvoiceId`, `SalesOrderId`, `SalesOrderLineId`, `ProductVariantId`, `Quantity`, `UnitPrice`, `DiscountPercent`, `TaxRateSnapshot`, `LineSubtotal/Discount/Tax/Total` (Recompute pola SalesOrderLine).

## Aturan bisnis
- **Sumber baris**: tiap SO line → 1 invoice line memakai `Quantity` & pricing SO line.
- **SO invoiceable**: status ∈ {Confirmed, PartiallyDelivered, Delivered, Closed}.
- **Cegah dobel-invoice**: SO yang line-nya sudah dirujuk invoice non-Cancelled tak bisa dipilih. `GetUninvoicedSalesOrdersAsync(customerId)`.
- **Konsistensi**: semua SO terpilih customer sama & currency sama.
- **Cancel**: Open & PaidAmount==0 → Cancelled (SO kembali uninvoiced).
- **Credit (peringatan)**: `GetCustomerCreditAsync` = (CreditLimit, Outstanding all open/partial invoices, Available). UI memperingatkan bila total baru > Available; tidak memblokir.

## Infrastructure / EF
- DbSets `CustomerInvoices`, `CustomerInvoiceLines`; config inline: `InvoiceNumber` unik, precision 18,2, `Status` string, FK Customer (Restrict), lines cascade; prefix `T_`.
- Migration `AddCustomerInvoice` + seed `NumberSequence` Id=9 (`CustomerInvoice`, `ARV`, yyyyMM, Monthly).
- `DocumentTypes.CustomerInvoice`.

## Application
- `ICustomerInvoiceService`: `GetPagedAsync(page,size,search?,status?)`, `GetByIdAsync`, `GetDashboardAsync`, `GetUninvoicedSalesOrdersAsync(customerId)`, `GetCustomerCreditAsync(customerId)`, `CreateAsync(CreateCustomerInvoiceRequest{ CustomerId, InvoiceDate, DueDate?, CustomerRef?, Notes?, SalesOrderIds[] })`, `UpdateHeaderAsync`, `CancelAsync`.
- DTO + FluentValidation. Impl mirror `SupplierInvoiceService`.

## Web (grup Finance)
- Resource `finance.ar-invoices` ("Customer Invoices", ikon `bi-receipt-cutoff", CRUD).
- Index `.pi` (KPI outstanding + chips status), Form `.cf` (pilih customer → SO belum-ter-invoice + preview baris + panel credit limit/outstanding/available + peringatan), Detail `.pf` (info + baris + ringkasan; riwayat receipt di 3b-ii).

## Permissions
- `finance.ar-invoices` (index/create/edit/delete) — auto-seed admin.

## Tests
- Create dari 1 & multi-SO: totals benar, status Open, nomor `ARV-`.
- SO ter-invoice keluar dari uninvoiced; Cancel mengembalikan.
- Tolak SO beda customer/currency, atau status belum Confirmed.
- `GetCustomerCreditAsync` menghitung available = limit − outstanding.

## Out of scope (3b-ii)
- Customer Receipt, ledger kas In, transisi PartiallyPaid/Paid, void ber-otorisasi.

## Verifikasi
- Build hijau; `dotnet test` lulus. Manual: SO Confirmed → Customer Invoice → outstanding & credit tampil; dobel-invoice dicegah; Cancel membebaskan SO.
