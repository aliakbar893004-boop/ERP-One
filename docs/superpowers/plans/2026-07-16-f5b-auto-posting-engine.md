# Fase 5b — Auto-Posting Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate balanced double-entry journals automatically from 8 operational events (GRN, Supplier/Customer Invoice, Supplier Payment, Delivery Order, Customer Receipt, Expense, POS) plus auto-reversing entries on void/cancel, driven by a central Posting Configuration.

**Architecture:** New `IJournalPostingService` builds a balanced `JournalEntry` (Source=System, linked by SourceType/SourceId) and adds it to the **caller's existing DB transaction** — each document service invokes it before `tx.CommitAsync`. Account resolution comes from a single-row `PostingConfiguration` (systemic accounts) + `GlAccountId` FKs on `CashBankAccount`/`ExpenseCategory`; a missing mapping throws `ValidationException` (fail-hard → rolls back the document). COA + mappings seed via a shared `AccountingSeeder` used by both runtime bootstrap and the test factory.

**Tech Stack:** .NET 10, Blazor Server, EF Core (`AppDbContext`, SQL Server; tests SQLite `EnsureCreated`), FluentValidation, xUnit. Builds on 5a (`Account`, `JournalEntry`, `IDocumentNumberService` JV Id=12, `ILedgerService`). Solution `ErpOne.slnx`.

## Global Constraints

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`. **App di Visual Studio HARUS di-stop** sebelum build/test (DLL lock MSB3021).
- Namespace flat: entity → `ErpOne.Domain.Entities`; infra service → `ErpOne.Infrastructure.Services` (folder tak memengaruhi namespace); Application → `ErpOne.Application.Accounting`.
- **Web project has a namespace collision on `Account`** (`ErpOne.Web.Components.Account`). In any Web-project C# that references the entity, use fully-qualified `ErpOne.Domain.Entities.Account`.
- Entity pattern: `private set`, private ctor `// EF Core` (single-row config has NO public ctor — seeded via HasData, mutated via `Update`), invariants throw `ArgumentException`/`InvalidOperationException`.
- Service: primary-ctor DI; money-movement wraps `await using var tx = await db.Database.BeginTransactionAsync(ct)`; errors via `private static ValidationException Fail(string)`.
- EF config INLINE in `OnModelCreating`; money `HasPrecision(18,2)`; enum `.HasConversion<string>().HasMaxLength(20)`; FK `OnDelete(Restrict)`; register new tables in `tablePrefixes` (M_/T_) or model build fails.
- **Fail-hard:** if a required GL account mapping is null, the posting throws `ValidationException` and the whole document operation rolls back. This is intended.
- `IJournalPostingService` NEVER opens its own transaction — it enlists in the caller's. It DOES call `db.SaveChangesAsync(ct)` internally (part of the caller's tx) to materialize the JE Id.
- Commit MANUAL oleh user — step "Commit" hanya penanda; **JANGAN** `git commit/merge/push`. Boleh `git add`. Git identity `aliakbar893004-boop`. Branch `Development`.
- Tests: SQLite `EnsureCreated` (schema+HasData from model). `CustomWebApplicationFactory.InitializeDatabase` must also run `AccountingSeeder` so document tests have COA + mappings (Task 5). DB shared per factory → isolasi via Id sendiri; seeding idempotent.

---

## File Structure

**Create — Domain:**
- `src/ErpOne.Domain/Entities/Accounting/JournalSource.cs`
- `src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs`

**Create — Application:**
- `src/ErpOne.Application/Accounting/IJournalPostingService.cs`
- `src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs`, `IPostingConfigurationService.cs`

**Create — Infrastructure:**
- `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`
- `src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs`
- `src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs`
- Migration `*_AddAutoPosting.cs`

**Create — Web:**
- `src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor`

**Create — Tests:**
- `tests/ErpOne.IntegrationTests/JournalPostingServiceTests.cs`
- `tests/ErpOne.IntegrationTests/AutoPostingIntegrationTests.cs`

**Modify:**
- `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs` (Source fields + `MarkSystemSource`)
- `src/ErpOne.Domain/Entities/Finance/CashBankAccount.cs`, `ExpenseCategory.cs` (+`GlAccountId`)
- `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (config + tablePrefix + HasData)
- `src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs` (guard System)
- `src/ErpOne.Infrastructure/Services/Transactions/GoodsReceiptService.cs`, `DeliveryOrderService.cs`
- `src/ErpOne.Infrastructure/Services/Finance/SupplierInvoiceService.cs`, `SupplierPaymentService.cs`, `CustomerInvoiceService.cs`, `CustomerReceiptService.cs`, `ExpenseService.cs`
- `src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs`
- `src/ErpOne.Infrastructure/DependencyInjection.cs`
- `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (call `AccountingSeeder`, remove inline COA from 5a)
- `src/ErpOne.Web/Authorization/AppMenus.cs` (`settings.posting-config`)
- `tests/ErpOne.IntegrationTests/CustomWebApplicationFactory.cs` (call `AccountingSeeder` in `InitializeDatabase`)

---

## Task 1: JournalEntry source linkage + JournalSource + manual-only guard

**Files:**
- Create: `src/ErpOne.Domain/Entities/Accounting/JournalSource.cs`
- Modify: `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs`

**Interfaces:**
- Produces: `JournalSource { Manual, System }`; `JournalEntry.Source/SourceType/SourceId` + `MarkSystemSource(string, int)`. `JournalEntryService` rejects Update/Delete/Reverse on `Source == System`.

- [ ] **Step 1: JournalSource enum**

Create `src/ErpOne.Domain/Entities/Accounting/JournalSource.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Asal jurnal: Manual (dibuat user) atau System (auto-post dari transaksi).</summary>
public enum JournalSource { Manual, System }
```

- [ ] **Step 2: Add source fields + MarkSystemSource to JournalEntry**

In `JournalEntry.cs`, add properties after `public int? ReversedByEntryId { get; private set; }`:
```csharp
    public JournalSource Source { get; private set; }
    public string? SourceType { get; private set; }
    public int? SourceId { get; private set; }
```
In the public ctor, after `Status = JournalEntryStatus.Draft;` add:
```csharp
        Source = JournalSource.Manual;
```
Add method after `MarkAsReversalOf`:
```csharp
    /// <summary>Tandai jurnal ini dihasilkan otomatis dari dokumen sumber.</summary>
    public void MarkSystemSource(string sourceType, int sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("sourceType is required.", nameof(sourceType));
        if (sourceId <= 0) throw new ArgumentException("sourceId is required.", nameof(sourceId));
        Source = JournalSource.System;
        SourceType = sourceType.Trim();
        SourceId = sourceId;
    }
```

- [ ] **Step 3: Guard system entries in JournalEntryService**

In `JournalEntryService.cs`, add a guard at the start of the body of `UpdateDraftAsync`, `DeleteDraftAsync`, and `ReverseAsync` — immediately after the entry is loaded (after the `?? throw Fail(...)` line in each):
```csharp
        if (entry.Source == JournalSource.System) throw Fail("System-generated entries cannot be modified manually.");
```
(In `ReverseAsync` the loaded variable is named `original`; use `if (original.Source == JournalSource.System) throw Fail(...)`.)

- [ ] **Step 4: Build Domain + Infrastructure**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Accounting/JournalSource.cs src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs
```

---

## Task 2: PostingConfiguration entity + GlAccountId on masters

**Files:**
- Create: `src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs`
- Modify: `src/ErpOne.Domain/Entities/Finance/CashBankAccount.cs`
- Modify: `src/ErpOne.Domain/Entities/Finance/ExpenseCategory.cs`

**Interfaces:**
- Produces: `PostingConfiguration` (9 nullable account FK props + `Update(...)`); `CashBankAccount.GlAccountId`, `ExpenseCategory.GlAccountId` (threaded through ctor/Update).

- [ ] **Step 1: PostingConfiguration entity**

Create `src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) pemetaan akun GL sistemik untuk auto-posting.</summary>
public class PostingConfiguration : AuditableEntity
{
    public int Id { get; private set; }
    public int? ArAccountId { get; private set; }
    public int? ApAccountId { get; private set; }
    public int? InventoryAccountId { get; private set; }
    public int? GrIrAccountId { get; private set; }
    public int? SalesAccountId { get; private set; }
    public int? CogsAccountId { get; private set; }
    public int? InputTaxAccountId { get; private set; }
    public int? OutputTaxAccountId { get; private set; }
    public int? PosCashAccountId { get; private set; }

    private PostingConfiguration() { } // EF Core; single row seeded via HasData

    public void Update(int? ar, int? ap, int? inventory, int? grIr, int? sales, int? cogs,
        int? inputTax, int? outputTax, int? posCash)
    {
        ArAccountId = ar;
        ApAccountId = ap;
        InventoryAccountId = inventory;
        GrIrAccountId = grIr;
        SalesAccountId = sales;
        CogsAccountId = cogs;
        InputTaxAccountId = inputTax;
        OutputTaxAccountId = outputTax;
        PosCashAccountId = posCash;
    }
}
```

- [ ] **Step 2: GlAccountId on CashBankAccount**

In `CashBankAccount.cs`: add property after `public bool IsActive { get; private set; }`:
```csharp
    public int? GlAccountId { get; private set; }
```
The ctor, `Update`, and `Set` share the arg list `(string code, string name, CashBankType type, string currency, decimal openingBalance, string? bankName, string? accountNumber, string? accountHolder, bool isActive)`. Add a trailing optional param `int? glAccountId = null` to **all three** signatures. In `Set`, add `GlAccountId = glAccountId;`. In ctor and `Update`, pass `glAccountId` through to `Set(...)`.

- [ ] **Step 3: GlAccountId on ExpenseCategory**

In `ExpenseCategory.cs`: add property after `IsActive`:
```csharp
    public int? GlAccountId { get; private set; }
```
The ctor/`Update`/`Set` share `(string code, string name, bool isActive)`. Add trailing `int? glAccountId = null` to all three; in `Set` add `GlAccountId = glAccountId;`; pass through from ctor and `Update`.

- [ ] **Step 4: Build Domain**

Run: `dotnet build src/ErpOne.Domain/ErpOne.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs src/ErpOne.Domain/Entities/Finance/CashBankAccount.cs src/ErpOne.Domain/Entities/Finance/ExpenseCategory.cs
```

---

## Task 3: EF wiring + migration

**Files:**
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Create: migration `*_AddAutoPosting.cs`

**Interfaces:**
- Consumes: Task 1–2 entities.
- Produces: `db.PostingConfigurations`; columns `T_JournalEntries.Source/SourceType/SourceId`; `M_CashBankAccounts.GlAccountId`; `M_ExpenseCategories.GlAccountId`; `M_PostingConfiguration` (Id=1 seeded).

- [ ] **Step 1: DbSet**

In `AppDbContext.cs`, after `public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();`:
```csharp
    public DbSet<PostingConfiguration> PostingConfigurations => Set<PostingConfiguration>();
```

- [ ] **Step 2: JournalEntry new columns (extend existing config block)**

In the `modelBuilder.Entity<JournalEntry>(...)` block, after `e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();` add:
```csharp
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.SourceType).HasMaxLength(40);
            e.HasIndex(x => new { x.SourceType, x.SourceId });
```

- [ ] **Step 3: GlAccountId FK on CashBankAccount + ExpenseCategory**

In the `modelBuilder.Entity<CashBankAccount>(...)` block, before the closing `});`, add:
```csharp
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.GlAccountId).OnDelete(DeleteBehavior.Restrict);
```
In the `modelBuilder.Entity<ExpenseCategory>(...)` block, before its closing `});`, add:
```csharp
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.GlAccountId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: PostingConfiguration config + single-row seed**

After the `JournalEntryLine` config block, add:
```csharp
        modelBuilder.Entity<PostingConfiguration>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ArAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ApAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.GrIrAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.SalesAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.CogsAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.InputTaxAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.OutputTaxAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.PosCashAccountId).OnDelete(DeleteBehavior.Restrict);

            e.HasData(new
            {
                Id = 1,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });
```

- [ ] **Step 5: tablePrefixes**

In the Master section of `tablePrefixes`, add:
```csharp
            [nameof(PostingConfiguration)] = "M_",
```

- [ ] **Step 6: Build Infrastructure (verify model + prefix guard)**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Generate migration**

App di VS di-stop. Run:
`dotnet ef migrations add AddAutoPosting -p src/ErpOne.Infrastructure -s src/ErpOne.Web`
Expected: `Up` adds `Source`(string,notnull default via runtime — see note)/`SourceType`/`SourceId` to `T_JournalEntries`, `GlAccountId` to `M_CashBankAccounts` & `M_ExpenseCategories`, creates `M_PostingConfiguration` + inserts Id=1, plus FKs/indexes. Snapshot updated.

> **Note (Source default):** existing `T_JournalEntries` rows (from 5a) need a value for the new non-null `Source` column. After generating, open the migration and ensure the `AddColumn<string>("Source", ...)` for `T_JournalEntries` has `defaultValue: "Manual"` (edit if `dotnet ef` emitted `defaultValue: ""`). This backfills 5a manual entries correctly.

**Fallback if `dotnet ef` unavailable:** create `src/ErpOne.Infrastructure/Persistence/Migrations/20260716130000_AddAutoPosting.cs` with `AddColumn<string>("Source","T_JournalEntries", maxLength:20, nullable:false, defaultValue:"Manual")`, `AddColumn<string>("SourceType","T_JournalEntries", maxLength:40, nullable:true)`, `AddColumn<int>("SourceId","T_JournalEntries", nullable:true)`, `AddColumn<int>("GlAccountId","M_CashBankAccounts", nullable:true)`, `AddColumn<int>("GlAccountId","M_ExpenseCategories", nullable:true)`, `CreateTable("M_PostingConfiguration", Id int PK identity + 9 nullable int account columns + audit columns)`, FKs to `M_Accounts` (Restrict), `InsertData("M_PostingConfiguration", Id=1, audit)`, and matching indexes; `Down` reverses. (Snapshot won't auto-update — note to regenerate later.)

- [ ] **Step 8: Build**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/
```

---

## Task 4: PostingConfigurationService (config CRUD)

**Files:**
- Create: `src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs`, `IPostingConfigurationService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Produces: `IPostingConfigurationService` (`GetAsync`, `UpdateAsync`), `PostingConfigurationDto`, `UpdatePostingConfigurationRequest`.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs`:
```csharp
namespace ErpOne.Application.Accounting;

public record PostingConfigurationDto(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId);

public record UpdatePostingConfigurationRequest(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/Accounting/IPostingConfigurationService.cs`:
```csharp
namespace ErpOne.Application.Accounting;

public interface IPostingConfigurationService
{
    Task<PostingConfigurationDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(UpdatePostingConfigurationRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 3: Service**

Create `src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PostingConfigurationService(AppDbContext db) : IPostingConfigurationService
{
    public async Task<PostingConfigurationDto> GetAsync(CancellationToken ct = default)
    {
        var c = await db.PostingConfigurations.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PostingConfiguration seed row (Id=1) is missing.");
        return new PostingConfigurationDto(c.ArAccountId, c.ApAccountId, c.InventoryAccountId, c.GrIrAccountId,
            c.SalesAccountId, c.CogsAccountId, c.InputTaxAccountId, c.OutputTaxAccountId, c.PosCashAccountId);
    }

    public async Task UpdateAsync(UpdatePostingConfigurationRequest r, CancellationToken ct = default)
    {
        var c = await db.PostingConfigurations.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PostingConfiguration seed row (Id=1) is missing.");
        c.Update(r.ArAccountId, r.ApAccountId, r.InventoryAccountId, r.GrIrAccountId,
            r.SalesAccountId, r.CogsAccountId, r.InputTaxAccountId, r.OutputTaxAccountId, r.PosCashAccountId);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: DI**

In `DependencyInjection.cs`, after `services.AddScoped<ILedgerService, LedgerService>();`:
```csharp
        services.AddScoped<IPostingConfigurationService, PostingConfigurationService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs src/ErpOne.Application/Accounting/IPostingConfigurationService.cs src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs src/ErpOne.Infrastructure/DependencyInjection.cs
```

---

## Task 5: Shared AccountingSeeder (COA + mappings) + wire into bootstrap & tests

**Files:**
- Create: `src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs`
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (replace 5a inline COA block with a call)
- Modify: `tests/ErpOne.IntegrationTests/CustomWebApplicationFactory.cs`

**Interfaces:**
- Produces: `AccountingSeeder.SeedAsync(AppDbContext db)` — idempotent: seeds 29 standard accounts (if none), maps PostingConfiguration (if unset), sets `CASH` + all ExpenseCategory `GlAccountId` defaults.

- [ ] **Step 1: AccountingSeeder**

Create `src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Domain.Entities;

namespace ErpOne.Infrastructure.Persistence;

/// <summary>Seed idempoten: COA standar Indonesia + PostingConfiguration + GlAccountId master.
/// Dipakai BootstrapSeeder (runtime) DAN test factory (agar auto-posting punya mapping).</summary>
public static class AccountingSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 1) Chart of Accounts (idempotent).
        if (!await db.Accounts.AnyAsync(ct))
        {
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
            foreach (var d in defs)
            {
                int? parentId = d.Parent is null ? null : byCode[d.Parent].Id;
                var acc = new Account(d.Code, d.Name, d.Type, parentId, d.Postable, null);
                db.Accounts.Add(acc);
                await db.SaveChangesAsync(ct);
                byCode[d.Code] = acc;
            }
        }

        // Lookup helper by code.
        async Task<int?> IdOf(string code) =>
            await db.Accounts.Where(a => a.Code == code).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

        // 2) PostingConfiguration mapping (only if the row exists and AR is still unset).
        var cfg = await db.PostingConfigurations.FirstOrDefaultAsync(ct);
        if (cfg is not null && cfg.ArAccountId is null)
        {
            cfg.Update(
                ar: await IdOf("1130"), ap: await IdOf("2110"), inventory: await IdOf("1140"),
                grIr: await IdOf("1160"), sales: await IdOf("4100"), cogs: await IdOf("5100"),
                inputTax: await IdOf("1150"), outputTax: await IdOf("2120"), posCash: await IdOf("1110"));
            await db.SaveChangesAsync(ct);
        }

        // 3) Master GlAccountId defaults.
        var cash1110 = await IdOf("1110");
        var beban6900 = await IdOf("6900");
        var cashAccounts = await db.CashBankAccounts.Where(a => a.GlAccountId == null).ToListAsync(ct);
        foreach (var a in cashAccounts)
            a.Update(a.Code, a.Name, a.Type, a.Currency, a.OpeningBalance, a.BankName, a.AccountNumber, a.AccountHolder, a.IsActive, cash1110);
        var cats = await db.ExpenseCategories.Where(c => c.GlAccountId == null).ToListAsync(ct);
        foreach (var c in cats)
            c.Update(c.Code, c.Name, c.IsActive, beban6900);
        if (cashAccounts.Count > 0 || cats.Count > 0) await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Replace 5a inline COA block in BootstrapSeeder with a call**

In `BootstrapSeeder.cs`, **delete** the entire 5a block that starts with the comment `// Seed COA standar Indonesia (idempotent — hanya bila belum ada akun sama sekali).` and its `if (!await db.Accounts.AnyAsync()) { ... }` body. Replace it with:
```csharp
        // Seed COA + posting configuration + master GL accounts (idempotent).
        await AccountingSeeder.SeedAsync(db);
```
Add `using ErpOne.Infrastructure.Persistence;` at the top if not already present (it is — `AppDbContext` is used).

- [ ] **Step 3: Seed accounting in the test factory**

In `tests/ErpOne.IntegrationTests/CustomWebApplicationFactory.cs`, change `InitializeDatabase`:
```csharp
    public void InitializeDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        AccountingSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }
```
Add `using ErpOne.Infrastructure.Persistence;` (already present).

- [ ] **Step 4: Build solution**

Run: `dotnet build ErpOne.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Run existing accounting tests (regression — seeding must not break 5a)**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AccountServiceTests|FullyQualifiedName~LedgerServiceTests|FullyQualifiedName~JournalEntryServiceTests"`
Expected: PASS (standard COA now present, but 5a tests use their own random-code accounts + `Assert.Single(predicate)` so they remain green).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs tests/ErpOne.IntegrationTests/CustomWebApplicationFactory.cs
```

---

## Task 6: JournalPostingService (the engine) — TDD

**Files:**
- Create: `src/ErpOne.Application/Accounting/IJournalPostingService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/JournalPostingServiceTests.cs`

**Interfaces:**
- Consumes: `PostingConfiguration`, `CashBankAccount.GlAccountId`, `ExpenseCategory.GlAccountId`, `JournalEntry`/`MarkSystemSource` (Task 1–2), `IDocumentNumberService`/`DocumentTypes.JournalEntry`, entity totals.
- Produces: `IJournalPostingService` with the 8 `Post*Async` methods + `ReverseForAsync`.

- [ ] **Step 1: Interface**

Create `src/ErpOne.Application/Accounting/IJournalPostingService.cs`:
```csharp
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Accounting;

/// <summary>Membangun jurnal double-entry otomatis dari dokumen. Ikut transaksi caller (tak buka tx sendiri).</summary>
public interface IJournalPostingService
{
    Task PostGoodsReceiptAsync(GoodsReceipt grn, CancellationToken ct = default);
    Task PostSupplierInvoiceAsync(SupplierInvoice inv, CancellationToken ct = default);
    Task PostSupplierPaymentAsync(SupplierPayment pay, CancellationToken ct = default);
    Task PostCustomerInvoiceAsync(CustomerInvoice inv, CancellationToken ct = default);
    Task PostDeliveryOrderAsync(DeliveryOrder dorder, CancellationToken ct = default);
    Task PostCustomerReceiptAsync(CustomerReceipt rec, CancellationToken ct = default);
    Task PostExpenseAsync(Expense exp, CancellationToken ct = default);
    Task PostPosSaleAsync(PosSale sale, CancellationToken ct = default);
    Task ReverseForAsync(string sourceType, int sourceId, DateTime date, string? note, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write failing tests**

Create `tests/ErpOne.IntegrationTests/JournalPostingServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpOne.IntegrationTests;

public class JournalPostingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public JournalPostingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<int> AccountId(AppDbContext db, string code) =>
        await db.Accounts.Where(a => a.Code == code).Select(a => a.Id).FirstAsync();

    [Fact]
    public async Task Expense_posts_debit_expense_credit_cash()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        // Seeded CASH account has GlAccountId=1110; seeded categories have GlAccountId=6900.
        // Create a category with an explicit GL account.
        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 250_000m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await poster.PostExpenseAsync(exp);

        var je = await db.JournalEntries.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id);
        Assert.NotNull(je);
        Assert.Equal(JournalSource.System, je!.Source);
        Assert.Equal(JournalEntryStatus.Posted, je.Status);
        Assert.Equal(250_000m, je.TotalDebit);
        Assert.Equal(je.TotalDebit, je.TotalCredit);
        Assert.Contains(je.Lines, l => l.AccountId == beban && l.Debit == 250_000m);
        Assert.Contains(je.Lines, l => l.AccountId == cash.GlAccountId && l.Credit == 250_000m);
    }

    [Fact]
    public async Task Missing_mapping_throws_and_writes_no_entry()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        // Category with NO GL account → expense posting must fail.
        var cat = new ExpenseCategory($"EC{Sfx()}", "NoGL", true, null);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 100m, null, "x", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => poster.PostExpenseAsync(exp));
        Assert.False(await db.JournalEntries.AnyAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id));
    }

    [Fact]
    public async Task Reverse_creates_mirror_and_nets_to_zero()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 500m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();
        await poster.PostExpenseAsync(exp);

        await poster.ReverseForAsync("Expense", exp.Id, DateTime.Today, "void");

        var entries = await db.JournalEntries
            .Where(x => x.SourceId == exp.Id && (x.SourceType == "Expense" || x.SourceType == "ExpenseVoid"))
            .Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, entries.Count);
        // Net debit on expense account = 0 (posted 500 then reversed 500).
        var net = entries.SelectMany(e => e.Lines).Where(l => l.AccountId == beban).Sum(l => l.Debit - l.Credit);
        Assert.Equal(0m, net);
    }

    [Fact]
    public async Task Posting_is_idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 700m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await poster.PostExpenseAsync(exp);
        await poster.PostExpenseAsync(exp);   // second call must be a no-op

        var count = await db.JournalEntries.CountAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id);
        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~JournalPostingServiceTests"`
Expected: FAIL (`IJournalPostingService` not registered).

- [ ] **Step 4: Implementation**

Create `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class JournalPostingService(AppDbContext db, IDocumentNumberService docNumbers) : IJournalPostingService
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private async Task<PostingConfiguration> ConfigAsync(CancellationToken ct) =>
        await db.PostingConfigurations.FirstOrDefaultAsync(ct)
        ?? throw Fail("Posting configuration is missing.");

    private static int RequireAccount(int? id, string label) =>
        id ?? throw Fail($"Account not mapped: {label}. Set it in Settings → Posting Configuration.");

    // Builds one balanced System journal. Idempotent by (sourceType, sourceId). Enlists in caller tx.
    private async Task PostBalancedAsync(DateTime date, string description, string sourceType, int sourceId,
        IEnumerable<(int AccountId, decimal Debit, decimal Credit, string? Memo)> lines, CancellationToken ct)
    {
        if (await db.JournalEntries.AnyAsync(x => x.SourceType == sourceType && x.SourceId == sourceId, ct))
            return; // already posted

        var filtered = lines.Where(l => l.Debit > 0m || l.Credit > 0m).ToList();
        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, date, ct);
        var je = new JournalEntry(number, date, description);
        je.SetLines(filtered.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        je.MarkSystemSource(sourceType, sourceId);
        je.Post();
        db.JournalEntries.Add(je);
        await db.SaveChangesAsync(ct);
    }

    public async Task PostGoodsReceiptAsync(GoodsReceipt grn, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");
        var value = grn.Lines.Sum(l => Round(l.QuantityReceived * l.UnitCost));
        await PostBalancedAsync(grn.ReceiptDate, $"GRN {grn.GrnNumber}", "GoodsReceipt", grn.Id,
            [(inventory, value, 0m, "Inventory received"), (grIr, 0m, value, "Goods received not invoiced")], ct);
    }

    public async Task PostSupplierInvoiceAsync(SupplierInvoice inv, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");
        var ap = RequireAccount(cfg.ApAccountId, "Accounts Payable");
        var net = inv.Subtotal - inv.DiscountTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (grIr, net, 0m, "Goods invoiced"),
            (ap, 0m, inv.GrandTotal, "Supplier payable"),
        };
        if (inv.TaxTotal > 0m)
            lines.Insert(1, (RequireAccount(cfg.InputTaxAccountId, "Input Tax"), inv.TaxTotal, 0m, "Input VAT"));
        await PostBalancedAsync(inv.InvoiceDate, $"Supplier Invoice {inv.InvoiceNumber}", "SupplierInvoice", inv.Id, lines, ct);
    }

    public async Task PostSupplierPaymentAsync(SupplierPayment pay, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ap = RequireAccount(cfg.ApAccountId, "Accounts Payable");
        var cash = RequireAccount(await CashGlAsync(pay.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(pay.PaymentDate, $"Supplier Payment {pay.PaymentNumber}", "SupplierPayment", pay.Id,
            [(ap, pay.Amount, 0m, "Settle payable"), (cash, 0m, pay.Amount, "Cash out")], ct);
    }

    public async Task PostCustomerInvoiceAsync(CustomerInvoice inv, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ar = RequireAccount(cfg.ArAccountId, "Accounts Receivable");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var net = inv.Subtotal - inv.DiscountTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (ar, inv.GrandTotal, 0m, "Customer receivable"),
            (sales, 0m, net, "Revenue"),
        };
        if (inv.TaxTotal > 0m)
            lines.Add((RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), 0m, inv.TaxTotal, "Output VAT"));
        await PostBalancedAsync(inv.InvoiceDate, $"Customer Invoice {inv.InvoiceNumber}", "CustomerInvoice", inv.Id, lines, ct);
    }

    public async Task PostDeliveryOrderAsync(DeliveryOrder dorder, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var value = dorder.Lines.Sum(l => Round(l.QuantityDelivered * l.UnitCost));
        if (value <= 0m) return; // nothing to post
        await PostBalancedAsync(dorder.DeliveryDate, $"Delivery {dorder.DoNumber}", "DeliveryOrder", dorder.Id,
            [(cogs, value, 0m, "Cost of goods sold"), (inventory, 0m, value, "Inventory shipped")], ct);
    }

    public async Task PostCustomerReceiptAsync(CustomerReceipt rec, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ar = RequireAccount(cfg.ArAccountId, "Accounts Receivable");
        var cash = RequireAccount(await CashGlAsync(rec.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(rec.ReceiptDate, $"Customer Receipt {rec.ReceiptNumber}", "CustomerReceipt", rec.Id,
            [(cash, rec.Amount, 0m, "Cash in"), (ar, 0m, rec.Amount, "Settle receivable")], ct);
    }

    public async Task PostExpenseAsync(Expense exp, CancellationToken ct = default)
    {
        var expenseAcc = RequireAccount(
            await db.ExpenseCategories.Where(c => c.Id == exp.ExpenseCategoryId).Select(c => c.GlAccountId).FirstOrDefaultAsync(ct),
            "Expense category GL account");
        var cash = RequireAccount(await CashGlAsync(exp.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(exp.ExpenseDate, $"Expense {exp.ExpenseNumber}", "Expense", exp.Id,
            [(expenseAcc, exp.Amount, 0m, exp.Description), (cash, 0m, exp.Amount, "Cash out")], ct);
    }

    public async Task PostPosSaleAsync(PosSale sale, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var posCash = RequireAccount(cfg.PosCashAccountId, "POS Cash");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var net = sale.GrandTotal - sale.TaxTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (posCash, sale.GrandTotal, 0m, "POS cash in"),
            (sales, 0m, net, "POS revenue"),
        };
        if (sale.TaxTotal > 0m)
            lines.Add((RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), 0m, sale.TaxTotal, "Output VAT"));
        if (sale.CogsTotal > 0m)
        {
            lines.Add((cogs, sale.CogsTotal, 0m, "COGS"));
            lines.Add((inventory, 0m, sale.CogsTotal, "Inventory sold"));
        }
        await PostBalancedAsync(sale.SaleDate, $"POS {sale.SaleNumber}", "PosSale", sale.Id, lines, ct);
    }

    public async Task ReverseForAsync(string sourceType, int sourceId, DateTime date, string? note, CancellationToken ct = default)
    {
        var original = await db.JournalEntries.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SourceType == sourceType && x.SourceId == sourceId
                && x.Source == JournalSource.System && x.Status == JournalEntryStatus.Posted && x.ReversedByEntryId == null, ct);
        if (original is null) return; // nothing posted / already reversed

        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, date, ct);
        var desc = string.IsNullOrWhiteSpace(note)
            ? $"Reversal of {original.EntryNumber}"
            : $"Reversal of {original.EntryNumber}: {note.Trim()}";
        var reversal = new JournalEntry(number, date, desc);
        reversal.SetLines(original.Lines.Select(l => (l.AccountId, l.Credit, l.Debit, l.Memo)));
        reversal.MarkSystemSource($"{sourceType}Void", sourceId);
        reversal.MarkAsReversalOf(original.Id);
        reversal.Post();
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);

        original.MarkReversed(reversal.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<int?> CashGlAsync(int cashBankAccountId, CancellationToken ct) =>
        await db.CashBankAccounts.Where(a => a.Id == cashBankAccountId).Select(a => a.GlAccountId).FirstOrDefaultAsync(ct);

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("Posting", message)]);
}
```

- [ ] **Step 5: DI**

In `DependencyInjection.cs`, after `services.AddScoped<IPostingConfigurationService, PostingConfigurationService>();`:
```csharp
        services.AddScoped<IJournalPostingService, JournalPostingService>();
```

- [ ] **Step 6: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~JournalPostingServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/Accounting/IJournalPostingService.cs src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/JournalPostingServiceTests.cs
```

---

## Task 7: Integrate purchase-side services

**Files:**
- Modify: `GoodsReceiptService.cs`, `SupplierInvoiceService.cs`, `SupplierPaymentService.cs`

**Interfaces:**
- Consumes: `IJournalPostingService` (Task 6).

For EACH service: add `IJournalPostingService journalPoster` to the primary constructor's parameter list, and add `using ErpOne.Application.Accounting;` at the top.

- [ ] **Step 1: GoodsReceiptService.PostAsync**

Add ctor param + using. In `PostAsync`, immediately **before** `await tx.CommitAsync(ct);` (line ~259):
```csharp
        await journalPoster.PostGoodsReceiptAsync(grn, ct);
```
(`grn` is the loaded receipt with `Lines`; it's already posted-stock at this point.)

- [ ] **Step 2: SupplierInvoiceService — CreateAsync + CancelAsync**

Add ctor param + using. In `CreateAsync`, before `await tx.CommitAsync(ct);` (line ~152):
```csharp
        await journalPoster.PostSupplierInvoiceAsync(invoice, ct);
```
In `CancelAsync` (no tx), before `await db.SaveChangesAsync(ct);` (line ~173), add reversal:
```csharp
        await journalPoster.ReverseForAsync("SupplierInvoice", id, DateTime.UtcNow.Date, "Invoice cancelled", ct);
```

- [ ] **Step 3: SupplierPaymentService — PostAsync (private) + VoidAsync**

Add ctor param + using. In the private `PostAsync(SupplierPayment payment, CancellationToken ct)`, after `payment.MarkPosted();` (line ~204), add:
```csharp
        await journalPoster.PostSupplierPaymentAsync(payment, ct);
```
In `VoidAsync`, before `await tx.CommitAsync(ct);` (line ~189):
```csharp
        await journalPoster.ReverseForAsync("SupplierPayment", id, DateTime.UtcNow.Date, "Payment voided", ct);
```

- [ ] **Step 4: Build + run purchase document tests (regression)**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~GoodsReceipt|FullyQualifiedName~SupplierInvoice|FullyQualifiedName~SupplierPayment"`
Expected: PASS — existing tests still green (mappings seeded via factory), now also producing JEs.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Transactions/GoodsReceiptService.cs src/ErpOne.Infrastructure/Services/Finance/SupplierInvoiceService.cs src/ErpOne.Infrastructure/Services/Finance/SupplierPaymentService.cs
```

---

## Task 8: Integrate sales-side services

**Files:**
- Modify: `CustomerInvoiceService.cs`, `DeliveryOrderService.cs`, `CustomerReceiptService.cs`

Add `IJournalPostingService journalPoster` ctor param + `using ErpOne.Application.Accounting;` to each.

- [ ] **Step 1: CustomerInvoiceService — CreateAsync + CancelAsync**

In `CreateAsync`, before `await tx.CommitAsync(ct);` (line ~149):
```csharp
        await journalPoster.PostCustomerInvoiceAsync(invoice, ct);
```
In `CancelAsync` (no tx), before `await db.SaveChangesAsync(ct);` (line ~169):
```csharp
        await journalPoster.ReverseForAsync("CustomerInvoice", id, DateTime.UtcNow.Date, "Invoice cancelled", ct);
```

- [ ] **Step 2: DeliveryOrderService.PostAsync**

In `PostAsync`, before `await tx.CommitAsync(ct);` (line ~266), after the line loop that sets `line.SetUnitCost(...)`:
```csharp
        await journalPoster.PostDeliveryOrderAsync(doc, ct);
```
(`doc.Lines` now have `UnitCost` set.)

- [ ] **Step 3: CustomerReceiptService — CreateAsync + VoidAsync**

In `CreateAsync`, before `await tx.CommitAsync(ct);` (line ~100):
```csharp
        await journalPoster.PostCustomerReceiptAsync(receipt, ct);
```
In `VoidAsync`, before `await tx.CommitAsync(ct);` (line ~124):
```csharp
        await journalPoster.ReverseForAsync("CustomerReceipt", id, DateTime.UtcNow.Date, "Receipt voided", ct);
```

- [ ] **Step 4: Build + run sales document tests**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~CustomerInvoice|FullyQualifiedName~DeliveryOrder|FullyQualifiedName~CustomerReceipt"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Finance/CustomerInvoiceService.cs src/ErpOne.Infrastructure/Services/Transactions/DeliveryOrderService.cs src/ErpOne.Infrastructure/Services/Finance/CustomerReceiptService.cs
```

---

## Task 9: Integrate Expense + POS + end-to-end test

**Files:**
- Modify: `ExpenseService.cs`, `PosSaleService.cs`
- Create: `tests/ErpOne.IntegrationTests/AutoPostingIntegrationTests.cs`

- [ ] **Step 1: ExpenseService — CreateAsync + VoidAsync**

Add ctor param + using. In `CreateAsync`, before `await tx.CommitAsync(ct);` (line ~76):
```csharp
        await journalPoster.PostExpenseAsync(expense, ct);
```
In `VoidAsync`, before `await tx.CommitAsync(ct);` (line ~93):
```csharp
        await journalPoster.ReverseForAsync("Expense", id, DateTime.UtcNow.Date, "Expense voided", ct);
```

- [ ] **Step 2: PosSaleService.CreateSaleAsync**

Add ctor param + using. In `CreateSaleAsync`, before `await tx.CommitAsync(ct);` (line ~108):
```csharp
        await journalPoster.PostPosSaleAsync(sale, ct);
```

- [ ] **Step 3: End-to-end test (Expense create → void via service posts + reverses JE)**

Create `tests/ErpOne.IntegrationTests/AutoPostingIntegrationTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Expenses;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AutoPostingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AutoPostingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Expense_create_then_void_posts_and_reverses_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var expenses = sp.GetRequiredService<IExpenseService>();

        // Seeded default category has GlAccountId (6900); CASH has GlAccountId (1110).
        var cat = new ExpenseCategory($"EC{Sfx()}", "Ops", true,
            await db.Accounts.Where(a => a.Code == "6300").Select(a => (int?)a.Id).FirstAsync());
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var dto = await expenses.CreateAsync(new CreateExpenseRequest(DateTime.Today, 1, cat.Id, 300_000m, null, "Listrik", null));
        Assert.True(await db.JournalEntries.AnyAsync(x => x.SourceType == "Expense" && x.SourceId == dto.Id));

        await expenses.VoidAsync(dto.Id, "tester");
        Assert.True(await db.JournalEntries.AnyAsync(x => x.SourceType == "ExpenseVoid" && x.SourceId == dto.Id));
    }
}
```
> Note: verify `CreateExpenseRequest` field order against `ProductDtos`-style record in `ErpOne.Application.Expenses` — the exact ctor is `(DateTime ExpenseDate, int CashBankAccountId, int ExpenseCategoryId, decimal Amount, string? Payee, string Description, string? Notes)`. Adjust the call if the actual record differs. CashBankAccountId `1` = seeded `CASH`.

- [ ] **Step 4: Build + run auto-posting tests**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AutoPosting|FullyQualifiedName~Expense"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Finance/ExpenseService.cs src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs tests/ErpOne.IntegrationTests/AutoPostingIntegrationTests.cs
```

---

## Task 10: Posting Configuration page + menu

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Create: `src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor`

**Interfaces:**
- Consumes: `IPostingConfigurationService`, `IAccountService` (postable list).

- [ ] **Step 1: Menu resource**

In `AppMenus.cs`, in the `Settings` group after `settings.document-numbering`:
```csharp
            new("settings.posting-config", "Posting Configuration", "bi-diagram-3-fill", [ActIndex, ActEdit]),
```

- [ ] **Step 2: Config form page**

Create `src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor`:
```razor
@page "/settings/posting-config"
@attribute [Authorize(Policy = "settings.posting-config.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Accounting
@using FluentValidation
@inject IPostingConfigurationService Config
@inject IAccountService Accounts

<PageTitle>Posting Configuration</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs"><a href="/">Home</a><i class="bi bi-chevron-right"></i><span>Settings</span><i class="bi bi-chevron-right"></i><span class="here">Posting Configuration</span></div>
        <h1>Posting Configuration</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else
    {
        @if (_saved) { <div class="cf-alert ok"><i class="bi bi-check2-circle"></i> Saved.</div> }
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-diagram-3-fill"></i></span><div class="hd-tx"><h2>GL account mapping</h2><p>Accounts used when transactions post to the ledger automatically.</p></div></div>
                <div class="card-b">
                    <div class="grid">
                        @Field("Accounts Receivable", _m.Ar, v => _m.Ar = v)
                        @Field("Accounts Payable", _m.Ap, v => _m.Ap = v)
                        @Field("Inventory", _m.Inventory, v => _m.Inventory = v)
                        @Field("Goods Received Not Invoiced (GR-IR)", _m.GrIr, v => _m.GrIr = v)
                        @Field("Sales / Revenue", _m.Sales, v => _m.Sales = v)
                        @Field("Cost of Goods Sold", _m.Cogs, v => _m.Cogs = v)
                        @Field("Input Tax (PPN Masukan)", _m.InputTax, v => _m.InputTax = v)
                        @Field("Output Tax (PPN Keluaran)", _m.OutputTax, v => _m.OutputTax = v)
                        @Field("POS Cash", _m.PosCash, v => _m.PosCash = v)
                    </div>
                </div>
            </section>
            <div class="pf-footer"><div class="in">
                <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving"><i class="bi bi-check2"></i> Save</button>
            </div></div>
        </div>
    }
</div>

@code {
    private sealed class Model
    {
        public int? Ar, Ap, Inventory, GrIr, Sales, Cogs, InputTax, OutputTax, PosCash;
    }
    private readonly Model _m = new();
    private IReadOnlyList<AccountDto> _postable = [];
    private bool _loading = true, _saving, _saved;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        _postable = await Accounts.GetPostableAsync();
        var c = await Config.GetAsync();
        _m.Ar = c.ArAccountId; _m.Ap = c.ApAccountId; _m.Inventory = c.InventoryAccountId; _m.GrIr = c.GrIrAccountId;
        _m.Sales = c.SalesAccountId; _m.Cogs = c.CogsAccountId; _m.InputTax = c.InputTaxAccountId;
        _m.OutputTax = c.OutputTaxAccountId; _m.PosCash = c.PosCashAccountId;
        _loading = false;
    }

    private RenderFragment Field(string label, int? value, Action<int?> setter) => __builder =>
    {
        <div class="f c6">
            <label class="fl">@label</label>
            <select class="ctl" value="@(value?.ToString() ?? "0")"
                    @onchange="e => setter(int.TryParse(e.Value?.ToString(), out var v) && v != 0 ? v : null)">
                <option value="0">— none —</option>
                @foreach (var a in _postable) { <option value="@a.Id">@a.Code — @a.Name</option> }
            </select>
        </div>
    };

    private async Task SaveAsync()
    {
        _error = null; _saved = false; _saving = true;
        try
        {
            await Config.UpdateAsync(new UpdatePostingConfigurationRequest(_m.Ar, _m.Ap, _m.Inventory, _m.GrIr,
                _m.Sales, _m.Cogs, _m.InputTax, _m.OutputTax, _m.PosCash));
            _saved = true;
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/
```

---

## Task 11: Full suite + verification

- [ ] **Step 1: Full test suite**

App di VS di-stop. Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS. Baseline 304 + new (4 JournalPostingService + 1 AutoPosting integration = ~5) = ~309, and **all pre-existing finance/transaction tests still green** (mappings seeded via factory). If any pre-existing document test fails with "Account not mapped", the factory seeding (Task 5 Step 3) is not wired — fix before proceeding.

- [ ] **Step 2: Manual verification (skill `run`/`verify`)**

Run app; sign out/in (permission `settings.posting-config.*`). BootstrapSeeder seeds COA + mapping. Verify:
1. **Settings → Posting Configuration** — all 9 accounts pre-filled.
2. Post a GRN → open `/finance/journal-entries` → a `System` JE `GRN …` (Dr Inventory / Cr GR-IR), balanced, not editable.
3. Create Supplier Invoice → JE Dr GR-IR (+Input Tax) / Cr AP. Post Supplier Payment → Dr AP / Cr Cash.
4. Create Customer Invoice → Dr AR / Cr Sales (+Output Tax). Post Delivery Order → Dr COGS / Cr Inventory. Customer Receipt → Dr Cash / Cr AR.
5. Create Expense → Dr Beban / Cr Cash; void it → reversing JE `ExpenseVoid`.
6. POS sale → Dr POS-Cash + COGS / Cr Sales + Output Tax + Inventory.
7. `/reports/trial-balance` — totals balance; accounts populated from real transactions.
8. Empty one mapping in Posting Configuration → attempt the matching transaction → operation rejected with "Account not mapped" (fail-hard), no partial data.

- [ ] **Step 3: Done marker**

Beritahu user Fase 5b siap di-commit manual. Semua file sudah di-`git add` per task.

---

## Self-Review

**Spec coverage:**
- JournalEntry Source/SourceType/SourceId + System guard → Task 1. ✓
- PostingConfiguration + master GlAccountId → Task 2, 3, 4. ✓
- 8 posting events + reversal composition (§5A) → Task 6 (engine) + Task 7/8/9 (integration). ✓
- Fail-hard on missing mapping → Task 6 `RequireAccount` + Task 6 test. ✓
- Auto reversing on void/cancel → Task 6 `ReverseForAsync` + integration Task 7/8/9. ✓
- Seed default mapping + COA shared → Task 5 (AccountingSeeder, bootstrap + test factory). ✓
- Idempotency → Task 6 `PostBalancedAsync` guard + test. ✓
- No backfill → not implemented (by design). ✓
- Posting Config UI + menu → Task 10. ✓
- Tests (per-event via engine, missing-mapping, reverse, idempotency, e2e) → Task 6, 9. ✓

**Placeholder scan:** No TBD. Migration fallback outlined (Task 3). One flagged verification: `CreateExpenseRequest` ctor order (Task 9 note) — confirm against the actual record when writing the e2e test.

**Type consistency:** `IJournalPostingService` signatures identical Task 6↔7↔8↔9. Entity property names verbatim from source (GRN `QuantityReceived`/`UnitCost`; DO `QuantityDelivered`/`UnitCost`; PosSale `GrandTotal`/`TaxTotal`/`CogsTotal`/`SaleDate`; invoices `Subtotal`/`DiscountTotal`/`TaxTotal`/`GrandTotal`/`InvoiceDate`; payment/receipt `Amount`/`CashBankAccountId`/`PaymentDate`|`ReceiptDate`). `PostingConfiguration.Update(...)` 9-arg order consistent Task 2↔4↔5. `CashBankAccount`/`ExpenseCategory` `Update(...)` extended arg lists consistent Task 2↔5. `MarkSystemSource`/`MarkReversed`/`MarkAsReversalOf` from Task 1 + 5a. Commit anchors (`await tx.CommitAsync(ct);`) verified present in each hook method via source inspection.

**Integration risk noted:** Fail-hard means every document test needs mappings — addressed by Task 5 seeding the test factory. Task 11 Step 1 explicitly checks for this regression.
