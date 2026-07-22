# Costing Tahap 4 — FIFO (Layer-based) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Fifo` costing: each inbound creates a `CostLayer` per (variant × warehouse); each outbound consumes oldest layers first (by `Id` asc) inside `GetOutboundUnitCostAsync` and returns the weighted cost of consumed layers. Display cost (`ProductStock.CostPrice` + variant headline) reflects remaining layers. MA, Standard, and AveragePerWarehouse stay bit-identical.

**Architecture:** New `CostLayer` entity is authoritative for FIFO. `CostingService` gains a `Fifo` branch: `OnInboundAsync` adds a layer; `GetOutboundUnitCostAsync` **mutates** (consumes layers oldest-first) and returns the weighted unit cost — zero outbound callsite changes (each caller already invokes it exactly once per outbound line, right before recording the movement). Transfer needs no change (Tahap 3 already wired the destination `OnInboundAsync` leg). GL and the movement-based valuation report are untouched. Only `StockService`'s per-warehouse display flag widens to include `Fifo`.

**Tech Stack:** .NET 10, EF Core (SQLite in-memory per test class), xUnit, Blazor Server.

## Global Constraints

- **MA / Standard / AveragePerWarehouse bit-identical.** The `Fifo` branch only runs when the active method is `Fifo`. `CostLayer` rows only exist in that mode. Full existing suite stays green with no changed numbers.
- **Layer per (variant × warehouse).** FIFO order = `CostLayer.Id` ascending (insert order); no separate sequence column. Consumed layers (`RemainingQty == 0`) are kept for audit, never pruned.
- **Round:** `Math.Round(v, 2, MidpointRounding.AwayFromZero)` everywhere (reuse `CostingService.Round`).
- **Local-aware everywhere:** within one transaction (multi-line GRN then sale, or transfer) newly-added/consumed layers are not yet flushed — merge tracked (`db.CostLayers.Local`) with DB rows.
- **Display cost:** `ProductStock[v,w].CostPrice` = weighted avg of that warehouse's remaining layers (0 if none). `variant.CostPrice` (headline) = weighted avg of remaining layers across all warehouses — **set only when total remaining > 0** (never overwrite with 0, to protect PO suggested-price).
- **GL unchanged:** FIFO = actual cost → no variance; transfer value-preserving on the single Inventory account → no journal. `JournalPostingService` not touched.
- **Read sites:** only `StockService`'s `perWh` flag widens to `method is AveragePerWarehouse or Fifo`. Dashboard total (Σ qty×headline) and `InventoryValuationReportService` (movement-based) stay correct with no change.
- **Method selectable:** `UpdateMethodAsync` accepts all four (`MovingAverage`, `StandardCost`, `AveragePerWarehouse`, `Fifo`); still rejects unknown enum values (e.g. `(CostingMethod)999`) with "Metode belum didukung."
- **Table prefix:** `CostLayer` → `S_` prefix (stok), registered in `AppDbContext.tablePrefixes`; otherwise the model-build guard throws.
- **Lock unchanged:** method chosen while `!StockMovements.Any()`.
- **Test isolation:** per-class SQLite in-memory DB; FIFO tests flip method via the `CostingSetting` entity in their own DB.
- **Enum values (verified):** `MovingAverage=0, StandardCost=1, AveragePerWarehouse=2, Fifo=3` (`src/ErpOne.Domain/Entities/Inventory/CostingMethod.cs`).

---

### Task 1: Domain + EF — `CostLayer` entity

**Files:**
- Create: `src/ErpOne.Domain/Entities/Inventory/CostLayer.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (add `DbSet` ~line 20; add `Entity<CostLayer>` config after the `ProductStock` block ~line 171; register prefix ~line 1081)
- Create: migration `<timestamp>_AddCostLayer`
- Test: `tests/ErpOne.UnitTests/CostLayerTests.cs`

**Interfaces:**
- Produces: `CostLayer(int productVariantId, int warehouseId, decimal unitCost, int quantity)`; properties `Id`, `ProductVariantId`, `WarehouseId`, `UnitCost`, `OriginalQty`, `RemainingQty` (all `{ get; private set; }`); method `int Consume(int qty)`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/ErpOne.UnitTests/CostLayerTests.cs
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CostLayerTests
{
    [Fact]
    public void Ctor_sets_remaining_equal_to_original()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Equal(10, l.OriginalQty);
        Assert.Equal(10, l.RemainingQty);
        Assert.Equal(1000m, l.UnitCost);
    }

    [Fact]
    public void Ctor_rejects_non_positive_quantity()
    {
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, 1000m, 0));
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, 1000m, -1));
    }

    [Fact]
    public void Ctor_rejects_negative_unit_cost()
    {
        Assert.Throws<ArgumentException>(() => new CostLayer(1, 1, -1m, 10));
    }

    [Fact]
    public void Consume_takes_min_of_request_and_remaining()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Equal(4, l.Consume(4));   // took 4
        Assert.Equal(6, l.RemainingQty);
        Assert.Equal(6, l.Consume(9));   // only 6 left, took 6
        Assert.Equal(0, l.RemainingQty);
        Assert.Equal(0, l.Consume(3));   // nothing left
    }

    [Fact]
    public void Consume_rejects_non_positive()
    {
        var l = new CostLayer(1, 1, 1000m, 10);
        Assert.Throws<ArgumentException>(() => l.Consume(0));
        Assert.Throws<ArgumentException>(() => l.Consume(-2));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.UnitTests --filter CostLayerTests`
Expected: FAIL — `CostLayer` does not exist.

- [ ] **Step 3: Create the `CostLayer` entity**

```csharp
// src/ErpOne.Domain/Entities/Inventory/CostLayer.cs
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Satu lapisan biaya FIFO per (varian, gudang). Dibuat tiap mutasi masuk;
/// dikonsumsi tertua-dulu (urut Id) saat mutasi keluar. Layer habis disimpan untuk audit.</summary>
public class CostLayer : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public decimal UnitCost { get; private set; }
    public int OriginalQty { get; private set; }
    public int RemainingQty { get; private set; }

    private CostLayer() { } // EF Core

    public CostLayer(int productVariantId, int warehouseId, decimal unitCost, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (unitCost < 0) throw new ArgumentException("UnitCost must be >= 0.", nameof(unitCost));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        UnitCost = unitCost;
        OriginalQty = quantity;
        RemainingQty = quantity;
    }

    /// <summary>Ambil min(qty, RemainingQty) dari layer; kurangi sisa; kembalikan jumlah yang diambil.</summary>
    public int Consume(int qty)
    {
        if (qty <= 0) throw new ArgumentException("Consume quantity must be > 0.", nameof(qty));
        var take = Math.Min(qty, RemainingQty);
        RemainingQty -= take;
        return take;
    }
}
```

- [ ] **Step 4: Add the `DbSet`**

In `AppDbContext.cs`, after `public DbSet<ProductStock> ProductStocks => Set<ProductStock>();` (line 20):

```csharp
    public DbSet<CostLayer> CostLayers => Set<CostLayer>();
```

- [ ] **Step 5: Add the EF config**

In `AppDbContext.cs`, immediately after the `modelBuilder.Entity<ProductStock>(e => { ... });` block (ends ~line 171):

```csharp
        modelBuilder.Entity<CostLayer>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.UnitCost).HasPrecision(18, 2);
            e.HasIndex(l => new { l.ProductVariantId, l.WarehouseId, l.Id });

            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(l => l.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany()
                .HasForeignKey(l => l.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 6: Register the table prefix**

In `AppDbContext.cs`, inside the `tablePrefixes` dictionary, in the `// Stok` group (after `[nameof(ProductStock)] = "S_",` ~line 1079):

```csharp
            [nameof(CostLayer)] = "S_",
```

- [ ] **Step 7: Create the migration**

Run: `dotnet ef migrations add AddCostLayer --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Expected: `CreateTable("S_CostLayers", ...)` with columns `Id` (PK, identity), `ProductVariantId`, `WarehouseId`, `UnitCost` (decimal 18,2), `OriginalQty`, `RemainingQty`, audit columns, plus the composite index and two restrict FKs. Confirm the table name carries the `S_` prefix.

- [ ] **Step 8: Run tests**

Run: `dotnet test tests/ErpOne.UnitTests --filter CostLayerTests`
Expected: PASS (5).

- [ ] **Step 9: Build (verifies model-build guard accepts the new table)**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings (the "Tabel tanpa prefix" guard does not throw).

- [ ] **Step 10: Commit**

```bash
git add src/ErpOne.Domain/Entities/Inventory/CostLayer.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.UnitTests/CostLayerTests.cs
git commit -m "feat(costing): CostLayer entity + EF mapping (S_CostLayers)"
```

---

### Task 2: `CostingService` — FIFO strategy (consume oldest-first + display refresh)

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs`
- Test: `tests/ErpOne.IntegrationTests/FifoCostingTests.cs`

**Interfaces:**
- Consumes: `CostLayer` ctor + `Consume`, `ProductStock.SetCost`, `ProductVariant.SetHeadlineCost`, `CostingMethod.Fifo`, existing `CostingService.Round` and `CurrentCostPriceAsync`.
- Produces: private `ConsumeFifoAsync`, `ConsumeFifoAndRefreshAsync`, `RefreshFifoDisplayAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ErpOne.IntegrationTests/FifoCostingTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class FifoCostingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FifoCostingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task SetFifoAsync(AppDbContext db)
    {
        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.Fifo);
        await db.SaveChangesAsync();
    }

    private static async Task InboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty, decimal cost)
    {
        await db.UpsertStockAsync(variantId, whId, qty, default);
        await costing.OnInboundAsync(variantId, whId, qty, cost, default);
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> OutboundAsync(AppDbContext db, ICostingService costing, int variantId, int whId, int qty)
    {
        var unit = await costing.GetOutboundUnitCostAsync(variantId, whId, qty, default);
        await db.UpsertStockAsync(variantId, whId, -qty, default);
        await db.SaveChangesAsync();
        return unit;
    }

    private static async Task<decimal> RowCostAsync(AppDbContext db, int variantId, int whId) =>
        await db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId && s.WarehouseId == whId)
            .Select(s => s.CostPrice).SingleAsync();

    private static (Warehouse whA, Warehouse whB, Product product, ProductVariant variant) NewFixtures(string id)
    {
        var whA = new Warehouse($"A{id}", $"GDA {id}", null, true, false);
        var whB = new Warehouse($"B{id}", $"GDB {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        return (whA, whB, product, variant);
    }

    [Fact]
    public async Task Outbound_consumes_oldest_layers_first_weighted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, _, product, variant) = NewFixtures(id);
        db.Warehouses.Add(whA); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1200m);

        // Consume 15: 10@1000 + 5@1200 = 16000 / 15 = 1066.666... -> 1066.67
        var unit = await OutboundAsync(db, costing, variant.Id, whA.Id, 15);
        Assert.Equal(1066.67m, unit);

        // Remaining: 5 @ 1200 -> display row cost 1200
        Assert.Equal(1200m, await RowCostAsync(db, variant.Id, whA.Id));

        // Next outbound of 5 -> exactly 1200; then remaining 0
        var unit2 = await OutboundAsync(db, costing, variant.Id, whA.Id, 5);
        Assert.Equal(1200m, unit2);
        Assert.Equal(0m, await RowCostAsync(db, variant.Id, whA.Id));
    }

    [Fact]
    public async Task Layers_are_independent_per_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        Assert.Equal(1400m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
    }

    [Fact]
    public async Task Transfer_moves_a_fifo_layer_into_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m); // A: layer 10@1000, B empty

        // Same call sequence StockTransferService.PostAsync uses: outbound source, then inbound dest.
        var cost = await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 5, default); // consumes A -> 1000
        await db.UpsertStockAsync(variant.Id, whA.Id, -5, default);
        await db.UpsertStockAsync(variant.Id, whB.Id, 5, default);
        await costing.OnInboundAsync(variant.Id, whB.Id, 5, cost, default); // new layer 5@1000 in B
        await db.SaveChangesAsync();

        Assert.Equal(1000m, cost);
        Assert.Equal(1000m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
        Assert.Equal(1000m, await RowCostAsync(db, variant.Id, whA.Id)); // A still 5@1000
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter FifoCostingTests`
Expected: FAIL — `OnInboundAsync`/`GetOutboundUnitCostAsync` throw `NotSupportedException` for `Fifo`.

- [ ] **Step 3: Add the FIFO inbound branch**

In `CostingService.OnInboundAsync`, add a case before `default:`:

```csharp
            case CostingMethod.Fifo:
                if (quantity <= 0) return;
                db.CostLayers.Add(new CostLayer(variantId, warehouseId, unitCost, quantity));
                await RefreshFifoDisplayAsync(variantId, warehouseId, ct);
                return;
```

- [ ] **Step 4: Add the FIFO outbound arm**

In `CostingService.GetOutboundUnitCostAsync`, add an arm before the `_ =>` default:

```csharp
            CostingMethod.Fifo => await ConsumeFifoAndRefreshAsync(variantId, warehouseId, quantity, ct),
```

- [ ] **Step 5: Add the three private FIFO helpers**

Append these to `CostingService` (after `PerWarehouseCostAsync`):

```csharp
    // Outbound for FIFO MUTATES: consumes oldest layers (Id asc) then refreshes display cost.
    private async Task<decimal> ConsumeFifoAndRefreshAsync(int variantId, int warehouseId, int quantity, CancellationToken ct)
    {
        var unit = await ConsumeFifoAsync(variantId, warehouseId, quantity, ct);
        await RefreshFifoDisplayAsync(variantId, warehouseId, ct);
        return unit;
    }

    // Consume oldest-first across layers of (variant,warehouse). Local-aware: LoadAsync attaches persisted
    // layers to the context WITHOUT overwriting already-tracked (mutated) ones, so Local is the authoritative,
    // mutation-visible set. Id==0 = added this transaction (not yet flushed) -> ordered LAST (newest).
    private async Task<decimal> ConsumeFifoAsync(int variantId, int warehouseId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0) return await CurrentCostPriceAsync(variantId, ct);

        await db.CostLayers
            .Where(l => l.ProductVariantId == variantId && l.WarehouseId == warehouseId && l.RemainingQty > 0)
            .LoadAsync(ct);
        var layers = db.CostLayers.Local
            .Where(l => l.ProductVariantId == variantId && l.WarehouseId == warehouseId && l.RemainingQty > 0)
            .OrderBy(l => l.Id == 0 ? int.MaxValue : l.Id)
            .ToList();

        var need = quantity;
        decimal acc = 0m;
        foreach (var layer in layers)
        {
            if (need <= 0) break;
            var take = layer.Consume(Math.Min(need, layer.RemainingQty));
            acc += take * layer.UnitCost;
            need -= take;
        }

        var consumedQty = quantity - need;
        // Fallback headline when no layers (shouldn't happen — stock validated upstream; guards div-by-zero).
        return consumedQty <= 0 ? await CurrentCostPriceAsync(variantId, ct) : Round(acc / consumedQty);
    }

    // Warehouse row cost = weighted avg of that warehouse's remaining layers (0 if none).
    // Headline = weighted avg across ALL warehouses; set only when total remaining > 0 (never overwrite with 0).
    private async Task RefreshFifoDisplayAsync(int variantId, int warehouseId, CancellationToken ct)
    {
        await db.CostLayers.Where(l => l.ProductVariantId == variantId && l.RemainingQty > 0).LoadAsync(ct);
        var layers = db.CostLayers.Local.Where(l => l.ProductVariantId == variantId && l.RemainingQty > 0).ToList();

        var wh = layers.Where(l => l.WarehouseId == warehouseId).ToList();
        var whQty = wh.Sum(l => l.RemainingQty);
        var row = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            ?? await db.ProductStocks.FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct);
        row?.SetCost(whQty > 0 ? Round(wh.Sum(l => l.RemainingQty * l.UnitCost) / whQty) : 0m);

        var vQty = layers.Sum(l => l.RemainingQty);
        if (vQty > 0)
        {
            var variant = db.ProductVariants.Local.FirstOrDefault(v => v.Id == variantId)
                ?? await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
            variant?.SetHeadlineCost(layers.Sum(l => l.RemainingQty * l.UnitCost) / vQty);
        }
    }
```

> `LoadAsync` is `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.LoadAsync` (namespace already imported). It runs the tracking query and attaches results; EF keeps existing tracked (mutated) instances rather than overwriting them, which is exactly what makes `Local` mutation-visible.

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter FifoCostingTests`
Expected: PASS (3).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs tests/ErpOne.IntegrationTests/FifoCostingTests.cs
git commit -m "feat(costing): FIFO strategy (layer consume oldest-first + display refresh)"
```

---

### Task 3: Accept `Fifo` (service + UI) + fix the now-stale rejection test

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs` (guard ~line 24)
- Modify: `src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor` (~line 38)
- Modify: `tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs` (`UpdateMethodAsync_rejects_unsupported_method` ~line 31; append an accept test)

- [ ] **Step 1: Update the stale rejection test + add an accept test**

In `CostingSettingServiceTests.cs`, replace the body of `UpdateMethodAsync_rejects_unsupported_method` (currently uses `CostingMethod.Fifo`, which is about to become valid) with an out-of-range enum value:

```csharp
    [Fact]
    public async Task UpdateMethodAsync_rejects_unsupported_method()
    {
        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Svc(scope).UpdateMethodAsync((CostingMethod)999));
        Assert.Contains("belum didukung", ex.Message);
    }
```

Then append a new fact (mirrors the AveragePerWarehouse accept test in this file):

```csharp
    [Fact]
    public async Task UpdateMethodAsync_accepts_fifo_when_unlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StockMovements.RemoveRange(db.StockMovements);
        await db.SaveChangesAsync();

        await Svc(scope).UpdateMethodAsync(CostingMethod.Fifo);
        Assert.Equal(CostingMethod.Fifo, await Svc(scope).GetMethodAsync());

        var cs = await db.CostingSettings.FirstAsync();
        cs.SetMethod(CostingMethod.MovingAverage); // restore for sibling tests
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: FAIL — `UpdateMethodAsync_accepts_fifo_when_unlocked` fails (guard still rejects `Fifo` with "belum didukung"). The updated rejection test with `(CostingMethod)999` should pass already.

- [ ] **Step 3: Widen the guard**

In `CostingSettingService.UpdateMethodAsync`, replace:

```csharp
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost or CostingMethod.AveragePerWarehouse))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

with:

```csharp
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost or CostingMethod.AveragePerWarehouse or CostingMethod.Fifo))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);
```

- [ ] **Step 4: Add the dropdown option**

In `CostingSettingIndex.razor`, after the Average per Warehouse `<option>` (line 38):

```razor
                            <option value="@((int)CostingMethod.Fifo)">FIFO</option>
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: PASS (all facts, including the two updated/new ones).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs
git commit -m "feat(costing): allow selecting FIFO; fix stale unsupported-method test"
```

---

### Task 4: StockLevel display shows per-warehouse cost under FIFO

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/StockService.cs` (`perWh` flag at ~line 19 and ~line 64)
- Test: append to `tests/ErpOne.IntegrationTests/FifoCostingTests.cs`

**Interfaces:**
- Consumes: `ICostingSettingService.GetMethodAsync`, `CostingMethod.Fifo`, `IStockService.GetLevelsByVariantAsync`.

- [ ] **Step 1: Widen the `perWh` flag at both sites**

In `StockService.GetLevelsByVariantAsync` (~line 19), replace:

```csharp
        var perWh = await costingSettings.GetMethodAsync(ct) == CostingMethod.AveragePerWarehouse;
```

with:

```csharp
        var method = await costingSettings.GetMethodAsync(ct);
        var perWh = method is CostingMethod.AveragePerWarehouse or CostingMethod.Fifo;
```

In `StockService.GetLevelsPagedAsync` (~line 64), replace the identical single-line assignment:

```csharp
        var perWh = await costingSettings.GetMethodAsync(ct) == CostingMethod.AveragePerWarehouse;
```

with:

```csharp
        var method = await costingSettings.GetMethodAsync(ct);
        var perWh = method is CostingMethod.AveragePerWarehouse or CostingMethod.Fifo;
```

- [ ] **Step 2: Write the test (append to `FifoCostingTests`)**

```csharp
    [Fact]
    public async Task StockLevels_show_per_warehouse_cost_under_fifo()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var stock = scope.ServiceProvider.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whB.Id, 10, 1400m);

        var levels = await stock.GetLevelsByVariantAsync(variant.Id);
        Assert.Equal(1000m, levels.Single(l => l.WarehouseId == whA.Id).CostPrice);
        Assert.Equal(1400m, levels.Single(l => l.WarehouseId == whB.Id).CostPrice);
    }
```

- [ ] **Step 3: Build + run**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter FifoCostingTests`
Expected: PASS (4).

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Inventory/StockService.cs tests/ErpOne.IntegrationTests/FifoCostingTests.cs
git commit -m "feat(costing): Stock Levels show per-warehouse cost under FIFO"
```

---

### Task 5: Full FIFO transfer via `StockTransferService` (no prod change — regression proof)

**Files:**
- Test: append to `tests/ErpOne.IntegrationTests/FifoCostingTests.cs`

> Per spec §5, `StockTransferService` already calls the destination `OnInboundAsync` leg (Tahap 3), so FIFO transfer works with **no production change**. This task adds an end-to-end test through the real service to prove it and lock it against regression. If this test fails, STOP — it means the transfer seam does not behave as the design assumes; do not "fix" by editing `StockTransferService` without re-reviewing the spec.

- [ ] **Step 1: Write the end-to-end transfer test (append to `FifoCostingTests`)**

```csharp
    [Fact]
    public async Task Full_transfer_service_moves_fifo_cost_to_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();
        var transfers = scope.ServiceProvider.GetRequiredService<ErpOne.Application.StockTransfers.IStockTransferService>();
        await SetFifoAsync(db);

        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (whA, whB, product, variant) = NewFixtures(id);
        db.Warehouses.AddRange(whA, whB); db.Products.Add(product);
        await db.SaveChangesAsync();

        // Two layers in source A: 10@1000 then 10@1200.
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1000m);
        await InboundAsync(db, costing, variant.Id, whA.Id, 10, 1200m);

        var created = await transfers.CreateAsync(new ErpOne.Application.StockTransfers.CreateStockTransferRequest(
            DateTime.UtcNow, whA.Id, whB.Id, null,
            new[] { new ErpOne.Application.StockTransfers.CreateStockTransferLineRequest(variant.Id, 15) }));
        await transfers.SubmitAsync(created.Id); // no approval steps configured in the test DB -> auto-posts

        // Source consumed 10@1000 + 5@1200 = 16000/15 = 1066.67; that weighted cost seeds ONE dest layer of 15.
        Assert.Equal(1066.67m, await costing.GetOutboundUnitCostAsync(variant.Id, whB.Id, 1, default));
        // Source A remaining: 5 @ 1200.
        Assert.Equal(1200m, await costing.GetOutboundUnitCostAsync(variant.Id, whA.Id, 1, default));
    }
```

> **Verify-before-embed:** confirm the exact constructor names/params of `CreateStockTransferRequest` and `CreateStockTransferLineRequest`, and that `SubmitAsync(id)` auto-posts when no approval steps exist in the seeded test DB (mirror how `StockTransferServiceTests` posts a transfer). Adjust the request construction and the post trigger to match that existing test's pattern if they differ. The **assertions** (1066.67 dest, 1200 source-remaining) are the contract and must not change.

- [ ] **Step 2: Run**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter FifoCostingTests`
Expected: PASS (5). If FAIL, STOP and re-read spec §5 before changing any production code.

- [ ] **Step 3: Commit**

```bash
git add tests/ErpOne.IntegrationTests/FifoCostingTests.cs
git commit -m "test(costing): end-to-end FIFO transfer through StockTransferService"
```

---

### Task 6: Final regression + self-review

- [ ] **Step 1: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors/0 warnings; unit + integration green. MA / Standard / AveragePerWarehouse numbers unchanged; new FIFO tests pass.

- [ ] **Step 2: Confirm other costing methods untouched**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "CostingServiceTests|StandardCost|AveragePerWarehouseTests|GoodsReceiptServiceTests|StockTransferServiceTests"`
Expected: PASS — MA moving-average, Standard PPV, per-warehouse, GRN, and existing MA transfer all unchanged.

- [ ] **Step 3: Straggler grep**

Run: `git grep -n "CostLayer\|\.CostPrice" -- src/ErpOne.Infrastructure/Services`
Expected: `CostLayer` referenced only inside `CostingService`; remaining `.CostPrice` hits are the FIFO/per-warehouse display branch (`s.CostPrice`), the seam internals, headline/display/PO-suggested-price/product-CRUD, and Standard GL only.

- [ ] **Step 4: Final commit (if fixes)**

```bash
git add -A
git commit -m "chore(costing): Tahap 4 FIFO complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 Domain/EF `CostLayer` (entity + config + prefix + migration) → Task 1 ✓; §2 seam FIFO (OnInbound adds layer, GetOutbound consumes oldest-first weighted, Local-aware) → Task 2 ✓; §3 display refresh (row cost + headline, headline only when remaining > 0) → Task 2 `RefreshFifoDisplayAsync` ✓; §4 read/display `perWh` widened to include Fifo → Task 4 ✓; §5 transfer unchanged, proven end-to-end → Task 5 ✓; §6 GL untouched → not modified (verified Task 6) ✓; §7 method select + UI + **fix stale `rejects_unsupported_method` test** → Task 3 ✓; §8 tests (oldest-first, next outbound, per-warehouse, display, transfer) → Tasks 2,4,5 ✓.

**MA/Standard/AveragePerWarehouse bit-identical:** the `Fifo` case runs only when the active method is `Fifo`; `CostLayer` rows exist only in that mode. No existing branch in `OnInboundAsync`/`GetOutboundUnitCostAsync` is edited. `StockService.perWh` gains `or Fifo` — for the other three methods the flag is unchanged. Verified in Task 6 regression.

**Type consistency:** `OnInboundAsync(int,int,int,decimal,ct)` / `GetOutboundUnitCostAsync(int,int,int,ct)` signatures unchanged. New: `CostLayer(int,int,decimal,int)` + `Consume(int)`; `CostingService.ConsumeFifoAsync`, `ConsumeFifoAndRefreshAsync`, `RefreshFifoDisplayAsync` (all consistent names, used only within the class). Reuses existing `Round`, `CurrentCostPriceAsync`, `SetCost`, `SetHeadlineCost`. Enum `Fifo=3` confirmed.

**Verify-before-embed flags:** (a) migration table name carries `S_` prefix (Task 1 Step 7); (b) `CreateStockTransferRequest`/`CreateStockTransferLineRequest` ctor shapes and the `SubmitAsync` auto-post path (Task 5 Step 1 — mirror `StockTransferServiceTests`); (c) `LoadAsync` namespace already imported in `CostingService`. Surrounding logic complete.
