# Fase 5a — Accounting Ledger Foundation (Design Spec)

**Tanggal:** 2026-07-16
**Branch kerja:** `Development`
**Bagian dari:** Fase 5 (Akuntansi Inti), dipecah menjadi 3 sub-proyek berurutan:
- **5a — Ledger Foundation** *(spec ini)*: Chart of Accounts + Journal Entry manual + General Ledger + Trial Balance.
- 5b — Auto-Posting Engine (spec terpisah nanti): posting rules → jurnal otomatis dari 7 titik transaksi.
- 5c — Financial Statements (spec terpisah nanti): Neraca, Laba/Rugi, Arus Kas dari GL.

Target akhir Fase 5 = full auto-posting GL engine. Spec ini hanya membangun **fondasi ledger manual** yang solid; 5b & 5c menumpang di atasnya.

---

## 1. Tujuan & Ruang Lingkup

Membangun buku besar (general ledger) inti berbasis double-entry:

- **Chart of Accounts (COA)** hierarkis dengan tipe akun, di-seed dengan COA standar Indonesia (retail/distribusi).
- **Journal Entry manual** dengan siklus `Draft → Posted → Reversed`, wajib balance (Σdebit = Σkredit), tanpa approval.
- **General Ledger** — daftar mutasi per akun + running balance, filter rentang tanggal, drill-down.
- **Trial Balance** — neraca saldo per akun untuk rentang tanggal, dengan export Excel/PDF.
- **Saldo awal** dimasukkan sebagai Journal Entry khusus (bukan field terpisah).

### Di luar scope 5a (ditegaskan)
- Auto-posting dari transaksi operasional (GRN/Invoice/Payment/Receipt/Expense/POS) → **5b**.
- Laporan keuangan bertingkat (Neraca, L/R, Arus Kas) → **5c**.
- Approval Chain untuk Journal Entry — tidak dipakai (kontrol via permission saja).
- Manajemen periode / tutup buku / penguncian bulan — **tidak ada**; posting bebas tanggal.
- Multi-currency pada jurnal — semua jurnal dalam IDR (mata uang dasar).

---

## 2. Keputusan Desain (hasil brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Struktur COA | Hierarkis (parent/child) + tipe akun (Asset/Liability/Equity/Revenue/Expense). Hanya leaf yang postable. |
| Isi awal COA | Seed COA standar Indonesia (lihat §7). |
| Siklus Journal Entry | `Draft → Posted → Reversed`. Koreksi via Reverse (jurnal balik otomatis), bukan edit/hapus setelah posted. |
| Approval JE manual | Tidak ada. Kontrol via permission `finance.journal-entries.post`. |
| Saldo awal | Via JE khusus (Dr aset, Cr kewajiban+ekuitas, selisih ke akun **3900 Saldo Awal**). |
| Kontrol periode | Tidak ada. Posting bebas tanggal; GL/TB pakai filter rentang. |
| Penyimpanan GL | **Tidak ada tabel GL terpisah.** GL & TB = query atas `JournalEntryLine` (Status=Posted) join `Account`. |

---

## 3. Domain Entities

Lokasi: `src/ErpOne.Domain/Entities/Accounting/`. Semua inherit `AuditableEntity` (audit fields di-stamp otomatis di `AppDbContext.StampAudit`). Pola: `private set`, private ctor `// EF Core`, backing `List<>` sebagai `IReadOnlyCollection`, invariant lewat method yang throw `ArgumentException`/`InvalidOperationException`.

### 3.1 `Account` (COA) — tabel `M_Accounts`

```
Id            int
Code          string   // unik, mis. "1110"
Name          string
Type          AccountType   // enum → string(20)
ParentId      int?     // self-FK, null = akun akar
IsPostable    bool     // true = leaf, boleh di-jurnal; false = header
IsActive      bool
Description   string?
```

- `enum AccountType { Asset, Liability, Equity, Revenue, Expense }`
- **Normal balance dihitung**, tidak disimpan: `Asset`/`Expense` = Debit; `Liability`/`Equity`/`Revenue` = Kredit. Expose sebagai computed property `NormalBalance` (enum `NormalBalanceSide { Debit, Credit }`), di-`Ignore()` pada EF.
- **Method / invariant:**
  - Ctor `(string code, string name, AccountType type, int? parentId, bool isPostable, string? description)`.
  - `Update(string name, AccountType type, int? parentId, bool isPostable, string? description)`.
  - `SetActive(bool)`.
  - Guard di service (bukan entity, karena butuh DB): tak boleh hapus akun yang punya anak atau sudah dipakai di `JournalEntryLine`; tak boleh set `IsPostable=false` bila sudah punya baris jurnal; tak boleh `ParentId` = diri sendiri / membentuk siklus.

### 3.2 `JournalEntry` — tabel `T_JournalEntries`

```
Id                  int
EntryNumber         string   // unik, dari IDocumentNumberService (kode "JournalEntry", prefix "JV")
EntryDate           DateTime
Description         string
Status              JournalEntryStatus   // enum → string(20)
ReversalOfEntryId   int?     // diisi bila entry ini adalah jurnal balik dari entry lain
ReversedByEntryId   int?     // diisi pada entry asal saat di-reverse
TotalDebit          decimal(18,2)   // = Σ line.Debit
TotalCredit         decimal(18,2)   // = Σ line.Credit
Lines               IReadOnlyCollection<JournalEntryLine>
```

- `enum JournalEntryStatus { Draft, Posted, Reversed }`.
- **Method / invariant:**
  - Ctor `(string entryNumber, DateTime entryDate, string description)` → Status = Draft.
  - `SetLines(IEnumerable<(int accountId, decimal debit, decimal credit, string? memo)>)` — **draft-only**; ganti seluruh baris; recompute `TotalDebit`/`TotalCredit`. (Validasi balance & ≥2 baris dilakukan saat `Post`, bukan `SetLines`, agar draft boleh disimpan sementara belum balance.)
  - `UpdateHeader(DateTime entryDate, string description)` — draft-only.
  - `Post()` — validasi: Status==Draft, ≥2 baris, `TotalDebit == TotalCredit` dan > 0, tiap baris tepat satu sisi > 0. Set Status=Posted. (Validasi akun postable & aktif dilakukan di service, butuh DB.)
  - `MarkReversed(int reversalEntryId)` — Status==Posted → Reversed, set `ReversedByEntryId`. Dipanggil service saat membuat jurnal balik.

### 3.3 `JournalEntryLine` — tabel `T_JournalEntryLines`

```
Id            int
AccountId     int
Debit         decimal(18,2)   // >= 0
Credit        decimal(18,2)   // >= 0
Memo          string?
```

- Invariant (dicek saat masuk `SetLines`): `Debit >= 0`, `Credit >= 0`, **tepat satu** dari keduanya > 0 (tidak boleh dua-duanya 0, tidak boleh dua-duanya > 0).
- Ctor `(int accountId, decimal debit, decimal credit, string? memo)`.

> **General Ledger & Trial Balance tidak punya tabel sendiri.** Keduanya adalah proyeksi query atas `JournalEntryLine` di mana parent `JournalEntry.Status == Posted`, join `Account`. Baris jurnal Reversed tetap ada di GL (baik entry asal maupun jurnal baliknya keduanya Posted secara akuntansi — lihat §5.3), sehingga saldonya saling meniadakan; ini benar secara audit.

---

## 4. Application Layer

Namespace: `ErpOne.Application.Accounting`.

### 4.1 Accounts
- **DTO** (`AccountDtos.cs`):
  - `AccountDto(int Id, string Code, string Name, AccountType Type, int? ParentId, bool IsPostable, bool IsActive, string? Description)`
  - `AccountTreeNodeDto(AccountDto Account, IReadOnlyList<AccountTreeNodeDto> Children)`
  - `CreateAccountRequest(string Code, string Name, AccountType Type, int? ParentId, bool IsPostable, string? Description)`
  - `UpdateAccountRequest(string Name, AccountType Type, int? ParentId, bool IsPostable, string? Description)`
- **`IAccountService`:** `GetTreeAsync(ct)`, `GetAllAsync(ct)` (flat), `GetPostableAsync(ct)` (leaf aktif, untuk dropdown jurnal), `GetByIdAsync(int)`, `CreateAsync(CreateAccountRequest)`, `UpdateAsync(int, UpdateAccountRequest)`, `DeleteAsync(int)`, `SetActiveAsync(int, bool)`.
- **Validator** (`AccountValidators.cs`): Code wajib & unik (unik dicek di service), Name wajib, Type valid, ParentId (bila ada) harus akun existing & bukan diri sendiri.

### 4.2 Journal Entries
- **DTO** (`JournalEntryDtos.cs`):
  - `JournalEntryLineInput(int AccountId, decimal Debit, decimal Credit, string? Memo)`
  - `CreateJournalEntryRequest(DateTime EntryDate, string Description, IReadOnlyList<JournalEntryLineInput> Lines)`
  - `JournalEntryLineDto(int Id, int AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Memo)`
  - `JournalEntryDto(int Id, string EntryNumber, DateTime EntryDate, string Description, JournalEntryStatus Status, decimal TotalDebit, decimal TotalCredit, int? ReversalOfEntryId, int? ReversedByEntryId, IReadOnlyList<JournalEntryLineDto> Lines)`
  - `JournalEntryListItemDto(int Id, string EntryNumber, DateTime EntryDate, string Description, JournalEntryStatus Status, decimal TotalDebit)`
  - `JournalEntryFilter(DateTime? From, DateTime? To, JournalEntryStatus? Status, string? Search)` + paging.
- **`IJournalEntryService`:** `GetPagedAsync(JournalEntryFilter, page, pageSize)`, `GetByIdAsync(int)`, `CreateDraftAsync(CreateJournalEntryRequest)`, `UpdateDraftAsync(int, CreateJournalEntryRequest)`, `DeleteDraftAsync(int)`, `PostAsync(int)`, `ReverseAsync(int, DateTime reversalDate, string? note)`.
- **Validator** (`JournalEntryValidators.cs`): ≥1 baris saat draft; saat post (dicek di service + validator create): ≥2 baris, tiap baris tepat satu sisi > 0, semua amount ≥ 0. Balance (Σdebit=Σkredit) & akun postable+aktif dicek di service (`PostAsync`).

### 4.3 Ledger
- **DTO** (`LedgerDtos.cs`):
  - `TrialBalanceRowDto(int AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit)` — Debit/Credit = saldo bersih akun pada sisi normalnya untuk rentang.
  - `TrialBalanceDto(DateTime From, DateTime To, IReadOnlyList<TrialBalanceRowDto> Rows, decimal TotalDebit, decimal TotalCredit)`.
  - `GeneralLedgerLineDto(DateTime EntryDate, string EntryNumber, string Description, decimal Debit, decimal Credit, decimal RunningBalance)`.
  - `GeneralLedgerDto(int AccountId, string Code, string Name, AccountType Type, DateTime From, DateTime To, decimal OpeningBalance, IReadOnlyList<GeneralLedgerLineDto> Lines, decimal ClosingBalance)`.
- **`ILedgerService`:**
  - `GetTrialBalanceAsync(DateTime from, DateTime to, ct)` → agregasi `JournalEntryLine` (Posted) per akun; saldo bersih ditempatkan di kolom Debit atau Credit sesuai tanda; total kolom harus sama.
  - `GetGeneralLedgerAsync(int accountId, DateTime from, DateTime to, ct)` → opening balance = Σ mutasi sebelum `from`; lalu baris per jurnal dalam rentang dengan running balance (naik/turun sesuai normal balance akun).
  - `BuildTrialBalanceReportAsync(from, to, ct)` → `ReportDocument` (reuse `IReportExporter` untuk Excel/PDF).
  - `BuildGeneralLedgerReportAsync(accountId, from, to, ct)` → `ReportDocument`.

---

## 5. Aturan Bisnis Kunci

### 5.1 Balance & validasi post
Sebuah JE hanya bisa `Post` bila: minimal 2 baris, setiap baris tepat satu sisi (debit XOR kredit) bernilai > 0, `Σdebit == Σkredit` dan totalnya > 0, dan **semua akun yang dipakai `IsPostable && IsActive`**. Kegagalan → `ValidationException` (pola `Fail(string)` seperti service finance lain).

### 5.2 Draft
JE Draft boleh disimpan meski belum balance / baru 1 baris (memudahkan penyusunan bertahap). Draft boleh di-edit (`UpdateDraftAsync`) & dihapus (`DeleteDraftAsync`). Setelah Posted: **tidak** boleh edit/hapus — hanya Reverse.

### 5.3 Reverse
`ReverseAsync(id, reversalDate, note)`:
1. Ambil JE asal (harus Status==Posted, belum pernah di-reverse).
2. Buat JE baru (nomor baru dari sequence), `EntryDate = reversalDate`, `Description` = `"Reversal of {EntryNumber}: {note/desc}"`, `ReversalOfEntryId = id`, baris = baris asal dengan **debit↔kredit ditukar**.
3. Post JE balik langsung (Status=Posted).
4. Panggil `asal.MarkReversed(reversalBaru.Id)` → asal Status=Reversed, `ReversedByEntryId` diisi.
5. Seluruhnya dalam satu `BeginTransactionAsync`/`CommitAsync`.

Baik entry asal maupun jurnal balik tetap muncul di GL (dua-duanya efektif Posted secara akuntansi); saldonya saling meniadakan. Status `Reversed` pada asal hanya penanda UI/audit — **tetap dihitung** di GL/TB.

### 5.4 Saldo awal
Tidak ada mekanisme khusus — user membuat satu JE biasa: Dr semua akun aset (saldo awalnya), Cr semua kewajiban & ekuitas, selisih penyeimbang ke **3900 Saldo Awal (Opening Balance Equity)**. Karena wajib balance, angka pasti konsisten.

### 5.5 Penomoran
Tambah `NumberSequence` **Id=12**: Code `JournalEntry`, Prefix `JV`, DateFormat `yyMM` (atau ikuti pola sequence finance lain — samakan dengan APV/ARR), Padding 4, ResetPeriod `Monthly`, Separator `-`. Tambah konstanta `DocumentTypes.JournalEntry`. Nomor diambil di `CreateDraftAsync` (nomor dialokasikan saat draft dibuat, konsisten dengan dokumen lain).

---

## 6. Infrastructure & Web

### 6.1 AppDbContext
- Tambah 3 `DbSet`: `Accounts`, `JournalEntries`, `JournalEntryLines`.
- Config inline di `OnModelCreating`:
  - Money `HasPrecision(18,2)`; enum `.HasConversion<string>().HasMaxLength(20)`.
  - `Account.Code` unik; self-FK `HasOne<Account>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict)`.
  - `JournalEntry.EntryNumber` unik; child `Lines` pakai backing field (`.SetPropertyAccessMode(PropertyAccessMode.Field)`), FK `OnDelete(Cascade)` dari line ke entry, `OnDelete(Restrict)` dari line ke account.
  - `Ignore` computed props (`Account.NormalBalance`).
- Tambah entri `tablePrefixes`: `M_Accounts`, `T_JournalEntries`, `T_JournalEntryLines`.
- Tambah `HasData` row `NumberSequence` Id=12.

### 6.2 Services (`Infrastructure/Services/Accounting/`)
`AccountService`, `JournalEntryService`, `LedgerService` — primary-ctor DI (`AppDbContext`, validators, `IDocumentNumberService`). Method money-movement (`PostAsync`/`ReverseAsync`) bungkus `BeginTransactionAsync`. Daftarkan ketiganya di `DependencyInjection.cs`.

### 6.3 Migration
`dotnet ef migrations add AddAccountingLedger -p src/ErpOne.Infrastructure -s src/ErpOne.Web` → buat `M_Accounts`, `T_JournalEntries`, `T_JournalEntryLines` + row NumberSequence Id=12. (App di Visual Studio harus di-stop dulu — DLL lock.) Fallback manual bila `dotnet ef` gagal (pola migration existing).

### 6.4 Menu & Permission (`AppMenus.cs`)
- Grup **Finance**: `finance.chart-of-accounts` (`CRUD`), `finance.journal-entries` (actions: index, create, edit, delete, post; tombol **Reverse** gated ke permission `.post`).
- Grup **Reports**: `reports.general-ledger` & `reports.trial-balance` (`ReportActions` = index + export).
- `BootstrapSeeder`: grant permission baru ke admin (idempotent, sudah otomatis via `AllPermissions`) **plus** seed COA standar Indonesia (§7) bila tabel `Accounts` kosong.

### 6.5 Pages (Blazor, desain global `.pi`/`.cf`/`.pf`)
- `/finance/chart-of-accounts` — `.pi` menampilkan COA sebagai **tree ber-indentasi** (parent → child), badge tipe akun & status. Form `.cf` (Atlas) untuk create/edit akun (pilih parent, tipe, postable toggle).
- `/finance/journal-entries` — `.pi` list (nomor, tanggal, deskripsi, status, total). Form `.cf`: header (tanggal, deskripsi) + tabel baris debit/kredit dinamis dengan **account picker** (dropdown postable) + **indikator balance live** (Σdebit vs Σkredit, tombol Post disable bila tak balance). Detail `.pf` (header + baris + tombol Post/Reverse sesuai status).
- `/reports/general-ledger` — `.pi` + filter akun + rentang tanggal; tampilkan opening, baris mutasi + running balance, closing; export.
- `/reports/trial-balance` — `.pi` + rentang tanggal; tabel akun Debit/Credit + total; export Excel/PDF.

---

## 7. Seed COA Standar Indonesia

Header = non-postable (`IsPostable=false`), leaf = postable. Di-seed di `BootstrapSeeder` bila `Accounts` kosong (idempotent).

| Kode | Nama | Tipe | Parent | Postable |
|------|------|------|--------|----------|
| 1000 | Aset | Asset | — | tidak |
| 1100 | Aset Lancar | Asset | 1000 | tidak |
| 1110 | Kas | Asset | 1100 | ya |
| 1120 | Bank | Asset | 1100 | ya |
| 1130 | Piutang Usaha | Asset | 1100 | ya |
| 1140 | Persediaan Barang | Asset | 1100 | ya |
| 1150 | PPN Masukan | Asset | 1100 | ya |
| 1160 | Barang Diterima Belum Ditagih (GR-IR) | Asset | 1100 | ya |
| 1200 | Aset Tetap | Asset | 1000 | tidak |
| 1210 | Peralatan | Asset | 1200 | ya |
| 1290 | Akumulasi Penyusutan | Asset | 1200 | ya |
| 2000 | Kewajiban | Liability | — | tidak |
| 2100 | Kewajiban Lancar | Liability | 2000 | tidak |
| 2110 | Hutang Usaha | Liability | 2100 | ya |
| 2120 | PPN Keluaran | Liability | 2100 | ya |
| 2130 | Hutang Pajak | Liability | 2100 | ya |
| 3000 | Ekuitas | Equity | — | tidak |
| 3100 | Modal | Equity | 3000 | ya |
| 3200 | Laba Ditahan | Equity | 3000 | ya |
| 3900 | Saldo Awal (Opening Balance Equity) | Equity | 3000 | ya |
| 4000 | Pendapatan | Revenue | — | tidak |
| 4100 | Penjualan | Revenue | 4000 | ya |
| 4200 | Diskon Penjualan | Revenue | 4000 | ya |
| 5000 | Harga Pokok Penjualan | Expense | — | tidak |
| 5100 | Harga Pokok Penjualan | Expense | 5000 | ya |
| 6000 | Beban Operasional | Expense | — | tidak |
| 6100 | Beban Gaji | Expense | 6000 | ya |
| 6200 | Beban Sewa | Expense | 6000 | ya |
| 6300 | Beban Utilitas | Expense | 6000 | ya |
| 6900 | Beban Lain-lain | Expense | 6000 | ya |

Akun-akun ini sudah mencakup kebutuhan auto-posting 5b (Kas, Bank, Piutang, Persediaan, PPN Masukan/Keluaran, GR-IR, Hutang Usaha, Penjualan, HPP).

---

## 8. Testing

Integration tests (SQLite `EnsureCreated` via `CustomWebApplicationFactory`, pola existing; DB shared antar test → isolasi via Id sendiri):

- **`AccountServiceTests`:** create hierarki (parent→child), guard tak bisa hapus akun ber-anak / terpakai jurnal, guard posting ke akun non-postable (via JournalEntry), `GetPostableAsync` hanya leaf aktif, `GetTreeAsync` bentuk pohon benar.
- **`JournalEntryServiceTests`:** draft boleh tak balance; `PostAsync` tolak jurnal tak balance / <2 baris / akun non-postable; post sukses set Status=Posted; edit/hapus posted ditolak; `ReverseAsync` buat jurnal balik (debit↔kredit tertukar), asal jadi Reversed, keduanya balance; JE saldo awal (Dr aset, Cr modal via 3900) balance.
- **`LedgerServiceTests`:** trial balance total debit == total credit; akun muncul di sisi normal; general ledger opening + running balance benar; jurnal balik meniadakan saldo asal di GL/TB.

Semua test lama tetap hijau. Setelah implementasi: `dotnet test ErpOne.slnx` (app Visual Studio di-stop dulu). Update assert count di `NumberSequenceServiceTests` (bertambah 1 sequence → Id=12).

---

## 9. Manual Steps (setelah pull)
- Restart app + sign out/in agar admin dapat permission baru (`finance.chart-of-accounts.*`, `finance.journal-entries.*`, `reports.general-ledger.*`, `reports.trial-balance.*`).
- `BootstrapSeeder` otomatis seed COA standar saat pertama jalan (bila `Accounts` kosong).
- Commit/merge/push dilakukan user manual (git identity `aliakbar893004-boop`).

---

## 10. File Impact (ringkas)

**Create:**
- `src/ErpOne.Domain/Entities/Accounting/Account.cs`, `AccountType.cs`, `NormalBalanceSide.cs`, `JournalEntry.cs`, `JournalEntryLine.cs`, `JournalEntryStatus.cs`
- `src/ErpOne.Application/Accounting/AccountDtos.cs`, `IAccountService.cs`, `AccountValidators.cs`, `JournalEntryDtos.cs`, `IJournalEntryService.cs`, `JournalEntryValidators.cs`, `LedgerDtos.cs`, `ILedgerService.cs`
- `src/ErpOne.Infrastructure/Services/Accounting/AccountService.cs`, `JournalEntryService.cs`, `LedgerService.cs`
- Migration `*_AddAccountingLedger.cs`
- `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/*` (Index + Form), `Finance/JournalEntries/*` (Index + Form + Detail), `Reports/GeneralLedger/*`, `Reports/TrialBalance/*`
- `tests/ErpOne.IntegrationTests/AccountServiceTests.cs`, `JournalEntryServiceTests.cs`, `LedgerServiceTests.cs`

**Modify:**
- `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (DbSet + config + tablePrefixes + NumberSequence HasData)
- `src/ErpOne.Infrastructure/DependencyInjection.cs` (3 service)
- `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs` (konstanta JournalEntry)
- `src/ErpOne.Web/Authorization/AppMenus.cs` (resource baru)
- `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (seed COA)
- `tests/ErpOne.IntegrationTests/...NumberSequenceServiceTests.cs` (count assert +1)
