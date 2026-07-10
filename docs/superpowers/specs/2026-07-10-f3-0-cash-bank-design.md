# Fase 3-0 — Finance Foundation + Cash/Bank Master — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Sub-proyek pertama Fase 3. Bangun grup menu `Finance` + master `CashBankAccount`. Fondasi tempat uang masuk/keluar untuk AP (3a), AR (3b), Expense (3c).

## Tujuan
Menyediakan master akun kas & bank yang bisa dipilih saat pembayaran/penerimaan. Master CRUD standar mengikuti pola yang sudah ada (`Currency`/`Brand`).

## Keputusan brainstorming
- **Field lengkap + saldo awal**; `CurrentBalance` **dihitung** nanti dari mutasi (bukan disimpan). Di 3-0 saldo = `OpeningBalance`.
- Resource key `finance.cash-bank` (route konvensi `/finance/cash-bank`).
- Seed 1 akun kas default.

## Domain
`enum CashBankType { Cash, Bank }`

`CashBankAccount : AuditableEntity`:
- `Code` — unik, uppercase
- `Name`
- `Type` — Cash | Bank
- `Currency` — string kode ISO (dropdown dari Currency master aktif; default `IDR`)
- `OpeningBalance` — decimal(18,2), ≥ 0 tidak dipaksa (bisa negatif? tidak — batasi ≥ 0)
- `BankName?`, `AccountNumber?`, `AccountHolder?` — opsional, relevan untuk Bank
- `IsActive`

Aturan: `Code`/`Name` wajib; `OpeningBalance` boleh 0. Field bank opsional (tidak dipaksa walau Type=Bank).

## Infrastructure / EF
- `DbSet<CashBankAccount>`, config inline di `AppDbContext`: unique index `Code`, `OpeningBalance` precision (18,2), `Type` `HasConversion<string>()` maxlen 20, string maxlengths.
- Table prefix `M_` (daftarkan di `tablePrefixes`).
- Seed via `HasData`: `{ Id=1, Code="CASH", Name="Main Cash", Type=Cash, Currency="IDR", OpeningBalance=0, IsActive=true }` (+ CreatedAt statik/CreatedBy "system").
- Migration: `AddCashBankAccount`.

## Application
- `ICashBankAccountService`: `GetAllAsync`, `GetActiveAsync`, `GetPagedAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`.
- `CashBankAccountDtos.cs`: `CashBankAccountDto`, `CreateCashBankAccountRequest`, `UpdateCashBankAccountRequest`.
- `CashBankAccountValidators.cs`: Code (wajib, maxlen 20, `^[A-Za-z0-9-]+$`), Name (wajib, maxlen 100), Currency (3 huruf), Type valid, OpeningBalance ≥ 0, bank fields maxlen.
- Service impl `CashBankAccountService` (pola `CurrencyService`): validasi + unik code.

## Web
- Resource `finance.cash-bank` di `AppMenus.cs`, **grup baru `Finance`**, actions CRUD, ikon `bi-bank`.
- NavMenu otomatis menampilkan (refactor data-driven) — route `/finance/cash-bank` dari konvensi; tak perlu override.
- Index `.pi` (`/finance/cash-bank`) + Form `.cf` (`/finance/cash-bank/new`, `/finance/cash-bank/{id}/edit`).
  - Form: dropdown Type & Currency (dari `GetActiveAsync` currency); field Bank (BankName/AccountNumber/AccountHolder) tampil saat Type=Bank.

## Permissions
- `finance.cash-bank` (index/create/edit/delete) — otomatis ter-seed ke role admin oleh `BootstrapSeeder`.

## Tests (`ErpOne.IntegrationTests`)
- `CashBankAccountServiceTests`: create menormalkan code; duplicate code ditolak (`ValidationException`); field bank tersimpan; `GetActiveAsync` hanya aktif; seed default `CASH` ada.

## Out of scope
- Mutasi/CurrentBalance (dari AP/AR/Expense — sub-proyek berikutnya).
- Approval untuk dokumen finance.
- Transfer antar akun kas/bank.

## Verifikasi
- Build hijau; `dotnet test` semua lulus.
- Manual: `/finance/cash-bank` CRUD; menu grup Finance muncul; dropdown Type/Currency & field bank kondisional.
