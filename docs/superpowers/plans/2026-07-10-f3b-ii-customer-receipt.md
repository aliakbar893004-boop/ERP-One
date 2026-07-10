# Fase 3b-ii — Customer Receipt Implementation Plan

> REQUIRED SUB-SKILL: superpowers:executing-plans.

**Goal:** Receive customer payments against Customer Invoices — post immediately (cash In + invoice ApplyPayment), void with supervisor authorization.

**Architecture:** Mirror of **SupplierPayment** (3a-ii) minus the approval lifecycle. Create = Posted. Swap Supplier→Customer, SupplierInvoice→CustomerInvoice, cash Out→In, `APP`→`ARR`, add `ApprovalDocumentType.CustomerReceiptVoid`. Reuse `CashBankMovement` + `CustomerInvoice.ApplyPayment/ReversePayment`.

## Global Constraints
- `.pi`/`.cf`/`.pf`; `T_` prefixes; precision 18,2; Status string. Numbering `ARR` (Id=10). Tests `EnsureCreated()`. Commit per task.

---

## Task 1: Domain + config + numbering + migration
**Files:** `CustomerReceiptStatus.cs`, `CustomerReceiptAllocation.cs`, `CustomerReceipt.cs` (Domain); modify `ApprovalDocumentType.cs` (+`CustomerReceiptVoid`), `DocumentTypes.cs` (+`CustomerReceipt`), `AppDbContext.cs`; migration `AddCustomerReceipt`.

- [ ] `CustomerReceiptStatus { Posted, Voided }`.
- [ ] `CustomerReceiptAllocation(int customerInvoiceId, decimal amount)` — props Id, CustomerReceiptId, CustomerInvoiceId, Amount; ctor guards (>0).
- [ ] `CustomerReceipt` — copy `SupplierPayment` shape but: no Submit/MarkPosted/ReturnToDraft; ctor sets `Status = Posted`; keep `SetAllocations` (Amount = Σ), `Void()` (Posted→Voided). Props: ReceiptNumber, CustomerId, CashBankAccountId, Currency, ReceiptDate, Amount, Notes, Status, Allocations. ctor `(receiptNumber, customerId, cashBankAccountId, currency, receiptDate, notes)` → after `SetAllocations` service will have set amount; set Status=Posted in ctor.
- [ ] `ApprovalDocumentType` += `CustomerReceiptVoid`; `DocumentTypes` += `public const string CustomerReceipt = "CustomerReceipt";`.
- [ ] AppDbContext: DbSets `CustomerReceipts`/`CustomerReceiptAllocations`; config mirrors SupplierPayment/Allocation (FK Customer + CashBankAccount Restrict; allocation FK CustomerInvoice Restrict; cascade allocations); `tablePrefixes` both `T_`; NumberSequence seed Id=10 `{Code="CustomerReceipt",Prefix="ARR",DateFormat="yyyyMM",Padding=4,Monthly}`.
- [ ] Build + `dotnet ef migrations add AddCustomerReceipt ...`; commit.

> Note: `SetAllocations` on SupplierPayment calls `EnsureDraft()`. For CustomerReceipt there is no Draft — make `SetAllocations` callable once at construction: implement it to just set lines + Amount without a draft guard (receipt is built once then posted).

---

## Task 2: CustomerReceiptService + DTOs + validators + DI + tests
**Files:** `ErpOne.Application/CustomerReceipts/*`; `Infrastructure/Services/CustomerReceiptService.cs`; DI; test.

- [ ] DTOs mirror SupplierPayment:
  - `CustomerReceiptListItemDto(int Id, string ReceiptNumber, string CustomerName, DateTime ReceiptDate, string CashBankAccountName, string Currency, decimal Amount, string Status)`
  - `CustomerReceiptAllocationDto(int Id, int CustomerInvoiceId, string InvoiceNumber, DateTime DueDate, string InvoiceStatus, decimal InvoiceGrandTotal, decimal InvoiceOutstanding, decimal Amount)`
  - `CustomerReceiptDto(int Id, string ReceiptNumber, int CustomerId, string CustomerName, int CashBankAccountId, string CashBankAccountName, string Currency, DateTime ReceiptDate, decimal Amount, string? Notes, string Status, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<CustomerReceiptAllocationDto> Allocations)`
  - `OpenInvoiceDto(int CustomerInvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal, decimal Outstanding)`
  - `CustomerReceiptDashboardDto(int Total, int Posted, int Voided, decimal PostedThisMonth)`
  - `ReceiptAllocationInput(int CustomerInvoiceId, decimal Amount)`
  - `CreateCustomerReceiptRequest(int CustomerId, int CashBankAccountId, DateTime ReceiptDate, string? Notes, IReadOnlyList<ReceiptAllocationInput> Allocations)`
- [ ] `ICustomerReceiptService`: GetPagedAsync, GetByIdAsync, GetDashboardAsync, `GetOpenInvoicesAsync(int customerId)`, CreateAsync, `VoidAsync(int id, string authorizedBy)`.
- [ ] Validators (CreateCustomerReceiptValidator): CustomerId>0, CashBankAccountId>0, Allocations NotEmpty, each SupplierInvoiceId... (CustomerInvoiceId>0, Amount>0).
- [ ] Service `CustomerReceiptService` — copy `SupplierPaymentService`, drop Submit/Approve/Reject and the approval field. `CreateAsync` = validate + number(ARR) + new receipt + SetAllocations + **PostAsync inline** (cash In movement + invoice.ApplyPayment) → save. `VoidAsync` mirrors SupplierPayment void (cash Out + invoice.ReversePayment). `GetOpenInvoicesAsync` = CustomerInvoices where CustomerId + status Open/PartiallyPaid + outstanding>0. `ValidateAsync` mirrors payment (currency match, allocation ≤ outstanding). RefType strings "CustomerReceipt"/"CustomerReceiptVoid".
- [ ] DI: `services.AddScoped<ICustomerReceiptService, CustomerReceiptService>();` + using.
- [ ] Tests `CustomerReceiptServiceTests` — seed customer + Confirmed SO + Customer Invoice (via ICustomerInvoiceService) + cash account. Cases: create posts+updates invoice+balance (In); full → Paid; over-outstanding throws; mismatched currency throws; void reverses.
- [ ] Run tests; commit.

---

## Task 3: Web pages + menu
**Files:** `Web/Components/Pages/Finance/ArReceipts/ArReceiptIndex.razor`(+css), `ArReceiptForm.razor`(+css), `ArReceiptDetail.razor`(+css); modify `AppMenus.cs`, `_Imports.razor`.

- [ ] AppMenus Finance: `new("finance.ar-receipts", "Customer Receipts", "bi-cash-stack", [ActIndex, ActCreate, ActVoid])`. `_Imports`: `@using ErpOne.Application.CustomerReceipts`.
- [ ] Index — copy `ApPaymentIndex.razor`(+css) → AR receipt (KPI Total/Posted/Voided/PostedThisMonth; status chips optional). Route `/finance/ar-receipts`.
- [ ] Form — copy `ApPaymentForm.razor`(+css) → customer dropdown + account + date + allocate to **open invoices** (`GetOpenInvoicesAsync`), pay-full/all/clear, account balance-after (this INCREASES balance). Save → CreateAsync (posts immediately, navigate to detail/list).
- [ ] Detail — copy `ApPaymentDetail.razor`(+css) → drop Submit/Approve/Reject (no approval); keep **Void with authorization popup** (chain `CustomerReceiptVoid`, permission `finance.ar-receipts.void`). Allocations table with invoice link.
- [ ] Build; commit.

## Final verification
- `dotnet build && dotnet test`; `dotnet ef database update`; smoke: Customer Invoice Open → Receipt → invoice Paid + cash balance up; Void (popup) restores. Update `NumberSequenceServiceTests` count → 10.

## Self-review
- Mirror of SupplierPayment; key diff = no approval (create=Posted), cash In, ARR, CustomerReceiptVoid. Reuses CustomerInvoice.ApplyPayment/ReversePayment + CashBankMovement.
