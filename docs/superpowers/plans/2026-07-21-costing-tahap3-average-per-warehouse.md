# Costing Tahap 3 — Average per Gudang — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `AveragePerWarehouse` costing: moving-average cost maintained per (variant × warehouse) in `ProductStock.CostPrice`, with `ProductVariant.CostPrice` kept as a weighted-average headline. MA and Standard behavior stay bit-identical.

**Architecture:** New `ProductStock.CostPrice` column is authoritative for per-warehouse COGS. `CostingService` gains an `AveragePerWarehouse` branch: `OnInboundAsync` recomputes the warehouse row's average and refreshes the variant headline; `GetOutboundUnitCostAsync` returns the warehouse row's cost. `StockTransferService` calls `OnInboundAsync` on the destination leg (no-op for MA/Standard, moves cost for per-warehouse). Only `StockLevelDto` display branches by method; GL and the movement-based valuation report are untouched.

**Tech Stack:** .NET 10, EF Core (SQLite in-memory per test class), xUnit, Blazor Server.

## Global Constraints

- **MA & Standard bit-identical.** The AveragePerWarehouse branch only runs when the active method is `AveragePerWarehouse`. `ProductStock.CostPrice` is only maintained in that mode (stays 0 for MA/Standard). Full existing suite stays green with no changed numbers.
- **Storage:** per-warehouse cost = `ProductStock.CostPrice` (decimal 18,2). Headline = `ProductVariant.CostPrice` = weighted average across warehouses, refreshed each inbound.
- **Round:** `Math.Round(v, 2, MidpointRounding.AwayFromZero)` everywhere.
- **Transfer unified:** destination leg always calls `OnInboundAsync`; no-op for MA (inUnitCost == global CostPrice → unchanged) and Standard (no-op by design).
- **GL unchanged:** average method → no variance; transfer value-preserving on the single Inventory account → no journal. `JournalPostingService` not touched.
- **Read sites:** only `StockLevelDto` branches (`perWh ? s.CostPrice : v.CostPrice`). Dashboard total (Σ qty×headline) and `InventoryValuationReportService` (movement-based) stay correct with no change.
- **Method selectable:** `UpdateMethodAsync` accepts `MovingAverage`, `StandardCost`, `AveragePerWarehouse`; rejects `Fifo`.
- **Lock unchanged:** method chosen while `!StockMovements.Any()`.
- **Test isolation:** per-class SQLite in-memory DB; per-warehouse tests flip method via the `CostingSetting` entity in their own DB.

---

### Task 1: Domain + EF — `ProductStock.CostPrice` + headline setter

**Files:**
- Modify: `src/ErpOne.Domain/Entities/Inventory/ProductStock.cs`
- Modify: `src/ErpOne.Domain/Entities/Master/ProductVariant.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (ProductStock config ~159-170)
- Create: migration `<timestamp>_AddProductStockCost`
- Test: `tests/ErpOne.UnitTests/ProductStockCostTests.cs`

**Interfaces:**
- Produces: `ProductStock.CostPrice` (get) + `ProductStock.SetCost(decimal)`; `ProductVariant.SetHeadlineCost(decimal)`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/ErpOne.UnitTests/ProductStockCostTests.cs
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ProductStockCostTests
{
    [Fact]
    public void SetCost_updates_cost_price()
    {
        var s = new ProductStock(1, 1, 10);
        s.SetCost(1250.50m);
        Assert.Equal(1250.50m, s.CostPrice);
    }

    [Fact]
    public void SetCost_rejects_negative()
    {
        var s = new ProductStock(1, 1, 10);
        Assert.Throws<ArgumentException>(() => s.SetCost(-1m));
    }

    [Fact]
    public void New_stock_defaults_cost_to_zero()
    {
        Assert.Equal(0m, new ProductStock(1, 1, 5).CostPrice);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.UnitTests --filter ProductStockCostTests`
Expected: FAIL — `CostPrice`/`SetCost` do not exist.

- [ ] **Step 3: Add `CostPrice` + `SetCost` to `ProductStock`**

Add property after `Quantity`:

```csharp
    public decimal CostPrice { get; private set; }
```

Add method after `ApplyDelta`:

```csharp
    /// <summary>Set biaya rata-rata per (varian,gudang). Dipakai strategi Average-per-gudang.</summary>
    public void SetCost(decimal cost)
    {
        if (cost < 0) throw new ArgumentException("Cost must be >= 0.", nameof(cost));
        CostPrice = cost;
    }
```

- [ ] **Step 4: Add `SetHeadlineCost` to `ProductVariant`**

Add after `ApplyMovingAverage`:

```csharp
    /// <summary>Set CostPrice sebagai headline (rata-rata tertimbang lintas gudang) untuk mode Average-per-gudang.
    /// Tidak dipakai MA (yang lewat ApplyMovingAverage) maupun Standard.</summary>
    public void SetHeadlineCost(decimal cost)
    {
        if (cost < 0) throw new ArgumentException("Cost must be >= 0.", nameof(cost));
        CostPrice = Math.Round(cost, 2, MidpointRounding.AwayFromZero);
    }
```

- [ ] **Step 5: EF config**

In `AppDbContext.cs`, inside `modelBuilder.Entity<ProductStock>` (after `HasIndex`):

```csharp
            e.Property(s => s.CostPrice).HasPrecision(18, 2);
```

- [ ] **Step 6: Migration**

Run: `dotnet ef migrations add AddProductStockCost --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Expected: `AddColumn<decimal>("CostPrice", "ProductStocks", ... defaultValue: 0m ...)`. Confirm default 0.

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/ErpOne.UnitTests --filter ProductStockCostTests`
Expected: PASS (3).

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Domain/Entities/Inventory/ProductStock.cs src/ErpOne.Domain/Entities/Master/ProductVariant.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.UnitTests/ProductStockCostTests.cs
git commit -m "feat(costing): ProductStock.CostPrice + ProductVariant.SetHeadlineCost"
```

---

### Task 2: `CostingService` — AveragePerWarehouse strategy

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs`
- Test: `tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs`

**Interfaces:**
- Consumes: `ProductStock.SetCost`, `ProductVariant.SetHeadlineCost`, `CostingMethod.AveragePerWarehouse`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AveragePerWarehouseTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AveragePerWarehouseTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetPerWarehouseAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.AveragePerWarehouse);
        await db.SaveChangesAsync();
    }

    private static async Task InboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty, decimal cost)
    {
        await db.UpsertStockAsync(variantId, whId, qty, default);
        await costing.OnInboundAsync(variantId, whId, qty, cost, default);
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> RowCostAsync(AppDbContext db, int variantId, int whId) =>
        await db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId && s.WarehouseId == whId)
            .Select(s => s.CostPrice).SingleAsync();

    [Fact]
    public async Task Per_warehouse_costs_are_independent_and_headline_is_weighted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id));
        Assert.Equal(1400m, await RowCostAsync(db, variant.Id, whB.Id));

        var headline = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id).Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1200m, headline); // (10*1000 + 10*1400)/20

        Assert.Equal(1000m, await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 1, default));
        Assert.Equal(1400m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
    }

    [Fact]
    public async Task Moving_average_within_a_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"W{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, wh.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, wh.Id, 10, 1200m);

        Assert.Equal(1100m, await RowCostAsync(db, variant.Id, wh.Id)); // (10*1000 + 10*1200)/20
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter AveragePerWarehouseTests`
Expected: FAIL — `OnInboundAsync` throws `NotSupportedException` for `AveragePerWarehouse`.

- [ ] **Step 3: Add a `Round` helper + per-warehouse branches to `CostingService`**

Add a private static rounding helper (top of class):

```csharp
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
```

In `OnInboundAsync`, add a case before `default:`:

```csharp
            case CostingMethod.AveragePerWarehouse:
                if (quantity <= 0) return;
                var row = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
                    ?? await db.ProductStocks.FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct)
                    ?? throw new InvalidOperationException($"ProductStock ({variantId},{warehouseId}) not found — call after UpsertStockAsync.");
                var rowQtyBefore = row.Quantity - quantity;
                var newRowCost = rowQtyBefore <= 0
                    ? unitCost
                    : Round((rowQtyBefore * row.CostPrice + quantity * unitCost) / (rowQtyBefore + quantity));
                row.SetCost(newRowCost);

                var variantH = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                    ?? throw new InvalidOperationException($"Variant {variantId} not found.");
                var (wQty, wVal) = await WeightedCostAsync(variantId, ct);
                variantH.SetHeadlineCost(wQty <= 0 ? newRowCost : wVal / wQty);
                return;
```

In `GetOutboundUnitCostAsync`, add an arm:

```csharp
            CostingMethod.AveragePerWarehouse => await PerWarehouseCostAsync(variantId, warehouseId, ct),
```

Add the two private helpers:

```csharp
    // Local-aware sum of (qty) and (qty*cost) across all warehouse rows of a variant.
    private async Task<(int qty, decimal value)> WeightedCostAsync(int variantId, CancellationToken ct)
    {
        var local = db.ProductStocks.Local.Where(s => s.ProductVariantId == variantId).ToList();
        var trackedWh = local.Select(s => s.WarehouseId).Distinct().ToList();
        var qty = local.Sum(s => s.Quantity);
        var value = local.Sum(s => s.Quantity * s.CostPrice);
        var dbRows = await db.ProductStocks
            .Where(s => s.ProductVariantId == variantId && !trackedWh.Contains(s.WarehouseId))
            .Select(s => new { s.Quantity, s.CostPrice }).ToListAsync(ct);
        qty += dbRows.Sum(s => s.Quantity);
        value += dbRows.Sum(s => s.Quantity * s.CostPrice);
        return (qty, value);
    }

    private async Task<decimal> PerWarehouseCostAsync(int variantId, int warehouseId, CancellationToken ct)
    {
        var local = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId);
        if (local is not null) return local.CostPrice != 0m || local.Quantity != 0 ? local.CostPrice : await CurrentCostPriceAsync(variantId, ct);
        var dbCost = await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            .Select(s => (decimal?)s.CostPrice).FirstOrDefaultAsync(ct);
        return dbCost is decimal c && c != 0m ? c : await CurrentCostPriceAsync(variantId, ct);
    }
```

> `CurrentCostPriceAsync` (existing, returns headline `variant.CostPrice`) is the fallback when a warehouse has no cost yet. `Round` on the headline is applied by `SetHeadlineCost`.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter AveragePerWarehouseTests`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs
git commit -m "feat(costing): AveragePerWarehouse strategy (per-warehouse MA + weighted headline)"
```

---

### Task 3: Accept `AveragePerWarehouse` (service + UI)

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs`
- Modify: `src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor`
- Test: append to `tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs`

- [ ] **Step 1: Write the failing test (append)**

```csharp
    [Fact]
    public async Task UpdateMethodAsync_accepts_average_per_warehouse_when_unlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StockMovements.RemoveRange(db.StockMovements);
        await db.SaveChangesAsync();

        await Svc(scope).UpdateMethodAsync(CostingMethod.AveragePerWarehouse);
        Assert.Equal(CostingMethod.AveragePerWarehouse, await Svc(scope).GetMethodAsync());

        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.MovingAverage); // restore for sibling tests
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: FAIL — guard rejects `AveragePerWarehouse` with "belum didukung".

- [ ] **Step 3: Widen the guard**

In `CostingSettingService.UpdateMethodAsync`, replace:

```csharp
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

with:

```csharp
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost or CostingMethod.AveragePerWarehouse))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

- [ ] **Step 4: Add the dropdown option**

In `CostingSettingIndex.razor`, after the Standard Cost `<option>`:

```razor
                            <option value="@((int)CostingMethod.AveragePerWarehouse)">Average per Warehouse</option>
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: PASS (all facts).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs
git commit -m "feat(costing): allow selecting AveragePerWarehouse"
```

---

### Task 4: StockTransfer — unified destination inbound leg

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs` (~147-156)
- Test: append to `tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs`

**Interfaces:**
- Consumes: `ICostingService.OnInboundAsync` (already injected in Task 7 of Tahap 1).

- [ ] **Step 1: Write the failing test (append to `AveragePerWarehouseTests`)**

```csharp
    [Fact]
    public async Task Transfer_moves_cost_into_destination_average()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m); // A: 10 @ 1000, B empty

        // Simulate a transfer of 5 A->B the way StockTransferService.PostAsync does it.
        var cost = await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 5, default); // = 1000
        await db.UpsertStockAsync(variant.Id, whA.Id, -5, default);
        await db.UpsertStockAsync(variant.Id, whB.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, whB.Id, 5, cost, default);
        await db.SaveChangesAsync();

        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id)); // source unchanged
        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whB.Id)); // dest now 1000
    }
```

> This test exercises the seam directly (the exact call sequence `StockTransferService.PostAsync` will use). Step 3 wires the real service.

- [ ] **Step 2: Run to verify pass-at-seam / then integrate**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter AveragePerWarehouseTests`
Expected: the new test PASSES already (it calls the seam directly, which Task 2 implemented). This confirms the seam; Step 3 makes `StockTransferService` use it.

- [ ] **Step 3: Wire the destination inbound leg in `StockTransferService.PostAsync`**

In the posting loop, after both `UpsertStockAsync` calls, add the destination inbound:

```csharp
        foreach (var line in t.Lines)
        {
            var cost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, t.SourceWarehouseId, line.Quantity, ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.SourceWarehouseId, MovementType.Transfer,
                -line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.DestinationWarehouseId, MovementType.Transfer,
                line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            await db.UpsertStockAsync(line.ProductVariantId, t.SourceWarehouseId, -line.Quantity, ct);
            await db.UpsertStockAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, ct);
            await costing.OnInboundAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, cost, ct);
        }
```

> MA: `OnInboundAsync(dest, qty, cost)` with `cost = global CostPrice` → weighted average = `CostPrice` unchanged (bit-identical). Standard: no-op. Per-warehouse: recomputes destination row.

- [ ] **Step 4: Build + run transfer + per-warehouse suites (regression)**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter "StockTransferServiceTests|AveragePerWarehouseTests"`
Expected: all pass — existing MA transfer tests unchanged, per-warehouse transfer moves cost.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs
git commit -m "feat(costing): transfer recomputes destination average (unified inbound leg)"
```

---

### Task 5: StockLevel display shows per-warehouse cost

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/StockService.cs` (`GetLevelsPagedAsync` ~59-63, `BuildLevelQuery` ~83-88, constructor)
- Test: append to `tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs`

**Interfaces:**
- Consumes: `ICostingSettingService.GetMethodAsync`.

- [ ] **Step 1: Inject `ICostingSettingService` into `StockService`**

```csharp
public class StockService(
    AppDbContext db,
    IValidator<StockAdjustmentRequest> adjustmentValidator,
    ICostingService costing,
    ICostingSettingService costingSettings) : IStockService
```

(`using ErpOne.Application.Costing;` already present from Tahap 1.)

- [ ] **Step 2: Branch the two `StockLevelDto` projections by method**

In `GetLevelsPagedAsync`, before building `items`, fetch the flag:

```csharp
        var perWh = await costingSettings.GetMethodAsync(ct) == CostingMethod.AveragePerWarehouse;
```

Change the projection cost argument:

```csharp
            .Select(x => new StockLevelDto(x.v.Id, x.v.Sku, x.p.Name, x.w.Id, x.w.Name, x.s.Quantity,
                perWh ? x.s.CostPrice : x.v.CostPrice))
```

`BuildLevelQuery` is used by `GetLevelsByVariantAsync` (no `ct` in its call chain there). Convert `BuildLevelQuery` to take a `bool perWh` param and have `GetLevelsByVariantAsync` fetch it first:

```csharp
    public async Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default)
    {
        var perWh = await costingSettings.GetMethodAsync(ct) == CostingMethod.AveragePerWarehouse;
        return await BuildLevelQuery(db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId), perWh)
            .ToListAsync(ct);
    }

    private IQueryable<StockLevelDto> BuildLevelQuery(IQueryable<ProductStock> source, bool perWh) =>
        from s in source
        join v in db.ProductVariants.AsNoTracking() on s.ProductVariantId equals v.Id
        join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
        join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
        select new StockLevelDto(v.Id, v.Sku, p.Name, w.Id, w.Name, s.Quantity, perWh ? s.CostPrice : v.CostPrice);
```

`CostingMethod` is in `ErpOne.Domain.Entities` (already imported via `using ErpOne.Domain.Entities;`).

- [ ] **Step 3: Write the test (append)**

```csharp
    [Fact]
    public async Task StockLevels_show_per_warehouse_cost_under_average_per_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var stock = scope.ServiceProvider.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SetPerWarehouseAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        var levels = await stock.GetLevelsByVariantAsync(variant.Id);
        Assert.Equal(1000m, levels.Single(l => l.WarehouseId == whA.Id).CostPrice);
        Assert.Equal(1400m, levels.Single(l => l.WarehouseId == whB.Id).CostPrice);
    }
```

> Confirm the `StockLevelDto` property name for cost (`CostPrice`) and `WarehouseId` against `StockLevelDto` definition; adjust if different.

- [ ] **Step 4: Build + run**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter AveragePerWarehouseTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/StockService.cs tests/ErpOne.IntegrationTests/AveragePerWarehouseTests.cs
git commit -m "feat(costing): Stock Levels show per-warehouse cost under average-per-warehouse"
```

---

### Task 6: Final regression + self-review

- [ ] **Step 1: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors/0 warnings; unit + integration green. MA & Standard numbers unchanged; new per-warehouse tests pass.

- [ ] **Step 2: Confirm MA/Standard costing untouched**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "CostingServiceTests|StandardCost|GoodsReceiptServiceTests|StockTransferServiceTests"`
Expected: PASS — MA moving-average, Standard PPV, MA/Standard GRN, and MA transfer all unchanged.

- [ ] **Step 3: Straggler grep**

Run: `git grep -n "\.CostPrice" -- src/ErpOne.Infrastructure/Services`
Expected: outbound/valuation costing goes through the seam or `s.CostPrice` (per-warehouse display); remaining `v.CostPrice` hits are headline/display/PO-suggested-price/product-CRUD only.

- [ ] **Step 4: Final commit (if fixes)**

```bash
git add -A
git commit -m "chore(costing): Tahap 3 Average per Warehouse complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 storage `ProductStock.CostPrice` + headline setter → Task 1 ✓; §2 per-warehouse seam (OnInbound row+headline, GetOutbound row) → Task 2 ✓; §3 UpsertStock seeding (rowQtyBefore<=0 → cost=unitCost) → Task 2 ✓; §4 transfer unified leg → Task 4 ✓; §5 GL unchanged → not touched (verified Task 6) ✓; §6 read sites — only StockLevelDto branches, dashboard/report unchanged → Task 5 ✓; §7 method select + UI → Task 3 ✓; §8 tests → Tasks 2,4,5 ✓.

**MA/Standard bit-identical:** `ProductStock.CostPrice` set only in the AveragePerWarehouse branch; MA/Standard leave it 0 and read sites use `v.CostPrice` for those methods. Transfer's new `OnInboundAsync(dest)` is a proven no-op for MA (weighted avg with inUnitCost==CostPrice) and Standard (no-op branch). Verified in Task 6 regression.

**Type consistency:** `OnInboundAsync(int,int,int,decimal,ct)` / `GetOutboundUnitCostAsync(int,int,int,ct)` unchanged. New: `ProductStock.SetCost(decimal)`, `ProductVariant.SetHeadlineCost(decimal)`, `CostingService.Round/WeightedCostAsync/PerWarehouseCostAsync`. `BuildLevelQuery` gains a `bool perWh` param — updated at its single caller (`GetLevelsByVariantAsync`).

**Verify-before-embed flags:** `StockLevelDto` cost/warehouse property names (Task 5 test); migration default 0 for `CostPrice`. Surrounding logic complete.
