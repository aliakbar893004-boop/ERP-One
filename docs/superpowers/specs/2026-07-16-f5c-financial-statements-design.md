# Fase 5c — Financial Statements (Design Spec)

**Tanggal:** 2026-07-16
**Branch kerja:** `Development`
**Prasyarat:** 5a (Ledger Foundation) + 5b (Auto-Posting Engine) — sudah diimplementasi. GL kini terisi otomatis dari transaksi.
**Bagian dari:** Fase 5. Urutan: 5a ✅ → 5b ✅ → **5c (spec ini)** = penutup Fase 5.

---

## 1. Tujuan & Ruang Lingkup

Menyusun laporan keuangan dari General Ledger (baris jurnal posted):
- **Neraca (Balance Sheet)** — as-of tanggal; Aset / Kewajiban / Ekuitas mengikuti hierarki COA + subtotal.
- **Laba/Rugi (Income Statement)** — per periode [from, to]; Pendapatan − Beban = Laba Bersih, terstruktur per hierarki.

### Di luar scope 5c
- **Arus Kas (Cash Flow)** — ditunda (butuh klasifikasi akun operating/investing/financing).
- Komparatif periode/tahun lalu; multi-currency (hanya IDR).
- Tutup buku / closing entry — tidak ada (Laba Tahun Berjalan dihitung on-the-fly).
- Gross Profit sebagai baris eksplisit — di-skip (COGS grup 5000 & Beban grup 6000 sudah tampil terpisah lewat hierarki).

---

## 2. Keputusan Desain (hasil brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Laporan | Neraca + Laba/Rugi (Arus Kas ditunda). |
| Struktur | Mengikuti hierarki COA (header parent = grup + subtotal, leaf = baris). |
| Laba Tahun Berjalan | Dihitung on-the-fly = Σ(Revenue) − Σ(Expense) s/d as-of; muncul sebagai baris di Ekuitas agar Neraca balance. |
| Sumber saldo | Agregasi `JournalEntryLine` (parent `Status != Draft`) join `Account`, pola sama `LedgerService`. |

---

## 3. Perhitungan

**Saldo signed per akun** = `Σ(Debit − Credit)` dari baris jurnal (parent Status != Draft) dengan filter tanggal:
- Neraca (as-of `d`): `EntryDate < d.AddDays(1)` (kumulatif sejak awal).
- Laba/Rugi (periode [from, to]): `from.Date <= EntryDate < to.AddDays(1)`.

**Roll-up hierarki:** `rolledSigned(akun) = ownSigned(akun) + Σ rolledSigned(anak)`. Header menampilkan subtotal roll-up; leaf menampilkan saldo sendiri.

**Natural sign (nilai tampil):**
- Asset, Expense → `natural = signed` (debit-positif).
- Liability, Equity, Revenue → `natural = −signed` (kredit-positif).

**Neraca:**
- Assets = akun tipe Asset (pohon). `TotalAssets = Σ natural root Asset`.
- Liabilities = akun tipe Liability (pohon). `TotalLiabilities`.
- Equity = akun tipe Equity (pohon) **+ baris "Laba Tahun Berjalan"** `= Σ natural(Revenue as-of) − Σ natural(Expense as-of)`. `TotalEquity = Σ natural root Equity + CurrentEarnings`.
- `TotalLiabilitiesAndEquity = TotalLiabilities + TotalEquity`. `IsBalanced = TotalAssets == TotalLiabilitiesAndEquity` (harus true karena semua JE balance).

**Laba/Rugi (periode):**
- Revenue = akun tipe Revenue (pohon). `TotalRevenue`.
- Expense = akun tipe Expense (pohon; COGS grup + Beban grup tampil terpisah). `TotalExpense`.
- `NetIncome = TotalRevenue − TotalExpense`.

**Baris kosong di-skip:** node dgn `rolledSigned == 0` (dan seluruh anaknya 0) tak ditampilkan.

---

## 4. Application Layer (`ErpOne.Application.Accounting`)

### DTO (`FinancialStatementDtos.cs`)
```
public record StatementLineDto(int AccountId, string Code, string Name, int Level, bool IsHeader, decimal Amount);
public record StatementSectionDto(string Title, IReadOnlyList<StatementLineDto> Lines, decimal Total);
public record BalanceSheetDto(DateTime AsOf, StatementSectionDto Assets, StatementSectionDto Liabilities,
    StatementSectionDto Equity, decimal CurrentEarnings, decimal TotalAssets, decimal TotalLiabilitiesAndEquity, bool IsBalanced);
public record IncomeStatementDto(DateTime From, DateTime To, StatementSectionDto Revenue, StatementSectionDto Expense,
    decimal TotalRevenue, decimal TotalExpense, decimal NetIncome);
```
`Equity.Lines` sudah termasuk baris sintetis "Laba Tahun Berjalan" (AccountId=0, IsHeader=false, Level=0) dan `Equity.Total` sudah termasuk `CurrentEarnings`.

### `IFinancialStatementService`
```
Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOf, CancellationToken ct = default);
Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, CancellationToken ct = default);
Task<ReportDocument> BuildBalanceSheetReportAsync(DateTime asOf, CancellationToken ct = default);
Task<ReportDocument> BuildIncomeStatementReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
```

## 5. Infrastructure
- `Infrastructure/Services/Accounting/FinancialStatementService.cs` — primary-ctor `(AppDbContext db)`. Helper privat: load semua akun, hitung `ownSigned` (grouped query), `rolledSigned` (rekursi + memo), `BuildSection(types, ...)` → `(lines, total)`, `Natural(type, signed)`. Report builder ubah section → `ReportDocument` (2 kolom: Account ber-indentasi, Amount; header/total pakai IsSubtotal/IsGrandTotal). Reuse `IReportExporter`.
- DI: `services.AddScoped<IFinancialStatementService, FinancialStatementService>();`

## 6. Web
- `/reports/balance-sheet` (`.pi` + input tanggal as-of + tabel hierarki ber-indentasi via `Level` + subtotal header + Total Assets / Total Liab+Equity + badge Balanced ✓/✗ + export Excel/PDF).
- `/reports/income-statement` (`.pi` + from/to + hierarki + Net Income + export).
- Menu grup **Reports**: `reports.balance-sheet`, `reports.income-statement` (`ReportActions` = index+export).

## 7. Testing
Integration (pola 5a/5b; seeded COA std via factory; isolasi via akun sendiri):
- Seed beberapa JE (mis. saldo awal Dr Kas / Cr Modal; penjualan tunai Dr Kas / Cr Penjualan; beban Dr Beban / Cr Kas). Assert:
  - Neraca `IsBalanced == true`; `TotalAssets == TotalLiabilitiesAndEquity`.
  - `CurrentEarnings == Revenue − Expense`; muncul di Equity.Lines.
  - Laba/Rugi `NetIncome == TotalRevenue − TotalExpense`; akun ada di section & natural sign benar.
  - Akun bersaldo 0 tak muncul.
- Reuse akun COA standar (1110/3100/4100/6100) + buat JE via `IJournalEntryService`.

## 8. Manual Steps
- Restart + sign out/in (permission `reports.balance-sheet.*`, `reports.income-statement.*`).
- Buka `/reports/balance-sheet` & `/reports/income-statement`; setelah beberapa transaksi (auto-post 5b), angka terisi & Neraca balance.

## 9. File Impact
**Create:** `src/ErpOne.Application/Accounting/FinancialStatementDtos.cs`, `IFinancialStatementService.cs`; `src/ErpOne.Infrastructure/Services/Accounting/FinancialStatementService.cs`; `src/ErpOne.Web/Components/Pages/Reports/BalanceSheet/BalanceSheetIndex.razor`, `Reports/IncomeStatement/IncomeStatementIndex.razor`; `tests/ErpOne.IntegrationTests/FinancialStatementServiceTests.cs`.
**Modify:** `src/ErpOne.Infrastructure/DependencyInjection.cs`; `src/ErpOne.Web/Authorization/AppMenus.cs`.
**Tak ada** entity/migration baru (murni read-model atas GL).
