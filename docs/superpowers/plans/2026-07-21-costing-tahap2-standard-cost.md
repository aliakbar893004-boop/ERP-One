# Costing Tahap 2 — Standard Cost + Purchase Price Variance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `StandardCost` a selectable costing method: inventory valued at a fixed per-variant standard cost, purchase price variance (actual − standard) posted to a new GL account, MA behavior untouched.

**Architecture:** Reuse `ProductVariant.CostPrice` as the standard cost (manual under Standard). `CostingService` gains a Standard branch (`OnInboundAsync` no-op; outbound returns `CostPrice`). `JournalPostingService.PostGoodsReceiptAsync` becomes method-aware: under Standard it debits Inventory at standard, credits GR-IR at actual, and balances with a Purchase Price Variance (PPV) line. A new COA account `5150 Selisih Harga Beli` is seeded and mapped in `PostingConfiguration`.

**Tech Stack:** .NET 10, C# primary constructors, EF Core (SQLite in-memory per test class), FluentValidation, xUnit, Blazor Server.

## Global Constraints

- **MA is default and must stay bit-identical.** The Standard branch only runs when the active method is `StandardCost`. Full existing suite must stay green with no changed numbers.
- **Storage:** standard cost = `ProductVariant.CostPrice`. No new variant field.
- **`CostingMethod.StandardCost`** already exists (Tahap 1). No enum change.
- **PPV account:** code `5150`, name `Selisih Harga Beli`, type `Expense`, parent `5000`, postable. Seeded idempotently (works for fresh AND pre-Tahap-2 databases).
- **PPV posting (Standard only):** `grValue = Σ Round(qty × actualUnitCost)` → Cr GR-IR; `invValue = Σ Round(qty × standardCost)` → Dr Inventory; `d = grValue − invValue`; PPV line `Dr Max(d,0) / Cr Max(-d,0)`. `Round = Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Lock unchanged:** method changeable only while `!db.StockMovements.Any()`.
- **Method selectable:** `UpdateMethodAsync` accepts `MovingAverage` + `StandardCost`; rejects `AveragePerWarehouse`/`Fifo` with `"Metode belum didukung."`.
- **Test isolation:** DB is SQLite in-memory per test class (`IClassFixture`). Standard tests flip the method in their own DB via the `CostingSetting` entity directly (bypassing the lock) — safe, does not affect MA tests in other classes.

---

### Task 1: PPV account plumbing (COA seed + `PostingConfiguration` field + DTO + service)

**Files:**
- Modify: `src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (PostingConfiguration config block ~912-920)
- Modify: `src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs`
- Create: migration `<timestamp>_AddPurchasePriceVarianceAccount`
- Test: `tests/ErpOne.IntegrationTests/PostingConfigurationPpvTests.cs`

**Interfaces:**
- Produces: `PostingConfiguration.PurchasePriceVarianceAccountId`; `Update(..., int? purchasePriceVariance)`; `PostingConfigurationDto(..., int? PurchasePriceVarianceAccountId)`; `UpdatePostingConfigurationRequest(..., int? PurchasePriceVarianceAccountId)`; COA account `5150` seeded and mapped.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ErpOne.IntegrationTests/PostingConfigurationPpvTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PostingConfigurationPpvTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PostingConfigurationPpvTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Ppv_account_5150_seeded_and_mapped()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var acc5150 = await db.Accounts.SingleOrDefaultAsync(a => a.Code == "5150");
        Assert.NotNull(acc5150);
        Assert.True(acc5150!.IsPostable);

        var cfg = await sp.GetRequiredService<IPostingConfigurationService>().GetAsync();
        Assert.Equal(acc5150.Id, cfg.PurchasePriceVarianceAccountId);
    }
}
```

> Note: verify the `Account` "is postable" property name (`IsPostable` vs `Postable`) against `Account.cs`; adjust the assertion. If `Account` has no public postable flag, drop that assertion and keep the existence + mapping checks.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter PostingConfigurationPpvTests`
Expected: FAIL — `PurchasePriceVarianceAccountId` does not exist (compile error) / account 5150 missing.

- [ ] **Step 3: Add the domain property + Update param**

In `PostingConfiguration.cs`, add the property after `PosCashAccountId`:

```csharp
    public int? PurchasePriceVarianceAccountId { get; private set; }
```

Extend `Update` (add trailing param + assignment):

```csharp
    public void Update(int? ar, int? ap, int? inventory, int? grIr, int? sales, int? cogs,
        int? inputTax, int? outputTax, int? posCash, int? purchasePriceVariance)
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
        PurchasePriceVarianceAccountId = purchasePriceVariance;
    }
```

- [ ] **Step 4: Add EF FK config**

In `AppDbContext.cs`, inside `modelBuilder.Entity<PostingConfiguration>`, after the `PosCashAccountId` FK line:

```csharp
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.PurchasePriceVarianceAccountId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Extend DTOs**

Replace `PostingConfigurationDtos.cs` contents:

```csharp
namespace ErpOne.Application.Accounting;

public record PostingConfigurationDto(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId,
    int? PurchasePriceVarianceAccountId);

public record UpdatePostingConfigurationRequest(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId,
    int? PurchasePriceVarianceAccountId);
```

- [ ] **Step 6: Update `PostingConfigurationService`**

`GetAsync` — append the new field to the returned DTO:

```csharp
        return new PostingConfigurationDto(c.ArAccountId, c.ApAccountId, c.InventoryAccountId, c.GrIrAccountId,
            c.SalesAccountId, c.CogsAccountId, c.InputTaxAccountId, c.OutputTaxAccountId, c.PosCashAccountId,
            c.PurchasePriceVarianceAccountId);
```

`UpdateAsync` — pass the new field to `Update`:

```csharp
        c.Update(r.ArAccountId, r.ApAccountId, r.InventoryAccountId, r.GrIrAccountId,
            r.SalesAccountId, r.CogsAccountId, r.InputTaxAccountId, r.OutputTaxAccountId, r.PosCashAccountId,
            r.PurchasePriceVarianceAccountId);
```

- [ ] **Step 7: Seed the COA account + mapping (idempotent) in `AccountingSeeder`**

(a) Add `5150` to the `defs` array immediately after the `"5100"` entry:

```csharp
                ("5100", "Harga Pokok Penjualan", AccountType.Expense, "5000", true),
                ("5150", "Selisih Harga Beli", AccountType.Expense, "5000", true),
```

(b) After the COA-empty `if` block closes (after line ~57, before the `IdOf` helper), add an idempotent guard for pre-existing databases:

```csharp
        // 1b) Idempotent: ensure PPV account 5150 exists (DBs seeded before Tahap 2).
        var cogsGroupId = await db.Accounts.Where(a => a.Code == "5000").Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (cogsGroupId is int parent5000 && !await db.Accounts.AnyAsync(a => a.Code == "5150", ct))
        {
            db.Accounts.Add(new Account("5150", "Selisih Harga Beli", AccountType.Expense, parent5000, true, null));
            await db.SaveChangesAsync(ct);
        }
```

(c) In the first-seed mapping block, add `purchasePriceVariance` to the `cfg.Update(...)` call:

```csharp
            cfg.Update(
                ar: await IdOf("1130"), ap: await IdOf("2110"), inventory: await IdOf("1140"),
                grIr: await IdOf("1160"), sales: await IdOf("4100"), cogs: await IdOf("5100"),
                inputTax: await IdOf("1150"), outputTax: await IdOf("2120"), posCash: await IdOf("1110"),
                purchasePriceVariance: await IdOf("5150"));
```

(d) After that mapping block, add an idempotent PPV-mapping guard for pre-existing configs:

```csharp
        // 2b) Idempotent: ensure PPV mapped (configs created before Tahap 2).
        if (cfg is not null && cfg.PurchasePriceVarianceAccountId is null && await IdOf("5150") is int ppvId)
        {
            cfg.Update(cfg.ArAccountId, cfg.ApAccountId, cfg.InventoryAccountId, cfg.GrIrAccountId,
                cfg.SalesAccountId, cfg.CogsAccountId, cfg.InputTaxAccountId, cfg.OutputTaxAccountId,
                cfg.PosCashAccountId, ppvId);
            await db.SaveChangesAsync(ct);
        }
```

> Note: `Account` constructor signature is `new Account(code, name, AccountType, int? parentId, bool postable, ...)` per the `defs` loop (`new Account(d.Code, d.Name, d.Type, parentId, d.Postable, null)`). Match it exactly.

- [ ] **Step 8: Create the migration**

Run: `dotnet ef migrations add AddPurchasePriceVarianceAccount --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Expected: adds nullable `PurchasePriceVarianceAccountId` column + FK to `M_PostingConfigurations`. (The `5150` account row is seeded by `AccountingSeeder`, not the migration.)

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter PostingConfigurationPpvTests`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/ErpOne.Domain/Entities/Accounting/PostingConfiguration.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Application/Accounting/PostingConfigurationDtos.cs src/ErpOne.Infrastructure/Services/Accounting/PostingConfigurationService.cs src/ErpOne.Infrastructure/Persistence/AccountingSeeder.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.IntegrationTests/PostingConfigurationPpvTests.cs
git commit -m "feat(costing): PPV GL account 5150 + PostingConfiguration mapping"
```

---

### Task 2: `CostingSettingService` accepts `StandardCost`

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs`
- Test: append to `tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs`

**Interfaces:**
- Consumes: `ICostingSettingService.UpdateMethodAsync`, `CostingMethod`.

- [ ] **Step 1: Write the failing test (append a new `[Fact]`)**

```csharp
    [Fact]
    public async Task UpdateMethodAsync_accepts_standard_cost_when_unlocked()
    {
        using var scope = _factory.Services.CreateScope();
        // Fresh isolated DB for this class: no StockMovements yet at construction, but other tests in
        // this class may add some. Guard by clearing method expectation via the entity if needed.
        await Svc(scope).UpdateMethodAsync(CostingMethod.StandardCost);
        Assert.Equal(CostingMethod.StandardCost, await Svc(scope).GetMethodAsync());
    }
```

> Note: `CostingSettingServiceTests` shares one in-memory DB across its `[Fact]`s. The existing `UpdateMethodAsync_rejected_once_stock_movement_exists` test adds a `StockMovement`, which would lock this test if it runs first. To keep this test order-independent, at its start delete any stock movements: `var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.StockMovements.RemoveRange(db.StockMovements); await db.SaveChangesAsync();` before calling `UpdateMethodAsync`. Add that guard.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: FAIL — Tahap 1 rejects `StandardCost` with "belum didukung".

- [ ] **Step 3: Update the guard**

In `CostingSettingService.UpdateMethodAsync`, replace:

```csharp
        if (method != CostingMethod.MovingAverage)
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

with:

```csharp
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: PASS (all facts, incl. `Fifo` still rejected).

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs
git commit -m "feat(costing): allow selecting StandardCost method"
```

---

### Task 3: `CostingService` Standard Cost strategy

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs`
- Test: `tests/ErpOne.IntegrationTests/StandardCostStrategyTests.cs`

**Interfaces:**
- Consumes: `ICostingService`, `CostingSetting.SetMethod`, `CostingMethod`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ErpOne.IntegrationTests/StandardCostStrategyTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StandardCostStrategyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StandardCostStrategyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetStandardAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.StandardCost);   // bypass lock: direct entity mutation for test setup
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Standard_inbound_is_noop_and_outbound_returns_standard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetStandardAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 1000m, null, null, true); // standard = 1000
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductStocks.Add(new ProductStock(variant.Id, wh.Id, 10));
        await db.SaveChangesAsync();

        // Inbound at a different actual cost must NOT change the standard.
        await db.UpsertStockAsync(variant.Id, wh.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, wh.Id, 5, 1300m, default);
        await db.SaveChangesAsync();

        var costPrice = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id)
            .Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1000m, costPrice); // unchanged

        var outbound = await costing.GetOutboundUnitCostAsync(variant.Id, wh.Id, 3, default);
        Assert.Equal(1000m, outbound);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StandardCostStrategyTests`
Expected: FAIL — `OnInboundAsync` currently throws `NotSupportedException` for `StandardCost`.

- [ ] **Step 3: Add the Standard branches in `CostingService`**

In `OnInboundAsync`, add a case before `default:`:

```csharp
            case CostingMethod.StandardCost:
                return; // biaya standar tetap; mutasi masuk tak mengubah CostPrice
```

In `GetOutboundUnitCostAsync`, add a `StandardCost` arm to the switch:

```csharp
        return method switch
        {
            CostingMethod.MovingAverage => await CurrentCostPriceAsync(variantId, ct),
            CostingMethod.StandardCost => await CurrentCostPriceAsync(variantId, ct),
            _ => throw new NotSupportedException($"Costing method {method} is not supported in Tahap 1.")
        };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StandardCostStrategyTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs tests/ErpOne.IntegrationTests/StandardCostStrategyTests.cs
git commit -m "feat(costing): StandardCost strategy (inbound no-op, outbound = standard)"
```

---

### Task 4: GRN auto-posting — Purchase Price Variance

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs` (constructor + `PostGoodsReceiptAsync` ~38-46)
- Test: `tests/ErpOne.IntegrationTests/StandardCostGrnPostingTests.cs`

**Interfaces:**
- Consumes: `ICostingSettingService.GetMethodAsync`, `PostingConfiguration.PurchasePriceVarianceAccountId` (Task 1), `ProductVariant.CostPrice`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ErpOne.IntegrationTests/StandardCostGrnPostingTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StandardCostGrnPostingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StandardCostGrnPostingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Returns (grnId, ppvAccountId, inventoryAccountId, grIrAccountId).
    private static async Task<(int grnId, int ppv, int inv, int grir)> PostStandardGrnAsync(
        IServiceProvider sp, decimal standardCost, decimal actualPrice, int qty)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        // Force Standard method (bypass lock for setup).
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.StandardCost);
        await db.SaveChangesAsync();

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, standardCost, null, null, true); // standard
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, DateTime.Today, null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, actualPrice, 0m, null)]));
        await po.SubmitAsync(created.Id); // empty chain → auto-confirms

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(created.Id, DateTime.Today, null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, qty, actualPrice)]));
        await grnSvc.PostAsync(grn.Id);

        var cfg = await db.PostingConfigurations.FirstAsync();
        return (grn.Id, cfg.PurchasePriceVarianceAccountId!.Value, cfg.InventoryAccountId!.Value, cfg.GrIrAccountId!.Value);
    }

    private static async Task<(decimal debit, decimal credit)> LineAsync(AppDbContext db, int grnId, int accountId)
    {
        var line = await db.JournalEntries.Where(j => j.SourceType == "GoodsReceipt" && j.SourceId == grnId)
            .SelectMany(j => j.Lines).Where(l => l.AccountId == accountId).SingleOrDefaultAsync();
        return line is null ? (0m, 0m) : (line.Debit, line.Credit);
    }

    [Fact]
    public async Task Unfavorable_variance_debits_ppv()
    {
        using var scope = _factory.Services.CreateScope();
        var (grnId, ppv, inv, grir) = await PostStandardGrnAsync(scope.ServiceProvider, standardCost: 1000m, actualPrice: 1300m, qty: 10);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal((10_000m, 0m), await LineAsync(db, grnId, inv));   // Dr Inventory @ standard
        Assert.Equal((0m, 13_000m), await LineAsync(db, grnId, grir));  // Cr GR-IR @ actual
        Assert.Equal((3_000m, 0m), await LineAsync(db, grnId, ppv));    // Dr PPV (unfavorable)
    }

    [Fact]
    public async Task Favorable_variance_credits_ppv()
    {
        using var scope = _factory.Services.CreateScope();
        var (grnId, ppv, inv, grir) = await PostStandardGrnAsync(scope.ServiceProvider, standardCost: 1000m, actualPrice: 800m, qty: 10);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal((10_000m, 0m), await LineAsync(db, grnId, inv));   // Dr Inventory @ standard
        Assert.Equal((0m, 8_000m), await LineAsync(db, grnId, grir));   // Cr GR-IR @ actual
        Assert.Equal((0m, 2_000m), await LineAsync(db, grnId, ppv));    // Cr PPV (favorable)
    }
}
```

> Note: verify `JournalEntryLine` exposes `AccountId`, `Debit`, `Credit` and that `JournalEntry.Lines` is queryable via `SelectMany`. Cross-check against `JournalPostingServiceTests.cs`. If lines aren't navigable that way, load the entry with `.Include(j => j.Lines)` and inspect in memory. Also confirm `CreatePurchaseOrderRequest`/`GoodsReceiptLineRequest`/`CreateGoodsReceiptRequest` signatures against `AgingReportServiceTests.cs:107-113` (copied from there).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StandardCostGrnPostingTests`
Expected: FAIL — under Standard, current code posts Inventory=GR-IR=actual (13000), no PPV line.

- [ ] **Step 3: Inject the setting service into `JournalPostingService`**

```csharp
public class JournalPostingService(AppDbContext db, IDocumentNumberService docNumbers,
    ICostingSettingService costingSettings) : IJournalPostingService
```

Add `using ErpOne.Application.Costing;` at the top.

- [ ] **Step 4: Make `PostGoodsReceiptAsync` method-aware**

Replace the body of `PostGoodsReceiptAsync`:

```csharp
    public async Task PostGoodsReceiptAsync(GoodsReceipt grn, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");
        var grValue = grn.Lines.Sum(l => Round(l.QuantityReceived * l.UnitCost)); // actual

        var method = await costingSettings.GetMethodAsync(ct);
        if (method != CostingMethod.StandardCost)
        {
            await PostBalancedAsync(grn.ReceiptDate, $"GRN {grn.GrnNumber}", "GoodsReceipt", grn.Id,
                [(inventory, grValue, 0m, "Inventory received"), (grIr, 0m, grValue, "Goods received not invoiced")], ct);
            return;
        }

        // Standard costing: inventory at standard (variant.CostPrice), GR-IR at actual, balance via PPV.
        var ppv = RequireAccount(cfg.PurchasePriceVarianceAccountId, "Purchase Price Variance");
        var variantIds = grn.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var standardByVariant = await db.ProductVariants.Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CostPrice, ct);
        var invValue = grn.Lines.Sum(l => Round(l.QuantityReceived * standardByVariant[l.ProductVariantId]));
        var d = grValue - invValue;

        await PostBalancedAsync(grn.ReceiptDate, $"GRN {grn.GrnNumber}", "GoodsReceipt", grn.Id,
        [
            (inventory, invValue, 0m, "Inventory received @ standard"),
            (grIr, 0m, grValue, "Goods received not invoiced"),
            (ppv, Math.Max(d, 0m), Math.Max(-d, 0m), "Purchase price variance"),
        ], ct);
    }
```

> `PostBalancedAsync` already filters out zero-both lines, so when `d == 0` the PPV line is dropped automatically.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StandardCostGrnPostingTests`
Expected: PASS (both facts).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs tests/ErpOne.IntegrationTests/StandardCostGrnPostingTests.cs
git commit -m "feat(costing): GRN posts purchase price variance under standard costing"
```

---

### Task 5: Web — editable costing selector + PPV config field

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (`settings.costing` → `[ActIndex, ActEdit]`)
- Modify: `src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor`

**Interfaces:**
- Consumes: `ICostingSettingService.GetAsync/UpdateMethodAsync`, `UpdatePostingConfigurationRequest.PurchasePriceVarianceAccountId`.

- [ ] **Step 1: Make the menu resource editable**

In `AppMenus.cs`, change the costing entry:

```csharp
            new("settings.costing",         "Costing Method",  "bi-calculator-fill",           [ActIndex, ActEdit]),
```

- [ ] **Step 2: Replace `CostingSettingIndex.razor` with an editable selector**

```razor
@page "/settings/costing"
@attribute [Authorize(Policy = "settings.costing.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Costing
@using ErpOne.Domain.Entities
@inject ICostingSettingService Costing

<PageTitle>Costing Method</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs"><a href="/">Home</a><i class="bi bi-chevron-right"></i><span>Settings</span><i class="bi bi-chevron-right"></i><span class="here">Costing Method</span></div>
        <h1>Costing Method</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else
    {
        @if (_saved) { <div class="cf-alert ok"><i class="bi bi-check2-circle"></i> Tersimpan.</div> }
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h">
                    <span class="hd-ic"><i class="bi bi-calculator-fill"></i></span>
                    <div class="hd-tx">
                        <h2>Inventory costing method</h2>
                        <p>Metode penilaian HPP (Harga Pokok) yang dipakai seluruh transaksi stok — global, company-wide.</p>
                    </div>
                </div>
                <div class="card-b">
                    <div class="f c6">
                        <label class="fl">Metode</label>
                        <select class="ctl" value="@((int)_method)" disabled="@_locked"
                                @onchange="e => _method = (CostingMethod)int.Parse(e.Value!.ToString()!)">
                            <option value="@((int)CostingMethod.MovingAverage)">Moving Average</option>
                            <option value="@((int)CostingMethod.StandardCost)">Standard Cost</option>
                        </select>
                    </div>
                    @if (_locked)
                    {
                        <div class="cf-alert ok"><i class="bi bi-lock-fill"></i> Terkunci — sudah ada transaksi stok; metode tidak dapat diubah.</div>
                    }
                </div>
                @if (!_locked)
                {
                    <div class="pf-footer"><div class="in">
                        <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving"><i class="bi bi-check2"></i> Save</button>
                    </div></div>
                }
            </section>
        </div>
    }
</div>

@code {
    private bool _loading = true, _saving, _saved, _locked;
    private string? _error;
    private CostingMethod _method = CostingMethod.MovingAverage;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var dto = await Costing.GetAsync();
        _method = dto.Method;
        _locked = dto.Locked;
        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = null; _saved = false; _saving = true;
        try
        {
            await Costing.UpdateMethodAsync(_method);
            _saved = true;
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Add the PPV field to `PostingConfigForm.razor`**

(a) In the `Model` class, add `Ppv`:

```csharp
        public int? Ar, Ap, Inventory, GrIr, Sales, Cogs, InputTax, OutputTax, PosCash, Ppv;
```

(b) In `OnInitializedAsync`, map it after `_m.PosCash`:

```csharp
        _m.Ppv = c.PurchasePriceVarianceAccountId;
```

(c) In the grid (after the POS Cash `@Field`), add:

```razor
                        @Field("Purchase Price Variance (Selisih Harga Beli)", _m.Ppv, v => _m.Ppv = v)
```

(d) In `SaveAsync`, pass it to the request:

```csharp
            await Config.UpdateAsync(new UpdatePostingConfigurationRequest(_m.Ar, _m.Ap, _m.Inventory, _m.GrIr,
                _m.Sales, _m.Cogs, _m.InputTax, _m.OutputTax, _m.PosCash, _m.Ppv));
```

- [ ] **Step 4: Build + full suite**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: build clean; suite green. If a permission/menu-count test exists, update its expected set to include `settings.costing.edit`.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor
git commit -m "feat(costing): editable method selector + PPV account in posting config UI"
```

---

### Task 6: Final regression + self-review

- [ ] **Step 1: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors, 0 warnings; unit + integration green. MA regression numbers unchanged; new Standard tests pass.

- [ ] **Step 2: Confirm MA GRN unchanged**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter GoodsReceiptServiceTests`
Expected: PASS — MA GRN path posts Inventory=GR-IR=actual with no PPV line (default method still MovingAverage).

- [ ] **Step 3: Evidence check**

Confirm integration test count grew only by the new Standard/PPV tests and no pre-existing expected number changed. This is the "MA behavior unchanged" evidence.

- [ ] **Step 4: Final commit (if any fixes)**

```bash
git add -A
git commit -m "chore(costing): Tahap 2 Standard Cost + PPV complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 domain reuse-CostPrice (no field) → Tasks 3/4 rely on it ✓; §2 COA 5150 + PostingConfiguration field + DTO + service + seeder → Task 1 ✓; §3 UpdateMethodAsync accepts Standard → Task 2 ✓; §4 CostingService Standard branches → Task 3 ✓; §5 GRN PPV posting → Task 4 ✓; §6 web selector + PPV config field → Task 5 ✓; §7 tests (inbound no-op, PPV unfavorable/favorable, outbound=standard, method switch, PostingConfig PPV, MA regression) → Tasks 1-4,6 ✓.

**PPV sign math:** `d = grValue − invValue`; PPV `(Max(d,0), Max(-d,0))`. actual>standard → d>0 → Dr PPV; actual<standard → Cr PPV. Debits = `invValue + Max(d,0)`, Credits = `grValue + Max(-d,0)`; both equal `Max(grValue, invValue)`. Verified in Task 4 tests (13000/8000 cases).

**Type consistency:** `Update(...)` gains one trailing `int? purchasePriceVariance` — applied identically in PostingConfiguration.cs, both DTO records, PostingConfigurationService, AccountingSeeder (both call sites), and PostingConfigForm SaveAsync. `PurchasePriceVarianceAccountId` name consistent across domain/DTO/config/journal/UI.

**Verify-before-embed flags (noted inline, not placeholders):** `Account` postable property name; `JournalEntryLine` navigation (`AccountId`/`Debit`/`Credit` + `SelectMany`); PO/GRN request signatures (copied from `AgingReportServiceTests`). Surrounding logic is complete.

**Test-isolation:** Standard tests flip the global `CostingSetting` via the entity in their own per-class in-memory DB; MA suite runs in separate DBs — no cross-contamination.
