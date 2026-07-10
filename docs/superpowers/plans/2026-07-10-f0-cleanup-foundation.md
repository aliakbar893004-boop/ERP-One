# Fase 0 — Cleanup & Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Fase 0 foundation — remove leftover template pages, add a Currency master, centralize document numbering (retrofitting all existing modules), add Company Settings wired into the POS receipt, and refactor the dashboard to the `.cr` design skeleton.

**Architecture:** Clean layering already established — `ErpOne.Domain` (entities) → `ErpOne.Infrastructure/Persistence` (EF, configs inline in `AppDbContext`) → `ErpOne.Application/<Feature>` (service interface + DTOs + FluentValidation) → `ErpOne.Infrastructure/Services` (implementation) → `ErpOne.Web` (Blazor `.pi` index / `.cf` form pages). Permissions and authorization policies are auto-derived from `AppMenus`. Seed data uses EF `HasData`.

**Tech Stack:** .NET / C#, EF Core (SQL Server prod, SQLite in-memory for tests), Blazor Server (InteractiveServer), FluentValidation, xUnit integration tests via `CustomWebApplicationFactory` (uses `db.Database.EnsureCreated()` — builds schema from the model, so tests do NOT require the EF migration).

## Global Constraints

- **UI language:** English for all new UI copy.
- **Design tokens:** index pages use the global `.pi` design; forms use the global `.cf` (Atlas) design. Do NOT use legacy `sh-header`/`fs-card`/`data-card`.
- **Table prefixes:** every new business entity MUST be registered in the `tablePrefixes` dictionary in `AppDbContext.OnModelCreating` (use `"M_"` for master/settings tables) — otherwise the model throws on build and all integration tests go red.
- **Audit:** entities inherit `AuditableEntity` (audit columns stamped automatically in `AppDbContext.SaveChanges`).
- **Permissions:** new resources are added to `AppMenus.Groups`; permissions & policies auto-derive from there (admin role auto-seeded on startup). No manual policy registration.
- **Currency storage:** `Supplier.DefaultCurrency` / `Customer.DefaultCurrency` stay `string(3)` storing the currency **Code** — no FK to `Currency` (preserves existing data, avoids schema churn). Only the UI changes to a dropdown.
- **Numbering formats must stay identical** to today: `PO-{yyyyMM}-{0000}`, `SO-{yyyyMM}-{0000}`, `GRN-{yyyyMM}-{0000}`, `DO-{yyyyMM}-{0000}` (monthly reset, 4-pad); `POS-{yyyyMMdd}-{0000}`, `SHIFT-{yyyyMMdd}-{0000}` (daily reset, 4-pad).
- **Commit** after each task's tests pass. Do not skip hooks or signing.

## Migration & test note

Integration tests build the schema from the model via `EnsureCreated()`, so tasks that add entities/configs are testable **before** the EF migration exists. The model is final after Task 4; **Task 5** generates the single migration for production. Services/web tasks (6+) do not change the model, so no further migrations are needed.

Standard EF command used in this repo (run from repo root):

```bash
dotnet ef migrations add <Name> --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web
```

---

## Task 1: Remove leftover template pages

**Files:**
- Delete: `src/ErpOne.Web/Components/Pages/Counter.razor`
- Delete: `src/ErpOne.Web/Components/Pages/Weather.razor`

- [ ] **Step 1: Confirm no references exist**

Run:
```bash
grep -rniE "counter|weather|WeatherForecast" src/ErpOne.Web --include=*.razor --include=*.cs | grep -viE "encounter|counterpart"
```
Expected: only matches inside `Counter.razor` / `Weather.razor` themselves. If any nav link (e.g. in `NavMenu.razor`) references them, note it for Step 2.

- [ ] **Step 2: Delete the files and any nav references**

```bash
git rm src/ErpOne.Web/Components/Pages/Counter.razor src/ErpOne.Web/Components/Pages/Weather.razor
```
If Step 1 found a `WeatherForecast` class or nav `<a href="/counter">`/`/weather` link, remove those lines too (edit the referencing file).

- [ ] **Step 3: Build to verify nothing broke**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: remove leftover Counter/Weather template pages"
```

---

## Task 2: Currency entity + EF config + seed

**Files:**
- Create: `src/ErpOne.Domain/Entities/Currency.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (add DbSet, config, seed, table prefix)

**Interfaces:**
- Produces: `Currency` with ctor `Currency(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)`, method `Update(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)`, and read-only properties `Id, Code, Name, Symbol, DecimalPlaces, IsBase, IsActive`. `AppDbContext.Currencies` DbSet.

- [ ] **Step 1: Create the `Currency` entity**

Create `src/ErpOne.Domain/Entities/Currency.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Mata uang (master). Tanpa kurs — konversi di luar scope Fase 0.</summary>
public class Currency : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;   // ISO 4217, mis. IDR
    public string Name { get; private set; } = default!;
    public string Symbol { get; private set; } = default!;
    public int DecimalPlaces { get; private set; }
    public bool IsBase { get; private set; }
    public bool IsActive { get; private set; }

    private Currency() { } // EF Core

    public Currency(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
        => Set(code, name, symbol, decimalPlaces, isBase, isActive);

    public void Update(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
        => Set(code, name, symbol, decimalPlaces, isBase, isActive);

    private void Set(string code, string name, string symbol, int decimalPlaces, bool isBase, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (decimalPlaces is < 0 or > 6) throw new ArgumentException("DecimalPlaces must be 0-6.", nameof(decimalPlaces));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Symbol = symbol.Trim();
        DecimalPlaces = decimalPlaces;
        IsBase = isBase;
        IsActive = isActive;
    }
}
```

- [ ] **Step 2: Register DbSet, config, seed, and table prefix in `AppDbContext`**

In `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`, add the DbSet next to the other master DbSets (after the `Brands` line ~24):
```csharp
    public DbSet<Currency> Currencies => Set<Currency>();
```

Add the entity config inside `OnModelCreating`, right after the `Brand` config block (after line ~179):
```csharp
        modelBuilder.Entity<Currency>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(3).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(6).IsRequired();

            // Base currency default (IDR). HasData butuh nilai statik.
            e.HasData(new
            {
                Id = 1,
                Code = "IDR",
                Name = "Rupiah",
                Symbol = "Rp",
                DecimalPlaces = 0,
                IsBase = true,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });
```

Add `Currency` to the `tablePrefixes` dictionary in the Master section (after `[nameof(Brand)] = "M_",`):
```csharp
            [nameof(Currency)] = "M_",
```

- [ ] **Step 3: Build to verify the model is valid**

Run: `dotnet build`
Expected: Build succeeded (no "table without prefix" exception at model build; that only surfaces at runtime/tests, so also proceed to Step 4).

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Domain/Entities/Currency.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: add Currency entity, EF config, and IDR seed"
```

---

## Task 3: NumberSequence + NumberSequenceCounter entities + EF config + seed

**Files:**
- Create: `src/ErpOne.Domain/Entities/ResetPeriod.cs`
- Create: `src/ErpOne.Domain/Entities/NumberSequence.cs`
- Create: `src/ErpOne.Domain/Entities/NumberSequenceCounter.cs`
- Create: `src/ErpOne.Application/Numbering/DocumentTypes.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Produces:
  - `enum ResetPeriod { Never, Daily, Monthly, Yearly }`
  - `NumberSequence(string code, string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)` + `Update(...)` same params; props `Id, Code, Prefix, DateFormat, Padding, ResetPeriod, Separator`.
  - `NumberSequenceCounter(string sequenceCode, string periodKey, int lastValue)`; method `int Next()`; props `Id, SequenceCode, PeriodKey, LastValue, Version`.
  - `static class DocumentTypes` with string consts `PurchaseOrder, SalesOrder, GoodsReceipt, DeliveryOrder, PosSale, CashierShift`.
  - `AppDbContext.NumberSequences`, `AppDbContext.NumberSequenceCounters`.

- [ ] **Step 1: Create the enum and entities**

Create `src/ErpOne.Domain/Entities/ResetPeriod.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Kapan counter penomoran di-reset ke 1.</summary>
public enum ResetPeriod { Never, Daily, Monthly, Yearly }
```

Create `src/ErpOne.Domain/Entities/NumberSequence.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Konfigurasi format penomoran per jenis dokumen (mis. PO-202607-0001).</summary>
public class NumberSequence : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;      // key jenis dokumen (lihat DocumentTypes)
    public string Prefix { get; private set; } = default!;    // mis. "PO"
    public string DateFormat { get; private set; } = "";      // "yyyyMM" | "yyyyMMdd" | "" (kosong = tanpa tanggal)
    public int Padding { get; private set; }                  // mis. 4
    public ResetPeriod ResetPeriod { get; private set; }
    public string Separator { get; private set; } = "-";

    private NumberSequence() { } // EF Core

    public NumberSequence(string code, string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
        => Set(code, prefix, dateFormat, padding, resetPeriod, separator);

    public void Update(string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
        => Set(Code, prefix, dateFormat, padding, resetPeriod, separator);

    private void Set(string code, string prefix, string dateFormat, int padding, ResetPeriod resetPeriod, string separator)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Prefix is required.", nameof(prefix));
        if (padding is < 1 or > 10) throw new ArgumentException("Padding must be 1-10.", nameof(padding));

        Code = code.Trim();
        Prefix = prefix.Trim().ToUpperInvariant();
        DateFormat = (dateFormat ?? "").Trim();
        Padding = padding;
        ResetPeriod = resetPeriod;
        Separator = separator ?? "-";
    }
}
```

Create `src/ErpOne.Domain/Entities/NumberSequenceCounter.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Counter berjalan per (SequenceCode, PeriodKey). Version = concurrency token
/// agar increment aman dari race (lintas provider: SQL Server & SQLite).</summary>
public class NumberSequenceCounter
{
    public int Id { get; private set; }
    public string SequenceCode { get; private set; } = default!;
    public string PeriodKey { get; private set; } = default!;   // mis. "202607" atau "20260710" atau "ALL"
    public int LastValue { get; private set; }
    public int Version { get; private set; }

    private NumberSequenceCounter() { } // EF Core

    public NumberSequenceCounter(string sequenceCode, string periodKey, int lastValue)
    {
        SequenceCode = sequenceCode;
        PeriodKey = periodKey;
        LastValue = lastValue;
    }

    /// <summary>Naikkan counter & bump concurrency token; kembalikan nilai baru.</summary>
    public int Next()
    {
        LastValue++;
        Version++;
        return LastValue;
    }
}
```

Create `src/ErpOne.Application/Numbering/DocumentTypes.cs`:
```csharp
namespace ErpOne.Application.Numbering;

/// <summary>Key jenis dokumen untuk NumberSequence / IDocumentNumberService.</summary>
public static class DocumentTypes
{
    public const string PurchaseOrder = "PurchaseOrder";
    public const string SalesOrder    = "SalesOrder";
    public const string GoodsReceipt  = "GoodsReceipt";
    public const string DeliveryOrder = "DeliveryOrder";
    public const string PosSale       = "PosSale";
    public const string CashierShift  = "CashierShift";
}
```

- [ ] **Step 2: Register DbSets, configs, seed, and table prefixes in `AppDbContext`**

Add DbSets after the `Currencies` line from Task 2:
```csharp
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<NumberSequenceCounter> NumberSequenceCounters => Set<NumberSequenceCounter>();
```

Add configs inside `OnModelCreating`, after the `Currency` config block. Note the seed uses the **current** live formats so numbering is unchanged:
```csharp
        modelBuilder.Entity<NumberSequence>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
            e.Property(x => x.DateFormat).HasMaxLength(12);
            e.Property(x => x.Separator).HasMaxLength(3);
            e.Property(x => x.ResetPeriod).HasConversion<string>().HasMaxLength(10).IsRequired();

            var seedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                new { Id = 1, Code = "PurchaseOrder", Prefix = "PO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 2, Code = "SalesOrder",    Prefix = "SO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 3, Code = "GoodsReceipt",  Prefix = "GRN",   DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 4, Code = "DeliveryOrder", Prefix = "DO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 5, Code = "PosSale",       Prefix = "POS",   DateFormat = "yyyyMMdd", Padding = 4, ResetPeriod = ResetPeriod.Daily,   Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 6, Code = "CashierShift",  Prefix = "SHIFT", DateFormat = "yyyyMMdd", Padding = 4, ResetPeriod = ResetPeriod.Daily,   Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
            );
        });

        modelBuilder.Entity<NumberSequenceCounter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SequenceCode).HasMaxLength(40).IsRequired();
            e.Property(x => x.PeriodKey).HasMaxLength(12).IsRequired();
            e.HasIndex(x => new { x.SequenceCode, x.PeriodKey }).IsUnique();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
```

Add to `tablePrefixes` (Master section):
```csharp
            [nameof(NumberSequence)] = "M_",
            [nameof(NumberSequenceCounter)] = "M_",
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Domain/Entities/ResetPeriod.cs src/ErpOne.Domain/Entities/NumberSequence.cs src/ErpOne.Domain/Entities/NumberSequenceCounter.cs src/ErpOne.Application/Numbering/DocumentTypes.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: add NumberSequence + counter entities, config, and seed"
```

---

## Task 4: CompanySetting entity + EF config + seed

**Files:**
- Create: `src/ErpOne.Domain/Entities/CompanySetting.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Produces: `CompanySetting` (single row, `Id = 1`) with `Update(string? companyName, string? address, string? phone, string? email, string? taxId, string? logoUrl, string? receiptHeader, string? receiptFooter)`; props `Id, CompanyName, Address, Phone, Email, TaxId, LogoUrl, ReceiptHeader, ReceiptFooter`. `AppDbContext.CompanySettings`.

- [ ] **Step 1: Create the `CompanySetting` entity**

Create `src/ErpOne.Domain/Entities/CompanySetting.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Profil perusahaan (baris tunggal, Id = 1). Dipakai struk POS & cetakan.</summary>
public class CompanySetting : AuditableEntity
{
    public int Id { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? TaxId { get; private set; }         // NPWP
    public string? LogoUrl { get; private set; }
    public string? ReceiptHeader { get; private set; }
    public string? ReceiptFooter { get; private set; }

    private CompanySetting() { } // EF Core

    public void Update(string? companyName, string? address, string? phone, string? email,
        string? taxId, string? logoUrl, string? receiptHeader, string? receiptFooter)
    {
        CompanyName   = Trim(companyName);
        Address       = Trim(address);
        Phone         = Trim(phone);
        Email         = Trim(email);
        TaxId         = Trim(taxId);
        LogoUrl       = Trim(logoUrl);
        ReceiptHeader = Trim(receiptHeader);
        ReceiptFooter = Trim(receiptFooter);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

- [ ] **Step 2: Register DbSet, config, seed, table prefix in `AppDbContext`**

Add DbSet after the `NumberSequenceCounters` line:
```csharp
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
```

Add config inside `OnModelCreating`, after the `NumberSequenceCounter` config:
```csharp
        modelBuilder.Entity<CompanySetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CompanyName).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(400);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.TaxId).HasMaxLength(40);
            e.Property(x => x.LogoUrl).HasMaxLength(400);
            e.Property(x => x.ReceiptHeader).HasMaxLength(500);
            e.Property(x => x.ReceiptFooter).HasMaxLength(500);

            // Baris tunggal default agar service selalu punya row untuk di-update.
            e.HasData(new
            {
                Id = 1,
                CompanyName = (string?)"ERP_One",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });
```

Add to `tablePrefixes` (Master section):
```csharp
            [nameof(CompanySetting)] = "M_",
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Domain/Entities/CompanySetting.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: add CompanySetting single-row entity, config, and seed"
```

---

## Task 5: Generate the EF migration (production schema)

**Files:**
- Create: `src/ErpOne.Infrastructure/Persistence/Migrations/<timestamp>_AddF0Foundation.cs` (+ `.Designer.cs`, snapshot update) — generated by the EF tool.

**Interfaces:**
- Consumes: entities/configs from Tasks 2–4. Produces: migration that creates `M_Currencies`, `M_NumberSequences`, `M_NumberSequenceCounters`, `M_CompanySettings` with seed rows.

- [ ] **Step 1: Generate the migration**

Run:
```bash
dotnet ef migrations add AddF0Foundation --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web
```
Expected: "Done." with new files under `src/ErpOne.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 2: Inspect the generated migration**

Open the new `<timestamp>_AddF0Foundation.cs` and confirm `Up()` creates the four tables with `M_` prefixes and `InsertData` for the IDR currency, 6 number sequences, and the CompanySetting row. Confirm no unintended changes to other tables.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/Migrations/
git commit -m "feat: add EF migration for F0 foundation tables"
```

---

## Task 6: CurrencyService + DTOs + validators + DI + tests

**Files:**
- Create: `src/ErpOne.Application/Currencies/CurrencyDtos.cs`
- Create: `src/ErpOne.Application/Currencies/ICurrencyService.cs`
- Create: `src/ErpOne.Application/Currencies/CurrencyValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/CurrencyService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/CurrencyServiceTests.cs`

**Interfaces:**
- Consumes: `Currency` entity (Task 2).
- Produces:
  - `record CurrencyDto(int Id, string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive, DateTime CreatedAt, string? CreatedBy)`
  - `record CreateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive)`
  - `record UpdateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive)`
  - `ICurrencyService` with `GetAllAsync`, `GetActiveAsync`, `GetPagedAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`.

- [ ] **Step 1: Write the DTOs, interface, and validators**

Create `src/ErpOne.Application/Currencies/CurrencyDtos.cs`:
```csharp
namespace ErpOne.Application.Currencies;

public record CurrencyDto(int Id, string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive, DateTime CreatedAt, string? CreatedBy);
public record CreateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive);
public record UpdateCurrencyRequest(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive);
```

Create `src/ErpOne.Application/Currencies/ICurrencyService.cs`:
```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.Currencies;

public interface ICurrencyService
{
    Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CurrencyDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<CurrencyDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CurrencyDto> CreateAsync(CreateCurrencyRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateCurrencyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

Create `src/ErpOne.Application/Currencies/CurrencyValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.Currencies;

public class CreateCurrencyValidator : AbstractValidator<CreateCurrencyRequest>
{
    public CreateCurrencyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3)
            .Matches("^[A-Za-z]{3}$").WithMessage("Code must be a 3-letter ISO code.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(6);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, 6);
    }
}

public class UpdateCurrencyValidator : AbstractValidator<UpdateCurrencyRequest>
{
    public UpdateCurrencyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3)
            .Matches("^[A-Za-z]{3}$").WithMessage("Code must be a 3-letter ISO code.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(6);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, 6);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ErpOne.IntegrationTests/CurrencyServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Currencies;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CurrencyServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CurrencyServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_normalizes_code_and_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        var created = await svc.CreateAsync(new CreateCurrencyRequest("usd", "US Dollar", "$", 2, false, true));
        Assert.Equal("USD", created.Code);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("US Dollar", fetched!.Name);
    }

    [Fact]
    public async Task Setting_new_base_demotes_previous_base()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        // IDR (Id=1) is seeded as base. Create a new base.
        var eur = await svc.CreateAsync(new CreateCurrencyRequest("eur", "Euro", "€", 2, true, true));
        Assert.True(eur.IsBase);

        var idr = await svc.GetByIdAsync(1);
        Assert.False(idr!.IsBase); // previous base demoted
    }

    [Fact]
    public async Task Delete_base_currency_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICurrencyService>();

        // Ensure IDR (Id=1) is base, then try to delete it.
        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(1));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CurrencyServiceTests`
Expected: FAIL — `ICurrencyService` cannot be resolved / not registered.

- [ ] **Step 4: Implement `CurrencyService`**

Create `src/ErpOne.Infrastructure/Services/CurrencyService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Currencies;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CurrencyService(
    AppDbContext db,
    IValidator<CreateCurrencyRequest> createValidator,
    IValidator<UpdateCurrencyRequest> updateValidator) : ICurrencyService
{
    public async Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking().OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<IReadOnlyList<CurrencyDto>> GetActiveAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<CurrencyDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Currencies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<CurrencyDto>(items, total, page, pageSize);
    }

    public async Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<CurrencyDto> CreateAsync(CreateCurrencyRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);

        var entity = new Currency(code, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase, request.IsActive);
        db.Currencies.Add(entity);
        if (request.IsBase) await DemoteOtherBasesAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCurrencyRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Currencies.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        entity.Update(code, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase, request.IsActive);
        if (request.IsBase) await DemoteOtherBasesAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Currencies.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        if (entity.IsBase)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(UpdateCurrencyRequest.Code),
                    "The base currency cannot be deleted.")
            ]);

        db.Currencies.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task DemoteOtherBasesAsync(Currency keep, CancellationToken ct)
    {
        var others = await db.Currencies.Where(c => c.IsBase && c != keep).ToListAsync(ct);
        foreach (var o in others)
            o.Update(o.Code, o.Name, o.Symbol, o.DecimalPlaces, false, o.IsActive);
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.Currencies.AsNoTracking()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateCurrencyRequest.Code), $"Code '{code}' is already in use.")
            ]);
    }

    private static CurrencyDto ToDto(Currency x) =>
        new(x.Id, x.Code, x.Name, x.Symbol, x.DecimalPlaces, x.IsBase, x.IsActive, x.CreatedAt, x.CreatedBy);
}
```

> Note: `DemoteOtherBasesAsync` uses `c != keep` reference comparison on tracked entities; for the create path the new entity is already added to the context so it is excluded correctly.

- [ ] **Step 5: Register the service in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using and registration. Add near the other `using ErpOne.Application.*;` lines:
```csharp
using ErpOne.Application.Currencies;
```
Add after `services.AddScoped<IBrandService, BrandService>();`:
```csharp
        services.AddScoped<ICurrencyService, CurrencyService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CurrencyServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/Currencies/ src/ErpOne.Infrastructure/Services/CurrencyService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/CurrencyServiceTests.cs
git commit -m "feat: add CurrencyService with CRUD, base-currency invariant, and tests"
```

---

## Task 7: Currency web pages + menu + Supplier/Customer currency dropdown

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Master/Currencies/CurrencyIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Master/Currencies/CurrencyForm.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/Components/Pages/Master/Suppliers/SupplierForm.razor`
- Modify: `src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor`

**Interfaces:**
- Consumes: `ICurrencyService` (Task 6).

- [ ] **Step 1: Register the `master.currencies` resource in `AppMenus`**

In `src/ErpOne.Web/Authorization/AppMenus.cs`, add to the **Master** group (after the `master.customers` line ~49):
```csharp
            new("master.currencies", "Currency", "bi-currency-exchange", CRUD),
```

- [ ] **Step 2: Create the index page (`.pi`)**

Create `src/ErpOne.Web/Components/Pages/Master/Currencies/CurrencyIndex.razor`:
```razor
@page "/master/currencies"
@attribute [Authorize(Policy = "master.currencies.index")]
@rendermode InteractiveServer
@inject ICurrencyService CurrencyService
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>Currencies</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Master</span><span class="sep">·</span><span class="here">Currencies</span></nav>
            <h1>Currencies</h1>
            <p>Manage currencies used by suppliers, customers, and documents.</p>
        </div>
        <AuthorizeView Policy="master.currencies.create">
            <Authorized>
                <div class="pi-actions">
                    <a class="btn btn-primary" href="/master/currencies/new"><i class="bi bi-plus-lg"></i> Add currency</a>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <div class="search">
            <i class="bi bi-search"></i>
            <input placeholder="Search code or name…" @bind="_search" @bind:event="oninput" @onkeyup="OnSearchKeyUp" />
        </div>
    </div>

    @if (_page is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_page.Total == 0)
    {
        <div class="empty">
            <div class="empty-ic"><i class="bi bi-currency-exchange"></i></div>
            <p>
                @if (string.IsNullOrEmpty(_search))
                {
                    <span>No currencies yet. <a href="/master/currencies/new">Add the first one.</a></span>
                }
                else
                {
                    <span>No currencies match your search.</span>
                }
            </p>
        </div>
    }
    else
    {
        <div class="card">
            <div class="card-top">
                <span class="n">Showing <b>@((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total)</b> of <b>@_page.Total.ToString("N0")</b></span>
            </div>
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:120px">Code</th>
                            <th>Name</th>
                            <th style="width:90px">Symbol</th>
                            <th style="width:90px" class="text-center">Decimals</th>
                            <th style="width:90px" class="text-center">Base</th>
                            <th style="width:90px" class="text-center">Active</th>
                            <th class="text-end pe-3" style="width:120px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var item in _page.Items)
                        {
                            <tr>
                                <td class="code mono">@item.Code</td>
                                <td class="nm">@item.Name</td>
                                <td class="mono">@item.Symbol</td>
                                <td class="text-center">@item.DecimalPlaces</td>
                                <td class="text-center">@(item.IsBase ? "★" : "—")</td>
                                <td class="text-center">@(item.IsActive ? "✔" : "—")</td>
                                <td class="text-end pe-3 text-nowrap">
                                    <AuthorizeView Policy="master.currencies.edit">
                                        <Authorized>
                                            <a class="btn btn-sm btn-outline-primary me-1" href="@($"/master/currencies/{item.Id}/edit")" title="Edit"><i class="bi bi-pencil"></i></a>
                                        </Authorized>
                                    </AuthorizeView>
                                    <AuthorizeView Policy="master.currencies.delete">
                                        <Authorized>
                                            <button class="btn btn-sm btn-outline-danger" @onclick="() => DeleteAsync(item.Id, item.Name)" title="Delete"><i class="bi bi-trash3"></i></button>
                                        </Authorized>
                                    </AuthorizeView>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>

            @if (_page.TotalPages > 1)
            {
                <div class="card-foot">
                    <Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" />
                </div>
            }
        </div>
    }
</div>

@code {
    private const int PageSize = 15;
    private PagedResult<CurrencyDto>? _page;
    private int _currentPage = 1;
    private string? _search;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        _page = await CurrencyService.GetPagedAsync(_currentPage, PageSize, _search);

    private async Task OnSearchKeyUp(KeyboardEventArgs e)
    {
        _currentPage = 1;
        await LoadAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _currentPage = page;
        await LoadAsync();
    }

    private async Task DeleteAsync(int id, string name)
    {
        if (!await Swal.ConfirmAsync("Delete currency?", $"\"{name}\" will be permanently removed."))
            return;
        try
        {
            await CurrencyService.DeleteAsync(id);
            await LoadAsync();
            await Swal.ToastAsync("success", "Currency deleted");
        }
        catch (FluentValidation.ValidationException ex)
        {
            await Swal.ToastAsync("error", ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Cannot delete currency");
        }
    }
}
```

> Confirm the `@using` needed for `CurrencyDto`, `PagedResult`, `SwalService`, `Pager`, `AppMenus`, and `KeyboardEventArgs` are covered by `_Imports.razor` (they are for the other master pages — Brand's index uses the same set without local `@using`). If the build reports a missing type, add the corresponding `@using` line at the top matching `BrandIndex.razor`'s resolution.

- [ ] **Step 3: Create the form page (`.cf`)**

Create `src/ErpOne.Web/Components/Pages/Master/Currencies/CurrencyForm.razor`:
```razor
@page "/master/currencies/new"
@page "/master/currencies/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@inject ICurrencyService CurrencyService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs">
            <a href="/master/currencies">Master</a>
            <i class="bi bi-chevron-right"></i>
            <a href="/master/currencies">Currencies</a>
            <i class="bi bi-chevron-right"></i>
            <span class="here">@(Id is null ? "New" : "Edit")</span>
        </div>
        <h1>@Title</h1>
    </div>

    @if (_loading)
    {
        <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
    }
    else if (_notFound)
    {
        <div class="cf-alert warn"><i class="bi bi-exclamation-triangle"></i> Currency not found.</div>
    }
    else
    {
        @if (_error is not null)
        {
            <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div>
        }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h">
                    <span class="hd-ic"><i class="bi bi-currency-exchange"></i></span>
                    <div class="hd-tx">
                        <h2>Currency information</h2>
                        <p>A currency that documents and partners can be denominated in.</p>
                    </div>
                </div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c3">
                            <label class="fl">Code <span class="req">*</span></label>
                            <input class="ctl mono up @(_codeError is not null ? "bad" : "")"
                                   placeholder="IDR" maxlength="3" @bind="_code" @bind:event="oninput" />
                            @if (_codeError is not null) { <div class="err-txt">@_codeError</div> }
                        </div>
                        <div class="f c6">
                            <label class="fl">Name <span class="req">*</span></label>
                            <input class="ctl @(_nameError is not null ? "bad" : "")"
                                   placeholder="Rupiah" maxlength="60" @bind="_name" @bind:event="oninput" />
                            @if (_nameError is not null) { <div class="err-txt">@_nameError</div> }
                        </div>
                        <div class="f c3">
                            <label class="fl">Symbol <span class="req">*</span></label>
                            <input class="ctl @(_symbolError is not null ? "bad" : "")"
                                   placeholder="Rp" maxlength="6" @bind="_symbol" @bind:event="oninput" />
                            @if (_symbolError is not null) { <div class="err-txt">@_symbolError</div> }
                        </div>
                        <div class="f c3">
                            <label class="fl">Decimal places</label>
                            <input type="number" min="0" max="6" class="ctl" @bind="_decimalPlaces" />
                        </div>
                        <div class="f c9 d-flex align-items-end gap-4">
                            <label class="fl-check"><input type="checkbox" @bind="_isBase" /> Base currency</label>
                            <label class="fl-check"><input type="checkbox" @bind="_isActive" /> Active</label>
                        </div>
                    </div>
                </div>
            </section>

            <div class="pf-footer">
                <div class="in">
                    <span class="note"><span class="req">*</span> required fields</span>
                    <a class="btn btn-ghost" href="/master/currencies"><i class="bi bi-x-lg"></i> Cancel</a>
                    <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving">
                        @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
                        else { <i class="bi bi-check2"></i> }
                        Save currency
                    </button>
                </div>
            </div>
        </div>
    }
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _symbol = string.Empty;
    private int _decimalPlaces = 2;
    private bool _isBase, _isActive = true;
    private bool _loading = true, _saving, _notFound;
    private string? _error, _codeError, _nameError, _symbolError;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private string Title => Id is null ? "Add Currency" : "Edit Currency";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null ? AppMenus.Perm("master.currencies", "create") : AppMenus.Perm("master.currencies", "edit");
        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded)
        {
            Nav.NavigateTo("/master/currencies");
            return;
        }

        if (Id is int id)
        {
            var c = await CurrencyService.GetByIdAsync(id);
            if (c is null) _notFound = true;
            else
            {
                _code = c.Code; _name = c.Name; _symbol = c.Symbol;
                _decimalPlaces = c.DecimalPlaces; _isBase = c.IsBase; _isActive = c.IsActive;
            }
        }
        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = _codeError = _nameError = _symbolError = null;
        _saving = true;
        try
        {
            if (Id is int id)
                await CurrencyService.UpdateAsync(id, new UpdateCurrencyRequest(_code, _name, _symbol, _decimalPlaces, _isBase, _isActive));
            else
                await CurrencyService.CreateAsync(new CreateCurrencyRequest(_code, _name, _symbol, _decimalPlaces, _isBase, _isActive));

            Nav.NavigateTo("/master/currencies");
        }
        catch (ValidationException ex)
        {
            foreach (var e in ex.Errors)
            {
                if (e.PropertyName == nameof(CreateCurrencyRequest.Code)) _codeError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateCurrencyRequest.Name)) _nameError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateCurrencyRequest.Symbol)) _symbolError = e.ErrorMessage;
                else _error = e.ErrorMessage;
            }
        }
        finally { _saving = false; }
    }
}
```

> If `.fl-check` isn't an existing utility class, the checkbox still renders; styling is cosmetic and can be aligned in a follow-up. Verify visually in Task 7 Step 6.

- [ ] **Step 4: Swap Supplier's currency free-text for a dropdown**

In `src/ErpOne.Web/Components/Pages/Master/Suppliers/SupplierForm.razor`:

Add the service injection near the other `@inject` lines at the top:
```razor
@inject ICurrencyService CurrencyService
```

Replace the currency input (currently at ~line 116):
```razor
                            <input class="ctl mono up" maxlength="3" placeholder="IDR" @bind="_currency" />
```
with:
```razor
                            <select class="ctl" @bind="_currency">
                                @foreach (var c in _currencies)
                                {
                                    <option value="@c.Code">@c.Code — @c.Name</option>
                                }
                            </select>
```

Add a backing field near `private string _currency = "IDR";` (~line 169):
```csharp
    private IReadOnlyList<ErpOne.Application.Currencies.CurrencyDto> _currencies = [];
```

Load currencies at the start of `OnInitializedAsync` (before the existing load logic):
```csharp
        _currencies = await CurrencyService.GetActiveAsync();
```
> If the loaded supplier's stored `DefaultCurrency` isn't among active currencies, the `<select>` shows no selection; that's acceptable — the admin re-picks. Keep `_currency` defaulting to `"IDR"` for the new-supplier case.

- [ ] **Step 5: Swap Customer's currency free-text for a dropdown**

Apply the exact same change as Step 4 to `src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor`: add `@inject ICurrencyService CurrencyService`, replace the `DefaultCurrency` `<input>` with the same `<select>` block bound to the customer form's currency field, add the `_currencies` field, and load `await CurrencyService.GetActiveAsync()` at the start of `OnInitializedAsync`.
> First open `CustomerForm.razor` and confirm the currency field name (it mirrors Supplier's `_currency`). If it differs, bind the `<select>` to whatever field the customer form uses for `DefaultCurrency`.

- [ ] **Step 6: Build, run, and verify**

Run: `dotnet build`
Expected: Build succeeded.

Then run the app (`dotnet run --project src/ErpOne.Web`), log in as admin, and verify:
- `/master/currencies` lists IDR (base ★).
- Add a currency (e.g. USD), edit it, and delete a non-base one.
- Supplier and Customer forms show the currency dropdown.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Master/Currencies/ src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/Pages/Master/Suppliers/SupplierForm.razor src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor
git commit -m "feat: add Currency master pages, menu, and supplier/customer currency dropdowns"
```

---

## Task 8: IDocumentNumberService + DI + tests

**Files:**
- Create: `src/ErpOne.Application/Numbering/IDocumentNumberService.cs`
- Create: `src/ErpOne.Infrastructure/Services/DocumentNumberService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/DocumentNumberServiceTests.cs`

**Interfaces:**
- Consumes: `NumberSequence`, `NumberSequenceCounter`, `DocumentTypes` (Task 3); the six document DbSets on `AppDbContext`.
- Produces: `IDocumentNumberService.NextAsync(string code, DateTime docDate, CancellationToken ct = default) : Task<string>`.

- [ ] **Step 1: Write the service interface**

Create `src/ErpOne.Application/Numbering/IDocumentNumberService.cs`:
```csharp
namespace ErpOne.Application.Numbering;

/// <summary>Penomoran dokumen terpusat, race-safe, dengan format per NumberSequence.</summary>
public interface IDocumentNumberService
{
    /// <summary>Alokasikan nomor berikutnya untuk jenis dokumen <paramref name="code"/> (lihat DocumentTypes).</summary>
    Task<string> NextAsync(string code, DateTime docDate, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ErpOne.IntegrationTests/DocumentNumberServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Numbering;
using Xunit;

namespace ErpOne.IntegrationTests;

public class DocumentNumberServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DocumentNumberServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Monthly_format_matches_legacy_PO()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var n = await svc.NextAsync(DocumentTypes.PurchaseOrder, new DateTime(2026, 6, 24));
        Assert.Matches(@"^PO-202606-\d{4}$", n);
    }

    [Fact]
    public async Task Daily_format_matches_legacy_POS()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var n = await svc.NextAsync(DocumentTypes.PosSale, new DateTime(2026, 7, 10, 9, 0, 0));
        Assert.Matches(@"^POS-20260710-\d{4}$", n);
    }

    [Fact]
    public async Task Sequential_calls_same_period_increment_and_are_unique()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var d = new DateTime(2026, 8, 1);
        var a = await svc.NextAsync(DocumentTypes.SalesOrder, d);
        var b = await svc.NextAsync(DocumentTypes.SalesOrder, d);
        var c = await svc.NextAsync(DocumentTypes.SalesOrder, d);

        Assert.Equal(new[] { a, b, c }.Distinct().Count(), 3);
        // last 4 digits strictly increasing
        int Seq(string s) => int.Parse(s[^4..]);
        Assert.True(Seq(a) < Seq(b) && Seq(b) < Seq(c));
    }

    [Fact]
    public async Task Unknown_code_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NextAsync("NopeDoc", DateTime.UtcNow));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter DocumentNumberServiceTests`
Expected: FAIL — `IDocumentNumberService` not registered.

- [ ] **Step 4: Implement `DocumentNumberService`**

Create `src/ErpOne.Infrastructure/Services/DocumentNumberService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class DocumentNumberService(AppDbContext db) : IDocumentNumberService
{
    private const int MaxAttempts = 6;

    public async Task<string> NextAsync(string code, DateTime docDate, CancellationToken ct = default)
    {
        var seq = await db.NumberSequences.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct)
            ?? throw new InvalidOperationException($"Number sequence '{code}' is not configured.");

        var periodKey = PeriodKeyFor(seq.ResetPeriod, docDate);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var counter = await db.NumberSequenceCounters
                .FirstOrDefaultAsync(c => c.SequenceCode == code && c.PeriodKey == periodKey, ct);

            int value;
            if (counter is null)
            {
                var start = await BackfillStartAsync(seq, periodKey, ct);
                counter = new NumberSequenceCounter(code, periodKey, start);
                value = counter.Next();
                db.NumberSequenceCounters.Add(counter);
            }
            else
            {
                value = counter.Next();
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return Format(seq, docDate, value);
            }
            catch (DbUpdateException)
            {
                // Concurrency conflict (token mismatch) or unique-insert race — reset and retry.
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException($"Could not allocate a number for '{code}' after {MaxAttempts} attempts.");
    }

    private static string PeriodKeyFor(ResetPeriod p, DateTime d) => p switch
    {
        ResetPeriod.Daily   => d.ToString("yyyyMMdd"),
        ResetPeriod.Monthly => d.ToString("yyyyMM"),
        ResetPeriod.Yearly  => d.ToString("yyyy"),
        _                   => "ALL"
    };

    private static string Format(NumberSequence seq, DateTime docDate, int value)
    {
        var datePart = string.IsNullOrEmpty(seq.DateFormat)
            ? ""
            : docDate.ToString(seq.DateFormat) + seq.Separator;
        return $"{seq.Prefix}{seq.Separator}{datePart}{value.ToString().PadLeft(seq.Padding, '0')}";
    }

    /// <summary>Untuk kontinuitas dengan dokumen lama: cari nomor terakhir existing pada prefix+period
    /// dan kembalikan nilai numeriknya (0 bila tak ada). Counter di-Next() dari nilai ini.</summary>
    private async Task<int> BackfillStartAsync(NumberSequence seq, string periodKey, CancellationToken ct)
    {
        var datePart = string.IsNullOrEmpty(seq.DateFormat) ? "" : periodKey + seq.Separator;
        var prefix = $"{seq.Prefix}{seq.Separator}{datePart}";

        string? last = seq.Code switch
        {
            DocumentTypes.PurchaseOrder => await MaxAsync(db.PurchaseOrders.Select(x => x.PoNumber), prefix, ct),
            DocumentTypes.SalesOrder    => await MaxAsync(db.SalesOrders.Select(x => x.SoNumber), prefix, ct),
            DocumentTypes.GoodsReceipt  => await MaxAsync(db.GoodsReceipts.Select(x => x.GrnNumber), prefix, ct),
            DocumentTypes.DeliveryOrder => await MaxAsync(db.DeliveryOrders.Select(x => x.DoNumber), prefix, ct),
            DocumentTypes.PosSale       => await MaxAsync(db.PosSales.Select(x => x.SaleNumber), prefix, ct),
            DocumentTypes.CashierShift  => await MaxAsync(db.CashierShifts.Select(x => x.ShiftNumber), prefix, ct),
            _                           => null
        };

        if (last is null || last.Length <= prefix.Length) return 0;
        return int.TryParse(last[prefix.Length..], out var n) ? n : 0;
    }

    private static async Task<string?> MaxAsync(IQueryable<string> numbers, string prefix, CancellationToken ct) =>
        await numbers.AsNoTracking()
            .Where(v => v.StartsWith(prefix))
            .OrderByDescending(v => v)
            .FirstOrDefaultAsync(ct);
}
```

> `.AsNoTracking()` on a projected `IQueryable<string>` is valid in EF Core. If the compiler rejects `AsNoTracking()` placement, move it before `.Select(...)` at each call site instead.

- [ ] **Step 5: Register the service in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using:
```csharp
using ErpOne.Application.Numbering;
```
Add after the `ICurrencyService` registration:
```csharp
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter DocumentNumberServiceTests`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/Numbering/IDocumentNumberService.cs src/ErpOne.Infrastructure/Services/DocumentNumberService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/DocumentNumberServiceTests.cs
git commit -m "feat: add centralized race-safe IDocumentNumberService with tests"
```

---

## Task 9: Retrofit all six services to use IDocumentNumberService

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/PurchaseOrderService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/SalesOrderService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/GoodsReceiptService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/DeliveryOrderService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/PosSaleService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/CashierShiftService.cs`

**Interfaces:**
- Consumes: `IDocumentNumberService.NextAsync` (Task 8), `DocumentTypes` (Task 3).

The existing per-service integration tests (`PurchaseOrderServiceTests`, etc.) already assert the number format (e.g. `Assert.StartsWith("PO-202606-", ...)`) — they are the regression suite for this task. Do NOT change them.

- [ ] **Step 1: Retrofit `PurchaseOrderService`**

In `src/ErpOne.Infrastructure/Services/PurchaseOrderService.cs`:

Add the constructor dependency. The class uses a primary constructor; add `IDocumentNumberService docNumbers` to its parameter list, and add `using ErpOne.Application.Numbering;` at the top. For example the declaration becomes:
```csharp
public class PurchaseOrderService(
    AppDbContext db,
    /* ...existing params... */
    IDocumentNumberService docNumbers) : IPurchaseOrderService
```
> Open the file and append `IDocumentNumberService docNumbers` as the last primary-constructor parameter, preserving all existing parameters exactly.

Replace the call site (line ~117):
```csharp
        var poNumber = await GenerateNumberAsync(request.OrderDate, ct);
```
with:
```csharp
        var poNumber = await docNumbers.NextAsync(DocumentTypes.PurchaseOrder, request.OrderDate, ct);
```

Delete the now-unused private method `GenerateNumberAsync` (lines ~228–240).

- [ ] **Step 2: Run PO regression test**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter PurchaseOrderServiceTests`
Expected: PASS — including `Create_generates_number_and_totals` (`PO-202606-` prefix preserved).

- [ ] **Step 3: Retrofit `SalesOrderService`**

In `src/ErpOne.Infrastructure/Services/SalesOrderService.cs`: add `using ErpOne.Application.Numbering;`, append `IDocumentNumberService docNumbers` to the primary constructor, replace the call site (line ~138):
```csharp
        var soNumber = await GenerateNumberAsync(request.OrderDate, ct);
```
with:
```csharp
        var soNumber = await docNumbers.NextAsync(DocumentTypes.SalesOrder, request.OrderDate, ct);
```
Delete the private `GenerateNumberAsync` (lines ~249+).

- [ ] **Step 4: Retrofit `GoodsReceiptService`**

In `src/ErpOne.Infrastructure/Services/GoodsReceiptService.cs`: add the using, append the ctor param, replace the inline call (line ~164):
```csharp
        var grn = new GoodsReceipt(await GenerateNumberAsync(request.ReceiptDate, ct),
```
with:
```csharp
        var grn = new GoodsReceipt(await docNumbers.NextAsync(DocumentTypes.GoodsReceipt, request.ReceiptDate, ct),
```
Delete the private `GenerateNumberAsync` (lines ~278+).

- [ ] **Step 5: Retrofit `DeliveryOrderService`**

In `src/ErpOne.Infrastructure/Services/DeliveryOrderService.cs`: add the using, append the ctor param, replace the inline call (line ~160):
```csharp
        var doc = new DeliveryOrder(await GenerateNumberAsync(request.DeliveryDate, ct),
```
with:
```csharp
        var doc = new DeliveryOrder(await docNumbers.NextAsync(DocumentTypes.DeliveryOrder, request.DeliveryDate, ct),
```
Delete the private `GenerateNumberAsync` (lines ~284+).

- [ ] **Step 6: Retrofit `PosSaleService`**

In `src/ErpOne.Infrastructure/Services/PosSaleService.cs`: add the using, append the ctor param, replace the inline call (line ~83):
```csharp
        var sale = new PosSale(await GenerateNumberAsync(now, ct), shift.Id, whId, now,
```
with:
```csharp
        var sale = new PosSale(await docNumbers.NextAsync(DocumentTypes.PosSale, now, ct), shift.Id, whId, now,
```
Delete the private `GenerateNumberAsync` (lines ~159+).

- [ ] **Step 7: Retrofit `CashierShiftService`**

In `src/ErpOne.Infrastructure/Services/CashierShiftService.cs`: add the using, append the ctor param, replace the inline call (line ~54):
```csharp
        var shift = new CashierShift(await GenerateNumberAsync(now, ct),
```
with:
```csharp
        var shift = new CashierShift(await docNumbers.NextAsync(DocumentTypes.CashierShift, now, ct),
```
Delete the private `GenerateNumberAsync` (lines ~140+).

- [ ] **Step 8: Run the full regression suite for all six modules**

Run:
```bash
dotnet test tests/ErpOne.IntegrationTests --filter "PurchaseOrderServiceTests|SalesOrderServiceTests|GoodsReceiptServiceTests|DeliveryOrderServiceTests|PosSaleServiceTests|CashierShiftServiceTests"
```
Expected: PASS — all existing number-format assertions still green (formats unchanged).

- [ ] **Step 9: Build and full test run**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/PurchaseOrderService.cs src/ErpOne.Infrastructure/Services/SalesOrderService.cs src/ErpOne.Infrastructure/Services/GoodsReceiptService.cs src/ErpOne.Infrastructure/Services/DeliveryOrderService.cs src/ErpOne.Infrastructure/Services/PosSaleService.cs src/ErpOne.Infrastructure/Services/CashierShiftService.cs
git commit -m "refactor: route all document numbering through IDocumentNumberService"
```

---

## Task 10: Document-numbering config page + menu

**Files:**
- Create: `src/ErpOne.Application/Numbering/NumberSequenceDtos.cs`
- Create: `src/ErpOne.Application/Numbering/INumberSequenceService.cs`
- Create: `src/ErpOne.Infrastructure/Services/NumberSequenceService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `src/ErpOne.Web/Components/Pages/Settings/NumberSequences/NumberSequenceIndex.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Test: `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`

**Interfaces:**
- Consumes: `NumberSequence` (Task 3).
- Produces:
  - `record NumberSequenceDto(int Id, string Code, string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator, string Sample)`
  - `record UpdateNumberSequenceRequest(string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator)`
  - `INumberSequenceService` with `GetAllAsync`, `GetByIdAsync`, `UpdateAsync`.

- [ ] **Step 1: Write DTOs and interface**

Create `src/ErpOne.Application/Numbering/NumberSequenceDtos.cs`:
```csharp
namespace ErpOne.Application.Numbering;

public record NumberSequenceDto(int Id, string Code, string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator, string Sample);
public record UpdateNumberSequenceRequest(string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator);
```

Create `src/ErpOne.Application/Numbering/INumberSequenceService.cs`:
```csharp
namespace ErpOne.Application.Numbering;

public interface INumberSequenceService
{
    Task<IReadOnlyList<NumberSequenceDto>> GetAllAsync(CancellationToken ct = default);
    Task<NumberSequenceDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateNumberSequenceRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Numbering;
using Xunit;

namespace ErpOne.IntegrationTests;

public class NumberSequenceServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public NumberSequenceServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task GetAll_returns_six_seeded_sequences_with_samples()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();

        var all = await svc.GetAllAsync();
        Assert.Equal(6, all.Count);
        var po = all.Single(x => x.Code == DocumentTypes.PurchaseOrder);
        Assert.StartsWith("PO-", po.Sample);
    }

    [Fact]
    public async Task Update_changes_padding()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();

        var po = (await svc.GetAllAsync()).Single(x => x.Code == DocumentTypes.PurchaseOrder);
        var ok = await svc.UpdateAsync(po.Id, new UpdateNumberSequenceRequest("PO", "yyyyMM", 6, "Monthly", "-"));
        Assert.True(ok);

        var updated = await svc.GetByIdAsync(po.Id);
        Assert.Equal(6, updated!.Padding);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter NumberSequenceServiceTests`
Expected: FAIL — `INumberSequenceService` not registered.

- [ ] **Step 4: Implement `NumberSequenceService`**

Create `src/ErpOne.Infrastructure/Services/NumberSequenceService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class NumberSequenceService(AppDbContext db) : INumberSequenceService
{
    public async Task<IReadOnlyList<NumberSequenceDto>> GetAllAsync(CancellationToken ct = default) =>
        (await db.NumberSequences.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<NumberSequenceDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.NumberSequences.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<bool> UpdateAsync(int id, UpdateNumberSequenceRequest request, CancellationToken ct = default)
    {
        var entity = await db.NumberSequences.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        if (!Enum.TryParse<ResetPeriod>(request.ResetPeriod, out var reset))
            reset = entity.ResetPeriod;

        entity.Update(request.Prefix, request.DateFormat, request.Padding, reset, request.Separator);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static NumberSequenceDto ToDto(NumberSequence x)
    {
        // Sample memakai tanggal statik agar deterministik (bukan DateTime.Now).
        var sampleDate = new DateTime(2026, 1, 1);
        var datePart = string.IsNullOrEmpty(x.DateFormat) ? "" : sampleDate.ToString(x.DateFormat) + x.Separator;
        var sample = $"{x.Prefix}{x.Separator}{datePart}{1.ToString().PadLeft(x.Padding, '0')}";
        return new NumberSequenceDto(x.Id, x.Code, x.Prefix, x.DateFormat, x.Padding, x.ResetPeriod.ToString(), x.Separator, sample);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add after the `IDocumentNumberService` registration:
```csharp
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter NumberSequenceServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Register the `settings.document-numbering` resource in `AppMenus`**

In `src/ErpOne.Web/Authorization/AppMenus.cs`, in the **Settings** group (after `settings.approval-chains` ~line 73), add. Use edit-only actions (view + edit; no create/delete — the 6 rows are fixed):
```csharp
            new("settings.document-numbering", "Document Numbering", "bi-123", [ActIndex, ActEdit]),
```

- [ ] **Step 8: Create the config index/edit page**

Create `src/ErpOne.Web/Components/Pages/Settings/NumberSequences/NumberSequenceIndex.razor`. This single page lists the six sequences with inline edit of prefix/date-format/padding/reset:
```razor
@page "/settings/document-numbering"
@attribute [Authorize(Policy = "settings.document-numbering.index")]
@rendermode InteractiveServer
@inject INumberSequenceService SeqService
@inject IAuthorizationService Auth
@inject SwalService Swal

<PageTitle>Document Numbering</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Settings</span><span class="sep">·</span><span class="here">Document Numbering</span></nav>
            <h1>Document Numbering</h1>
            <p>Configure the number format and reset cycle for each document type.</p>
        </div>
    </div>

    @if (_rows is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>Document</th>
                            <th style="width:110px">Prefix</th>
                            <th style="width:150px">Date format</th>
                            <th style="width:110px">Padding</th>
                            <th style="width:150px">Reset</th>
                            <th style="width:220px">Sample</th>
                            <th class="text-end pe-3" style="width:90px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var r in _rows)
                        {
                            <tr>
                                <td class="nm">@r.Code</td>
                                <td><input class="ctl mono up" maxlength="10" @bind="r.Prefix" disabled="@(!_canEdit)" /></td>
                                <td>
                                    <select class="ctl" @bind="r.DateFormat" disabled="@(!_canEdit)">
                                        <option value="">(none)</option>
                                        <option value="yyyyMM">yyyyMM</option>
                                        <option value="yyyyMMdd">yyyyMMdd</option>
                                        <option value="yyyy">yyyy</option>
                                    </select>
                                </td>
                                <td><input type="number" min="1" max="10" class="ctl" @bind="r.Padding" disabled="@(!_canEdit)" /></td>
                                <td>
                                    <select class="ctl" @bind="r.ResetPeriod" disabled="@(!_canEdit)">
                                        <option value="Never">Never</option>
                                        <option value="Daily">Daily</option>
                                        <option value="Monthly">Monthly</option>
                                        <option value="Yearly">Yearly</option>
                                    </select>
                                </td>
                                <td class="code mono">@r.Sample</td>
                                <td class="text-end pe-3">
                                    @if (_canEdit)
                                    {
                                        <button class="btn btn-sm btn-outline-primary" @onclick="() => SaveAsync(r)" title="Save"><i class="bi bi-check2"></i></button>
                                    }
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private List<Row>? _rows;
    private bool _canEdit;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private sealed class Row
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string DateFormat { get; set; } = "";
        public int Padding { get; set; }
        public string ResetPeriod { get; set; } = "Monthly";
        public string Separator { get; set; } = "-";
        public string Sample { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        _canEdit = (await Auth.AuthorizeAsync(state.User, AppMenus.Perm("settings.document-numbering", "edit"))).Succeeded;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var all = await SeqService.GetAllAsync();
        _rows = all.Select(x => new Row
        {
            Id = x.Id, Code = x.Code, Prefix = x.Prefix, DateFormat = x.DateFormat,
            Padding = x.Padding, ResetPeriod = x.ResetPeriod, Separator = x.Separator, Sample = x.Sample
        }).ToList();
    }

    private async Task SaveAsync(Row r)
    {
        await SeqService.UpdateAsync(r.Id, new UpdateNumberSequenceRequest(r.Prefix, r.DateFormat, r.Padding, r.ResetPeriod, r.Separator));
        await LoadAsync();
        await Swal.ToastAsync("success", $"{r.Code} numbering updated");
    }
}
```

- [ ] **Step 9: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

Run the app, open `/settings/document-numbering`, change PO padding to 6, save, and confirm the Sample updates. (Optionally create a new PO and confirm the new format, then revert padding to 4.)

- [ ] **Step 10: Commit**

```bash
git add src/ErpOne.Application/Numbering/NumberSequenceDtos.cs src/ErpOne.Application/Numbering/INumberSequenceService.cs src/ErpOne.Infrastructure/Services/NumberSequenceService.cs src/ErpOne.Infrastructure/DependencyInjection.cs src/ErpOne.Web/Components/Pages/Settings/NumberSequences/ src/ErpOne.Web/Authorization/AppMenus.cs tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs
git commit -m "feat: add document-numbering config page and service"
```

---

## Task 11: CompanySettingService + DI + tests

**Files:**
- Create: `src/ErpOne.Application/CompanySettings/CompanySettingDtos.cs`
- Create: `src/ErpOne.Application/CompanySettings/ICompanySettingService.cs`
- Create: `src/ErpOne.Infrastructure/Services/CompanySettingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/CompanySettingServiceTests.cs`

**Interfaces:**
- Consumes: `CompanySetting` (Task 4).
- Produces:
  - `record CompanySettingDto(int Id, string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter)`
  - `record UpdateCompanySettingRequest(string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter)`
  - `ICompanySettingService` with `GetAsync(CancellationToken) : Task<CompanySettingDto>` and `UpdateAsync(UpdateCompanySettingRequest, CancellationToken) : Task`.

- [ ] **Step 1: Write DTOs and interface**

Create `src/ErpOne.Application/CompanySettings/CompanySettingDtos.cs`:
```csharp
namespace ErpOne.Application.CompanySettings;

public record CompanySettingDto(int Id, string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter);
public record UpdateCompanySettingRequest(string? CompanyName, string? Address, string? Phone, string? Email, string? TaxId, string? LogoUrl, string? ReceiptHeader, string? ReceiptFooter);
```

Create `src/ErpOne.Application/CompanySettings/ICompanySettingService.cs`:
```csharp
namespace ErpOne.Application.CompanySettings;

public interface ICompanySettingService
{
    /// <summary>Ambil profil perusahaan; buat baris default (Id=1) bila belum ada.</summary>
    Task<CompanySettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(UpdateCompanySettingRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ErpOne.IntegrationTests/CompanySettingServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CompanySettings;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CompanySettingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CompanySettingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Get_returns_seeded_single_row()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompanySettingService>();

        var s = await svc.GetAsync();
        Assert.Equal(1, s.Id);
    }

    [Fact]
    public async Task Update_then_Get_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompanySettingService>();

        await svc.UpdateAsync(new UpdateCompanySettingRequest(
            "Toko Maju", "Jl. Merdeka 1", "021-555", "hi@maju.co", "01.234.567.8-000",
            null, "Selamat datang", "Terima kasih"));

        var s = await svc.GetAsync();
        Assert.Equal("Toko Maju", s.CompanyName);
        Assert.Equal("Terima kasih", s.ReceiptFooter);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CompanySettingServiceTests`
Expected: FAIL — `ICompanySettingService` not registered.

- [ ] **Step 4: Implement `CompanySettingService`**

The `CompanySetting` parameterless ctor is `private` (EF-only), so the service never constructs one — it relies on the `HasData` seed row (Id=1), which exists in both prod (migration) and tests (`EnsureCreated`). If the row is missing, fail loudly.

Create `src/ErpOne.Infrastructure/Services/CompanySettingService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.CompanySettings;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CompanySettingService(AppDbContext db) : ICompanySettingService
{
    public async Task<CompanySettingDto> GetAsync(CancellationToken ct = default)
    {
        var entity = await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CompanySetting seed row (Id=1) is missing.");
        return ToDto(entity);
    }

    public async Task UpdateAsync(UpdateCompanySettingRequest request, CancellationToken ct = default)
    {
        var entity = await db.CompanySettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CompanySetting seed row (Id=1) is missing.");

        entity.Update(request.CompanyName, request.Address, request.Phone, request.Email,
            request.TaxId, request.LogoUrl, request.ReceiptHeader, request.ReceiptFooter);
        await db.SaveChangesAsync(ct);
    }

    private static CompanySettingDto ToDto(CompanySetting x) =>
        new(x.Id, x.CompanyName, x.Address, x.Phone, x.Email, x.TaxId, x.LogoUrl, x.ReceiptHeader, x.ReceiptFooter);
}
```

- [ ] **Step 5: Register in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using:
```csharp
using ErpOne.Application.CompanySettings;
```
Add after the `INumberSequenceService` registration:
```csharp
        services.AddScoped<ICompanySettingService, CompanySettingService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CompanySettingServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/CompanySettings/ src/ErpOne.Infrastructure/Services/CompanySettingService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/CompanySettingServiceTests.cs
git commit -m "feat: add CompanySettingService (single-row get-or-fail) with tests"
```

---

## Task 12: Company Settings page + logo upload + menu

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Settings/Company/CompanySettingForm.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`

**Interfaces:**
- Consumes: `ICompanySettingService` (Task 11), `IFileStorage.SaveAsync(Stream, string originalFileName, string subFolder, CancellationToken) : Task<StoredFile>` (existing, `StoredFile.RelativePath`).

- [ ] **Step 1: Register the `settings.company` resource in `AppMenus`**

In `src/ErpOne.Web/Authorization/AppMenus.cs`, in the **Settings** group, add (view + edit only):
```csharp
            new("settings.company", "Company Profile", "bi-building-fill-gear", [ActIndex, ActEdit]),
```

- [ ] **Step 2: Create the Company Settings form page**

Create `src/ErpOne.Web/Components/Pages/Settings/Company/CompanySettingForm.razor`:
```razor
@page "/settings/company"
@attribute [Authorize(Policy = "settings.company.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@inject ICompanySettingService CompanyService
@inject IFileStorage FileStorage
@inject IAuthorizationService Auth
@inject SwalService Swal

<PageTitle>Company Profile</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs">
            <span>Settings</span><i class="bi bi-chevron-right"></i><span class="here">Company Profile</span>
        </div>
        <h1>Company Profile</h1>
    </div>

    @if (_loading)
    {
        <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
    }
    else
    {
        @if (_error is not null)
        {
            <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div>
        }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h">
                    <span class="hd-ic"><i class="bi bi-building-fill"></i></span>
                    <div class="hd-tx">
                        <h2>Company information</h2>
                        <p>Shown on the POS receipt and printed documents.</p>
                    </div>
                </div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c8">
                            <label class="fl">Company name</label>
                            <input class="ctl" maxlength="200" @bind="_companyName" />
                        </div>
                        <div class="f c4">
                            <label class="fl">Tax ID (NPWP)</label>
                            <input class="ctl" maxlength="40" @bind="_taxId" />
                        </div>
                        <div class="f c12">
                            <label class="fl">Address</label>
                            <textarea class="ctl" rows="2" maxlength="400" @bind="_address"></textarea>
                        </div>
                        <div class="f c6">
                            <label class="fl">Phone</label>
                            <input class="ctl" maxlength="40" @bind="_phone" />
                        </div>
                        <div class="f c6">
                            <label class="fl">Email</label>
                            <input class="ctl" maxlength="120" @bind="_email" />
                        </div>
                        <div class="f c12">
                            <label class="fl">Logo</label>
                            @if (!string.IsNullOrEmpty(_logoUrl))
                            {
                                <div class="mb-2"><img src="@_logoUrl" alt="Logo" style="max-height:64px" /></div>
                            }
                            <InputFile OnChange="OnLogoSelected" accept="image/*" disabled="@(!_canEdit)" />
                        </div>
                        <div class="f c6">
                            <label class="fl">Receipt header</label>
                            <textarea class="ctl" rows="2" maxlength="500" @bind="_receiptHeader" placeholder="Text above the receipt items"></textarea>
                        </div>
                        <div class="f c6">
                            <label class="fl">Receipt footer</label>
                            <textarea class="ctl" rows="2" maxlength="500" @bind="_receiptFooter" placeholder="e.g. Thank you for your visit"></textarea>
                        </div>
                    </div>
                </div>
            </section>

            <div class="pf-footer">
                <div class="in">
                    <a class="btn btn-ghost" href="/"><i class="bi bi-x-lg"></i> Close</a>
                    <button class="btn btn-primary" @onclick="SaveAsync" disabled="@(_saving || !_canEdit)">
                        @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
                        else { <i class="bi bi-check2"></i> }
                        Save
                    </button>
                </div>
            </div>
        </div>
    }
</div>

@code {
    private string? _companyName, _address, _phone, _email, _taxId, _logoUrl, _receiptHeader, _receiptFooter;
    private bool _loading = true, _saving, _canEdit;
    private string? _error;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        _canEdit = (await Auth.AuthorizeAsync(state.User, AppMenus.Perm("settings.company", "edit"))).Succeeded;

        var s = await CompanyService.GetAsync();
        _companyName = s.CompanyName; _address = s.Address; _phone = s.Phone; _email = s.Email;
        _taxId = s.TaxId; _logoUrl = s.LogoUrl; _receiptHeader = s.ReceiptHeader; _receiptFooter = s.ReceiptFooter;
        _loading = false;
    }

    private async Task OnLogoSelected(InputFileChangeEventArgs e)
    {
        try
        {
            var file = e.File;
            await using var stream = file.OpenReadStream(maxAllowedSize: 4 * 1024 * 1024); // 4 MB
            var stored = await FileStorage.SaveAsync(stream, file.Name, "uploads/company");
            _logoUrl = "/" + stored.RelativePath;
        }
        catch (Exception ex)
        {
            _error = $"Logo upload failed: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        _error = null;
        _saving = true;
        try
        {
            await CompanyService.UpdateAsync(new UpdateCompanySettingRequest(
                _companyName, _address, _phone, _email, _taxId, _logoUrl, _receiptHeader, _receiptFooter));
            await Swal.ToastAsync("success", "Company profile saved");
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally { _saving = false; }
    }
}
```

> Confirm `IFileStorage` is registered in the web host DI (it backs product image upload). If not resolvable, check `Program.cs` for the existing `LocalFileStorage` registration and reuse it.

- [ ] **Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

Run the app, open `/settings/company`, fill fields, upload a logo, save, reload — confirm values persist and the logo displays.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Settings/Company/ src/ErpOne.Web/Authorization/AppMenus.cs
git commit -m "feat: add Company Profile settings page with logo upload"
```

---

## Task 13: Wire company profile into the POS receipt

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Cashier/Pos/PosSaleDetail.razor`

**Interfaces:**
- Consumes: `ICompanySettingService.GetAsync` (Task 11).

- [ ] **Step 1: Inject the company setting service and load it**

In `src/ErpOne.Web/Components/Pages/Cashier/Pos/PosSaleDetail.razor`, add an inject near the top with the others:
```razor
@inject ICompanySettingService CompanyService
```

Add a backing field in `@code` (near `private PosSaleDto? _sale;`):
```csharp
    private ErpOne.Application.CompanySettings.CompanySettingDto? _company;
```

In `OnInitializedAsync`, after `_sale = await Pos.GetByIdAsync(Id);`, load the company profile:
```csharp
        _company = await CompanyService.GetAsync();
```

- [ ] **Step 2: Replace the hardcoded receipt header/footer**

Replace the current receipt title/sub block (lines ~150–151):
```razor
        <div class="rc-title">@_sale.WarehouseName</div>
        <div class="rc-sub">Sales Receipt · @_sale.SaleNumber<br />@_sale.SaleDate.ToString("dd MMM yyyy HH:mm") · @_sale.CashierName</div>
```
with (company name as title, then address/phone/tax/header, then the warehouse + receipt line):
```razor
        <div class="rc-title">@(string.IsNullOrWhiteSpace(_company?.CompanyName) ? _sale.WarehouseName : _company!.CompanyName)</div>
        @if (!string.IsNullOrWhiteSpace(_company?.Address)) { <div class="rc-sub">@_company!.Address</div> }
        @if (!string.IsNullOrWhiteSpace(_company?.Phone)) { <div class="rc-sub">@_company!.Phone</div> }
        @if (!string.IsNullOrWhiteSpace(_company?.TaxId)) { <div class="rc-sub">NPWP: @_company!.TaxId</div> }
        @if (!string.IsNullOrWhiteSpace(_company?.ReceiptHeader)) { <div class="rc-sub">@_company!.ReceiptHeader</div> }
        <div class="rc-sub">@_sale.WarehouseName · @_sale.SaleNumber<br />@_sale.SaleDate.ToString("dd MMM yyyy HH:mm") · @_sale.CashierName</div>
```

Replace the hardcoded footer (line ~177):
```razor
        <div class="rc-foot">Thank you for your visit 🙏</div>
```
with:
```razor
        <div class="rc-foot">@(string.IsNullOrWhiteSpace(_company?.ReceiptFooter) ? "Thank you for your visit 🙏" : _company!.ReceiptFooter)</div>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

Run the app, set a company name/header/footer in `/settings/company`, complete a POS sale, open its detail, and confirm the receipt shows the company header and footer.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Cashier/Pos/PosSaleDetail.razor
git commit -m "feat: show company profile header/footer on POS receipt"
```

---

## Task 14: Refactor dashboard to the `.cr` skeleton

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Home.razor`
- Modify (if present): `src/ErpOne.Web/Components/Pages/Home.razor.css`

**Interfaces:**
- Consumes: `IProductService.GetDashboardAsync()` (existing) → `ProductDashboardDto` with `TotalProducts, TotalStock, InventoryValue, TotalCategories, ByStatus, ByCategory, OutOfStockCount, LowStockCount, LowStock` (as used in the current `Home.razor`).

**Goal:** Keep the existing product/stock data but re-present it using the shared `.cr` design (`.cr-hero`, `.cr-kpis`) already used by `ShiftDetail.razor` / `PosSaleDetail.razor`, structured so Fase 4 can drop in sales/AR/AP KPIs. Do NOT change `IProductService` or its DTO.

- [ ] **Step 1: Inspect the existing `.cr` classes to reuse**

Run:
```bash
grep -nE "cr-hero|cr-kpis|cr-kpi|cr-card" src/ErpOne.Web/wwwroot/app.css | head -40
```
Expected: shows the `.cr-*` class definitions. Note the exact structural classes (`cr-hero`, `cr-kpis`, and the per-KPI element/value/label classes) so the markup below matches. If the sub-element class names differ from those used here, align the markup to the real class names found (they are shared/global, so no new CSS is needed).

- [ ] **Step 2: Rewrite `Home.razor` using the `.cr` skeleton**

Replace the entire body of `src/ErpOne.Web/Components/Pages/Home.razor` (keep the `@page "/"`, `@rendermode`, and `@inject IProductService ProductService` directives) with a `.cr`-based layout. Use a hero header + a KPI row for the four stat values, then keep the existing status-breakdown, stock-by-category, and low-stock sections inside `.cr` cards:

```razor
@page "/"
@rendermode InteractiveServer
@inject IProductService ProductService
@inject NavigationManager Nav

<PageTitle>Dashboard</PageTitle>

<div class="cr">
    <div class="cr-hero">
        <div class="cr-hero-tx">
            <h1>Dashboard</h1>
            <p>Product &amp; stock overview</p>
        </div>
        <a class="btn btn-outline-primary btn-sm" href="/master/products"><i class="bi bi-box-seam me-1"></i>Manage products</a>
    </div>

    @if (_data is null)
    {
        <div class="text-center py-5 text-muted"><div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...</div>
    }
    else
    {
        <div class="cr-kpis">
            <div class="cr-kpi">
                <div class="cr-kpi-ic"><i class="bi bi-box-seam-fill"></i></div>
                <div class="cr-kpi-v">@_data.TotalProducts.ToString("N0")</div>
                <div class="cr-kpi-l">Total Products</div>
            </div>
            <div class="cr-kpi">
                <div class="cr-kpi-ic"><i class="bi bi-boxes"></i></div>
                <div class="cr-kpi-v">@_data.TotalStock.ToString("N0")</div>
                <div class="cr-kpi-l">Total Stock</div>
            </div>
            <div class="cr-kpi">
                <div class="cr-kpi-ic"><i class="bi bi-cash-stack"></i></div>
                <div class="cr-kpi-v">Rp @_data.InventoryValue.ToString("N0")</div>
                <div class="cr-kpi-l">Inventory Value</div>
            </div>
            <div class="cr-kpi">
                <div class="cr-kpi-ic"><i class="bi bi-tags-fill"></i></div>
                <div class="cr-kpi-v">@_data.TotalCategories.ToString("N0")</div>
                <div class="cr-kpi-l">Categories</div>
            </div>
        </div>

        <div class="row g-4 mt-1">
            <div class="col-12 col-lg-5">
                <div class="cr-card">
                    <div class="cr-card-h"><i class="bi bi-pie-chart-fill me-2"></i>Products by Status</div>
                    <div class="cr-card-b">
                        @if (_data.TotalProducts == 0)
                        {
                            <div class="text-muted small">No products yet.</div>
                        }
                        else
                        {
                            @foreach (var s in Enum.GetValues<ProductStatus>())
                            {
                                var count = _data.ByStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
                                var pct = _data.TotalProducts == 0 ? 0 : (int)Math.Round(count * 100.0 / _data.TotalProducts);
                                <div class="mb-3">
                                    <div class="d-flex justify-content-between small mb-1">
                                        <span>@s</span><span class="text-muted">@count · @pct%</span>
                                    </div>
                                    <div class="progress" style="height:6px"><div class="progress-bar" style="width:@pct%"></div></div>
                                </div>
                            }
                        }
                    </div>
                </div>
            </div>

            <div class="col-12 col-lg-7">
                <div class="cr-card h-100">
                    <div class="cr-card-h"><i class="bi bi-bar-chart-line-fill me-2"></i>Stock by Category</div>
                    <div class="cr-card-b">
                        @if (_data.ByCategory.Count == 0)
                        {
                            <div class="text-muted small">No categorized products yet.</div>
                        }
                        else
                        {
                            var maxStock = _data.ByCategory.Max(c => c.TotalStock);
                            @foreach (var c in _data.ByCategory)
                            {
                                var pct = maxStock == 0 ? 0 : (int)Math.Round(c.TotalStock * 100.0 / maxStock);
                                <div class="mb-3">
                                    <div class="d-flex justify-content-between small mb-1">
                                        <span class="fw-medium">@c.CategoryName</span>
                                        <span class="text-muted">@c.TotalStock pcs · @c.ProductCount item(s)</span>
                                    </div>
                                    <div class="progress" style="height:6px"><div class="progress-bar" style="width:@(Math.Max(pct, 3))%"></div></div>
                                </div>
                            }
                        }
                    </div>
                </div>
            </div>

            <div class="col-12">
                <div class="cr-card">
                    <div class="cr-card-h"><i class="bi bi-clipboard2-pulse-fill me-2"></i>Low / Out of Stock <span class="text-muted fw-normal ms-2 small">(≤ 5 pcs)</span></div>
                    <div class="cr-card-b p-0">
                        @if (_data.LowStock.Count == 0)
                        {
                            <div class="text-muted small p-4 text-center"><i class="bi bi-check2-circle me-1"></i>All products are well stocked.</div>
                        }
                        else
                        {
                            <div class="table-responsive">
                                <table class="table table-hover align-middle mb-0">
                                    <thead class="table-light">
                                        <tr><th class="ps-3">SKU</th><th>Name</th><th class="text-center">Status</th><th class="text-end pe-3">Stock</th></tr>
                                    </thead>
                                    <tbody>
                                        @foreach (var item in _data.LowStock)
                                        {
                                            <tr style="cursor:pointer" @onclick="@(() => GoToProduct(item.Id))">
                                                <td class="ps-3 text-muted small text-nowrap">@item.Sku</td>
                                                <td class="fw-medium">@item.Name</td>
                                                <td class="text-center">@item.Status</td>
                                                <td class="text-end pe-3"><span class="badge @(item.Stock == 0 ? "bg-danger" : "bg-warning text-dark")">@item.Stock</span></td>
                                            </tr>
                                        }
                                    </tbody>
                                </table>
                            </div>
                        }
                    </div>
                </div>
            </div>
        </div>
    }
</div>

@code {
    private ProductDashboardDto? _data;

    protected override async Task OnInitializedAsync() =>
        _data = await ProductService.GetDashboardAsync();

    private void GoToProduct(int id) => Nav.NavigateTo($"/master/products/{id}/edit");
}
```

> The `.cr-card` / `.cr-kpi-*` sub-element class names above must match what Step 1 found in `app.css`. If the real shared classes differ (e.g. `.cr-card-head` instead of `.cr-card-h`), rename the markup classes to the actual ones — do NOT invent new CSS. If `.cr` doesn't define KPI/card sub-elements at all, fall back to reusing the classes `ShiftDetail.razor`/`PosSaleDetail.razor` use for their KPI tiles and cards (open those files to copy the exact class names).

- [ ] **Step 3: Remove now-dead legacy CSS**

If `src/ErpOne.Web/Components/Pages/Home.razor.css` exists and defines `.stat-card`, `.d-card`, `.mini-alert`, `.grad-*`, `.bar`, etc. that are no longer referenced anywhere else, delete the file (or remove those rules). First confirm they aren't used elsewhere:
```bash
grep -rnE "stat-card|d-card|mini-alert" src/ErpOne.Web --include=*.razor
```
Expected: no matches outside the (now rewritten) `Home.razor`. If matches exist elsewhere, leave the shared rules in place.

- [ ] **Step 4: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

Run the app, open `/`, and confirm the dashboard renders with the `.cr` hero + KPI row and the three data sections, with real product/stock numbers.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Home.razor
git commit -m "refactor: rebuild dashboard on the shared .cr design skeleton"
```

---

## Final verification

- [ ] **Step 1: Full build + test**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; all tests pass (existing + new Currency/DocumentNumber/NumberSequence/CompanySetting suites, and all six retrofit regression suites).

- [ ] **Step 2: Smoke-test the app end-to-end**

Run the app and verify each Fase 0 deliverable: no Counter/Weather routes; `/master/currencies` CRUD + supplier/customer dropdowns; `/settings/document-numbering` edit + sample; `/settings/company` save + logo; POS receipt shows company header/footer; `/` dashboard on `.cr`.

---

## Self-review notes (author)

- **Spec coverage:** Item 1 → Task 1; Item 2 (Currency) → Tasks 2, 6, 7; Item 3 (Numbering) → Tasks 3, 5, 8, 9, 10; Item 4 (Company + POS) → Tasks 4, 5, 11, 12, 13; Item 5 (Dashboard) → Task 14; Cross-cutting (menu/seed/migration/tests) → Tasks 2–5, 7, 10, 12 + Final verification. All spec sections mapped.
- **Race-safety:** SQLite in tests can't run raw T-SQL locks, so numbering uses an EF concurrency token (`Version`) + retry loop (provider-agnostic, correct on SQL Server too).
- **Continuity:** `BackfillStartAsync` seeds each period's counter from the max existing document number so retrofitted modules don't reset to `0001` and collide with existing rows.
- **Currency storage:** kept as `string(3)` on Supplier/Customer per the global constraint — UI-only dropdown, no FK/migration churn.
