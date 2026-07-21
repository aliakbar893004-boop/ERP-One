# Fase 5a — Accounting Ledger Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bangun fondasi buku besar double-entry: Chart of Accounts hierarkis + Journal Entry manual (Draft→Posted→Reversed) + General Ledger + Trial Balance, dengan seed COA standar Indonesia.

**Architecture:** Ikuti arsitektur 4-layer existing (Domain/Application/Infrastructure/Web). Entity di `ErpOne.Domain.Entities` (namespace flat), service impl di `ErpOne.Infrastructure.Services.Accounting`, interface+DTO di `ErpOne.Application.Accounting`. **General Ledger & Trial Balance tidak punya tabel** — keduanya query atas `JournalEntryLine` (parent Status=Posted) join `Account`. Posting/reverse dibungkus DB transaction. Reuse `IDocumentNumberService`, `AuditableEntity`, `IReportExporter`, desain `.pi/.cf/.pf`.

**Tech Stack:** .NET 10, Blazor Server, EF Core (`AppDbContext`, SQL Server; test SQLite `EnsureCreated`), FluentValidation, xUnit integration tests, ClosedXML+QuestPDF (export). Solution `ErpOne.slnx`.

## Global Constraints

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`. **App di Visual Studio HARUS di-stop dulu** (DLL lock MSB3021) sebelum build/test.
- Namespace: entity → `ErpOne.Domain.Entities` (folder tak memengaruhi namespace); infra service → `ErpOne.Infrastructure.Services` (folder `Accounting` opsional, namespace tetap `ErpOne.Infrastructure.Services`); Application → `ErpOne.Application.Accounting`; DocumentTypes → `ErpOne.Application.Numbering`.
- Entity: `private set`, private ctor `// EF Core`, backing `List<>` sebagai `IReadOnlyCollection`, invariant via method yang throw `ArgumentException`/`InvalidOperationException`.
- Service: primary-ctor DI; money-movement bungkus `await using var tx = await db.Database.BeginTransactionAsync(ct)` → `tx.CommitAsync(ct)`; error via `private static ValidationException Fail(string)`.
- EF config INLINE di `AppDbContext.OnModelCreating` (tak ada `IEntityTypeConfiguration`). Money `HasPrecision(18,2)`, enum `.HasConversion<string>().HasMaxLength(20)`, doc-number `IsUnique`, FK `OnDelete(Restrict)` kecuali child aggregate (`Cascade`), child collection `.SetPropertyAccessMode(PropertyAccessMode.Field)`, computed `.Ignore(...)`. **Wajib** daftarkan tabel baru di `tablePrefixes` (M_/T_) atau model gagal dibangun.
- Money default currency IDR. Semua jurnal IDR (mata uang dasar).
- Commit MANUAL oleh user — step "Commit" hanya penanda; **JANGAN** `git commit/merge/push`. Boleh `git add`. Git identity `aliakbar893004-boop`. Branch kerja = `Development`.
- Integration test SQLite `EnsureCreated` bangun schema dari MODEL → kolom/tabel baru otomatis ada; migration hanya untuk SQL Server. DB shared antar test → isolasi via Id sendiri.

---

## File Structure

**Create — Domain:**
- `src/ErpOne.Domain/Entities/Accounting/AccountType.cs` — enum tipe akun.
- `src/ErpOne.Domain/Entities/Accounting/NormalBalanceSide.cs` — enum sisi normal.
- `src/ErpOne.Domain/Entities/Accounting/Account.cs` — entity COA.
- `src/ErpOne.Domain/Entities/Accounting/JournalEntryStatus.cs` — enum status jurnal.
- `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs` — header jurnal (aggregate root).
- `src/ErpOne.Domain/Entities/Accounting/JournalEntryLine.cs` — baris jurnal.

**Create — Application (`ErpOne.Application.Accounting`):**
- `src/ErpOne.Application/Accounting/AccountDtos.cs`, `IAccountService.cs`, `AccountValidators.cs`
- `src/ErpOne.Application/Accounting/JournalEntryDtos.cs`, `IJournalEntryService.cs`, `JournalEntryValidators.cs`
- `src/ErpOne.Application/Accounting/LedgerDtos.cs`, `ILedgerService.cs`

**Create — Infrastructure:**
- `src/ErpOne.Infrastructure/Services/Accounting/AccountService.cs`, `JournalEntryService.cs`, `LedgerService.cs`
- Migration `*_AddAccountingLedger.cs` (auto via `dotnet ef`).

**Create — Web:**
- `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/ChartOfAccountsIndex.razor`, `AccountForm.razor`
- `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryIndex.razor`, `JournalEntryForm.razor`, `JournalEntryDetail.razor`
- `src/ErpOne.Web/Components/Pages/Reports/GeneralLedger/GeneralLedgerIndex.razor`
- `src/ErpOne.Web/Components/Pages/Reports/TrialBalance/TrialBalanceIndex.razor`

**Create — Tests:**
- `tests/ErpOne.IntegrationTests/AccountServiceTests.cs`, `JournalEntryServiceTests.cs`, `LedgerServiceTests.cs`

**Modify:**
- `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (3 DbSet + config + tablePrefixes + NumberSequence Id=12 HasData)
- `src/ErpOne.Application/Numbering/DocumentTypes.cs` (konstanta JournalEntry)
- `src/ErpOne.Infrastructure/DependencyInjection.cs` (3 service)
- `src/ErpOne.Web/Authorization/AppMenus.cs` (resource baru)
- `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (seed COA)
- `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs` (assert 11 → 12)

---

## Task 1: Domain — Account entity + enums

**Files:**
- Create: `src/ErpOne.Domain/Entities/Accounting/AccountType.cs`
- Create: `src/ErpOne.Domain/Entities/Accounting/NormalBalanceSide.cs`
- Create: `src/ErpOne.Domain/Entities/Accounting/Account.cs`

**Interfaces:**
- Produces: `AccountType { Asset, Liability, Equity, Revenue, Expense }`, `NormalBalanceSide { Debit, Credit }`, `Account` (props `Id, Code, Name, Type, ParentId, IsPostable, IsActive, Description`, computed `NormalBalance`; ctor `(string code, string name, AccountType type, int? parentId, bool isPostable, string? description)`; methods `Update(...)`, `SetActive(bool)`).

- [ ] **Step 1: Enum AccountType**

Create `src/ErpOne.Domain/Entities/Accounting/AccountType.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Klasifikasi akun COA; menentukan sisi normal & posisi di laporan keuangan.</summary>
public enum AccountType { Asset, Liability, Equity, Revenue, Expense }
```

- [ ] **Step 2: Enum NormalBalanceSide**

Create `src/ErpOne.Domain/Entities/Accounting/NormalBalanceSide.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Sisi saldo normal sebuah akun.</summary>
public enum NormalBalanceSide { Debit, Credit }
```

- [ ] **Step 3: Account entity**

Create `src/ErpOne.Domain/Entities/Accounting/Account.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Akun Chart of Accounts. Hierarkis (ParentId); hanya akun postable (leaf) boleh dijurnal.</summary>
public class Account : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public AccountType Type { get; private set; }
    public int? ParentId { get; private set; }
    public bool IsPostable { get; private set; }
    public bool IsActive { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Sisi normal dihitung dari Type (tidak disimpan).</summary>
    public NormalBalanceSide NormalBalance =>
        Type is AccountType.Asset or AccountType.Expense ? NormalBalanceSide.Debit : NormalBalanceSide.Credit;

    private Account() { } // EF Core

    public Account(string code, string name, AccountType type, int? parentId, bool isPostable, string? description)
    {
        SetCode(code);
        SetName(name);
        Type = type;
        ParentId = parentId;
        IsPostable = isPostable;
        Description = Trim(description);
        IsActive = true;
    }

    public void Update(string name, AccountType type, int? parentId, bool isPostable, string? description)
    {
        SetName(name);
        Type = type;
        ParentId = parentId;
        IsPostable = isPostable;
        Description = Trim(description);
    }

    public void SetActive(bool active) => IsActive = active;

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

- [ ] **Step 4: Build Domain**

Run: `dotnet build src/ErpOne.Domain/ErpOne.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit (penanda batas task)**

```bash
git add src/ErpOne.Domain/Entities/Accounting/AccountType.cs src/ErpOne.Domain/Entities/Accounting/NormalBalanceSide.cs src/ErpOne.Domain/Entities/Accounting/Account.cs
```

---

## Task 2: Domain — JournalEntry + JournalEntryLine + status

**Files:**
- Create: `src/ErpOne.Domain/Entities/Accounting/JournalEntryStatus.cs`
- Create: `src/ErpOne.Domain/Entities/Accounting/JournalEntryLine.cs`
- Create: `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs`

**Interfaces:**
- Produces:
  - `JournalEntryStatus { Draft, Posted, Reversed }`.
  - `JournalEntryLine` (props `Id, AccountId, Debit, Credit, Memo`; ctor `(int accountId, decimal debit, decimal credit, string? memo)`).
  - `JournalEntry` (props `Id, EntryNumber, EntryDate, Description, Status, ReversalOfEntryId, ReversedByEntryId, TotalDebit, TotalCredit, IReadOnlyCollection<JournalEntryLine> Lines`; ctor `(string entryNumber, DateTime entryDate, string description)`; methods `SetLines(IEnumerable<(int AccountId, decimal Debit, decimal Credit, string? Memo)>)`, `UpdateHeader(DateTime, string)`, `Post()`, `MarkAsReversalOf(int)`, `MarkReversed(int)`).

- [ ] **Step 1: Enum JournalEntryStatus**

Create `src/ErpOne.Domain/Entities/Accounting/JournalEntryStatus.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup jurnal: Draft (bisa edit) → Posted (masuk GL) → Reversed (dibalik).</summary>
public enum JournalEntryStatus { Draft, Posted, Reversed }
```

- [ ] **Step 2: JournalEntryLine entity**

Create `src/ErpOne.Domain/Entities/Accounting/JournalEntryLine.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Baris jurnal: tepat satu sisi (Debit XOR Credit) bernilai &gt; 0.</summary>
public class JournalEntryLine
{
    public int Id { get; private set; }
    public int JournalEntryId { get; private set; }
    public int AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string? Memo { get; private set; }

    private JournalEntryLine() { } // EF Core

    public JournalEntryLine(int accountId, decimal debit, decimal credit, string? memo)
    {
        if (accountId <= 0) throw new ArgumentException("AccountId is required.", nameof(accountId));
        if (debit < 0) throw new ArgumentException("Debit must be >= 0.", nameof(debit));
        if (credit < 0) throw new ArgumentException("Credit must be >= 0.", nameof(credit));
        if (debit > 0 && credit > 0) throw new ArgumentException("A line cannot have both debit and credit.");
        if (debit == 0 && credit == 0) throw new ArgumentException("A line must have a debit or a credit.");
        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
    }
}
```

- [ ] **Step 3: JournalEntry aggregate**

Create `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Jurnal double-entry. Draft boleh belum balance; Post menuntut balance &amp; ≥2 baris.</summary>
public class JournalEntry : AuditableEntity
{
    private readonly List<JournalEntryLine> _lines = new();

    public int Id { get; private set; }
    public string EntryNumber { get; private set; } = default!;
    public DateTime EntryDate { get; private set; }
    public string Description { get; private set; } = default!;
    public JournalEntryStatus Status { get; private set; }
    public int? ReversalOfEntryId { get; private set; }
    public int? ReversedByEntryId { get; private set; }
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }

    public IReadOnlyCollection<JournalEntryLine> Lines => _lines.AsReadOnly();

    private JournalEntry() { } // EF Core

    public JournalEntry(string entryNumber, DateTime entryDate, string description)
    {
        if (string.IsNullOrWhiteSpace(entryNumber)) throw new ArgumentException("EntryNumber is required.", nameof(entryNumber));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        EntryNumber = entryNumber.Trim();
        EntryDate = entryDate;
        Description = description.Trim();
        Status = JournalEntryStatus.Draft;
    }

    public void UpdateHeader(DateTime entryDate, string description)
    {
        RequireDraft();
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        EntryDate = entryDate;
        Description = description.Trim();
    }

    public void SetLines(IEnumerable<(int AccountId, decimal Debit, decimal Credit, string? Memo)> lines)
    {
        RequireDraft();
        _lines.Clear();
        foreach (var l in lines)
            _lines.Add(new JournalEntryLine(l.AccountId, l.Debit, l.Credit, l.Memo));
        TotalDebit = _lines.Sum(x => x.Debit);
        TotalCredit = _lines.Sum(x => x.Credit);
    }

    public void Post()
    {
        RequireDraft();
        if (_lines.Count < 2) throw new InvalidOperationException("A journal entry must have at least 2 lines.");
        if (TotalDebit <= 0m) throw new InvalidOperationException("A journal entry total must be > 0.");
        if (TotalDebit != TotalCredit) throw new InvalidOperationException("Journal entry is not balanced (debit ≠ credit).");
        Status = JournalEntryStatus.Posted;
    }

    /// <summary>Tandai entry ini sebagai jurnal balik dari entry lain (dipanggil sebelum Post di service reverse).</summary>
    public void MarkAsReversalOf(int originalEntryId)
    {
        if (originalEntryId <= 0) throw new ArgumentException("originalEntryId is required.", nameof(originalEntryId));
        ReversalOfEntryId = originalEntryId;
    }

    /// <summary>Tandai entry (Posted) telah dibalik oleh entry lain.</summary>
    public void MarkReversed(int reversalEntryId)
    {
        if (Status != JournalEntryStatus.Posted) throw new InvalidOperationException("Only a posted entry can be reversed.");
        Status = JournalEntryStatus.Reversed;
        ReversedByEntryId = reversalEntryId;
    }

    private void RequireDraft()
    {
        if (Status != JournalEntryStatus.Draft) throw new InvalidOperationException("Only a draft journal entry can be modified.");
    }
}
```

- [ ] **Step 4: Build Domain**

Run: `dotnet build src/ErpOne.Domain/ErpOne.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Accounting/JournalEntryStatus.cs src/ErpOne.Domain/Entities/Accounting/JournalEntryLine.cs src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs
```

---

## Task 3: EF wiring — DbContext, DocumentTypes, NumberSequence, migration

**Files:**
- Modify: `src/ErpOne.Application/Numbering/DocumentTypes.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`
- Create: migration `*_AddAccountingLedger.cs` (auto)

**Interfaces:**
- Consumes: `Account`, `JournalEntry`, `JournalEntryLine` (Task 1–2).
- Produces: `db.Accounts`, `db.JournalEntries`, `db.JournalEntryLines`; `DocumentTypes.JournalEntry`; NumberSequence Id=12 (`JournalEntry`/`JV`).

- [ ] **Step 1: DocumentTypes constant**

In `src/ErpOne.Application/Numbering/DocumentTypes.cs`, add after the `Expense` line:
```csharp
    public const string JournalEntry = "JournalEntry";
```

- [ ] **Step 2: DbSets**

In `AppDbContext.cs`, after `public DbSet<Expense> Expenses => Set<Expense>();` (line ~56), add:
```csharp
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
```

- [ ] **Step 3: Entity config blocks**

In `OnModelCreating`, after the `Expense` config block (ends line ~756, before `ApprovalChainStep`), add:
```csharp
        modelBuilder.Entity<Account>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Ignore(x => x.NormalBalance);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntryNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.EntryNumber).IsUnique();
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.TotalDebit).HasPrecision(18, 2);
            e.Property(x => x.TotalCredit).HasPrecision(18, 2);
            e.HasIndex(x => x.EntryDate);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(JournalEntry.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<JournalEntryLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Debit).HasPrecision(18, 2);
            e.Property(x => x.Credit).HasPrecision(18, 2);
            e.Property(x => x.Memo).HasMaxLength(300);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AccountId);
        });
```

- [ ] **Step 4: NumberSequence seed row Id=12**

In the `NumberSequence` `HasData(...)` block, add a comma after the `Id = 11` row and append:
```csharp
                ,new { Id = 12, Code = "JournalEntry", Prefix = "JV", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 5: tablePrefixes**

In the `tablePrefixes` dictionary: add to the Master section:
```csharp
            [nameof(Account)] = "M_",
```
and to the Transaksi section:
```csharp
            [nameof(JournalEntry)] = "T_",
            [nameof(JournalEntryLine)] = "T_",
```

- [ ] **Step 6: Bump NumberSequence test assert**

In `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs` line 23, change:
```csharp
        Assert.Equal(11, all.Count);   // 6 core + AP invoice/payment + AR invoice/receipt + Expense
```
to:
```csharp
        Assert.Equal(12, all.Count);   // 6 core + AP invoice/payment + AR invoice/receipt + Expense + JournalEntry
```

- [ ] **Step 7: Build Infrastructure (verify model builds & prefix guard passes)**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded (model guard tak melempar — 3 tabel baru terdaftar di tablePrefixes).

- [ ] **Step 8: Generate migration**

Ensure app di Visual Studio di-stop. Run:
`dotnet ef migrations add AddAccountingLedger -p src/ErpOne.Infrastructure -s src/ErpOne.Web`
Expected: file migration baru + `AppDbContextModelSnapshot.cs` terupdate; `Up` membuat `M_Accounts`, `T_JournalEntries`, `T_JournalEntryLines` + insert NumberSequence Id=12.

**Fallback bila `dotnet ef` tak bisa dipakai:** buat manual `src/ErpOne.Infrastructure/Persistence/Migrations/20260716120000_AddAccountingLedger.cs`:
```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    public partial class AddAccountingLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(maxLength: 20, nullable: false),
                    Name = table.Column<string>(maxLength: 150, nullable: false),
                    Type = table.Column<string>(maxLength: 20, nullable: false),
                    ParentId = table.Column<int>(nullable: true),
                    IsPostable = table.Column<bool>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    Description = table.Column<string>(maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(nullable: true),
                    ModifiedBy = table.Column<string>(maxLength: 256, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_Accounts", x => x.Id);
                    table.ForeignKey("FK_M_Accounts_M_Accounts_ParentId", x => x.ParentId, "M_Accounts", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    EntryNumber = table.Column<string>(maxLength: 30, nullable: false),
                    EntryDate = table.Column<DateTime>(nullable: false),
                    Description = table.Column<string>(maxLength: 300, nullable: false),
                    Status = table.Column<string>(maxLength: 20, nullable: false),
                    ReversalOfEntryId = table.Column<int>(nullable: true),
                    ReversedByEntryId = table.Column<int>(nullable: true),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(nullable: true),
                    ModifiedBy = table.Column<string>(maxLength: 256, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_T_JournalEntries", x => x.Id));

            migrationBuilder.CreateTable(
                name: "T_JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    JournalEntryId = table.Column<int>(nullable: false),
                    AccountId = table.Column<int>(nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Memo = table.Column<string>(maxLength: 300, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_JournalEntryLines", x => x.Id);
                    table.ForeignKey("FK_T_JournalEntryLines_T_JournalEntries_JournalEntryId", x => x.JournalEntryId, "T_JournalEntries", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_T_JournalEntryLines_M_Accounts_AccountId", x => x.AccountId, "M_Accounts", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "Prefix", "DateFormat", "Padding", "ResetPeriod", "Separator", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy" },
                values: new object[] { 12, "JournalEntry", "JV", "yyyyMM", 4, "Monthly", "-", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "system", null, null });

            migrationBuilder.CreateIndex("IX_M_Accounts_Code", "M_Accounts", "Code", unique: true);
            migrationBuilder.CreateIndex("IX_M_Accounts_ParentId", "M_Accounts", "ParentId");
            migrationBuilder.CreateIndex("IX_T_JournalEntries_EntryNumber", "T_JournalEntries", "EntryNumber", unique: true);
            migrationBuilder.CreateIndex("IX_T_JournalEntries_EntryDate", "T_JournalEntries", "EntryDate");
            migrationBuilder.CreateIndex("IX_T_JournalEntryLines_AccountId", "T_JournalEntryLines", "AccountId");
            migrationBuilder.CreateIndex("IX_T_JournalEntryLines_JournalEntryId", "T_JournalEntryLines", "JournalEntryId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("M_NumberSequences", "Id", 12);
            migrationBuilder.DropTable("T_JournalEntryLines");
            migrationBuilder.DropTable("T_JournalEntries");
            migrationBuilder.DropTable("M_Accounts");
        }
    }
}
```
(Fallback manual TIDAK memperbarui `AppDbContextModelSnapshot.cs`; catat ke user agar snapshot di-regenerate via `dotnet ef` sebelum migration berikutnya. Test SQLite tetap jalan via EnsureCreated.)

- [ ] **Step 9: Build to verify migration compiles**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 10: Commit**

```bash
git add src/ErpOne.Application/Numbering/DocumentTypes.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs
```

---

## Task 4: AccountService (DTO + interface + validator + impl + DI) — TDD

**Files:**
- Create: `src/ErpOne.Application/Accounting/AccountDtos.cs`, `IAccountService.cs`, `AccountValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/AccountService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/AccountServiceTests.cs`

**Interfaces:**
- Consumes: `Account`, `AccountType` (Task 1); `AppDbContext.Accounts`, `db.JournalEntryLines` (Task 3).
- Produces: `IAccountService` (`GetTreeAsync`, `GetAllAsync`, `GetPostableAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `SetActiveAsync`), `AccountDto`, `AccountTreeNodeDto`, `CreateAccountRequest`, `UpdateAccountRequest`.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/Accounting/AccountDtos.cs`:
```csharp
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record AccountDto(int Id, string Code, string Name, AccountType Type, int? ParentId,
    bool IsPostable, bool IsActive, string? Description);

public record AccountTreeNodeDto(AccountDto Account, IReadOnlyList<AccountTreeNodeDto> Children);

public record CreateAccountRequest(string Code, string Name, AccountType Type, int? ParentId,
    bool IsPostable, string? Description);

public record UpdateAccountRequest(string Name, AccountType Type, int? ParentId,
    bool IsPostable, string? Description);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/Accounting/IAccountService.cs`:
```csharp
namespace ErpOne.Application.Accounting;

public interface IAccountService
{
    Task<IReadOnlyList<AccountTreeNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> GetPostableAsync(CancellationToken ct = default);
    Task<AccountDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AccountDto> CreateAsync(CreateAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAsync(int id, UpdateAccountRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetActiveAsync(int id, bool active, CancellationToken ct = default);
}
```

- [ ] **Step 3: Validators**

Create `src/ErpOne.Application/Accounting/AccountValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.Accounting;

public class CreateAccountValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public class UpdateAccountValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Type).IsInEnum();
    }
}
```

- [ ] **Step 4: Write failing tests**

Create `tests/ErpOne.IntegrationTests/AccountServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AccountServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AccountServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Create_parent_and_child_and_get_tree()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var parent = await svc.CreateAsync(new CreateAccountRequest($"P{id}", "Aset Lancar", AccountType.Asset, null, false, null));
        var child = await svc.CreateAsync(new CreateAccountRequest($"C{id}", "Kas", AccountType.Asset, parent.Id, true, null));

        Assert.False(parent.IsPostable);
        Assert.True(child.IsPostable);
        Assert.Equal(parent.Id, child.ParentId);

        var tree = await svc.GetTreeAsync();
        var parentNode = Assert.Single(tree, n => n.Account.Id == parent.Id);
        Assert.Contains(parentNode.Children, c => c.Account.Id == child.Id);
    }

    [Fact]
    public async Task Get_postable_returns_only_active_leaf_accounts()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var header = await svc.CreateAsync(new CreateAccountRequest($"H{id}", "Header", AccountType.Asset, null, false, null));
        var leaf = await svc.CreateAsync(new CreateAccountRequest($"L{id}", "Leaf", AccountType.Asset, header.Id, true, null));
        var inactive = await svc.CreateAsync(new CreateAccountRequest($"I{id}", "Inactive", AccountType.Asset, header.Id, true, null));
        await svc.SetActiveAsync(inactive.Id, false);

        var postable = await svc.GetPostableAsync();
        Assert.Contains(postable, a => a.Id == leaf.Id);
        Assert.DoesNotContain(postable, a => a.Id == header.Id);
        Assert.DoesNotContain(postable, a => a.Id == inactive.Id);
    }

    [Fact]
    public async Task Cannot_delete_account_with_children()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var parent = await svc.CreateAsync(new CreateAccountRequest($"P{id}", "Parent", AccountType.Asset, null, false, null));
        await svc.CreateAsync(new CreateAccountRequest($"K{id}", "Kid", AccountType.Asset, parent.Id, true, null));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.DeleteAsync(parent.Id));
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var code = $"D{Sfx()}";

        await svc.CreateAsync(new CreateAccountRequest(code, "First", AccountType.Asset, null, true, null));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreateAccountRequest(code, "Second", AccountType.Asset, null, true, null)));
    }
}
```

- [ ] **Step 5: Run — verify fail (no impl/DI)**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AccountServiceTests"`
Expected: FAIL (`IAccountService` belum terdaftar).

- [ ] **Step 6: Implementation**

Create `src/ErpOne.Infrastructure/Services/Accounting/AccountService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class AccountService(
    AppDbContext db,
    IValidator<CreateAccountRequest> createValidator,
    IValidator<UpdateAccountRequest> updateValidator) : IAccountService
{
    public async Task<IReadOnlyList<AccountDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Accounts.AsNoTracking().OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccountDto>> GetPostableAsync(CancellationToken ct = default)
    {
        return await db.Accounts.AsNoTracking().Where(a => a.IsPostable && a.IsActive).OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccountTreeNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        var byParent = all.ToLookup(a => a.ParentId);
        List<AccountTreeNodeDto> Build(int? parentId) =>
            byParent[parentId].OrderBy(a => a.Code)
                .Select(a => new AccountTreeNodeDto(a, Build(a.Id)))
                .ToList();
        return Build(null);
    }

    public async Task<AccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : new AccountDto(a.Id, a.Code, a.Name, a.Type, a.ParentId, a.IsPostable, a.IsActive, a.Description);
    }

    public async Task<AccountDto> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim();
        if (await db.Accounts.AnyAsync(a => a.Code == code, ct)) throw Fail("Account code already exists.");
        if (request.ParentId is int pid && !await db.Accounts.AnyAsync(a => a.Id == pid, ct)) throw Fail("Parent account not found.");

        var account = new Account(code, request.Name, request.Type, request.ParentId, request.IsPostable, request.Description);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(account.Id, ct))!;
    }

    public async Task<AccountDto> UpdateAsync(int id, UpdateAccountRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        if (request.ParentId == id) throw Fail("An account cannot be its own parent.");
        if (request.ParentId is int pid && !await db.Accounts.AnyAsync(a => a.Id == pid, ct)) throw Fail("Parent account not found.");
        if (!request.IsPostable && await db.JournalEntryLines.AnyAsync(l => l.AccountId == id, ct))
            throw Fail("Cannot mark a used account as non-postable.");

        account.Update(request.Name, request.Type, request.ParentId, request.IsPostable, request.Description);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        if (await db.Accounts.AnyAsync(a => a.ParentId == id, ct)) throw Fail("Cannot delete an account that has children.");
        if (await db.JournalEntryLines.AnyAsync(l => l.AccountId == id, ct)) throw Fail("Cannot delete an account used in journal entries.");
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Fail("Account not found.");
        account.SetActive(active);
        await db.SaveChangesAsync(ct);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("Account", message)]);
}
```

- [ ] **Step 7: Register DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add `using ErpOne.Application.Accounting;` near the other `using ErpOne.Application.*;` lines, and after `services.AddScoped<IExpenseService, ExpenseService>();`:
```csharp
        services.AddScoped<IAccountService, AccountService>();
```

- [ ] **Step 8: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AccountServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Application/Accounting/AccountDtos.cs src/ErpOne.Application/Accounting/IAccountService.cs src/ErpOne.Application/Accounting/AccountValidators.cs src/ErpOne.Infrastructure/Services/Accounting/AccountService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/AccountServiceTests.cs
```

---

## Task 5: JournalEntryService (DTO + interface + validator + impl + DI) — TDD

**Files:**
- Create: `src/ErpOne.Application/Accounting/JournalEntryDtos.cs`, `IJournalEntryService.cs`, `JournalEntryValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/JournalEntryServiceTests.cs`

**Interfaces:**
- Consumes: `JournalEntry`, `JournalEntryLine`, `JournalEntryStatus` (Task 2); `IAccountService`/`db.Accounts` (Task 1/4); `IDocumentNumberService`, `DocumentTypes.JournalEntry` (Task 3); `PagedResult<T>` (`ErpOne.Application.Common`).
- Produces: `IJournalEntryService` (`GetPagedAsync`, `GetByIdAsync`, `CreateDraftAsync`, `UpdateDraftAsync`, `DeleteDraftAsync`, `PostAsync`, `ReverseAsync`), `JournalEntryDto`, `JournalEntryLineDto`, `JournalEntryListItemDto`, `CreateJournalEntryRequest`, `JournalEntryLineInput`, `JournalEntryFilter`.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/Accounting/JournalEntryDtos.cs`:
```csharp
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record JournalEntryLineInput(int AccountId, decimal Debit, decimal Credit, string? Memo);

public record CreateJournalEntryRequest(DateTime EntryDate, string Description, IReadOnlyList<JournalEntryLineInput> Lines);

public record JournalEntryLineDto(int Id, int AccountId, string AccountCode, string AccountName,
    decimal Debit, decimal Credit, string? Memo);

public record JournalEntryDto(int Id, string EntryNumber, DateTime EntryDate, string Description,
    JournalEntryStatus Status, decimal TotalDebit, decimal TotalCredit,
    int? ReversalOfEntryId, int? ReversedByEntryId, IReadOnlyList<JournalEntryLineDto> Lines);

public record JournalEntryListItemDto(int Id, string EntryNumber, DateTime EntryDate, string Description,
    JournalEntryStatus Status, decimal TotalDebit);

public record JournalEntryFilter(DateTime? From, DateTime? To, JournalEntryStatus? Status, string? Search);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/Accounting/IJournalEntryService.cs`:
```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.Accounting;

public interface IJournalEntryService
{
    Task<PagedResult<JournalEntryListItemDto>> GetPagedAsync(JournalEntryFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<JournalEntryDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<JournalEntryDto> CreateDraftAsync(CreateJournalEntryRequest request, CancellationToken ct = default);
    Task<JournalEntryDto> UpdateDraftAsync(int id, CreateJournalEntryRequest request, CancellationToken ct = default);
    Task DeleteDraftAsync(int id, CancellationToken ct = default);
    Task PostAsync(int id, CancellationToken ct = default);
    Task<JournalEntryDto> ReverseAsync(int id, DateTime reversalDate, string? note, CancellationToken ct = default);
}
```

- [ ] **Step 3: Validator**

Create `src/ErpOne.Application/Accounting/JournalEntryValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.Accounting;

public class CreateJournalEntryValidator : AbstractValidator<CreateJournalEntryRequest>
{
    public CreateJournalEntryValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountId).GreaterThan(0);
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l).Must(l => (l.Debit > 0) ^ (l.Credit > 0))
                .WithMessage("Each line must have exactly one of debit or credit > 0.");
        });
    }
}
```

- [ ] **Step 4: Write failing tests**

Create `tests/ErpOne.IntegrationTests/JournalEntryServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class JournalEntryServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public JournalEntryServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seed two postable accounts, return their ids.
    private static async Task<(int cash, int capital)> SeedAccountsAsync(IServiceProvider sp)
    {
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var cash = await acc.CreateAsync(new CreateAccountRequest($"K{id}", "Kas", AccountType.Asset, null, true, null));
        var capital = await acc.CreateAsync(new CreateAccountRequest($"M{id}", "Modal", AccountType.Equity, null, true, null));
        return (cash.Id, capital.Id);
    }

    [Fact]
    public async Task Draft_may_be_unbalanced_but_post_requires_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        // Unbalanced draft is allowed (single line, no balance).
        var draft = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Unbalanced", [new JournalEntryLineInput(cash, 100m, 0m, null)]));
        Assert.Equal(JournalEntryStatus.Draft, draft.Status);

        // Posting an unbalanced/1-line entry fails.
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => svc.PostAsync(draft.Id));
    }

    [Fact]
    public async Task Post_balanced_entry_succeeds_and_locks()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening balance",
            [new JournalEntryLineInput(cash, 1000m, 0m, "cash"), new JournalEntryLineInput(capital, 0m, 1000m, "equity")]));

        await svc.PostAsync(je.Id);
        var posted = await svc.GetByIdAsync(je.Id);
        Assert.Equal(JournalEntryStatus.Posted, posted!.Status);
        Assert.Equal(1000m, posted.TotalDebit);
        Assert.Equal(1000m, posted.TotalCredit);

        // Posted entry can no longer be edited or deleted.
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            svc.UpdateDraftAsync(je.Id, new CreateJournalEntryRequest(DateTime.Today, "x",
                [new JournalEntryLineInput(cash, 1m, 0m, null), new JournalEntryLineInput(capital, 0m, 1m, null)])));
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => svc.DeleteDraftAsync(je.Id));
    }

    [Fact]
    public async Task Post_rejects_non_postable_account()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var header = await acc.CreateAsync(new CreateAccountRequest($"H{id}", "Header", AccountType.Asset, null, false, null));
        var leaf = await acc.CreateAsync(new CreateAccountRequest($"L{id}", "Leaf", AccountType.Equity, null, true, null));
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Bad account",
            [new JournalEntryLineInput(header.Id, 50m, 0m, null), new JournalEntryLineInput(leaf.Id, 0m, 50m, null)]));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.PostAsync(je.Id));
    }

    [Fact]
    public async Task Reverse_creates_mirror_entry_and_marks_original()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Original",
            [new JournalEntryLineInput(cash, 500m, 0m, null), new JournalEntryLineInput(capital, 0m, 500m, null)]));
        await svc.PostAsync(je.Id);

        var reversal = await svc.ReverseAsync(je.Id, DateTime.Today, "mistake");

        Assert.Equal(JournalEntryStatus.Posted, reversal.Status);
        Assert.Equal(je.Id, reversal.ReversalOfEntryId);
        // Mirror: cash now credited, capital debited.
        Assert.Contains(reversal.Lines, l => l.AccountId == cash && l.Credit == 500m);
        Assert.Contains(reversal.Lines, l => l.AccountId == capital && l.Debit == 500m);

        var original = await svc.GetByIdAsync(je.Id);
        Assert.Equal(JournalEntryStatus.Reversed, original!.Status);
        Assert.Equal(reversal.Id, original.ReversedByEntryId);

        // Cannot reverse twice.
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ReverseAsync(je.Id, DateTime.Today, null));
    }
}
```

- [ ] **Step 5: Run — verify fail**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~JournalEntryServiceTests"`
Expected: FAIL (`IJournalEntryService` belum terdaftar).

- [ ] **Step 6: Implementation**

Create `src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class JournalEntryService(
    AppDbContext db,
    IValidator<CreateJournalEntryRequest> createValidator,
    IDocumentNumberService docNumbers) : IJournalEntryService
{
    public async Task<PagedResult<JournalEntryListItemDto>> GetPagedAsync(
        JournalEntryFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.JournalEntries.AsNoTracking();
        if (filter.From is { } f) query = query.Where(x => x.EntryDate >= f.Date);
        if (filter.To is { } t) query = query.Where(x => x.EntryDate < t.Date.AddDays(1));
        if (filter.Status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x => x.EntryNumber.Contains(filter.Search) || x.Description.Contains(filter.Search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new JournalEntryListItemDto(x.Id, x.EntryNumber, x.EntryDate, x.Description, x.Status, x.TotalDebit))
            .ToListAsync(ct);
        return new PagedResult<JournalEntryListItemDto>(items, total, page, pageSize);
    }

    public async Task<JournalEntryDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var e = await db.JournalEntries.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var accIds = e.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accs = await db.Accounts.AsNoTracking().Where(a => accIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.Code, a.Name }, ct);
        var lines = e.Lines.Select(l => new JournalEntryLineDto(l.Id, l.AccountId,
            accs.TryGetValue(l.AccountId, out var a) ? a.Code : "?",
            accs.TryGetValue(l.AccountId, out var a2) ? a2.Name : "(unknown)",
            l.Debit, l.Credit, l.Memo)).ToList();
        return new JournalEntryDto(e.Id, e.EntryNumber, e.EntryDate, e.Description, e.Status,
            e.TotalDebit, e.TotalCredit, e.ReversalOfEntryId, e.ReversedByEntryId, lines);
    }

    public async Task<JournalEntryDto> CreateDraftAsync(CreateJournalEntryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, request.EntryDate, ct);
        var entry = new JournalEntry(number, request.EntryDate, request.Description);
        entry.SetLines(request.Lines.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(entry.Id, ct))!;
    }

    public async Task<JournalEntryDto> UpdateDraftAsync(int id, CreateJournalEntryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        entry.UpdateHeader(request.EntryDate, request.Description);      // throws if not draft
        entry.SetLines(request.Lines.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        if (entry.Status != JournalEntryStatus.Draft) throw new InvalidOperationException("Only a draft entry can be deleted.");
        db.JournalEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");

        var accIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accIds.Contains(a.Id)).ToListAsync(ct);
        if (accounts.Count != accIds.Count) throw Fail("One or more accounts do not exist.");
        if (accounts.Any(a => !a.IsPostable)) throw Fail("Cannot post to a non-postable (header) account.");
        if (accounts.Any(a => !a.IsActive)) throw Fail("Cannot post to an inactive account.");

        entry.Post();   // domain validation: balance, >= 2 lines, total > 0
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<JournalEntryDto> ReverseAsync(int id, DateTime reversalDate, string? note, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var original = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        if (original.Status != JournalEntryStatus.Posted) throw Fail("Only a posted entry can be reversed.");
        if (original.ReversedByEntryId is not null) throw Fail("This entry has already been reversed.");

        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, reversalDate, ct);
        var desc = string.IsNullOrWhiteSpace(note)
            ? $"Reversal of {original.EntryNumber}"
            : $"Reversal of {original.EntryNumber}: {note.Trim()}";
        var reversal = new JournalEntry(number, reversalDate, desc);
        reversal.SetLines(original.Lines.Select(l => (l.AccountId, l.Credit, l.Debit, l.Memo)));   // swap sides
        reversal.MarkAsReversalOf(original.Id);
        reversal.Post();
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);

        original.MarkReversed(reversal.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(reversal.Id, ct))!;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("JournalEntry", message)]);
}
```

- [ ] **Step 7: Register DI**

In `DependencyInjection.cs`, after `services.AddScoped<IAccountService, AccountService>();`:
```csharp
        services.AddScoped<IJournalEntryService, JournalEntryService>();
```

- [ ] **Step 8: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~JournalEntryServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Application/Accounting/JournalEntryDtos.cs src/ErpOne.Application/Accounting/IJournalEntryService.cs src/ErpOne.Application/Accounting/JournalEntryValidators.cs src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/JournalEntryServiceTests.cs
```

---

## Task 6: LedgerService (Trial Balance + General Ledger + report builders) — TDD

**Files:**
- Create: `src/ErpOne.Application/Accounting/LedgerDtos.cs`, `ILedgerService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/LedgerService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/LedgerServiceTests.cs`

**Interfaces:**
- Consumes: `db.JournalEntries`, `db.JournalEntryLines`, `db.Accounts`; `JournalEntryStatus` (Task 2–3); `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign` (`ErpOne.Application.Reports`).
- Produces: `ILedgerService` (`GetTrialBalanceAsync`, `GetGeneralLedgerAsync`, `BuildTrialBalanceReportAsync`, `BuildGeneralLedgerReportAsync`), `TrialBalanceDto`, `TrialBalanceRowDto`, `GeneralLedgerDto`, `GeneralLedgerLineDto`.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/Accounting/LedgerDtos.cs`:
```csharp
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

public record TrialBalanceRowDto(int AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit);

public record TrialBalanceDto(DateTime From, DateTime To, IReadOnlyList<TrialBalanceRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit);

public record GeneralLedgerLineDto(DateTime EntryDate, string EntryNumber, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

public record GeneralLedgerDto(int AccountId, string Code, string Name, AccountType Type,
    DateTime From, DateTime To, decimal OpeningBalance, IReadOnlyList<GeneralLedgerLineDto> Lines, decimal ClosingBalance);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/Accounting/ILedgerService.cs`:
```csharp
using ErpOne.Application.Reports;

namespace ErpOne.Application.Accounting;

public interface ILedgerService
{
    Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<GeneralLedgerDto?> GetGeneralLedgerAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildTrialBalanceReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReportDocument> BuildGeneralLedgerReportAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing tests**

Create `tests/ErpOne.IntegrationTests/LedgerServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class LedgerServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public LedgerServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<(int cash, int capital)> SeedAccountsAsync(IServiceProvider sp)
    {
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var cash = await acc.CreateAsync(new CreateAccountRequest($"K{id}", "Kas", AccountType.Asset, null, true, null));
        var capital = await acc.CreateAsync(new CreateAccountRequest($"M{id}", "Modal", AccountType.Equity, null, true, null));
        return (cash.Id, capital.Id);
    }

    [Fact]
    public async Task Trial_balance_totals_match_and_place_accounts_on_normal_side()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening",
            [new JournalEntryLineInput(cash, 1000m, 0m, null), new JournalEntryLineInput(capital, 0m, 1000m, null)]));
        await je.PostAsync(created.Id);

        var ledger = sp.GetRequiredService<ILedgerService>();
        var tb = await ledger.GetTrialBalanceAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        var cashRow = Assert.Single(tb.Rows, r => r.AccountId == cash);
        var capRow = Assert.Single(tb.Rows, r => r.AccountId == capital);
        Assert.Equal(1000m, cashRow.Debit);
        Assert.Equal(0m, cashRow.Credit);
        Assert.Equal(1000m, capRow.Credit);
        Assert.Equal(0m, capRow.Debit);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
    }

    [Fact]
    public async Task General_ledger_running_balance_and_reversal_nets_to_zero()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening",
            [new JournalEntryLineInput(cash, 750m, 0m, null), new JournalEntryLineInput(capital, 0m, 750m, null)]));
        await je.PostAsync(created.Id);
        await je.ReverseAsync(created.Id, DateTime.Today, "undo");

        var ledger = sp.GetRequiredService<ILedgerService>();
        var gl = await ledger.GetGeneralLedgerAsync(cash, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        Assert.NotNull(gl);
        // Two lines: +750 then -750 → closing balance 0.
        Assert.Equal(2, gl!.Lines.Count);
        Assert.Equal(0m, gl.ClosingBalance);
    }
}
```

- [ ] **Step 4: Run — verify fail**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~LedgerServiceTests"`
Expected: FAIL (`ILedgerService` belum terdaftar).

- [ ] **Step 5: Implementation**

Create `src/ErpOne.Infrastructure/Services/Accounting/LedgerService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class LedgerService(AppDbContext db) : ILedgerService
{
    // Natural sign: debit-normal accounts count debit as +; credit-normal accounts count credit as +.
    private static int Sign(AccountType type) =>
        type is AccountType.Asset or AccountType.Expense ? 1 : -1;

    public async Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var fromDate = from.Date;
        var toExclusive = to.Date.AddDays(1);

        var raw = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            join a in db.Accounts.AsNoTracking() on l.AccountId equals a.Id
            where e.Status == JournalEntryStatus.Posted && e.EntryDate >= fromDate && e.EntryDate < toExclusive
            group new { l.Debit, l.Credit } by new { a.Id, a.Code, a.Name, a.Type } into g
            select new { g.Key.Id, g.Key.Code, g.Key.Name, g.Key.Type,
                         Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(ct);

        var rows = raw
            .Select(x =>
            {
                var net = x.Debit - x.Credit;                     // + => debit balance, - => credit balance
                return new TrialBalanceRowDto(x.Id, x.Code, x.Name, x.Type,
                    net >= 0 ? net : 0m, net < 0 ? -net : 0m);
            })
            .Where(r => r.Debit != 0m || r.Credit != 0m)
            .OrderBy(r => r.Code)
            .ToList();

        return new TrialBalanceDto(fromDate, to.Date, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public async Task<GeneralLedgerDto?> GetGeneralLedgerAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return null;
        var sign = Sign(account.Type);
        var fromDate = from.Date;
        var toExclusive = to.Date.AddDays(1);

        var opening = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId && e.Status == JournalEntryStatus.Posted && e.EntryDate < fromDate
            select (decimal?)(l.Debit - l.Credit)).SumAsync(ct) ?? 0m;
        var openingBalance = sign * opening;

        var inRange = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId && e.Status == JournalEntryStatus.Posted
                  && e.EntryDate >= fromDate && e.EntryDate < toExclusive
            orderby e.EntryDate, e.Id
            select new { e.EntryDate, e.EntryNumber, e.Description, l.Debit, l.Credit })
            .ToListAsync(ct);

        var running = openingBalance;
        var lines = new List<GeneralLedgerLineDto>();
        foreach (var r in inRange)
        {
            running += sign * (r.Debit - r.Credit);
            lines.Add(new GeneralLedgerLineDto(r.EntryDate, r.EntryNumber, r.Description, r.Debit, r.Credit, running));
        }

        return new GeneralLedgerDto(account.Id, account.Code, account.Name, account.Type,
            fromDate, to.Date, openingBalance, lines, running);
    }

    public async Task<ReportDocument> BuildTrialBalanceReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var tb = await GetTrialBalanceAsync(from, to, ct);
        var rows = tb.Rows
            .Select(r => new ReportRow { Cells = [r.Code, r.Name, r.Type.ToString(), r.Debit, r.Credit] })
            .ToList();
        return new ReportDocument
        {
            Title = "Trial Balance",
            Subtitle = $"{tb.From:d MMM yyyy} – {tb.To:d MMM yyyy}",
            FilterSummary = null,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Code"),
                new ReportColumn("Account"),
                new ReportColumn("Type"),
                new ReportColumn("Debit", ReportAlign.Right, "N0"),
                new ReportColumn("Credit", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["", "Grand total", "", tb.TotalDebit, tb.TotalCredit] },
        };
    }

    public async Task<ReportDocument> BuildGeneralLedgerReportAsync(int accountId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var gl = await GetGeneralLedgerAsync(accountId, from, to, ct);
        if (gl is null)
            return new ReportDocument { Title = "General Ledger", GeneratedAt = DateTime.Now,
                Columns = [new ReportColumn("Date")], Rows = [] };

        var rows = new List<ReportRow>
        {
            new() { IsSubtotal = true, Cells = ["", "", "Opening balance", "", "", gl.OpeningBalance] }
        };
        rows.AddRange(gl.Lines.Select(l => new ReportRow
        {
            Cells = [l.EntryDate, l.EntryNumber, l.Description, l.Debit, l.Credit, l.RunningBalance]
        }));

        return new ReportDocument
        {
            Title = $"General Ledger — {gl.Code} {gl.Name}",
            Subtitle = $"{gl.From:d MMM yyyy} – {gl.To:d MMM yyyy}",
            FilterSummary = null,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Entry #"),
                new ReportColumn("Description"),
                new ReportColumn("Debit", ReportAlign.Right, "N0"),
                new ReportColumn("Credit", ReportAlign.Right, "N0"),
                new ReportColumn("Balance", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["", "", "Closing balance", "", "", gl.ClosingBalance] },
        };
    }
}
```

- [ ] **Step 6: Register DI**

In `DependencyInjection.cs`, after `services.AddScoped<IJournalEntryService, JournalEntryService>();`:
```csharp
        services.AddScoped<ILedgerService, LedgerService>();
```

- [ ] **Step 7: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~LedgerServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Application/Accounting/LedgerDtos.cs src/ErpOne.Application/Accounting/ILedgerService.cs src/ErpOne.Infrastructure/Services/Accounting/LedgerService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/LedgerServiceTests.cs
```

---

## Task 7: Seed standard Indonesian COA in BootstrapSeeder

**Files:**
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs`

**Interfaces:**
- Consumes: `db.Accounts`, `Account`, `AccountType` (Task 1/3).
- Produces: 29 akun COA standar (bila tabel Accounts kosong).

- [ ] **Step 1: Add COA seed block**

In `BootstrapSeeder.cs`, add `using ErpOne.Domain.Entities;` is already present. After the Supplier Payment approval-chain seed block (line ~67, before `// Buat user admin`), add:
```csharp
        // Seed COA standar Indonesia (idempotent — hanya bila belum ada akun sama sekali).
        if (!await db.Accounts.AnyAsync())
        {
            // (Code, Name, Type, ParentCode|null, IsPostable)
            var defs = new (string Code, string Name, AccountType Type, string? Parent, bool Postable)[]
            {
                ("1000", "Aset", AccountType.Asset, null, false),
                ("1100", "Aset Lancar", AccountType.Asset, "1000", false),
                ("1110", "Kas", AccountType.Asset, "1100", true),
                ("1120", "Bank", AccountType.Asset, "1100", true),
                ("1130", "Piutang Usaha", AccountType.Asset, "1100", true),
                ("1140", "Persediaan Barang", AccountType.Asset, "1100", true),
                ("1150", "PPN Masukan", AccountType.Asset, "1100", true),
                ("1160", "Barang Diterima Belum Ditagih", AccountType.Asset, "1100", true),
                ("1200", "Aset Tetap", AccountType.Asset, "1000", false),
                ("1210", "Peralatan", AccountType.Asset, "1200", true),
                ("1290", "Akumulasi Penyusutan", AccountType.Asset, "1200", true),
                ("2000", "Kewajiban", AccountType.Liability, null, false),
                ("2100", "Kewajiban Lancar", AccountType.Liability, "2000", false),
                ("2110", "Hutang Usaha", AccountType.Liability, "2100", true),
                ("2120", "PPN Keluaran", AccountType.Liability, "2100", true),
                ("2130", "Hutang Pajak", AccountType.Liability, "2100", true),
                ("3000", "Ekuitas", AccountType.Equity, null, false),
                ("3100", "Modal", AccountType.Equity, "3000", true),
                ("3200", "Laba Ditahan", AccountType.Equity, "3000", true),
                ("3900", "Saldo Awal (Opening Balance Equity)", AccountType.Equity, "3000", true),
                ("4000", "Pendapatan", AccountType.Revenue, null, false),
                ("4100", "Penjualan", AccountType.Revenue, "4000", true),
                ("4200", "Diskon Penjualan", AccountType.Revenue, "4000", true),
                ("5000", "Harga Pokok Penjualan", AccountType.Expense, null, false),
                ("5100", "Harga Pokok Penjualan", AccountType.Expense, "5000", true),
                ("6000", "Beban Operasional", AccountType.Expense, null, false),
                ("6100", "Beban Gaji", AccountType.Expense, "6000", true),
                ("6200", "Beban Sewa", AccountType.Expense, "6000", true),
                ("6300", "Beban Utilitas", AccountType.Expense, "6000", true),
                ("6900", "Beban Lain-lain", AccountType.Expense, "6000", true),
            };

            var byCode = new Dictionary<string, Account>();
            // Parents appear before children (ordered by code), so ParentId resolves after each SaveChanges.
            foreach (var d in defs)
            {
                int? parentId = d.Parent is null ? null : byCode[d.Parent].Id;
                var acc = new Account(d.Code, d.Name, d.Type, parentId, d.Postable, null);
                db.Accounts.Add(acc);
                await db.SaveChangesAsync();   // materialize Id so children can reference it
                byCode[d.Code] = acc;
            }
        }
```

- [ ] **Step 2: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs
```

---

## Task 8: Menu resources

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`

**Interfaces:**
- Produces: permissions `finance.chart-of-accounts.*`, `finance.journal-entries.*`, `reports.general-ledger.*`, `reports.trial-balance.*` (auto ke `AllPermissions`, di-seed admin). Routes via konvensi (page @page attributes).

- [ ] **Step 1: Add JournalEntry action-set helper**

In `AppMenus.cs`, after the `SupplierPaymentActions` helper (line ~33), add:
```csharp
    private static AppAction[] JournalEntryActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
```

- [ ] **Step 2: Add Finance resources**

In the `Finance` group (after `finance.expenses`, line ~84), add:
```csharp
            new("finance.chart-of-accounts", "Chart of Accounts", "bi-diagram-3", CRUD),
            new("finance.journal-entries", "Journal Entries", "bi-journal-plus", JournalEntryActions),
```

- [ ] **Step 3: Add Reports resources**

In the `Reports` group (after `reports.cashier-shifts`, line ~95), add:
```csharp
            new("reports.general-ledger", "General Ledger", "bi-journals", ReportActions),
            new("reports.trial-balance", "Trial Balance", "bi-list-columns-reverse", ReportActions),
```

- [ ] **Step 4: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 9: Chart of Accounts pages (Index tree + Form)

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/ChartOfAccountsIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/AccountForm.razor`

**Interfaces:**
- Consumes: `IAccountService`, `AccountTreeNodeDto`, `AccountDto`, `CreateAccountRequest`, `UpdateAccountRequest`, `AccountType` (Task 1/4).

- [ ] **Step 1: Index page (tree)**

Create `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/ChartOfAccountsIndex.razor`:
```razor
@page "/finance/chart-of-accounts"
@attribute [Authorize(Policy = "finance.chart-of-accounts.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Domain.Entities
@inject IAccountService Accounts
@inject NavigationManager Nav

<PageTitle>Chart of Accounts</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Finance</span><span class="sep">·</span><span class="here">Chart of Accounts</span>
            </nav>
            <h1>Chart of Accounts</h1>
            <p>Hierarchical ledger accounts. Only postable (leaf) accounts can be journaled.</p>
        </div>
        <AuthorizeView Policy="finance.chart-of-accounts.create">
            <Authorized>
                <div class="pi-actions">
                    <a class="btn btn-primary" href="/finance/chart-of-accounts/new"><i class="bi bi-plus-circle"></i> New Account</a>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_tree is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_tree.Count == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-diagram-3"></i></div><p>No accounts yet.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr><th style="width:160px">Code</th><th>Name</th><th style="width:120px">Type</th><th style="width:110px">Postable</th><th style="width:100px">Status</th><th style="width:90px"></th></tr>
                    </thead>
                    <tbody>
                        @foreach (var node in _tree)
                        {
                            @RenderNode(node, 0)
                        }
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private IReadOnlyList<AccountTreeNodeDto>? _tree;

    protected override async Task OnInitializedAsync() => _tree = await Accounts.GetTreeAsync();

    private RenderFragment RenderNode(AccountTreeNodeDto node, int depth) => __builder =>
    {
        var a = node.Account;
        <tr>
            <td class="code mono" style="padding-left:@(12 + depth * 22)px">@a.Code</td>
            <td class="nm">@a.Name</td>
            <td><span class="badge bg-light text-dark">@a.Type</span></td>
            <td>@(a.IsPostable ? "Yes" : "—")</td>
            <td>@(a.IsActive ? "Active" : "Inactive")</td>
            <td>
                <AuthorizeView Policy="finance.chart-of-accounts.edit">
                    <Authorized>
                        <a class="btn btn-sm btn-outline-secondary" href="/finance/chart-of-accounts/@a.Id"><i class="bi bi-pencil"></i></a>
                    </Authorized>
                </AuthorizeView>
            </td>
        </tr>
        @foreach (var child in node.Children)
        {
            @RenderNode(child, depth + 1)
        }
    };
}
```

- [ ] **Step 2: Form page**

Create `src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/AccountForm.razor`:
```razor
@page "/finance/chart-of-accounts/new"
@page "/finance/chart-of-accounts/{Id:int}"
@attribute [Authorize(Policy = "finance.chart-of-accounts.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Domain.Entities
@inject IAccountService Accounts
@inject NavigationManager Nav

<PageTitle>@(Id is null ? "New Account" : "Edit Account")</PageTitle>

<div class="cf">
    <div class="cf-head">
        <nav class="crumbs">
            <a href="/finance/chart-of-accounts">Chart of Accounts</a><span class="sep">·</span><span class="here">@(Id is null ? "New" : "Edit")</span>
        </nav>
        <h1>@(Id is null ? "New Account" : "Edit Account")</h1>
    </div>

    @if (_error is not null) { <div class="alert alert-danger">@_error</div> }

    <div class="cf-card">
        <div class="cf-grid">
            <div class="c6">
                <label class="fl">Code</label>
                <input class="ctl mono" @bind="_code" disabled="@(Id is not null)" placeholder="e.g. 1110" />
            </div>
            <div class="c6">
                <label class="fl">Type</label>
                <select class="ctl" @bind="_type">
                    @foreach (var t in Enum.GetValues<AccountType>()) { <option value="@t">@t</option> }
                </select>
            </div>
            <div class="c12">
                <label class="fl">Name</label>
                <input class="ctl" @bind="_name" placeholder="Account name" />
            </div>
            <div class="c6">
                <label class="fl">Parent Account</label>
                <select class="ctl" @bind="_parentId">
                    <option value="0">(none — top level)</option>
                    @foreach (var a in _allAccounts.Where(a => Id is null || a.Id != Id))
                    { <option value="@a.Id">@a.Code — @a.Name</option> }
                </select>
            </div>
            <div class="c6">
                <label class="fl">Postable</label>
                <div class="form-check form-switch mt-2">
                    <input class="form-check-input" type="checkbox" @bind="_isPostable" />
                    <label class="form-check-label">Allow journaling to this account</label>
                </div>
            </div>
            <div class="c12">
                <label class="fl">Description</label>
                <input class="ctl" @bind="_description" />
            </div>
        </div>

        <div class="cf-actions">
            <a class="btn btn-light" href="/finance/chart-of-accounts">Cancel</a>
            <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving">Save</button>
        </div>
    </div>
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private IReadOnlyList<AccountDto> _allAccounts = [];
    private string _code = "";
    private string _name = "";
    private AccountType _type = AccountType.Asset;
    private int _parentId;
    private bool _isPostable = true;
    private string? _description;
    private string? _error;
    private bool _saving;

    protected override async Task OnParametersSetAsync()
    {
        _allAccounts = await Accounts.GetAllAsync();
        if (Id is int id)
        {
            var a = await Accounts.GetByIdAsync(id);
            if (a is null) { Nav.NavigateTo("/finance/chart-of-accounts"); return; }
            _code = a.Code; _name = a.Name; _type = a.Type;
            _parentId = a.ParentId ?? 0; _isPostable = a.IsPostable; _description = a.Description;
        }
    }

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            int? parent = _parentId == 0 ? null : _parentId;
            if (Id is int id)
                await Accounts.UpdateAsync(id, new UpdateAccountRequest(_name, _type, parent, _isPostable, _description));
            else
                await Accounts.CreateAsync(new CreateAccountRequest(_code, _name, _type, parent, _isPostable, _description));
            Nav.NavigateTo("/finance/chart-of-accounts");
        }
        catch (FluentValidation.ValidationException ex) { _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)); }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Finance/ChartOfAccounts/
```

---

## Task 10: Journal Entry pages (Index + Form + Detail)

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryForm.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryDetail.razor`

**Interfaces:**
- Consumes: `IJournalEntryService`, `IAccountService`, all Journal DTOs, `JournalEntryStatus`, `PagedResult<T>` (Task 4/5).

- [ ] **Step 1: Index page**

Create `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryIndex.razor`:
```razor
@page "/finance/journal-entries"
@attribute [Authorize(Policy = "finance.journal-entries.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Application.Common
@using ErpOne.Domain.Entities
@inject IJournalEntryService Journals

<PageTitle>Journal Entries</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Finance</span><span class="sep">·</span><span class="here">Journal Entries</span>
            </nav>
            <h1>Journal Entries</h1>
            <p>Manual double-entry journals. Draft → Posted → Reversed.</p>
        </div>
        <AuthorizeView Policy="finance.journal-entries.create">
            <Authorized>
                <div class="pi-actions">
                    <a class="btn btn-primary" href="/finance/journal-entries/new"><i class="bi bi-plus-circle"></i> New Journal</a>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
        <select @bind="_status" @bind:after="ReloadAsync">
            <option value="">All statuses</option>
            @foreach (var s in Enum.GetValues<JournalEntryStatus>()) { <option value="@s">@s</option> }
        </select>
        <input class="form-control" placeholder="Search #/description…" @bind="_search" @bind:after="ReloadAsync" />
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.Items.Count == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-journal-plus"></i></div><p>No journal entries for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr><th style="width:150px">Entry #</th><th style="width:120px">Date</th><th>Description</th><th class="r" style="width:140px">Amount</th><th style="width:110px">Status</th></tr>
                    </thead>
                    <tbody>
                        @foreach (var j in _result.Items)
                        {
                            <tr style="cursor:pointer" @onclick="@(() => Go(j.Id))">
                                <td class="code mono">@j.EntryNumber</td>
                                <td class="mono">@j.EntryDate.ToString("yyyy-MM-dd")</td>
                                <td class="nm">@j.Description</td>
                                <td class="r mono">@j.TotalDebit.ToString("N0")</td>
                                <td>@StatusBadge(j.Status)</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
        <Pager Page="_result.Page" PageSize="_result.PageSize" Total="_result.Total" OnPage="OnPage" />
    }
</div>

@code {
    [Inject] private NavigationManager Nav { get; set; } = default!;
    private PagedResult<JournalEntryListItemDto>? _result;
    private DateTime _from = DateTime.Today.AddMonths(-1);
    private DateTime _to = DateTime.Today;
    private string _status = "";
    private string _search = "";
    private int _page = 1;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        JournalEntryStatus? st = string.IsNullOrEmpty(_status) ? null : Enum.Parse<JournalEntryStatus>(_status);
        _result = await Journals.GetPagedAsync(new JournalEntryFilter(_from, _to, st, _search), _page, 20);
    }

    private async Task ReloadAsync() { _page = 1; await LoadAsync(); }
    private async Task OnPage(int p) { _page = p; await LoadAsync(); }
    private void Go(int id) => Nav.NavigateTo($"/finance/journal-entries/{id}");

    private static RenderFragment StatusBadge(JournalEntryStatus s) => __builder =>
    {
        var cls = s switch { JournalEntryStatus.Posted => "bg-success", JournalEntryStatus.Reversed => "bg-secondary", _ => "bg-warning text-dark" };
        <span class="badge @cls">@s</span>
    };
}
```

- [ ] **Step 2: Form page (create/edit draft with live balance)**

Create `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryForm.razor`:
```razor
@page "/finance/journal-entries/new"
@page "/finance/journal-entries/{Id:int}/edit"
@attribute [Authorize(Policy = "finance.journal-entries.create")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@inject IJournalEntryService Journals
@inject IAccountService Accounts
@inject NavigationManager Nav

<PageTitle>@(Id is null ? "New Journal" : "Edit Journal")</PageTitle>

<div class="cf">
    <div class="cf-head">
        <nav class="crumbs">
            <a href="/finance/journal-entries">Journal Entries</a><span class="sep">·</span><span class="here">@(Id is null ? "New" : "Edit")</span>
        </nav>
        <h1>@(Id is null ? "New Journal Entry" : "Edit Draft")</h1>
    </div>

    @if (_error is not null) { <div class="alert alert-danger">@_error</div> }

    <div class="cf-card">
        <div class="cf-grid">
            <div class="c4">
                <label class="fl">Date</label>
                <input class="ctl" type="date" @bind="_date" />
            </div>
            <div class="c8">
                <label class="fl">Description</label>
                <input class="ctl" @bind="_description" placeholder="e.g. Opening balance" />
            </div>
        </div>

        <table class="table mt-3">
            <thead>
                <tr><th>Account</th><th class="r" style="width:160px">Debit</th><th class="r" style="width:160px">Credit</th><th>Memo</th><th style="width:44px"></th></tr>
            </thead>
            <tbody>
                @for (int i = 0; i < _rows.Count; i++)
                {
                    var idx = i;
                    <tr>
                        <td>
                            <select class="ctl" @bind="_rows[idx].AccountId">
                                <option value="0">— select —</option>
                                @foreach (var a in _postable) { <option value="@a.Id">@a.Code — @a.Name</option> }
                            </select>
                        </td>
                        <td><input class="ctl mono text-end" type="number" step="0.01" min="0" @bind="_rows[idx].Debit" /></td>
                        <td><input class="ctl mono text-end" type="number" step="0.01" min="0" @bind="_rows[idx].Credit" /></td>
                        <td><input class="ctl" @bind="_rows[idx].Memo" /></td>
                        <td><button class="btn btn-sm btn-outline-danger" @onclick="@(() => _rows.RemoveAt(idx))"><i class="bi bi-x"></i></button></td>
                    </tr>
                }
            </tbody>
            <tfoot>
                <tr class="fw-bold">
                    <td class="text-end">Totals</td>
                    <td class="r mono">@TotalDebit.ToString("N2")</td>
                    <td class="r mono">@TotalCredit.ToString("N2")</td>
                    <td colspan="2">
                        @if (IsBalanced) { <span class="badge bg-success">Balanced</span> }
                        else { <span class="badge bg-warning text-dark">Off by @Math.Abs(TotalDebit - TotalCredit).ToString("N2")</span> }
                    </td>
                </tr>
            </tfoot>
        </table>

        <button class="btn btn-outline-secondary btn-sm" @onclick="AddRow"><i class="bi bi-plus"></i> Add line</button>

        <div class="cf-actions">
            <a class="btn btn-light" href="/finance/journal-entries">Cancel</a>
            <button class="btn btn-secondary" @onclick="@(() => SaveAsync(false))" disabled="@_saving">Save Draft</button>
            <AuthorizeView Policy="finance.journal-entries.post">
                <Authorized>
                    <button class="btn btn-primary" @onclick="@(() => SaveAsync(true))" disabled="@(_saving || !IsBalanced || _rows.Count < 2)">Save & Post</button>
                </Authorized>
            </AuthorizeView>
        </div>
    </div>
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private sealed class Row { public int AccountId { get; set; } public decimal Debit { get; set; } public decimal Credit { get; set; } public string? Memo { get; set; } }

    private IReadOnlyList<AccountDto> _postable = [];
    private readonly List<Row> _rows = new();
    private DateTime _date = DateTime.Today;
    private string _description = "";
    private string? _error;
    private bool _saving;

    private decimal TotalDebit => _rows.Sum(r => r.Debit);
    private decimal TotalCredit => _rows.Sum(r => r.Credit);
    private bool IsBalanced => TotalDebit > 0 && TotalDebit == TotalCredit;

    protected override async Task OnParametersSetAsync()
    {
        _postable = await Accounts.GetPostableAsync();
        if (Id is int id && _rows.Count == 0)
        {
            var je = await Journals.GetByIdAsync(id);
            if (je is null || je.Status != Domain.Entities.JournalEntryStatus.Draft) { Nav.NavigateTo("/finance/journal-entries"); return; }
            _date = je.EntryDate; _description = je.Description;
            foreach (var l in je.Lines) _rows.Add(new Row { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, Memo = l.Memo });
        }
        if (_rows.Count == 0) { AddRow(); AddRow(); }
    }

    private void AddRow() => _rows.Add(new Row());

    private async Task SaveAsync(bool post)
    {
        _error = null; _saving = true;
        try
        {
            var req = new CreateJournalEntryRequest(_date, _description,
                _rows.Where(r => r.AccountId > 0 && (r.Debit > 0 || r.Credit > 0))
                     .Select(r => new JournalEntryLineInput(r.AccountId, r.Debit, r.Credit, r.Memo)).ToList());
            JournalEntryDto saved = Id is int id
                ? await Journals.UpdateDraftAsync(id, req)
                : await Journals.CreateDraftAsync(req);
            if (post) await Journals.PostAsync(saved.Id);
            Nav.NavigateTo($"/finance/journal-entries/{saved.Id}");
        }
        catch (FluentValidation.ValidationException ex) { _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)); }
        catch (System.InvalidOperationException ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Detail page (view + Post/Reverse actions)**

Create `src/ErpOne.Web/Components/Pages/Finance/JournalEntries/JournalEntryDetail.razor`:
```razor
@page "/finance/journal-entries/{Id:int}"
@attribute [Authorize(Policy = "finance.journal-entries.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Domain.Entities
@inject IJournalEntryService Journals
@inject NavigationManager Nav

<PageTitle>Journal @(_je?.EntryNumber)</PageTitle>

@if (_je is null)
{
    <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
}
else
{
    <div class="pf">
        <div class="pf-head">
            <nav class="crumbs">
                <a href="/finance/journal-entries">Journal Entries</a><span class="sep">·</span><span class="here">@_je.EntryNumber</span>
            </nav>
            <h1>@_je.EntryNumber</h1>
            <div class="pf-actions">
                @if (_je.Status == JournalEntryStatus.Draft)
                {
                    <AuthorizeView Policy="finance.journal-entries.edit"><Authorized>
                        <a class="btn btn-outline-secondary" href="/finance/journal-entries/@_je.Id/edit"><i class="bi bi-pencil"></i> Edit</a>
                    </Authorized></AuthorizeView>
                    <AuthorizeView Policy="finance.journal-entries.post"><Authorized>
                        <button class="btn btn-primary" @onclick="PostAsync" disabled="@_busy"><i class="bi bi-box-arrow-in-down"></i> Post</button>
                    </Authorized></AuthorizeView>
                }
                else if (_je.Status == JournalEntryStatus.Posted)
                {
                    <AuthorizeView Policy="finance.journal-entries.post"><Authorized>
                        <button class="btn btn-outline-danger" @onclick="ReverseAsync" disabled="@_busy"><i class="bi bi-arrow-counterclockwise"></i> Reverse</button>
                    </Authorized></AuthorizeView>
                }
            </div>
        </div>

        @if (_error is not null) { <div class="alert alert-danger">@_error</div> }

        <div class="pf-info">
            <div><span class="k">Date</span><span class="v">@_je.EntryDate.ToString("yyyy-MM-dd")</span></div>
            <div><span class="k">Status</span><span class="v">@_je.Status</span></div>
            <div><span class="k">Description</span><span class="v">@_je.Description</span></div>
            @if (_je.ReversalOfEntryId is not null) { <div><span class="k">Reversal of</span><span class="v">#@_je.ReversalOfEntryId</span></div> }
            @if (_je.ReversedByEntryId is not null) { <div><span class="k">Reversed by</span><span class="v">#@_je.ReversedByEntryId</span></div> }
        </div>

        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th>Account</th><th class="r" style="width:160px">Debit</th><th class="r" style="width:160px">Credit</th><th>Memo</th></tr></thead>
                    <tbody>
                        @foreach (var l in _je.Lines)
                        {
                            <tr>
                                <td class="nm"><span class="mono">@l.AccountCode</span> — @l.AccountName</td>
                                <td class="r mono">@(l.Debit == 0 ? "–" : l.Debit.ToString("N2"))</td>
                                <td class="r mono">@(l.Credit == 0 ? "–" : l.Credit.ToString("N2"))</td>
                                <td>@l.Memo</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold"><td class="text-end">Totals</td><td class="r mono">@_je.TotalDebit.ToString("N2")</td><td class="r mono">@_je.TotalCredit.ToString("N2")</td><td></td></tr>
                    </tfoot>
                </table>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public int Id { get; set; }
    private JournalEntryDto? _je;
    private string? _error;
    private bool _busy;

    protected override async Task OnParametersSetAsync() => _je = await Journals.GetByIdAsync(Id);

    private async Task PostAsync()
    {
        _error = null; _busy = true;
        try { await Journals.PostAsync(Id); _je = await Journals.GetByIdAsync(Id); }
        catch (System.Exception ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task ReverseAsync()
    {
        _error = null; _busy = true;
        try { var rev = await Journals.ReverseAsync(Id, DateTime.Today, null); Nav.NavigateTo($"/finance/journal-entries/{rev.Id}"); }
        catch (System.Exception ex) { _error = ex.Message; _busy = false; }
    }
}
```

- [ ] **Step 4: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Finance/JournalEntries/
```

---

## Task 11: General Ledger + Trial Balance report pages

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Reports/TrialBalance/TrialBalanceIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Reports/GeneralLedger/GeneralLedgerIndex.razor`

**Interfaces:**
- Consumes: `ILedgerService`, `IAccountService`, `TrialBalanceDto`, `GeneralLedgerDto`, `IReportExporter` (Task 4/6), JS `saveAsFile` (existing in app-interop.js).

- [ ] **Step 1: Trial Balance page**

Create `src/ErpOne.Web/Components/Pages/Reports/TrialBalance/TrialBalanceIndex.razor`:
```razor
@page "/reports/trial-balance"
@attribute [Authorize(Policy = "reports.trial-balance.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using ErpOne.Application.Reports
@using Microsoft.JSInterop
@inject ILedgerService Ledger
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>Trial Balance</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">Trial Balance</span>
            </nav>
            <h1>Trial Balance</h1>
            <p>Debit/credit balance per account for the selected period.</p>
        </div>
        <AuthorizeView Policy="reports.trial-balance.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.Rows.Count == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-list-columns-reverse"></i></div><p>No posted entries for this period.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th style="width:140px">Code</th><th>Account</th><th style="width:120px">Type</th><th class="r" style="width:160px">Debit</th><th class="r" style="width:160px">Credit</th></tr></thead>
                    <tbody>
                        @foreach (var r in _result.Rows)
                        {
                            <tr>
                                <td class="code mono">@r.Code</td>
                                <td class="nm">@r.Name</td>
                                <td><span class="badge bg-light text-dark">@r.Type</span></td>
                                <td class="r mono">@(r.Debit == 0 ? "–" : r.Debit.ToString("N0"))</td>
                                <td class="r mono">@(r.Credit == 0 ? "–" : r.Credit.ToString("N0"))</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold"><td colspan="3" class="text-end">Grand total</td><td class="r mono">@_result.TotalDebit.ToString("N0")</td><td class="r mono">@_result.TotalCredit.ToString("N0")</td></tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private TrialBalanceDto? _result;
    private DateTime _from = new(DateTime.Today.Year, 1, 1);
    private DateTime _to = DateTime.Today;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync() => _result = await Ledger.GetTrialBalanceAsync(_from, _to);
    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Ledger.BuildTrialBalanceReportAsync(_from, _to);
        await Download(Exporter.ToExcel(doc), "trial-balance.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    private async Task ExportPdf()
    {
        var doc = await Ledger.BuildTrialBalanceReportAsync(_from, _to);
        await Download(await Exporter.ToPdfAsync(doc), "trial-balance.pdf", "application/pdf");
    }
    private async Task Download(byte[] bytes, string name, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", name, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 2: General Ledger page**

Create `src/ErpOne.Web/Components/Pages/Reports/GeneralLedger/GeneralLedgerIndex.razor`:
```razor
@page "/reports/general-ledger"
@attribute [Authorize(Policy = "reports.general-ledger.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using Microsoft.JSInterop
@inject ILedgerService Ledger
@inject IAccountService Accounts
@inject ErpOne.Application.Reports.IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>General Ledger</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">General Ledger</span>
            </nav>
            <h1>General Ledger</h1>
            <p>Posted movements and running balance for one account.</p>
        </div>
        <AuthorizeView Policy="reports.general-ledger.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel" disabled="@(_accountId == 0)"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf" disabled="@(_accountId == 0)"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <select @bind="_accountId" @bind:after="ReloadAsync">
            <option value="0">— select account —</option>
            @foreach (var a in _accounts) { <option value="@a.Id">@a.Code — @a.Name</option> }
        </select>
        <input type="date" @bind="_from" @bind:after="ReloadAsync" />
        <input type="date" @bind="_to" @bind:after="ReloadAsync" />
    </div>

    @if (_accountId == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-journals"></i></div><p>Select an account to view its ledger.</p></div>
    }
    else if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th style="width:120px">Date</th><th style="width:150px">Entry #</th><th>Description</th><th class="r" style="width:140px">Debit</th><th class="r" style="width:140px">Credit</th><th class="r" style="width:150px">Balance</th></tr></thead>
                    <tbody>
                        <tr class="fw-bold table-light"><td colspan="5">Opening balance</td><td class="r mono">@_result.OpeningBalance.ToString("N0")</td></tr>
                        @foreach (var l in _result.Lines)
                        {
                            <tr>
                                <td class="mono">@l.EntryDate.ToString("yyyy-MM-dd")</td>
                                <td class="code mono">@l.EntryNumber</td>
                                <td class="nm">@l.Description</td>
                                <td class="r mono">@(l.Debit == 0 ? "–" : l.Debit.ToString("N0"))</td>
                                <td class="r mono">@(l.Credit == 0 ? "–" : l.Credit.ToString("N0"))</td>
                                <td class="r mono">@l.RunningBalance.ToString("N0")</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold"><td colspan="5" class="text-end">Closing balance</td><td class="r mono">@_result.ClosingBalance.ToString("N0")</td></tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private IReadOnlyList<AccountDto> _accounts = [];
    private GeneralLedgerDto? _result;
    private int _accountId;
    private DateTime _from = new(DateTime.Today.Year, 1, 1);
    private DateTime _to = DateTime.Today;

    protected override async Task OnInitializedAsync() => _accounts = await Accounts.GetPostableAsync();

    private async Task LoadAsync()
    {
        _result = _accountId == 0 ? null : await Ledger.GetGeneralLedgerAsync(_accountId, _from, _to);
    }
    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Ledger.BuildGeneralLedgerReportAsync(_accountId, _from, _to);
        await Download(Exporter.ToExcel(doc), "general-ledger.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    private async Task ExportPdf()
    {
        var doc = await Ledger.BuildGeneralLedgerReportAsync(_accountId, _from, _to);
        await Download(await Exporter.ToPdfAsync(doc), "general-ledger.pdf", "application/pdf");
    }
    private async Task Download(byte[] bytes, string name, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", name, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 3: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Reports/TrialBalance/ src/ErpOne.Web/Components/Pages/Reports/GeneralLedger/
```

---

## Task 12: Full suite + manual verification

**Files:** none (validation task).

- [ ] **Step 1: Run full test suite**

Ensure app di Visual Studio di-stop. Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS. Baseline 294 + 10 baru (4 Account + 4 JournalEntry + 2 Ledger) = **304**. `NumberSequenceServiceTests` hijau (assert 12).

- [ ] **Step 2: Manual verification (skill `run`/`verify`)**

Jalankan app; sign out/in (agar admin dapat permission baru). BootstrapSeeder otomatis seed 29 akun COA. Verifikasi:
1. `/finance/chart-of-accounts` — tampil tree COA standar; buat akun baru (leaf di bawah "Kas"), edit, cek muncul di tree.
2. `/finance/journal-entries` → New — buat jurnal saldo awal: Dr Kas 10.000.000, Cr Modal 10.000.000; indikator "Balanced"; Save & Post → Detail status Posted.
3. Di Detail jurnal Posted → Reverse → muncul jurnal balik (Dr Modal / Cr Kas), asal jadi Reversed.
4. `/reports/trial-balance` (range tahun berjalan) — Kas & Modal muncul di sisi normal, total debit == total credit; export Excel & PDF terunduh.
5. `/reports/general-ledger` — pilih akun Kas → opening + baris + running balance + closing; setelah reverse, saldo Kas kembali 0.
6. Smoke headless: route `/finance/journal-entries` tanpa login → 302 ke login.

- [ ] **Step 3: Commit (penanda selesai)**

Tak ada file baru. Pastikan semua task sebelumnya sudah di-`git add`. Beritahu user Fase 5a siap di-commit manual.

---

## Self-Review

**Spec coverage:**
- COA hierarkis + tipe akun + normal balance computed → Task 1. ✓
- JournalEntry Draft→Posted→Reversed, balance enforce, no approval → Task 2, 5. ✓
- Tidak ada tabel GL (query lines) → Task 6 (LedgerService query JournalEntryLine+JournalEntry Status=Posted). ✓
- Saldo awal via JE ke akun 3900 → didukung oleh JournalEntryService generik + akun 3900 di seed (Task 5, 7); diverifikasi manual Task 12. ✓
- Penomoran JV seq Id=12 → Task 3. ✓
- Seed COA standar Indonesia (29 akun) → Task 7. ✓
- Menu Finance (COA, Journal) + Reports (GL, TB) → Task 8. ✓
- Pages .pi/.cf/.pf + export → Task 9, 10, 11. ✓
- Reverse: swap debit/kredit, post langsung, asal Reversed → Task 5 impl + test. ✓
- Trial Balance total debit==credit; GL running balance → Task 6 test. ✓
- Testing (hierarki, guard, lifecycle, TB, GL) → Task 4, 5, 6. ✓

**Placeholder scan:** Tak ada TBD/TODO. Fallback migration manual lengkap (Task 3). Semua step berisi kode nyata.

**Type consistency:** `Account`/`AccountType`/`NormalBalanceSide` konsisten Task 1↔3↔4. `JournalEntry.SetLines(IEnumerable<(int,decimal,decimal,string?)>)`, `.Post()`, `.MarkAsReversalOf(int)`, `.MarkReversed(int)` konsisten Task 2↔5. `IJournalEntryService` signatures konsisten Task 5↔10. `ILedgerService` (`GetTrialBalanceAsync`, `GetGeneralLedgerAsync`, `BuildTrialBalanceReportAsync`, `BuildGeneralLedgerReportAsync`) konsisten Task 6↔11. `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign` sesuai signature existing (`AgingReportService` dikonfirmasi). `PagedResult<T>` di `ErpOne.Application.Common`, `Pager` component existing. DocumentTypes.JournalEntry konsisten Task 3↔5. Permission keys (`finance.chart-of-accounts.*`, `finance.journal-entries.*`, `reports.general-ledger.*`, `reports.trial-balance.*`) konsisten Task 8↔9↔10↔11.

**Catatan reviewer:** `PagedResult<T>` shape (property `Items`, `Total`, `Page`, `PageSize`) diasumsikan dari pola `ExpenseService` (`new PagedResult<...>(items, total, page, pageSize)`) & dipakai di JournalEntryIndex — verifikasi field names saat Task 10 bila `Pager` memakai nama berbeda. Kelas `.cf`/`.pf` (`cf-grid`, `c4/c6/c8/c12`, `pf-info`) diasumsikan ada dari desain Atlas existing; bila nama util berbeda, samakan dengan `ProductForm.razor`/detail page existing saat Task 9-10.
