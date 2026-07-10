# Fase 3b-ii — Customer Receipt (Accounts Receivable) — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Bagian kedua Fase 3b. Menerima pembayaran dari customer atas Customer Invoice: ledger kas In, alokasi, transisi PartiallyPaid/Paid, void ber-otorisasi. Melengkapi 3b-i.

## Keputusan brainstorming
- **Tanpa approval** — receipt langsung **Posted** saat dibuat (uang masuk, risiko rendah).
- **Void ber-otorisasi**: popup user+password; role authorizer dari chain `CustomerReceiptVoid` (fallback permission `finance.ar-receipts.void`). Mirror Supplier Payment void.
- Akun kas/bank **wajib** currency sama; alokasi ≤ outstanding; Σ alokasi = Amount.

## Domain
- `enum CustomerReceiptStatus { Posted, Voided }`
- `ApprovalDocumentType += CustomerReceiptVoid` (auto-listed di Approval Chain settings).
- `CustomerReceipt : AuditableEntity`: `ReceiptNumber` (`ARR-yyyyMM-0001`), `CustomerId`, `CashBankAccountId`, `Currency`, `ReceiptDate`, `Amount`, `Notes`, `Status`, `Allocations`.
  - ctor(→Posted setelah SetAllocations); `SetAllocations` (hitung Amount = Σ); `Void()` (Posted→Voided).
- `CustomerReceiptAllocation`: `CustomerReceiptId`, `CustomerInvoiceId`, `Amount`.
- Pakai `CustomerInvoice.ApplyPayment(amount)` / `ReversePayment(amount)` (sudah ada di 3b-i).

## Alur
- **CreateAsync**: validasi → buat receipt (Posted) + `CashBankMovement(In, Amount, RefType="CustomerReceipt")` + tiap alokasi `invoice.ApplyPayment(amount)`. Transaksi DB.
- **VoidAsync(id, authorizedBy)**: `receipt.Void()` + `CashBankMovement(Out, Amount, RefType="CustomerReceiptVoid", note incl authorizer)` + tiap alokasi `invoice.ReversePayment(amount)`.

## Validasi (ValidateAsync)
- Akun aktif & currency == invoice currency.
- Invoice: milik customer, status Open/PartiallyPaid (bukan Cancelled/Paid), currency sama, tak duplikat.
- Tiap alokasi > 0 & ≤ outstanding.

## Infrastructure / EF
- DbSets `CustomerReceipts`, `CustomerReceiptAllocations`; config precision 18,2, Status string, FK Customer & CashBankAccount (Restrict), alokasi FK CustomerInvoice (Restrict), cascade allocations; prefix `T_`.
- Migration `AddCustomerReceipt` + seed `NumberSequence` Id=10 (`CustomerReceipt`, `ARR`, yyyyMM, Monthly).
- `DocumentTypes.CustomerReceipt`.

## Application
- `ICustomerReceiptService`: `GetPagedAsync(page,size,search?,status?)`, `GetByIdAsync`, `GetDashboardAsync`, `GetOpenInvoicesAsync(customerId)`, `CreateAsync(CreateCustomerReceiptRequest{ CustomerId, CashBankAccountId, ReceiptDate, Notes?, Allocations[] })`, `VoidAsync(int id, string authorizedBy)`.
- DTOs mirror SupplierPayment (allocation DTO includes invoice number/outstanding). Validators.

## Web (grup Finance)
- Resource `finance.ar-receipts` ("Customer Receipts", ikon `bi-cash-stack`, actions index/create/void).
- Index `.pi` (KPI: total diterima, count; chips status). Form `.cf` (customer + akun kas/bank + tanggal + alokasi ke invoice outstanding + saldo akun + total). Detail `.pf` (info + alokasi + link invoice + tombol Void → popup otorisasi).
- Void popup: mirror ApPaymentDetail (UserManager.CheckPasswordAsync + role dari `CustomerReceiptVoid` chain / fallback permission).

## Tests
- Create → Posted: mutasi In, invoice PaidAmount naik & status PartiallyPaid/Paid, saldo akun naik.
- Full allocation → invoice Paid.
- Alokasi > outstanding → ValidationException; akun beda currency → ditolak.
- Void → mutasi Out balik, invoice PaidAmount turun & status kembali, saldo akun pulih.

## Out of scope
- Approval lifecycle, uang muka tanpa invoice, biaya bank, void parsial.

## Verifikasi
- Build hijau; `dotnet test` lulus. Manual: Customer Invoice Open → Receipt (alokasi) → invoice Paid/PartiallyPaid, saldo kas naik; Void (popup otorisasi) mengembalikan.
