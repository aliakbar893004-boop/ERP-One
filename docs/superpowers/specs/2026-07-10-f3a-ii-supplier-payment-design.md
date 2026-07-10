# Fase 3a-ii — Supplier Payment (Accounts Payable) — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Bagian kedua Fase 3a. Membayar Supplier Invoice: ledger kas/bank, payment + alokasi, approval, dan transisi status invoice (PartiallyPaid/Paid). Melengkapi 3a-i.

## Keputusan brainstorming
- Uang keluar & invoice ter-update **saat Posted** (final approval). Chain kosong → auto-Posted.
- **Isi total dulu, lalu alokasikan** — validasi Σ alokasi = total.
- **Boleh void** payment Posted (reversal mutasi + invoice).
- Akun kas/bank **wajib** ber-currency sama dengan payment/invoice.
- Approval: Supplier Payment lewat Approval Chain (`ApprovalDocumentType.SupplierPayment`).

## Domain
Enums:
- `SupplierPaymentStatus { Draft, PendingApproval, Posted, Voided }`
- `CashBankMovementDirection { In, Out }`

`CashBankMovement : AuditableEntity` (ledger mutasi kas/bank):
- `CashBankAccountId`, `Date`, `Direction`, `Amount` (decimal 18,2), `RefType` (mis. `"SupplierPayment"`), `RefId`, `Note`
- Saldo = `OpeningBalance + Σ(In) − Σ(Out)`.

`SupplierPayment : AuditableEntity`:
- `PaymentNumber` (`APP-yyyyMM-0001`), `SupplierId`, `CashBankAccountId`, `Currency`, `PaymentDate`, `Amount` (total), `Notes`, `Status`, `RejectionNote`
- `Allocations : IReadOnlyCollection<SupplierPaymentAllocation>`
- Methods: ctor (Draft), `SetAllocations(...)` (hanya Draft; hitung/validasi Amount = Σ alokasi), `UpdateHeader(...)` (Draft), `Submit()` (Draft→PendingApproval, butuh alokasi), `MarkPosted()` (PendingApproval→Posted), `ReturnToDraft(reason)`, `Void()` (Posted→Voided).

`SupplierPaymentAllocation`: `SupplierPaymentId`, `SupplierInvoiceId`, `Amount` (decimal 18,2).

Tambahan pada `SupplierInvoice` (dari 3a-i):
- `ApplyPayment(decimal amount)` → `PaidAmount += amount`; set `Status` = `PaidAmount >= GrandTotal ? Paid : PartiallyPaid` (guard `0 ≤ PaidAmount ≤ GrandTotal`, tolak bila Cancelled).
- `ReversePayment(decimal amount)` → `PaidAmount -= amount`; set `Status` = `PaidAmount <= 0 ? Open : PartiallyPaid` (guard `PaidAmount ≥ 0`).

## Alur (mirror PurchaseOrderService)
1. **CreateDraftAsync** → validasi; simpan Draft + alokasi. Tidak menyentuh kas/invoice.
2. **SubmitAsync** → `payment.Submit()`; `approval.ResetAsync` + `approval.SubmitAsync`; bila fully-approved → **Post** (apply).
3. **ApproveAsync** → `approval.ApproveAsync`; bila fully-approved → **Post**.
4. **RejectAsync** → `approval.RejectAsync`; `payment.ReturnToDraft(reason)`; `approval.ResetAsync`.
5. **Post (privat, saat fully-approved)**: tulis `CashBankMovement(Out, Amount, RefType="SupplierPayment", RefId=payment.Id)`; tiap alokasi → `invoice.ApplyPayment(amount)`; `payment.MarkPosted()`.
6. **VoidAsync** (dari Posted): tulis `CashBankMovement(In, Amount, ...)`; tiap alokasi → `invoice.ReversePayment(amount)`; `payment.Void()`.

Semua langkah dalam transaksi DB.

## Aturan validasi
- Σ alokasi = `Amount`; tiap alokasi > 0 & ≤ `invoice.Outstanding`.
- Semua invoice: milik `SupplierId` yang dipilih, `Currency` sama, status `Open`/`PartiallyPaid` (bukan Cancelled/Paid/duplikat).
- Akun kas/bank aktif & `Currency` == payment `Currency`.
- `Amount > 0`.

## Infrastructure / EF
- DbSets: `CashBankMovements`, `SupplierPayments`, `SupplierPaymentAllocations`.
- Config inline: precision 18,2; `Status`/`Direction` `HasConversion<string>()`; `PaymentNumber` unik; FK Supplier & CashBankAccount (Restrict), invoice FK di alokasi (Restrict); prefix `T_` (movement/payment/allocation → transaksi/stok? Gunakan `T_`).
- Migration `AddSupplierPayment` + seed `NumberSequence` Id=8 (`SupplierPayment`, `APP`, yyyyMM, Monthly).
- `ApprovalDocumentType.SupplierPayment` (enum baru).

## Application
- Perluas `ICashBankAccountService` dengan `GetBalanceAsync(int accountId)` (opening + ledger) dan sertakan `CurrentBalance` di `CashBankAccountDto` untuk list (dihitung).
- `ISupplierPaymentService`: `GetPagedAsync`, `GetByIdAsync`, `GetDashboardAsync`, `GetPayableInvoicesAsync(supplierId)`, `CreateDraftAsync`, `UpdateDraftAsync`, `DeleteDraftAsync`, `SubmitAsync`, `ApproveAsync`, `RejectAsync`, `VoidAsync`, `GetApprovalStepsAsync`.
- DTO + FluentValidation. Impl mirror `PurchaseOrderService` + `SupplierInvoiceService`.

## Web (grup Finance)
- AppAction baru `void` (`ActVoid`).
- Resource `finance.ap-payments` ("Supplier Payments", ikon `bi-cash-coin`, actions index/create/edit/delete/approve/void).
- Index `.pi` (KPI: total dibayar bulan ini / pending approval; chips status), Form `.cf` (supplier + akun kas/bank + tanggal + total → tabel invoice outstanding + input alokasi + indikator Σ), Detail `.pf` (info + alokasi + timeline approval + tombol Submit/Approve/Reject/Void per status & izin — pola PoDetail).
- Update **Cash & Bank index**: kolom **Current balance**.
- Seed approval chain default untuk `SupplierPayment` di `BootstrapSeeder`.

## Tests (`ErpOne.IntegrationTests`)
- Seed: supplier + PO confirmed + GRN posted + SupplierInvoice Open (pakai helper 3a-i) + akun kas.
- Create draft → submit (chain kosong → Posted): mutasi Out tercatat, invoice `PaidAmount` naik, status `PartiallyPaid`/`Paid`, saldo akun turun.
- Full allocation → invoice `Paid`.
- Σ alokasi ≠ total → ValidationException; alokasi > outstanding → ditolak; akun beda currency → ditolak.
- Void payment Posted → mutasi In balik, invoice `PaidAmount` turun & status kembali, saldo akun pulih.

## Out of scope
- Multi-currency/FX, uang muka tanpa invoice, biaya/potongan bank, void parsial.

## Verifikasi
- Build hijau; `dotnet test` lulus.
- Manual: buat invoice (3a-i) → buat payment, alokasikan, submit/approve → invoice jadi Paid/PartiallyPaid, saldo kas turun; Void mengembalikan.
