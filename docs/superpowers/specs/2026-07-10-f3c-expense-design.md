# Fase 3c — Expense — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Sub-proyek terakhir Fase 3. Biaya operasional dibayar dari kas/bank → mutasi kas Out. Master ExpenseCategory + void ber-otorisasi.

## Keputusan brainstorming
- Master **ExpenseCategory** (CRUD, dropdown di form).
- Void **ber-otorisasi** (popup user+password, chain `ExpenseVoid`, fallback permission `finance.expenses.void`). Mirror Receipt/Payment void.
- Dibuat langsung **Posted** (mutasi kas Out). Amount tunggal; currency = currency akun.

## Domain
- `ExpenseCategory : AuditableEntity` { `Code` (unik, upper), `Name`, `IsActive` }.
- `enum ExpenseStatus { Posted, Voided }`.
- `ApprovalDocumentType += ExpenseVoid`.
- `Expense : AuditableEntity` { `ExpenseNumber` (`EXP-yyyyMM-0001`), `ExpenseDate`, `CashBankAccountId`, `ExpenseCategoryId`, `Currency`, `Amount`, `Payee?`, `Description`, `Notes?`, `Status` }. ctor → Posted; `Void()` → Voided.

## Numbering
- `DocumentTypes.Expense`; seed `NumberSequence` Id=11 (`Expense`, `EXP`, yyyyMM, Monthly).

## Infrastructure / EF
- DbSets `ExpenseCategories` (M_), `Expenses` (T_); config: ExpenseCategory unik Code; Expense unik Number, Amount precision 18,2, Status string, FK CashBankAccount + ExpenseCategory (Restrict). Migration `AddExpense` (both tables + seq seed).

## Application
- `IExpenseCategoryService`: GetAll/GetActive/GetPaged/GetById/Create/Update/Delete (pola Currency).
- `IExpenseService`: `GetPagedAsync(page,size,search?,status?)`, `GetByIdAsync`, `GetDashboardAsync` (total posted, count), `CreateAsync(CreateExpenseRequest{ ExpenseDate, CashBankAccountId, ExpenseCategoryId, Amount, Payee?, Description, Notes? })`, `VoidAsync(id, authorizedBy)`.
- CreateAsync: validate (account active, category active, amount>0) → currency = account.Currency → number(EXP) → new Expense (Posted) → `CashBankMovement(Out)`. VoidAsync: `Void()` + `CashBankMovement(In)` reversal.
- DTOs + validators.

## Web (grup Finance)
- `finance.expense-categories` (CRUD): Index `.pi` + Form `.cf` (mirror Currency master).
- `finance.expenses` (index/create/void): Index `.pi` (KPI total expense; status chips) + Form `.cf` (category + account + date + amount + payee + description) + Detail `.pf` (info + void popup otorisasi, chain `ExpenseVoid`).

## Permissions
- `finance.expense-categories` (CRUD), `finance.expenses` (index/create/void) — auto-seed admin.

## Tests
- ExpenseCategory: create normalizes code, duplicate rejected, GetActive filters.
- Expense: create → Posted, EXP number, cash movement Out, balance drops; void → In reversal, balance restored; amount≤0 rejected; inactive account rejected.

## Out of scope
- Approval lifecycle, expense with tax breakdown, recurring expenses, attachments.

## Verifikasi
- Build hijau; `dotnet test` lulus; migration; smoke: buat kategori → expense (saldo kas turun) → void (saldo pulih).
