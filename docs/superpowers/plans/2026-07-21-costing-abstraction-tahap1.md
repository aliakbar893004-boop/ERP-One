# Costing Abstraction — Tahap 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap the hardcoded Moving Average costing logic behind an `ICostingService` seam plus a global `CostingSetting` (locked once any stock movement exists), with **zero behavior change** — MA results stay bit-identical, proven by the existing regression suite staying green.

**Architecture:** Introduce a `CostingMethod` enum and a single-row `CostingSetting` entity (mirrors `PostingConfiguration`). Two application interfaces — `ICostingSettingService` (read/lock the active method) and `ICostingService` (`OnInboundAsync` updates the cost basis after an inbound upsert; `GetOutboundUnitCostAsync` returns the unit cost for outbound movements). The MA strategy delegates to the existing `ProductVariant.ApplyMovingAverage`, fed by a new local-aware on-hand helper so multi-line GRNs of the same variant compute identically to today. Every current callsite that reads `variant.CostPrice` or calls `ApplyMovingAverage` is rerouted through the seam.

**Tech Stack:** .NET 10, C# primary constructors, EF Core (SQL Server; tests use EnsureCreated), FluentValidation, xUnit integration tests (`CustomWebApplicationFactory`).

## Global Constraints

- **No MA behavior change.** MA math, rounding (`Math.Round(x, 2, MidpointRounding.AwayFromZero)`), and results must be identical to the current implementation. The whole existing integration suite (currently 189 passing) must stay green with no changed numbers.
- **Setting is global, company-wide.** One row, `Id = 1`, seeded `Method = MovingAverage`.
- **Locked after first movement.** `UpdateMethodAsync` is rejected (`FluentValidation.ValidationException`) once `db.StockMovements.Any()` is true.
- **Tahap 1 supports only `MovingAverage`.** Selecting any other method is rejected with "Metode belum didukung."; non-MA strategy branches `throw new NotSupportedException(...)`.
- **New entities MUST be registered in `tablePrefixes`** (`AppDbContext`) or the model fails to build. `CostingSetting` → prefix `"M_"`.
- **Namespaces:** domain enum/entity → `ErpOne.Domain.Entities`; application contracts → `ErpOne.Application.Costing`; infra services → `ErpOne.Infrastructure.Services`.
- **DI registrations** live in `src/ErpOne.Infrastructure/DependencyInjection.cs`.
- **`OnInboundAsync` contract:** called AFTER `UpsertStockAsync`. It computes the post-upsert local-aware total across all warehouses, derives `totalBefore = totalAfter - qty`, and calls `ApplyMovingAverage`.
- **`GetOutboundUnitCostAsync` contract:** returns a **unit** cost (caller multiplies by qty). Called at the same point the code currently reads `variant.CostPrice` (before the outbound upsert).

---

### Task 1: Domain — `CostingMethod` enum + `CostingSetting` entity

**Files:**
- Create: `src/ErpOne.Domain/Entities/Inventory/CostingMethod.cs`
- Create: `src/ErpOne.Domain/Entities/Inventory/CostingSetting.cs`
- Test: `tests/ErpOne.UnitTests/CostingSettingTests.cs`

**Interfaces:**
- Consumes: `AuditableEntity` (from `ErpOne.Domain.Common`).
- Produces: `enum CostingMethod { MovingAverage, StandardCost, AveragePerWarehouse, Fifo }`; `class CostingSetting` with `int Id`, `CostingMethod Method`, `void SetMethod(CostingMethod method)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ErpOne.UnitTests/CostingSettingTests.cs
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CostingSettingTests
{
    [Fact]
    public void SetMethod_updates_the_method()
    {
        var setting = new CostingSetting();
        setting.SetMethod(CostingMethod.StandardCost);
        Assert.Equal(CostingMethod.StandardCost, setting.Method);
    }

    [Fact]
    public void Default_method_is_moving_average()
    {
        Assert.Equal(CostingMethod.MovingAverage, new CostingSetting().Method);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.UnitTests --filter CostingSettingTests`
Expected: FAIL — `CostingSetting` / `CostingMethod` do not exist (compile error).

- [ ] **Step 3: Create the enum**

```csharp
// src/ErpOne.Domain/Entities/Inventory/CostingMethod.cs
namespace ErpOne.Domain.Entities;

/// <summary>Metode penilaian HPP. Tahap 1 hanya MovingAverage yang didukung.</summary>
public enum CostingMethod
{
    MovingAverage,
    StandardCost,
    AveragePerWarehouse,
    Fifo
}
```

- [ ] **Step 4: Create the entity**

```csharp
// src/ErpOne.Domain/Entities/Inventory/CostingSetting.cs
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) metode HPP aktif, company-wide. Pola PostingConfiguration.</summary>
public class CostingSetting : AuditableEntity
{
    public int Id { get; private set; }
    public CostingMethod Method { get; private set; } = CostingMethod.MovingAverage;

    // EF Core; single row seeded via HasData. Also used by unit tests.
    public CostingSetting() { }

    public void SetMethod(CostingMethod method) => Method = method;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.UnitTests --filter CostingSettingTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Domain/Entities/Inventory/CostingMethod.cs src/ErpOne.Domain/Entities/Inventory/CostingSetting.cs tests/ErpOne.UnitTests/CostingSettingTests.cs
git commit -m "feat(costing): add CostingMethod enum + CostingSetting entity"
```

---

### Task 2: EF wiring — DbSet, config, seed, migration

**Files:**
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (DbSet near line 66; entity config near the `PostingConfiguration` block ~909; `tablePrefixes` near line 1018)
- Create: migration `src/ErpOne.Infrastructure/Persistence/Migrations/<timestamp>_AddCostingSetting.cs` (generated)
- Test: `tests/ErpOne.IntegrationTests/CostingSettingSeedTests.cs`

**Interfaces:**
- Consumes: `CostingSetting`, `CostingMethod` (Task 1).
- Produces: `db.CostingSettings` DbSet; a seeded row `{ Id = 1, Method = MovingAverage }` present after `EnsureCreated`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ErpOne.IntegrationTests/CostingSettingSeedTests.cs
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CostingSettingSeedTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Seed_row_exists_with_moving_average()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.CostingSettings.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Id);
        Assert.Equal(CostingMethod.MovingAverage, row.Method);
    }
}
```

> Note: confirm the existing test fixture constructor signature matches other test classes in this project (e.g. `AccountServiceTests`). If they inject the factory differently, mirror that exact pattern.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingSeedTests`
Expected: FAIL — `db.CostingSettings` does not exist (compile error).

- [ ] **Step 3: Add the DbSet**

In `AppDbContext.cs`, next to `public DbSet<PostingConfiguration> PostingConfigurations => Set<PostingConfiguration>();` (line ~66):

```csharp
    public DbSet<CostingSetting> CostingSettings => Set<CostingSetting>();
```

- [ ] **Step 4: Add entity config + seed**

In `AppDbContext.cs`, immediately after the `PostingConfiguration` config block (closes ~line 928):

```csharp
        modelBuilder.Entity<CostingSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasData(new
            {
                Id = 1,
                Method = CostingMethod.MovingAverage,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });
```

- [ ] **Step 5: Register the table prefix**

In `AppDbContext.cs`, inside `tablePrefixes` (next to `[nameof(PostingConfiguration)] = "M_",` at line ~1018):

```csharp
            [nameof(CostingSetting)] = "M_",
```

- [ ] **Step 6: Create the migration**

Run: `dotnet ef migrations add AddCostingSetting --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Expected: a new `M_CostingSettings` table with an `InsertData` for `Id = 1, Method = "MovingAverage"`. Open the generated migration and confirm the seed row is present.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingSeedTests`
Expected: PASS. (No `CustomWebApplicationFactory` change needed — `EnsureCreated` applies `HasData` seeds automatically.)

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.IntegrationTests/CostingSettingSeedTests.cs
git commit -m "feat(costing): EF wiring + seed for CostingSetting (M_CostingSettings)"
```

---

### Task 3: Application + Infra — `ICostingSettingService`

**Files:**
- Create: `src/ErpOne.Application/Costing/ICostingSettingService.cs`
- Create: `src/ErpOne.Application/Costing/CostingDtos.cs`
- Create: `src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `CostingSetting`, `CostingMethod`.
- Produces:
  - `record CostingSettingDto(CostingMethod Method, bool Locked)`
  - `ICostingSettingService`: `Task<CostingMethod> GetMethodAsync(CancellationToken ct = default)`, `Task<CostingSettingDto> GetAsync(CancellationToken ct = default)`, `Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CostingSettingServiceTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private ICostingSettingService Svc(IServiceScope s) => s.ServiceProvider.GetRequiredService<ICostingSettingService>();

    [Fact]
    public async Task GetMethodAsync_defaults_to_moving_average()
    {
        using var scope = factory.Services.CreateScope();
        Assert.Equal(CostingMethod.MovingAverage, await Svc(scope).GetMethodAsync());
    }

    [Fact]
    public async Task UpdateMethodAsync_rejects_unsupported_method()
    {
        using var scope = factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Svc(scope).UpdateMethodAsync(CostingMethod.Fifo));
        Assert.Contains("belum didukung", ex.Message);
    }

    [Fact]
    public async Task UpdateMethodAsync_rejected_once_stock_movement_exists()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StockMovements.Add(new StockMovement(1, 1, MovementType.In, 1, 1000m, DateTime.UtcNow, refType: "Test"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Svc(scope).UpdateMethodAsync(CostingMethod.MovingAverage));
        Assert.Contains("terkunci", ex.Message);
    }
}
```

> Note: verify the `StockMovement` constructor signature against `GoodsReceiptService.cs:243` (`new StockMovement(variantId, warehouseId, MovementType, qty, unitCost, date, refType:, refId:, note:)`) and adjust the test's construction to match exactly.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: FAIL — `ICostingSettingService` does not exist (compile error).

- [ ] **Step 3: Create the DTO + interface**

```csharp
// src/ErpOne.Application/Costing/CostingDtos.cs
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Costing;

/// <summary>Metode HPP aktif + apakah terkunci (sudah ada StockMovement).</summary>
public record CostingSettingDto(CostingMethod Method, bool Locked);
```

```csharp
// src/ErpOne.Application/Costing/ICostingSettingService.cs
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Costing;

public interface ICostingSettingService
{
    Task<CostingMethod> GetMethodAsync(CancellationToken ct = default);
    Task<CostingSettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement the service**

```csharp
// src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CostingSettingService(AppDbContext db) : ICostingSettingService
{
    public async Task<CostingMethod> GetMethodAsync(CancellationToken ct = default) =>
        await db.CostingSettings.AsNoTracking().Select(x => x.Method).FirstOrDefaultAsync(ct);

    public async Task<CostingSettingDto> GetAsync(CancellationToken ct = default)
    {
        var method = await GetMethodAsync(ct);
        var locked = await db.StockMovements.AnyAsync(ct);
        return new CostingSettingDto(method, locked);
    }

    public async Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default)
    {
        if (method != CostingMethod.MovingAverage)
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);

        if (await db.StockMovements.AnyAsync(ct))
            throw new ValidationException([new ValidationFailure("Method", "Metode HPP terkunci: sudah ada transaksi stok.")]);

        var row = await db.CostingSettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CostingSetting seed row (Id=1) is missing.");
        row.SetMethod(method);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using near the other `ErpOne.Application.*` usings:

```csharp
using ErpOne.Application.Costing;
```

Add the registration alongside the other `AddScoped` service lines (near `IPostingConfigurationService`):

```csharp
        services.AddScoped<ICostingSettingService, CostingSettingService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingSettingServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/Costing/ src/ErpOne.Infrastructure/Services/Inventory/CostingSettingService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/CostingSettingServiceTests.cs
git commit -m "feat(costing): ICostingSettingService (read + lock method)"
```

---

### Task 4: Infra — local-aware on-hand helper

**Files:**
- Create: `src/ErpOne.Infrastructure/Persistence/StockReadExtensions.cs`
- Test: `tests/ErpOne.IntegrationTests/StockReadExtensionsTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `db.ProductStocks` (+ `.Local`).
- Produces: `static Task<int> TotalOnHandLocalAwareAsync(this AppDbContext db, int variantId, CancellationToken ct = default)` — total on-hand across ALL warehouses, counting tracked (`Local`, not yet flushed) rows plus untracked DB rows, with no double-count.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ErpOne.IntegrationTests/StockReadExtensionsTests.cs
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockReadExtensionsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Counts_unflushed_local_rows_without_double_counting_db_rows()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Two variants so this test is isolated from any seeded/other data: use a fresh variant id.
        // Arrange: one persisted row (warehouse 1) + one unflushed upsert into warehouse 1 + a new warehouse 2 row.
        var variantId = 99001;
        db.ProductStocks.Add(new ProductStock(variantId, 1, 10));
        await db.SaveChangesAsync();          // flushed: (v99001, wh1) = 10

        await db.UpsertStockAsync(variantId, 1, 5, default);   // tracked delta -> wh1 becomes 15 (unflushed)
        await db.UpsertStockAsync(variantId, 2, 7, default);   // new tracked row -> wh2 = 7 (unflushed)

        var total = await db.TotalOnHandLocalAwareAsync(variantId, default);
        Assert.Equal(22, total);              // 15 + 7, NOT 10 + 5 + 7 (=22 coincidentally) — see assertion note
    }
}
```

> Assertion note: the double-count trap is warehouse 1 (DB=10, tracked=15). A naive `dbSum + localSum` would give `10 + 15 + 7 = 32`. The correct local-aware total is `15 (tracked wh1) + 7 (tracked wh2) = 22`. The test value `22` distinguishes correct from naive.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StockReadExtensionsTests`
Expected: FAIL — `TotalOnHandLocalAwareAsync` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

```csharp
// src/ErpOne.Infrastructure/Persistence/StockReadExtensions.cs
using Microsoft.EntityFrameworkCore;

namespace ErpOne.Infrastructure.Persistence;

public static class StockReadExtensions
{
    /// <summary>Total on-hand varian di SEMUA gudang, memperhitungkan baris ProductStock yang sedang
    /// dilacak (Local, belum di-flush) agar konsisten dgn UpsertStockAsync. Baris Local dihitung penuh;
    /// baris DB pada (varian,gudang) yang sudah dilacak dikecualikan agar tidak dobel.</summary>
    public static async Task<int> TotalOnHandLocalAwareAsync(
        this AppDbContext db, int variantId, CancellationToken ct = default)
    {
        var local = db.ProductStocks.Local
            .Where(s => s.ProductVariantId == variantId)
            .ToList();
        var trackedWarehouses = local.Select(s => s.WarehouseId).Distinct().ToList();
        var localSum = local.Sum(s => s.Quantity);

        var dbSum = await db.ProductStocks
            .Where(s => s.ProductVariantId == variantId && !trackedWarehouses.Contains(s.WarehouseId))
            .SumAsync(s => (int?)s.Quantity, ct) ?? 0;

        return localSum + dbSum;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter StockReadExtensionsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/StockReadExtensions.cs tests/ErpOne.IntegrationTests/StockReadExtensionsTests.cs
git commit -m "feat(costing): local-aware on-hand helper (TotalOnHandLocalAwareAsync)"
```

---

### Task 5: Application + Infra — `ICostingService` (Moving Average strategy)

**Files:**
- Create: `src/ErpOne.Application/Costing/ICostingService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/CostingServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `ICostingSettingService`, `CostingMethod`, `db.TotalOnHandLocalAwareAsync` (Task 4), `ProductVariant.ApplyMovingAverage`, `ProductVariant.CostPrice`.
- Produces:
  - `ICostingService`: `Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)`, `Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing test (behavioral equivalence, same-variant multi-inbound)**

```csharp
// tests/ErpOne.IntegrationTests/CostingServiceTests.cs
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CostingServiceTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task OnInbound_two_receipts_same_variant_matches_manual_moving_average()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costing = scope.ServiceProvider.GetRequiredService<ICostingService>();

        // Fresh product + variant with a known opening cost basis.
        var product = new Product("Costing Test", null, null, null, null, true);
        var variant = product.AddVariant("SKU-COST-1", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Opening 100 @ 1000 in warehouse 1 (persisted), so CostPrice basis = 1000, on-hand = 100.
        db.ProductStocks.Add(new ProductStock(variant.Id, 1, 100));
        variant.GetType(); // (variant already tracked)
        typeof(ProductVariant).GetMethod("ApplyMovingAverage")!.Invoke(variant, new object[] { 0, 100, 1000m });
        await db.SaveChangesAsync();

        // Receipt line 1: +50 @ 1300, then line 2: +60 @ 1250 — same variant, one transaction, no flush between.
        await db.UpsertStockAsync(variant.Id, 1, 50, default);
        await costing.OnInboundAsync(variant.Id, 1, 50, 1300m, default);   // -> (100*1000 + 50*1300)/150 = 1100.00
        await db.UpsertStockAsync(variant.Id, 1, 60, default);
        await costing.OnInboundAsync(variant.Id, 1, 60, 1250m, default);   // -> (150*1100 + 60*1250)/210 = 1142.857 -> 1142.86

        await db.SaveChangesAsync();

        var costPrice = await db.ProductVariants.AsNoTracking().Where(v => v.Id == variant.Id)
            .Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(1142.86m, costPrice);

        var outbound = await costing.GetOutboundUnitCostAsync(variant.Id, 1, 10, default);
        Assert.Equal(1142.86m, outbound);
    }
}
```

> Note: `Product`/`AddVariant` constructor signatures — verify against `ProductService.cs:84` and `Product.cs`. Adjust the arg list to match the real signatures. The reflective `ApplyMovingAverage` seeding of the opening basis is only to avoid depending on GRN here; if `ProductVariant` exposes a simpler way to set the opening basis in tests already used elsewhere, prefer that.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingServiceTests`
Expected: FAIL — `ICostingService` does not exist (compile error).

- [ ] **Step 3: Create the interface**

```csharp
// src/ErpOne.Application/Costing/ICostingService.cs
namespace ErpOne.Application.Costing;

public interface ICostingService
{
    /// <summary>Dipanggil SETELAH UpsertStockAsync. Perbarui basis biaya varian dari mutasi masuk.</summary>
    Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);

    /// <summary>Unit cost untuk pengeluaran (caller mengalikan dengan qty sendiri).</summary>
    Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement the service (MA strategy)**

```csharp
// src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CostingService(AppDbContext db, ICostingSettingService settings) : ICostingService
{
    public async Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)
    {
        var method = await settings.GetMethodAsync(ct);
        switch (method)
        {
            case CostingMethod.MovingAverage:
                if (quantity <= 0) return;
                var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                    ?? throw new InvalidOperationException($"Variant {variantId} not found.");
                var totalAfter = await db.TotalOnHandLocalAwareAsync(variantId, ct);
                var totalBefore = totalAfter - quantity;
                variant.ApplyMovingAverage(totalBefore, quantity, unitCost);
                return;
            default:
                throw new NotSupportedException($"Costing method {method} is not supported in Tahap 1.");
        }
    }

    public async Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default)
    {
        var method = await settings.GetMethodAsync(ct);
        return method switch
        {
            CostingMethod.MovingAverage => await CurrentCostPriceAsync(variantId, ct),
            _ => throw new NotSupportedException($"Costing method {method} is not supported in Tahap 1.")
        };
    }

    // Membaca CostPrice dari entitas yang dilacak bila ada (agar melihat perubahan MA yang belum di-flush),
    // jika tidak, dari DB. Untuk MA, warehouseId & quantity diabaikan.
    private async Task<decimal> CurrentCostPriceAsync(int variantId, CancellationToken ct)
    {
        var tracked = db.ProductVariants.Local.FirstOrDefault(v => v.Id == variantId);
        if (tracked is not null) return tracked.CostPrice;
        return await db.ProductVariants.AsNoTracking()
            .Where(v => v.Id == variantId).Select(v => v.CostPrice).FirstOrDefaultAsync(ct);
    }
}
```

> Design note: MA `GetOutboundUnitCostAsync` reads the *current running* `CostPrice`. At every current outbound callsite the variant is loaded (tracked) just before the read, so `Local` returns the same instance today's code reads — no behavior change. The DB fallback covers callers that don't pre-load the variant.

- [ ] **Step 5: Register in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, next to the `ICostingSettingService` registration:

```csharp
        services.AddScoped<ICostingService, CostingService>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter CostingServiceTests`
Expected: PASS. `CostPrice == 1142.86`.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/Costing/ICostingService.cs src/ErpOne.Infrastructure/Services/Inventory/CostingService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/CostingServiceTests.cs
git commit -m "feat(costing): ICostingService moving-average strategy behind the seam"
```

---

### Task 6: Reroute inbound callsites through `OnInboundAsync`

Replace every direct `variant.ApplyMovingAverage(...)` inbound call with `costing.OnInboundAsync(...)` called AFTER `UpsertStockAsync`. This is a pure refactor — the whole regression suite is the test.

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Transactions/GoodsReceiptService.cs` (~228-253)
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/StockService.cs` (`RecordOpeningAsync` ~90-107, `RecordAdjustmentAsync` ~109-141, constructor ~10-13)

**Interfaces:**
- Consumes: `ICostingService.OnInboundAsync` (Task 5).

- [ ] **Step 1: Inject `ICostingService` into `GoodsReceiptService`**

Add `ICostingService costing` to the primary constructor parameter list (add `using ErpOne.Application.Costing;` at top). Example — if the current constructor is `public class GoodsReceiptService(AppDbContext db, IJournalPostingService journalPoster, ...)`, append `, ICostingService costing`.

- [ ] **Step 2: Rewrite the GRN posting loop body**

Replace lines ~226-253 (the `addedPerVariant` dictionary, per-line `dbTotal`/`totalBefore`, the `variant` load, `ApplyMovingAverage`) with:

```csharp
        foreach (var line in grn.Lines)
        {
            var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId)
                ?? throw Fail($"PO line {line.PurchaseOrderLineId} not found on PO {po.PoNumber}.");

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, po.WarehouseId, MovementType.In,
                line.QuantityReceived, line.UnitCost, grn.ReceiptDate, refType: "GRN", refId: grn.Id,
                note: grn.GrnNumber));

            await db.UpsertStockAsync(line.ProductVariantId, po.WarehouseId, line.QuantityReceived, ct);
            await costing.OnInboundAsync(line.ProductVariantId, po.WarehouseId, line.QuantityReceived, line.UnitCost, ct);
            poLine.ApplyReceipt(line.QuantityReceived, Tolerance);
        }
```

> The manual `addedPerVariant`/`dbTotal`/`totalBefore` bookkeeping is now handled inside `OnInboundAsync` via `TotalOnHandLocalAwareAsync` (called post-upsert). The variant-existence guard is redundant (GRN lines derive from validated PO lines) and moves into the costing service.

- [ ] **Step 3: Inject `ICostingService` into `StockService`**

```csharp
public class StockService(
    AppDbContext db,
    IValidator<StockAdjustmentRequest> adjustmentValidator,
    ICostingService costing) : IStockService
```

Add `using ErpOne.Application.Costing;` at the top.

- [ ] **Step 4: Rewrite `RecordOpeningAsync` (lines ~90-107)**

```csharp
    public async Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)
    {
        if (quantity == 0) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.ProductVariants.AnyAsync(v => v.Id == variantId, ct))
            throw new InvalidOperationException($"Variant {variantId} not found.");

        db.StockMovements.Add(new StockMovement(variantId, warehouseId, MovementType.Adjustment,
            quantity, unitCost, DateTime.UtcNow, refType: "Opening", note: "Saldo awal"));

        await db.UpsertStockAsync(variantId, warehouseId, quantity, ct);
        if (quantity > 0) await costing.OnInboundAsync(variantId, warehouseId, quantity, unitCost, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
```

- [ ] **Step 5: Rewrite the `RecordAdjustmentAsync` loop body (lines ~120-137)**

```csharp
        foreach (var line in request.Lines)
        {
            if (!await db.ProductVariants.AnyAsync(v => v.Id == line.VariantId, ct))
                throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                    "Lines", $"Variant {line.VariantId} not found.")]);

            // Mutasi masuk pakai UnitCost input; mutasi keluar pakai unit cost dari costing service (COGS).
            var unitCost = line.DeltaQuantity > 0
                ? line.UnitCost
                : await costing.GetOutboundUnitCostAsync(line.VariantId, request.WarehouseId, -line.DeltaQuantity, ct);

            db.StockMovements.Add(new StockMovement(line.VariantId, request.WarehouseId, MovementType.Adjustment,
                line.DeltaQuantity, unitCost, request.Date, refType: "Opname", note: line.Reason ?? request.Note));

            await db.UpsertStockAsync(line.VariantId, request.WarehouseId, line.DeltaQuantity, ct);
            if (line.DeltaQuantity > 0)
                await costing.OnInboundAsync(line.VariantId, request.WarehouseId, line.DeltaQuantity, line.UnitCost, ct);
        }
```

> `RecordAdjustmentAsync` positive delta previously used `line.UnitCost` for MA; negative delta used `variant.CostPrice`. Both preserved: positive → `OnInboundAsync(..., line.UnitCost)`; negative → `GetOutboundUnitCostAsync` (returns running CostPrice for MA). Step 5 also drops the now-unused `variant` load and `totalBefore`.

- [ ] **Step 6: Build + run the full suite (regression gate)**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: build 0 errors/0 warnings; **all tests pass, no changed numbers** (GRN multi-line, opening, adjustment).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Transactions/GoodsReceiptService.cs src/ErpOne.Infrastructure/Services/Inventory/StockService.cs
git commit -m "refactor(costing): route inbound (GRN, opening, adjustment+) through ICostingService"
```

---

### Task 7: Reroute outbound callsites through `GetOutboundUnitCostAsync`

Replace every direct `variant.CostPrice` read used as an outbound/COGS unit cost with `await costing.GetOutboundUnitCostAsync(...)`. Pure refactor; regression suite is the test.

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs` (~90-100, constructor)
- Modify: `src/ErpOne.Infrastructure/Services/Transactions/DeliveryOrderService.cs` (~250-258, constructor)
- Modify: `src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs` (~147-156, constructor)
- Modify: `src/ErpOne.Infrastructure/Services/Inventory/StockOpnameService.cs` (~151-161, constructor)

**Interfaces:**
- Consumes: `ICostingService.GetOutboundUnitCostAsync` (Task 5).

- [ ] **Step 1: PosSaleService** — inject `ICostingService costing` (+ `using ErpOne.Application.Costing;`). In the sale loop, compute the cost once and reuse it for both the line snapshot and the movement:

```csharp
        foreach (var line in request.Lines)
        {
            var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Varian {line.ProductVariantId} tidak ditemukan.");
            var name = await db.Products.Where(p => p.Id == v.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "—";

            var unitCost = await costing.GetOutboundUnitCostAsync(v.Id, whId, line.Quantity, ct);
            sale.AddLine(v.Id, v.Sku, name, line.Quantity, line.UnitPrice, line.DiscountPercent, unitCost);

            db.StockMovements.Add(new StockMovement(v.Id, whId, MovementType.Out,
                -line.Quantity, unitCost, now, refType: "POS", refId: null, note: sale.SaleNumber));
            await db.UpsertStockAsync(v.Id, whId, -line.Quantity, ct);
        }
```

- [ ] **Step 2: DeliveryOrderService** — inject `ICostingService costing` (+ using). In the Fase-2 mutation loop, replace the two `variant.CostPrice` reads:

```csharp
        foreach (var line in doc.Lines)
        {
            var soLine = so.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId)
                ?? throw Fail($"SO line {line.SalesOrderLineId} not found on SO {so.SoNumber}.");

            var unitCost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, so.WarehouseId, line.QuantityDelivered, ct);

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, so.WarehouseId, MovementType.Out,
                -line.QuantityDelivered, unitCost, doc.DeliveryDate, refType: "DO", refId: doc.Id,
                note: doc.DoNumber));

            await db.UpsertStockAsync(line.ProductVariantId, so.WarehouseId, -line.QuantityDelivered, ct);
            line.SetUnitCost(unitCost); // COGS snapshot; MA TIDAK diubah
            soLine.ApplyDelivery(line.QuantityDelivered);
        }
```

> The `variant` load at ~250 becomes unused — remove it. (Availability was already validated in Fase 1.)

- [ ] **Step 3: StockTransferService** — inject `ICostingService costing` (+ using). Replace the `cost` read at ~149:

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
        }
```

> Transfer is an internal move at the same unit cost on both legs (value unchanged) — `GetOutboundUnitCostAsync` returns the running MA cost, identical to today.

- [ ] **Step 4: StockOpnameService** — inject `ICostingService costing` (+ using). Replace the `cost` read at ~156:

```csharp
        foreach (var line in o.Lines)
        {
            var onHand = await stock.GetOnHandAsync(line.ProductVariantId, o.WarehouseId, ct);
            var delta = line.PhysicalQty - onHand;
            if (delta == 0) continue;
            var cost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, o.WarehouseId, Math.Abs(delta), ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, o.WarehouseId, MovementType.Adjustment,
                delta, cost, o.OpnameDate, "StockOpname", o.Id, o.OpnameNumber));
            await db.UpsertStockAsync(line.ProductVariantId, o.WarehouseId, delta, ct);
        }
```

> Opname posts the variance at the running cost and never recomputes MA (spec §A.3) — even a positive variance uses `GetOutboundUnitCostAsync`, matching current behavior (the old code used `variant.CostPrice` for both signs).

- [ ] **Step 5: Build + run the full suite (regression gate)**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: build 0 errors/0 warnings; all tests pass with no changed numbers (POS COGS, DO COGS, transfer, opname, adjustment−).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs src/ErpOne.Infrastructure/Services/Transactions/DeliveryOrderService.cs src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs src/ErpOne.Infrastructure/Services/Inventory/StockOpnameService.cs
git commit -m "refactor(costing): route outbound COGS (POS, DO, transfer, opname) through ICostingService"
```

---

### Task 8: Web — read-only "Costing Method" settings card + permission

Minimal Tahap 1 surface: show the active method + locked indicator. Selector stays disabled (only MovingAverage exists).

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (Settings group)
- Verify: permission auto-seeds via `BootstrapSeeder` (resources derived from `AppMenus`)

**Interfaces:**
- Consumes: `ICostingSettingService.GetAsync` → `CostingSettingDto(Method, Locked)`.

- [ ] **Step 1: Add the menu resource**

In `AppMenus.cs`, in the Settings group (near `settings.posting-configuration` if present, else the Settings `ResourceGroup`), add:

```csharp
            new("settings.costing", "Costing Method", "bi-calculator", ViewOnly),
```

> Use the same `ViewOnly` action set other read-only settings pages use. Confirm the icon token style matches sibling entries.

- [ ] **Step 2: Create the page**

```razor
@* src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor *@
@page "/settings/costing"
@using ErpOne.Application.Costing
@attribute [Authorize(Policy = "settings.costing.index")]
@inject ICostingSettingService Costing
@rendermode InteractiveServer

<PageTitle>Costing Method</PageTitle>

<div class="pi">
    <div class="pi-hero">
        <h1>Costing Method</h1>
        <p>Metode penilaian HPP (Harga Pokok) yang dipakai seluruh transaksi stok.</p>
    </div>

    @if (_dto is null)
    {
        <p>Loading…</p>
    }
    else
    {
        <div class="cf">
            <label class="cf-field">
                <span>Metode aktif</span>
                <select class="ctl" disabled>
                    <option>@_dto.Method (Moving Average)</option>
                </select>
            </label>
            <p class="cf-hint">
                @if (_dto.Locked)
                {
                    <span>🔒 Terkunci — sudah ada transaksi stok. Metode tidak dapat diubah.</span>
                }
                else
                {
                    <span>Belum ada transaksi stok. Metode masih dapat diubah (metode lain menyusul).</span>
                }
            </p>
        </div>
    }
</div>

@code {
    private CostingSettingDto? _dto;
    protected override async Task OnInitializedAsync() => _dto = await Costing.GetAsync();
}
```

> Match the real class names/markup conventions of an existing read-only settings page (e.g. `PostingConfigForm.razor`) — the `.pi`/`.cf` classes, `Authorize` policy naming (`<resource>.<action>`), and `@rendermode` must mirror siblings exactly. Adjust if this project's policy string format differs.

- [ ] **Step 3: Build + verify permission seeding**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: build clean; suite green. If a test asserts the full permission/menu set, update its expected list to include `settings.costing`.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor src/ErpOne.Web/Authorization/AppMenus.cs
git commit -m "feat(costing): read-only Costing Method settings page + permission"
```

---

### Task 9: Final regression + self-review

- [ ] **Step 1: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors, 0 warnings; unit + integration suites fully green with no changed assertions from before Tahap 1 (except the new tests added here).

- [ ] **Step 2: Grep for stragglers**

Run: `git grep -n "ApplyMovingAverage" -- src/ErpOne.Infrastructure`
Expected: only `CostingService.cs` calls it. Any other infra callsite is a missed refactor — fix it.

Run: `git grep -n "\.CostPrice" -- src/ErpOne.Infrastructure/Services`
Expected: remaining hits are read-only projections (stock levels, PO suggested price, product DTOs) — NOT outbound movement costs. Confirm none feed a `StockMovement`/COGS.

- [ ] **Step 3: Verify no behavior change claim**

Confirm the integration test count only grew by the tests this plan added and that no pre-existing test's expected numbers changed. This is the "no behavior change" evidence.

- [ ] **Step 4: Final commit (if any fixes)**

```bash
git add -A
git commit -m "chore(costing): Tahap 1 abstraction complete — MA behavior unchanged"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** enum + `CostingSetting` (Task 1) ✓ §1; EF wiring + seed + prefix + migration (Task 2) ✓ §4; `ICostingSettingService` with lock + unsupported rejection (Task 3) ✓ §2/§2-dto; local-aware helper (Task 4) ✓ §3; `ICostingService` MA strategy (Task 5) ✓ §2/§3; inbound refactor GRN/opening/adjustment+ (Task 6) ✓ §5-inbound; outbound refactor POS/DO/transfer/opname/adjustment− (Task 7) ✓ §5-outbound; web read-only card + permission (Task 8) ✓ §6; regression gate (Tasks 6,7,9) ✓ §7.

**PosRefund:** intentionally NOT rerouted — refund stock-in uses the sale line's snapshot cost and never recomputes MA (spec §A.3 / auto-posting §C.2); it does not read `variant.CostPrice`. Confirmed no costing seam needed.

**Type consistency:** `OnInboundAsync(int, int, int, decimal, ct)` and `GetOutboundUnitCostAsync(int, int, int, ct)` used identically across Tasks 5–7. `CostingSettingDto(CostingMethod, bool)`, `ICostingSettingService.GetMethodAsync/GetAsync/UpdateMethodAsync` consistent Tasks 3/5/8.

**Verify-before-embed flags:** three test/code spots depend on signatures to confirm at implementation time (flagged inline): `StockMovement` ctor, `Product`/`AddVariant` ctor, and the existing read-only settings page conventions (`.pi/.cf`, policy string, rendermode). These are noted, not placeholders — the surrounding logic is complete.
