# F2 Stock Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the temporary `ProductVariant.Stock` column with a per-warehouse ledger (`StockMovement`) + materialized balance (`ProductStock`), add a manual stock-adjustment/opname UI, and compute Moving-Average cost (HPP) on inbound movements.

**Architecture:** `StockMovement` is the append-only source of truth; `ProductStock` is a per-(variant,warehouse) balance cache updated in the same DB transaction. Moving-Average cost lives on `ProductVariant.CostPrice` (global per variant, recomputed on inbound). All stock changes flow through `IStockService`. Existing `variant.Stock` data is migrated into opening `StockMovement(Adjustment)` rows in the default warehouse, then the column is dropped.

**Tech Stack:** .NET 10, Blazor (InteractiveServer), EF Core (SQL Server), FluentValidation, Clean Architecture (Domain / Application / Infrastructure / Web), service-based pattern.

**Spec:** `docs/superpowers/specs/2026-06-19-f2-stock-model-design.md`

## Global Constraints

- Rich domain entities: `private set`, factory constructor, validation inside entity (`SetXxx`), `Id` `private set`; inherit `AuditableEntity`.
- EF config is **inline** in `AppDbContext.OnModelCreating` (no separate `IEntityTypeConfiguration` files — match existing style).
- `decimal` money columns use `HasPrecision(18, 2)`; weight uses `(18, 3)`.
- Migrations are EF Core; data backfill is hand-edited raw SQL and must be **idempotent** and assume exactly one `Warehouse` with `IsDefault = 1` (seeded in F0 with `Id = 1`, `Code = "WH-MAIN"`).
- Authorization: every UI resource is an `AppResource` in `AppMenus.Groups`; `BootstrapSeeder` auto-grants `AppMenus.AllPermissions` to the admin role and `PermissionPolicyProvider` auto-creates policies — **no extra wiring needed** when adding resources.
- Blazor pages live under `Components/Pages/<Group>/<Name>/` as `XxxIndex.razor` + `XxxForm.razor`, `@rendermode InteractiveServer`, guarded by `[Authorize(Policy = "...")]`.
- **Testing per F1 policy:** keep tests compiling; do NOT treat tests as a gate. Verification = `dotnet build MyApp.slnx` (0 errors) + `dotnet ef database update` applies cleanly.
- `StockMovement` is immutable: no `Update`/`Delete` methods; corrections are new movements.
- Moving Average is **global per variant** (one HPP number on `ProductVariant.CostPrice`), not per warehouse.
- `Quantity` on movements/balances is `int` (matches the old `Stock` type).

---

## File Structure

| File | Responsibility |
|------|----------------|
| `MyApp.Domain/Entities/MovementType.cs` | enum In/Out/Transfer/Adjustment |
| `MyApp.Domain/Entities/StockMovement.cs` | immutable ledger entry |
| `MyApp.Domain/Entities/ProductStock.cs` | materialized balance per (variant,warehouse) |
| `MyApp.Domain/Entities/ProductVariant.cs` | remove `Stock`; add `ApplyMovingAverage` |
| `MyApp.Domain/Entities/Product.cs` | `AddVariant` loses `stock` param |
| `MyApp.Application/Stock/IStockService.cs` | stock operations contract |
| `MyApp.Application/Stock/StockDtos.cs` | DTOs + request records |
| `MyApp.Application/Stock/StockValidators.cs` | FluentValidation for adjustment request |
| `MyApp.Infrastructure/Services/StockService.cs` | service implementation |
| `MyApp.Infrastructure/Persistence/AppDbContext.cs` | 2 DbSets + config |
| `MyApp.Infrastructure/Persistence/Migrations/*_AddStockModel.cs` | create tables + backfill |
| `MyApp.Infrastructure/Persistence/Migrations/*_DropVariantStock.cs` | drop `ProductVariant.Stock` |
| `MyApp.Infrastructure/Services/ProductService.cs` | read stock from `ProductStock`; opening movements; dashboard |
| `MyApp.Infrastructure/DependencyInjection.cs` | register `IStockService` |
| `MyApp.Web/Authorization/AppMenus.cs` | new "Inventory" group |
| `MyApp.Web/Components/Pages/Inventory/StockLevels/StockLevelIndex.razor` | balances list |
| `MyApp.Web/Components/Pages/Inventory/StockAdjustments/StockAdjustmentIndex.razor` | history list |
| `MyApp.Web/Components/Pages/Inventory/StockAdjustments/StockAdjustmentForm.razor` | opname entry |
| `MyApp.Web/Components/Pages/Master/Products/ProductForm.razor` | opening stock (create) / read-only (edit) |

---

## Task 1: Domain — stock entities + Moving Average

**Files:**
- Create: `src/MyApp.Domain/Entities/MovementType.cs`
- Create: `src/MyApp.Domain/Entities/StockMovement.cs`
- Create: `src/MyApp.Domain/Entities/ProductStock.cs`
- Modify: `src/MyApp.Domain/Entities/ProductVariant.cs` (add `ApplyMovingAverage` only — keep `Stock` for now)
- Test: `tests/MyApp.UnitTests/StockDomainTests.cs`

**Interfaces:**
- Produces:
  - `enum MovementType { In, Out, Transfer, Adjustment }`
  - `StockMovement(int productVariantId, int warehouseId, MovementType type, int quantity, decimal unitCost, DateTime movementDate, string? refType = null, int? refId = null, string? note = null)` with read-only props `Id, ProductVariantId, WarehouseId, Type, Quantity, UnitCost, MovementDate, RefType, RefId, Note`.
  - `ProductStock(int productVariantId, int warehouseId, int quantity)` with props `Id, ProductVariantId, WarehouseId, Quantity`; method `void ApplyDelta(int delta)`.
  - `ProductVariant.ApplyMovingAverage(int totalQtyBefore, int inQty, decimal inUnitCost)` — recomputes `CostPrice`.

- [ ] **Step 1: Write the failing test**

Create `tests/MyApp.UnitTests/StockDomainTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class StockDomainTests
{
    [Fact]
    public void ProductStock_ApplyDelta_increases_and_decreases()
    {
        var s = new ProductStock(1, 1, 10);
        s.ApplyDelta(5);
        Assert.Equal(15, s.Quantity);
        s.ApplyDelta(-7);
        Assert.Equal(8, s.Quantity);
    }

    [Fact]
    public void ProductStock_ApplyDelta_rejects_negative_result()
    {
        var s = new ProductStock(1, 1, 3);
        Assert.Throws<InvalidOperationException>(() => s.ApplyDelta(-4));
    }

    [Fact]
    public void StockMovement_rejects_zero_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            new StockMovement(1, 1, MovementType.Adjustment, 0, 0m, new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void ApplyMovingAverage_computes_weighted_average()
    {
        // 10 @ 1000 already on hand (CostPrice 1000), add 10 @ 2000 -> avg 1500
        var v = MakeVariant(costPrice: 1000m);
        v.ApplyMovingAverage(totalQtyBefore: 10, inQty: 10, inUnitCost: 2000m);
        Assert.Equal(1500m, v.CostPrice);
    }

    [Fact]
    public void ApplyMovingAverage_from_zero_onhand_takes_incoming_cost()
    {
        var v = MakeVariant(costPrice: 0m);
        v.ApplyMovingAverage(totalQtyBefore: 0, inQty: 5, inUnitCost: 1234m);
        Assert.Equal(1234m, v.CostPrice);
    }

    [Fact]
    public void ApplyMovingAverage_ignores_non_positive_inQty()
    {
        var v = MakeVariant(costPrice: 500m);
        v.ApplyMovingAverage(totalQtyBefore: 10, inQty: 0, inUnitCost: 9999m);
        Assert.Equal(500m, v.CostPrice);
    }

    private static ProductVariant MakeVariant(decimal costPrice) =>
        new("SKU-1", null, price: 100m, discountPrice: null, costPrice: costPrice,
            weight: null, dimensions: null, stock: 0, isActive: true);
}
```

> NOTE: `MakeVariant` passes `stock: 0` to match the **current** `ProductVariant` constructor. Task 1 does NOT change that constructor — it only adds `ApplyMovingAverage` and the new entities. The `stock` parameter is removed later in Task 6, at which point this test is updated to drop `stock: 0`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter StockDomainTests`
Expected: FAIL — `MovementType`/`StockMovement`/`ProductStock` not defined (the new types don't exist yet).

- [ ] **Step 3: Create the enum and entities, and add ApplyMovingAverage**

Create `src/MyApp.Domain/Entities/MovementType.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Jenis mutasi stok pada buku besar.</summary>
public enum MovementType
{
    In,
    Out,
    Transfer,
    Adjustment
}
```

Create `src/MyApp.Domain/Entities/StockMovement.cs`:

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Buku besar mutasi stok — append-only (tidak pernah diubah/dihapus).</summary>
public class StockMovement : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public MovementType Type { get; private set; }
    public int Quantity { get; private set; }          // bertanda: + masuk / − keluar
    public decimal UnitCost { get; private set; }       // HPP per unit pada mutasi
    public DateTime MovementDate { get; private set; }
    public string? RefType { get; private set; }
    public int? RefId { get; private set; }
    public string? Note { get; private set; }

    private StockMovement() { } // EF Core

    public StockMovement(int productVariantId, int warehouseId, MovementType type, int quantity,
        decimal unitCost, DateTime movementDate, string? refType = null, int? refId = null, string? note = null)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity == 0) throw new ArgumentException("Quantity must not be zero.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("Unit cost must be >= 0.", nameof(unitCost));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        Type = type;
        Quantity = quantity;
        UnitCost = unitCost;
        MovementDate = movementDate;
        RefType = string.IsNullOrWhiteSpace(refType) ? null : refType.Trim();
        RefId = refId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
```

Create `src/MyApp.Domain/Entities/ProductStock.cs`:

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Saldo stok materialized per (varian, gudang). Sumber kebenaran tetap StockMovement.</summary>
public class ProductStock : AuditableEntity
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public int Quantity { get; private set; }

    private ProductStock() { } // EF Core

    public ProductStock(int productVariantId, int warehouseId, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity < 0) throw new ArgumentException("Quantity must be >= 0.", nameof(quantity));

        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        Quantity = quantity;
    }

    /// <summary>Tambah/kurangi saldo; tolak hasil negatif.</summary>
    public void ApplyDelta(int delta)
    {
        var result = Quantity + delta;
        if (result < 0)
            throw new InvalidOperationException("Stock cannot go negative.");
        Quantity = result;
    }
}
```

In `src/MyApp.Domain/Entities/ProductVariant.cs`, add the Moving-Average method (place it after `SetAttributeValues`). Do **not** remove `Stock` yet:

```csharp
    /// <summary>Hitung ulang HPP (Moving Average) saat ada mutasi masuk. CostPrice di-bulatkan 2 desimal.</summary>
    public void ApplyMovingAverage(int totalQtyBefore, int inQty, decimal inUnitCost)
    {
        if (inQty <= 0) return;
        if (inUnitCost < 0) throw new ArgumentException("Unit cost must be >= 0.", nameof(inUnitCost));
        if (totalQtyBefore < 0) totalQtyBefore = 0;
        var totalAfter = totalQtyBefore + inQty;
        var newCost = (totalQtyBefore * CostPrice + inQty * inUnitCost) / totalAfter;
        CostPrice = Math.Round(newCost, 2, MidpointRounding.AwayFromZero);
    }
```

**Do NOT touch the `ProductVariant` constructor, `Update`, or `Stock` property in this task.** Removing `Stock` cascades into `ProductService`, `Product.AddVariant`, DTOs, validators, and `ProductForm`; to keep every task's build green, all of those move together in **Task 6**. Task 1 is purely additive: the new enum + two entities + the `ApplyMovingAverage` method. `Stock` stays exactly as it is.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter StockDomainTests`
Expected: PASS (5 tests). Then `dotnet build MyApp.slnx` → 0 errors (Stock still present, nothing else broken).

- [ ] **Step 5: Commit**

(No git in this repo — record progress in the F2 progress ledger instead; see "Progress tracking" at the end.)

---

## Task 2: DbContext config + migration to create stock tables

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/<timestamp>_AddStockModel.cs` (scaffold, then hand-edit)

**Interfaces:**
- Consumes: `StockMovement`, `ProductStock`, `MovementType` (Task 1).
- Produces: `db.StockMovements`, `db.ProductStocks` DbSets; tables `StockMovements`, `ProductStocks` with unique index `(ProductVariantId, WarehouseId)` on `ProductStocks`.

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `ProductImages` DbSet (line ~18) add:

```csharp
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
```

- [ ] **Step 2: Add entity configuration**

In `OnModelCreating`, after the `ProductVariantAttribute` config block (line ~93) add:

```csharp
        modelBuilder.Entity<StockMovement>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(m => m.UnitCost).HasPrecision(18, 2);
            e.Property(m => m.RefType).HasMaxLength(50);
            e.Property(m => m.Note).HasMaxLength(500);

            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(m => m.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany()
                .HasForeignKey(m => m.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(m => new { m.ProductVariantId, m.WarehouseId });
        });

        modelBuilder.Entity<ProductStock>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ProductVariantId, s.WarehouseId }).IsUnique();

            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(s => s.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany()
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 3: Scaffold the migration**

Run from `src/MyApp.Infrastructure`:
`dotnet ef migrations add AddStockModel --startup-project ../MyApp.Web`
Expected: scaffold creates `*_AddStockModel.cs` containing `CreateTable` for `ProductStocks` and `StockMovements`. It will **not** include backfill or any drop (the `Stock` column is untouched here).

- [ ] **Step 4: Hand-edit the migration to backfill opening stock**

Open the generated `*_AddStockModel.cs`. After the two `CreateTable` calls and their indexes (i.e. at the end of `Up`, before it returns), append the backfill SQL. Keep the scaffolded table/index creation as-is. Add:

```csharp
            // Backfill: seed opening balances from the temporary ProductVariants.Stock into the
            // default warehouse. Idempotent: only inserts when no movement/stock row exists yet.
            // Assumes exactly one Warehouse with IsDefault = 1 (seeded in F0).
            migrationBuilder.Sql(@"
                DECLARE @wh INT = (SELECT TOP 1 Id FROM Warehouses WHERE IsDefault = 1 ORDER BY Id);
                IF @wh IS NULL
                    THROW 50000, 'No default warehouse (IsDefault=1) found; cannot backfill stock.', 1;

                INSERT INTO StockMovements
                    (ProductVariantId, WarehouseId, Type, Quantity, UnitCost, MovementDate, RefType, RefId, Note, CreatedAt, CreatedBy)
                SELECT v.Id, @wh, 'Adjustment', v.Stock, v.CostPrice, SYSUTCDATETIME(), 'Opening', NULL,
                       N'Saldo awal migrasi F2', SYSUTCDATETIME(), 'migration'
                FROM ProductVariants v
                WHERE v.Stock > 0
                  AND NOT EXISTS (SELECT 1 FROM StockMovements m
                                  WHERE m.ProductVariantId = v.Id AND m.RefType = 'Opening');

                INSERT INTO ProductStocks
                    (ProductVariantId, WarehouseId, Quantity, CreatedAt, CreatedBy)
                SELECT v.Id, @wh, v.Stock, SYSUTCDATETIME(), 'migration'
                FROM ProductVariants v
                WHERE v.Stock > 0
                  AND NOT EXISTS (SELECT 1 FROM ProductStocks s
                                  WHERE s.ProductVariantId = v.Id AND s.WarehouseId = @wh);
            ");
```

In `Down`, **before** the scaffolded `DropTable` calls, add nothing extra — dropping the tables discards the backfilled rows automatically. Leave the scaffolded `Down` as generated.

- [ ] **Step 5: Apply the migration**

Run from `src/MyApp.Infrastructure`:
`dotnet ef database update --startup-project ../MyApp.Web`
Expected: applies cleanly; `ProductStocks` and `StockMovements` created; opening rows present for variants that had `Stock > 0`.

Verify (optional): `SELECT COUNT(*) FROM StockMovements WHERE RefType='Opening';` matches the number of variants with `Stock > 0`.

- [ ] **Step 6: Build + commit**

Run: `dotnet build MyApp.slnx` → 0 errors. Record progress in the ledger.

---

## Task 3: Application — IStockService, DTOs, validators

**Files:**
- Create: `src/MyApp.Application/Stock/IStockService.cs`
- Create: `src/MyApp.Application/Stock/StockDtos.cs`
- Create: `src/MyApp.Application/Stock/StockValidators.cs`

**Interfaces:**
- Consumes: `MovementType` (Domain), `PagedResult<T>` (`MyApp.Application.Common`).
- Produces:
  - `IStockService` with: `GetLevelsByVariantAsync(int variantId, ct)`, `GetOnHandAsync(int variantId, int warehouseId, ct)`, `GetMovementsByVariantAsync(int variantId, ct)`, `GetLevelsPagedAsync(int page, int pageSize, int? warehouseId, string? search, ct)`, `RecordAdjustmentAsync(StockAdjustmentRequest, ct)`, `RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, ct)`.
  - Records: `StockLevelDto`, `StockMovementDto`, `StockAdjustmentRequest`, `StockAdjustmentLine`.

- [ ] **Step 1: Create the DTOs**

Create `src/MyApp.Application/Stock/StockDtos.cs`:

```csharp
using MyApp.Domain.Entities;

namespace MyApp.Application.Stock;

public record StockLevelDto(
    int VariantId, string Sku, string ProductName,
    int WarehouseId, string WarehouseName,
    int Quantity, decimal CostPrice);

public record StockMovementDto(
    int Id, int VariantId, int WarehouseId, string WarehouseName,
    MovementType Type, int Quantity, decimal UnitCost,
    DateTime MovementDate, string? RefType, string? Note);

/// <summary>Satu baris opname: selisih qty (bertanda) untuk satu varian.</summary>
public record StockAdjustmentLine(int VariantId, int DeltaQuantity, decimal UnitCost, string? Reason);

public record StockAdjustmentRequest(
    int WarehouseId, DateTime Date, string? Note,
    IReadOnlyList<StockAdjustmentLine> Lines);
```

- [ ] **Step 2: Create the service interface**

Create `src/MyApp.Application/Stock/IStockService.cs`:

```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Stock;

public interface IStockService
{
    Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default);
    Task<int> GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovementDto>> GetMovementsByVariantAsync(int variantId, CancellationToken ct = default);
    Task<PagedResult<StockLevelDto>> GetLevelsPagedAsync(
        int page, int pageSize, int? warehouseId, string? search, CancellationToken ct = default);

    /// <summary>Opname: terapkan selisih bertanda per varian dalam satu transaksi.</summary>
    Task RecordAdjustmentAsync(StockAdjustmentRequest request, CancellationToken ct = default);

    /// <summary>Saldo awal: satu mutasi masuk (Adjustment/Opening) + recompute Moving Average.</summary>
    Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create the validator**

Create `src/MyApp.Application/Stock/StockValidators.cs`:

```csharp
using FluentValidation;

namespace MyApp.Application.Stock;

public class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage("Warehouse is required.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new StockAdjustmentLineValidator());
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.VariantId).Distinct().Count() == lines.Count)
            .WithMessage("Each variant may appear only once per adjustment.")
            .When(x => x.Lines is { Count: > 0 });
    }
}

public class StockAdjustmentLineValidator : AbstractValidator<StockAdjustmentLine>
{
    public StockAdjustmentLineValidator()
    {
        RuleFor(l => l.VariantId).GreaterThan(0);
        RuleFor(l => l.DeltaQuantity).NotEqual(0).WithMessage("Delta quantity must not be zero.");
        RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0);
        RuleFor(l => l.Reason).MaximumLength(200);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build MyApp.slnx` → 0 errors (interface + DTOs + validator only; no consumers yet).

- [ ] **Step 5: Commit** — record progress in the ledger.

---

## Task 4: Infrastructure — StockService implementation + DI

**Files:**
- Create: `src/MyApp.Infrastructure/Services/StockService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `IStockService`, DTOs (Task 3); `AppDbContext` with `StockMovements`/`ProductStocks` (Task 2); domain entities (Task 1).
- Produces: `StockService : IStockService`; DI registration `services.AddScoped<IStockService, StockService>();`.

- [ ] **Step 1: Create the service**

Create `src/MyApp.Infrastructure/Services/StockService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.Stock;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class StockService(
    AppDbContext db,
    IValidator<StockAdjustmentRequest> adjustmentValidator) : IStockService
{
    public async Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default) =>
        await BuildLevelQuery(db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId))
            .ToListAsync(ct);

    public async Task<int> GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct = default) =>
        await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            .Select(s => (int?)s.Quantity).FirstOrDefaultAsync(ct) ?? 0;

    public async Task<IReadOnlyList<StockMovementDto>> GetMovementsByVariantAsync(int variantId, CancellationToken ct = default) =>
        await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductVariantId == variantId)
            .OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id)
            .Join(db.Warehouses.AsNoTracking(), m => m.WarehouseId, w => w.Id, (m, w) => new StockMovementDto(
                m.Id, m.ProductVariantId, m.WarehouseId, w.Name,
                m.Type, m.Quantity, m.UnitCost, m.MovementDate, m.RefType, m.Note))
            .ToListAsync(ct);

    public async Task<PagedResult<StockLevelDto>> GetLevelsPagedAsync(
        int page, int pageSize, int? warehouseId, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.ProductStocks.AsNoTracking();
        if (warehouseId is int wid) q = q.Where(s => s.WarehouseId == wid);

        var levels = BuildLevelQuery(q);
        if (!string.IsNullOrWhiteSpace(search))
            levels = levels.Where(l => l.Sku.Contains(search) || l.ProductName.Contains(search));

        var total = await levels.CountAsync(ct);
        var items = await levels.OrderBy(l => l.ProductName).ThenBy(l => l.Sku)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<StockLevelDto>(items, total, page, pageSize);
    }

    /// <summary>Proyeksi ProductStock -> StockLevelDto dengan SKU/nama produk/nama gudang/HPP.</summary>
    private IQueryable<StockLevelDto> BuildLevelQuery(IQueryable<ProductStock> source) =>
        from s in source
        join v in db.ProductVariants.AsNoTracking() on s.ProductVariantId equals v.Id
        join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
        join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
        select new StockLevelDto(v.Id, v.Sku, p.Name, w.Id, w.Name, s.Quantity, v.CostPrice);

    public async Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)
    {
        if (quantity == 0) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
            ?? throw new InvalidOperationException($"Variant {variantId} not found.");
        var totalBefore = await db.ProductStocks.Where(s => s.ProductVariantId == variantId).SumAsync(s => (int?)s.Quantity, ct) ?? 0;

        db.StockMovements.Add(new StockMovement(variantId, warehouseId, MovementType.Adjustment,
            quantity, unitCost, DateTime.UtcNow, refType: "Opening", note: "Saldo awal"));

        await UpsertStockAsync(variantId, warehouseId, quantity, ct);
        if (quantity > 0) variant.ApplyMovingAverage(totalBefore, quantity, unitCost);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecordAdjustmentAsync(StockAdjustmentRequest request, CancellationToken ct = default)
    {
        await adjustmentValidator.ValidateAndThrowAsync(request, ct);

        var whExists = await db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId, ct);
        if (!whExists)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                nameof(StockAdjustmentRequest.WarehouseId), "Warehouse not found.")]);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in request.Lines)
        {
            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.VariantId, ct)
                ?? throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                    "Lines", $"Variant {line.VariantId} not found.")]);

            var totalBefore = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.VariantId).SumAsync(s => (int?)s.Quantity, ct) ?? 0;

            // Mutasi masuk pakai UnitCost input; mutasi keluar pakai HPP saat ini (COGS), MA tidak berubah.
            var unitCost = line.DeltaQuantity > 0 ? line.UnitCost : variant.CostPrice;

            db.StockMovements.Add(new StockMovement(line.VariantId, request.WarehouseId, MovementType.Adjustment,
                line.DeltaQuantity, unitCost, request.Date, refType: "Opname", note: line.Reason ?? request.Note));

            await UpsertStockAsync(line.VariantId, request.WarehouseId, line.DeltaQuantity, ct);
            if (line.DeltaQuantity > 0) variant.ApplyMovingAverage(totalBefore, line.DeltaQuantity, line.UnitCost);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Buat/Update baris ProductStock (varian+gudang) dengan delta bertanda; tolak hasil negatif.</summary>
    private async Task UpsertStockAsync(int variantId, int warehouseId, int delta, CancellationToken ct)
    {
        var stock = await db.ProductStocks
            .FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct);
        if (stock is null)
        {
            if (delta < 0) throw new InvalidOperationException("Stock cannot go negative.");
            db.ProductStocks.Add(new ProductStock(variantId, warehouseId, delta));
        }
        else
        {
            stock.ApplyDelta(delta);
        }
    }
}
```

- [ ] **Step 2: Register in DI**

In `DependencyInjection.cs`, add the using and registration. After `using MyApp.Application.Attributes;` add:

```csharp
using MyApp.Application.Stock;
```

After `services.AddScoped<IAttributeService, AttributeService>();` add:

```csharp
        services.AddScoped<IStockService, StockService>();
```

(The existing `AddValidatorsFromAssemblyContaining<CreateProductValidator>()` already registers `StockAdjustmentRequestValidator` since it's in the same Application assembly.)

- [ ] **Step 3: Build**

Run: `dotnet build MyApp.slnx` → 0 errors.

- [ ] **Step 4: Commit** — record progress in the ledger.

---

## Task 5: Cut over reads to ProductStock + opening movements

**Files:**
- Modify: `src/MyApp.Infrastructure/Services/ProductService.cs`

**Interfaces:**
- Consumes: `IStockService.RecordOpeningAsync` (Task 4); `db.ProductStocks` (Task 2).
- Produces: `ProductService` no longer reads `variant.Stock` for dashboard/DTO; create/import seed opening stock via `IStockService`. (`variant.Stock` is still read for the import/create *input* value `VariantInput.Stock` which now means "opening stock".)

> This task keeps `ProductVariant.Stock` present (removed in Task 6) but stops using it as the stock source-of-truth. `VariantInput.Stock` and `ProductImportRow.Stock` remain — they are now interpreted as **opening stock**.

- [ ] **Step 1: Inject IStockService**

Change the `ProductService` primary constructor to add the dependency:

```csharp
public class ProductService(
    AppDbContext db,
    IFileStorage fileStorage,
    IStockService stockService,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : IProductService
```

Add `using MyApp.Application.Stock;` at the top.

- [ ] **Step 2: Seed opening stock in CreateAsync**

Replace the tail of `CreateAsync` (from `db.Products.Add(product);` to the end) with:

```csharp
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        // Saldo awal -> ledger (gudang default). Cocokkan input ke varian via SKU.
        var defaultWhId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct);
        if (defaultWhId > 0)
        {
            var openingBySku = new Dictionary<string, (int Stock, decimal Cost)>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in request.Variants)
            {
                var sku = BuildSku(code, v.AttributeValueIds, valueLabels);
                openingBySku[sku] = (v.Stock, v.CostPrice);
            }
            foreach (var variant in product.Variants)
            {
                if (openingBySku.TryGetValue(variant.Sku, out var o) && o.Stock > 0)
                    await stockService.RecordOpeningAsync(variant.Id, defaultWhId, o.Stock, o.Cost, ct);
            }
        }

        return (await GetByIdAsync(product.Id, ct))!;
```

In the variant-building loop earlier in `CreateAsync`, the `product.AddVariant(...)` call still passes `v.Stock` until Task 6 removes that parameter. Leave it for now.

- [ ] **Step 3: Seed opening stock in ImportAsync**

In `ImportAsync`, the product/variant are created in the loop and saved at the end. After the existing `if (added > 0) await db.SaveChangesAsync(ct);` line, add opening-stock seeding. Replace the loop's product tracking so we can map saved variants — simplest: after save, re-seed from a captured list. Change the import loop to collect openings, then apply:

Add, just before the `foreach (var row in rows)` loop:

```csharp
        var openings = new List<(Product Product, int Stock, decimal Cost)>();
```

Inside the loop, right after `product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, stock, true);` add:

```csharp
                if (stock > 0) openings.Add((product, stock, 0m));
```

Replace the final save block with:

```csharp
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            var defaultWhId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct);
            if (defaultWhId > 0)
                foreach (var (product, stock, cost) in openings)
                {
                    var variant = product.Variants.FirstOrDefault();
                    if (variant is not null)
                        await stockService.RecordOpeningAsync(variant.Id, defaultWhId, stock, cost, ct);
                }
        }
        return new ProductImportResult(added, errors.Count, errors);
```

- [ ] **Step 4: Rewrite dashboard aggregation from ProductStock**

Replace the body of `GetDashboardAsync` lines that compute `totalStock`, `inventoryValue`, and `stockByProduct` with ProductStock-based queries. Replace:

```csharp
        var totalStock = await db.ProductVariants.SumAsync(v => (int?)v.Stock, ct) ?? 0;
        var inventoryValue = await db.ProductVariants.SumAsync(v => (decimal?)(v.Price * v.Stock), ct) ?? 0m;
        var activeCount = await db.Products.CountAsync(p => p.Status == ProductStatus.Aktif, ct);

        // Stok per produk = jumlah stok semua variannya.
        var stockByProduct = await db.ProductVariants
            .GroupBy(v => v.ProductId)
            .Select(g => new { ProductId = g.Key, Stock = g.Sum(x => x.Stock) })
            .ToListAsync(ct);
```

with:

```csharp
        var totalStock = await db.ProductStocks.SumAsync(s => (int?)s.Quantity, ct) ?? 0;
        var inventoryValue = await db.ProductStocks
            .Join(db.ProductVariants, s => s.ProductVariantId, v => v.Id, (s, v) => (decimal?)(s.Quantity * v.CostPrice))
            .SumAsync(ct) ?? 0m;
        var activeCount = await db.Products.CountAsync(p => p.Status == ProductStatus.Aktif, ct);

        // Stok per produk = jumlah saldo ProductStock semua variannya, lintas gudang.
        var stockByProduct = await db.ProductStocks
            .Join(db.ProductVariants, s => s.ProductVariantId, v => v.Id, (s, v) => new { v.ProductId, s.Quantity })
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Stock = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
```

The rest of `GetDashboardAsync` (uses `stockByProduct`, `stockMap`) is unchanged and now reflects ledger balances. `ProductDashboardDto` and `Home.razor` are unchanged.

- [ ] **Step 5: Compute variant/product stock in DTO mapping from ProductStock**

In `ToDtosAsync`, add a stock lookup and pass it to `ToDto`. After the `values` dictionary is built, add:

```csharp
        var variantIds = products.SelectMany(p => p.Variants).Select(v => v.Id).ToList();
        var stockByVariant = await db.ProductStocks.AsNoTracking()
            .Where(s => variantIds.Contains(s.ProductVariantId))
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Qty, ct);

        return products.Select(p => ToDto(p, brands, units, taxes, values, stockByVariant)).ToList();
```

Change the `ToDto` signature and the variant projection to use the lookup instead of `v.Stock`:

```csharp
    private static ProductDto ToDto(Product p,
        Dictionary<int, string> brands, Dictionary<int, string> units, Dictionary<int, string> taxes,
        Dictionary<int, (string AttrName, string Code, string Value)> values,
        Dictionary<int, int> stockByVariant)
    {
        // ...images/primary unchanged...

        var variants = p.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantDto(
            v.Id, v.Sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions,
            stockByVariant.TryGetValue(v.Id, out var q) ? q : 0, v.IsActive,
            v.Attributes.Where(a => values.ContainsKey(a.AttributeValueId))
                .Select(a => { var x = values[a.AttributeValueId]; return new AttributeValueRefDto(a.AttributeValueId, x.AttrName, x.Code, x.Value); })
                .ToList())).ToList();

        // ...prices unchanged...
        // TotalStock now derives from the same per-variant balances:
        // variants.Sum(v => v.Stock) already reflects ProductStock totals.
```

The final `new ProductDto(...)` line keeps `variants.Sum(v => v.Stock)` for `TotalStock` (now ledger-based).

- [ ] **Step 6: Build**

Run: `dotnet build MyApp.slnx` → 0 errors. (`ProductVariant.Stock` still exists; only its role changed.)

- [ ] **Step 7: Commit** — record progress in the ledger.

---

## Task 6: Remove ProductVariant.Stock column

**Files:**
- Modify: `src/MyApp.Domain/Entities/ProductVariant.cs`
- Modify: `src/MyApp.Domain/Entities/Product.cs`
- Modify: `src/MyApp.Application/Products/ProductDtos.cs`
- Modify: `src/MyApp.Application/Products/CreateProductValidator.cs`
- Modify: `src/MyApp.Infrastructure/Services/ProductService.cs`
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/<timestamp>_DropVariantStock.cs`

**Interfaces:**
- Produces: `ProductVariant` without `Stock`; `Product.AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice, decimal costPrice, decimal? weight, string? dimensions, bool isActive)`; `VariantInput` without `Stock`; `ProductVariantDto` keeps `Stock` (computed, not stored).

> `ProductVariantDto.Stock` stays (it's a computed read value). `VariantInput.Stock` is **removed** — opening stock for create now comes from a separate field captured in the form (Task 9) and passed via a new `VariantInput.OpeningStock`. To minimize churn we **rename** `VariantInput.Stock` → `OpeningStock`.

- [ ] **Step 1: Remove Stock from ProductVariant**

In `ProductVariant.cs`: delete the `Stock` property (line ~19), the `SetStock` method, and the two `SetStock(stock)` calls; remove the `stock` parameter from the constructor and `Update`. The result:

```csharp
    public ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive)
    {
        SetSku(sku);
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
    }

    /// <summary>Perbarui; SKU sengaja tidak diubah (dikunci). Stok TIDAK diubah di sini (lewat StockMovement).</summary>
    public void Update(string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive)
    {
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
    }
```

- [ ] **Step 2: Remove stock from Product.AddVariant**

In `Product.cs`, change `AddVariant`:

```csharp
    public ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive)
    {
        var v = new ProductVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive);
        _variants.Add(v);
        return v;
    }
```

- [ ] **Step 3: Rename VariantInput.Stock → OpeningStock**

In `ProductDtos.cs`, change `VariantInput`:

```csharp
public record VariantInput(
    string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int OpeningStock, bool IsActive,
    IReadOnlyList<int> AttributeValueIds);
```

Leave `ProductVariantDto.Stock` as-is (computed value).

- [ ] **Step 4: Update validator**

In `CreateProductValidator.cs`, change the `VariantInputValidator` rule:

```csharp
        RuleFor(v => v.OpeningStock).GreaterThanOrEqualTo(0);
```

- [ ] **Step 5: Update ProductService call sites**

In `ProductService.cs`:
- In both `AddVariant(...)` calls (CreateAsync ~line 81, UpdateAsync ~line 119) remove the `v.Stock` argument:
  `product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions, v.IsActive);`
- In the CreateAsync opening block (Task 5 Step 2), change `(v.Stock, v.CostPrice)` to `(v.OpeningStock, v.CostPrice)`.
- In `ImportAsync`, change `product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, stock, true);` to drop the `stock` arg:
  `product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, true);` (the `stock` local is still used for the `openings.Add(...)` line).

- [ ] **Step 6: Scaffold + hand-edit the drop migration**

Run from `src/MyApp.Infrastructure`:
`dotnet ef migrations add DropVariantStock --startup-project ../MyApp.Web`
Expected: scaffold contains `migrationBuilder.DropColumn(name: "Stock", table: "ProductVariants");` in `Up` and an `AddColumn` in `Down`.

Hand-edit `Down` to re-backfill `Stock` from `ProductStocks` (sum across warehouses) after the column is re-added, so a rollback restores totals. Make `Down`:

```csharp
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Stock", table: "ProductVariants", type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE v SET v.Stock = ISNULL(t.Qty, 0)
                FROM ProductVariants v
                LEFT JOIN (SELECT ProductVariantId, SUM(Quantity) AS Qty
                           FROM ProductStocks GROUP BY ProductVariantId) t
                  ON t.ProductVariantId = v.Id;");
        }
```

Leave the scaffolded `Up` (`DropColumn`) as generated.

- [ ] **Step 7: Apply + build**

Run from `src/MyApp.Infrastructure`: `dotnet ef database update --startup-project ../MyApp.Web` → applies (Stock column dropped).
Run: `dotnet build MyApp.slnx` → 0 errors.

> Tests referencing `VariantInput(..., Stock: ...)` or `AddVariant(..., stock, ...)` will break compilation. Update them to the new signatures (rename to `OpeningStock`, drop the variant `stock` arg) to keep the solution compiling — per F1 policy tests need only compile.

- [ ] **Step 8: Commit** — record progress in the ledger.

---

## Task 7: AppMenus — Inventory group + sidebar wiring

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Program.cs` (add `inventory.any` aggregate policy)
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor` (add the Inventory section — the sidebar is **hardcoded**, NOT auto-rendered from `AppMenus.Groups`)

**Interfaces:**
- Produces: resources `inventory.stock-levels` (View) and `inventory.adjustments` (View + Create). Per-permission policies (`inventory.stock-levels.index`, `inventory.adjustments.create`) are auto-created by `PermissionPolicyProvider`; admin role is auto-granted by `BootstrapSeeder`. The group header policy `inventory.any` and the sidebar links are **manual**.

> Correction to an earlier assumption: adding a group to `AppMenus.Groups` does NOT make it appear in the sidebar. `NavMenu.razor` lists each link by hand (one `<AuthorizeView Policy="…">` + `<NavLink>` per resource), and `Program.cs` registers a `<group>.any` aggregate policy per group for the section header. Both must be added manually.

- [ ] **Step 1: Add the group to AppMenus**

In `AppMenus.cs`, add a private helper for the View+Create action set (next to `CRUD`/`ViewOnly`):

```csharp
    private static AppAction[] ViewCreate => [ActIndex, ActCreate];
```

Insert into `Groups` after the "Master" group:

```csharp
        new("Inventory",
        [
            new("inventory.stock-levels", "Stock Levels",      "bi-boxes",            ViewOnly),
            new("inventory.adjustments",  "Stock Adjustment",  "bi-clipboard2-check-fill", ViewCreate),
        ]),
```

- [ ] **Step 2: Add the `inventory.any` aggregate policy**

In `Program.cs`, after the `settings.any` policy (~line 94), add (mirrors `master.any` at line 81):

```csharp
        options.AddPolicy("inventory.any", policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
                AppMenus.AllResources
                    .Where(r => r.Key.StartsWith("inventory."))
                    .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));
```

- [ ] **Step 3: Add the sidebar section**

In `NavMenu.razor`, after the Master `</AuthorizeView>` block (~line 95) and before the Settings block, add:

```razor
        <AuthorizeView Policy="inventory.any" Context="invCtx">
            <Authorized>
                <div class="nav-item px-3 mt-2">
                    <span class="nav-section-label">Inventory</span>
                </div>
                <AuthorizeView Policy="inventory.stock-levels.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="inventory/stock-levels">
                                <i class="bi bi-boxes nav-icon" aria-hidden="true"></i> Stock Levels
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
                <AuthorizeView Policy="inventory.adjustments.create">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="inventory/adjustments/new">
                                <i class="bi bi-clipboard2-check-fill nav-icon" aria-hidden="true"></i> Stock Adjustment
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
            </Authorized>
        </AuthorizeView>
```

- [ ] **Step 4: Build + verify**

Run: `dotnet build MyApp.slnx` → 0 errors. On next app start `BootstrapSeeder` grants the two new permissions to the admin role; the "Inventory" section appears in the sidebar for users holding `inventory.*.index`/`.create`.

- [ ] **Step 5: Commit** — record progress in the ledger.

---

## Task 8: Stock Levels page

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Inventory/StockLevels/StockLevelIndex.razor`

**Interfaces:**
- Consumes: `IStockService.GetLevelsPagedAsync`, `IWarehouseService.GetAllAsync`, `PagedResult<StockLevelDto>`, `Pager` component.

- [ ] **Step 1: Create the page**

Model it on `WarehouseIndex.razor` (search card + `data-card` table + `Pager`). Create `StockLevelIndex.razor`:

```razor
@page "/inventory/stock-levels"
@attribute [Authorize(Policy = "inventory.stock-levels.index")]
@rendermode InteractiveServer
@using MyApp.Application.Stock
@using MyApp.Application.Warehouses
@inject IStockService StockService
@inject IWarehouseService WarehouseService

<PageTitle>Stock Levels</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0 fw-semibold">Stock Levels</h1>
</div>

<div class="search-card mb-4 d-flex gap-2">
    <select class="form-select" style="max-width:220px" @bind="_warehouseId" @bind:after="ReloadAsync">
        <option value="0">All warehouses</option>
        @foreach (var w in _warehouses)
        {
            <option value="@w.Id">@w.Name</option>
        }
    </select>
    <input class="form-control" placeholder="Search SKU or product..."
           @bind="_search" @bind:event="oninput" @onkeyup="ReloadAsync" />
</div>

@if (_page is null)
{
    <div class="text-center py-5 text-muted">
        <div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...
    </div>
}
else if (_page.Total == 0)
{
    <div class="empty-state"><p class="empty-text">No stock records found.</p></div>
}
else
{
    <div class="data-card">
        <div class="table-responsive">
            <table class="table table-hover align-middle mb-0">
                <thead class="table-head">
                    <tr>
                        <th class="ps-3">SKU</th>
                        <th>Product</th>
                        <th>Warehouse</th>
                        <th class="text-end">Qty</th>
                        <th class="text-end pe-3">Cost (HPP)</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var l in _page.Items)
                    {
                        <tr>
                            <td class="ps-3"><span class="badge bg-light text-dark border">@l.Sku</span></td>
                            <td class="fw-medium">@l.ProductName</td>
                            <td class="text-muted small">@l.WarehouseName</td>
                            <td class="text-end">@l.Quantity.ToString("N0")</td>
                            <td class="text-end pe-3">@l.CostPrice.ToString("N2")</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
        @if (_page.TotalPages > 1)
        {
            <div class="data-card-footer d-flex justify-content-end">
                <Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" />
            </div>
        }
    </div>
}

@code {
    private const int PageSize = 15;
    private PagedResult<StockLevelDto>? _page;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private int _currentPage = 1;
    private int _warehouseId;
    private string? _search;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() =>
        _page = await StockService.GetLevelsPagedAsync(
            _currentPage, PageSize, _warehouseId == 0 ? null : _warehouseId, _search);

    private async Task ReloadAsync() { _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }
}
```

- [ ] **Step 2: Build + smoke test**

Run: `dotnet build MyApp.slnx` → 0 errors. Navigate to `/inventory/stock-levels`; confirm seeded opening balances appear.

- [ ] **Step 3: Commit** — record progress in the ledger.

---

## Task 9: Stock Adjustment (opname) pages

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Inventory/StockAdjustments/StockAdjustmentForm.razor`
- (Optional) Create: `src/MyApp.Web/Components/Pages/Inventory/StockAdjustments/StockAdjustmentIndex.razor`

**Interfaces:**
- Consumes: `IStockService.RecordAdjustmentAsync`, `StockAdjustmentRequest/Line`, `IWarehouseService.GetAllAsync`, `IProductService.GetAllAsync` (to pick variants), `SwalService`.

> The opname form is the only writer of stock in F2. It lets the user pick a warehouse + date, add variant rows with a signed delta quantity + (for positive deltas) unit cost + reason, and submits one `StockAdjustmentRequest`. A read-only history Index is optional; if built, list recent `Adjustment` movements — but there is no `GetAdjustmentHistory` method, so either add one to `IStockService` (a paged query over `StockMovements` where `Type = Adjustment`) or skip the Index. **Decision: skip the Index in F2** — the Stock Levels page (Task 8) plus the per-variant movement view cover visibility; add the history Index in F4 alongside transactions.

- [ ] **Step 1: Create the opname form**

Create `StockAdjustmentForm.razor`:

```razor
@page "/inventory/adjustments/new"
@attribute [Authorize(Policy = "inventory.adjustments.create")]
@rendermode InteractiveServer
@using FluentValidation
@using MyApp.Application.Stock
@using MyApp.Application.Warehouses
@using MyApp.Application.Products
@inject IStockService StockService
@inject IWarehouseService WarehouseService
@inject IProductService ProductService
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>New Stock Adjustment</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/inventory/stock-levels"><i class="bi bi-arrow-left me-1"></i>Back</a>
    <h4 class="uf-title">Stock Adjustment (Opname)</h4>
</div>

@if (_error is not null)
{
    <div class="alert alert-danger py-2">@_error</div>
}

<div class="fs-card mb-4">
    <div class="row g-3">
        <div class="col-12 col-md-4">
            <label class="form-label lbl-required">Warehouse</label>
            <select class="form-select" @bind="_warehouseId">
                <option value="0">— select —</option>
                @foreach (var w in _warehouses)
                {
                    <option value="@w.Id">@w.Name</option>
                }
            </select>
        </div>
        <div class="col-12 col-md-4">
            <label class="form-label">Date</label>
            <input type="date" class="form-control" @bind="_date" />
        </div>
        <div class="col-12">
            <label class="form-label">Note</label>
            <input class="form-control" maxlength="200" @bind="_note" placeholder="Optional" />
        </div>
    </div>
</div>

<div class="fs-card mb-4">
    <div class="d-flex justify-content-between align-items-center mb-2">
        <div class="fs-card-title mb-0">Lines</div>
        <button class="btn btn-sm btn-outline-primary" @onclick="AddLine"><i class="bi bi-plus-lg me-1"></i>Add line</button>
    </div>
    <div class="table-responsive">
        <table class="table align-middle mb-0">
            <thead class="table-head">
                <tr>
                    <th>Variant</th>
                    <th style="width:130px" class="text-end">Delta Qty</th>
                    <th style="width:150px" class="text-end">Unit Cost</th>
                    <th>Reason</th>
                    <th style="width:48px"></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var line in _lines)
                {
                    <tr>
                        <td>
                            <select class="form-select form-select-sm" @bind="line.VariantId">
                                <option value="0">— select —</option>
                                @foreach (var v in _variants)
                                {
                                    <option value="@v.Id">@v.Sku — @v.Name</option>
                                }
                            </select>
                        </td>
                        <td><input type="number" class="form-control form-control-sm text-end" @bind="line.DeltaQuantity" /></td>
                        <td><input type="number" step="0.01" class="form-control form-control-sm text-end" @bind="line.UnitCost" /></td>
                        <td><input class="form-control form-control-sm" maxlength="200" @bind="line.Reason" /></td>
                        <td><button class="btn btn-sm btn-outline-danger" @onclick="() => _lines.Remove(line)"><i class="bi bi-trash3"></i></button></td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
    <div class="form-text mt-2">Positive delta adds stock (uses Unit Cost for Moving Average); negative delta removes stock.</div>
</div>

<div class="d-flex gap-2 justify-content-end">
    <button class="btn btn-primary btn-sm px-3" @onclick="SaveAsync" disabled="@_saving">
        @if (_saving) { <span class="spinner-border spinner-border-sm me-1"></span> } else { <i class="bi bi-floppy2-fill me-1"></i> }
        Save
    </button>
    <a class="btn btn-outline-secondary btn-sm" href="/inventory/stock-levels"><i class="bi bi-x-lg me-1"></i>Cancel</a>
</div>

@code {
    private sealed class LineModel
    {
        public int VariantId { get; set; }
        public int DeltaQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Reason { get; set; }
    }

    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private List<VariantRow> _variants = [];
    private readonly List<LineModel> _lines = [new()];
    private int _warehouseId;
    private DateTime _date = DateTime.Today;
    private string? _note;
    private bool _saving;
    private string? _error;

    private sealed record VariantRow(int Id, string Sku, string Name);

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        var products = await ProductService.GetAllAsync();
        _variants = products
            .SelectMany(p => p.Variants.Select(v => new VariantRow(v.Id, v.Sku, p.Name)))
            .OrderBy(v => v.Name).ThenBy(v => v.Sku).ToList();
        var def = _warehouses.FirstOrDefault(w => w.IsDefault);
        if (def is not null) _warehouseId = def.Id;
    }

    private void AddLine() => _lines.Add(new());

    private async Task SaveAsync()
    {
        _error = null;
        _saving = true;
        try
        {
            var lines = _lines
                .Where(l => l.VariantId > 0 && l.DeltaQuantity != 0)
                .Select(l => new StockAdjustmentLine(l.VariantId, l.DeltaQuantity, l.UnitCost, l.Reason))
                .ToList();
            var request = new StockAdjustmentRequest(_warehouseId, _date.ToUniversalTime(), _note, lines);
            await StockService.RecordAdjustmentAsync(request);
            await Swal.ToastAsync("success", "Stock adjusted");
            Nav.NavigateTo("/inventory/stock-levels");
        }
        catch (ValidationException ex)
        {
            _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            _error = ex.Message; // e.g. "Stock cannot go negative."
        }
        finally
        {
            _saving = false;
        }
    }
}
```

- [ ] **Step 2: Link from Stock Levels page**

In `StockLevelIndex.razor`, add an action button in the header (mirrors `WarehouseIndex` "Add New"):

```razor
    <AuthorizeView Policy="inventory.adjustments.create">
        <Authorized>
            <a class="btn btn-primary btn-sm" href="/inventory/adjustments/new">
                <i class="bi bi-plus-lg me-1"></i>New Adjustment
            </a>
        </Authorized>
    </AuthorizeView>
```

- [ ] **Step 3: Build + manual test**

Run: `dotnet build MyApp.slnx` → 0 errors. At `/inventory/adjustments/new`: add a positive delta with a unit cost → save → confirm Stock Levels qty increased and HPP moved; add a negative delta exceeding on-hand → confirm friendly "Stock cannot go negative." error.

- [ ] **Step 4: Commit** — record progress in the ledger.

---

## Task 10: ProductForm — opening stock (create) / read-only (edit)

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`

**Interfaces:**
- Consumes: `VariantInput.OpeningStock` (Task 6); `IStockService.GetLevelsByVariantAsync` (for edit display).

> `ProductForm.razor` is large and builds `VariantInput` lists for both the single-variant and multi-variant modes. Read it fully before editing. Apply three changes consistently across both modes.

- [ ] **Step 1: Rename the stock input to "Opening stock" and gate it to create mode**

Find every place the variant row binds a stock value into `VariantInput`. The field that previously fed `VariantInput`'s `Stock` is now `OpeningStock`. In the markup:
- When `Id is null` (create): label the input "Opening stock" and keep it editable; keep the existing cost-price input adjacent (it is the opening HPP).
- When `Id is not null` (edit): render the stock value as **read-only** text (disabled input or plain text), not an editable field.

Use the page's existing `Id` parameter (same pattern as `WarehouseForm` `Id is int`) to switch. Example for a variant row cell:

```razor
@if (Id is null)
{
    <input type="number" class="form-control form-control-sm text-end" @bind="row.OpeningStock" />
}
else
{
    <span class="text-muted">@row.OnHand.ToString("N0")</span>
}
```

- [ ] **Step 2: Populate on-hand for edit mode**

When loading an existing product (the `Id is int` branch of `OnInitializedAsync`), fetch per-variant on-hand totals for display. For each variant row, set `row.OnHand` from `await StockService.GetLevelsByVariantAsync(variantId)` summed over warehouses, or reuse `ProductVariantDto.Stock` already returned by `ProductService.GetByIdAsync` (preferred — no extra call):

```razor
row.OnHand = variantDto.Stock;
```

Add `@inject IStockService StockService` and `@using MyApp.Application.Stock` only if you take the explicit-fetch route; the `ProductVariantDto.Stock` route needs neither.

- [ ] **Step 3: Build the VariantInput with OpeningStock**

Where the form constructs `new VariantInput(...)` for submission, pass `OpeningStock` (the create-mode value; on edit it is ignored by the service):

```csharp
new VariantInput(row.Barcode, row.Price, row.DiscountPrice, row.CostPrice,
    row.Weight, row.Dimensions, Id is null ? row.OpeningStock : 0, row.IsActive, row.AttributeValueIds)
```

- [ ] **Step 4: Build + manual test**

Run: `dotnet build MyApp.slnx` → 0 errors.
- Create a product with opening stock 25 and cost 1000 → save → Stock Levels shows 25 in the default warehouse, HPP 1000.
- Edit that product → stock shows read-only 25; changing other fields and saving does not alter stock.

- [ ] **Step 5: Commit** — record progress in the ledger.

---

## Progress tracking

This repo is **not** a git repository (F1 ran the same way). Instead of commits, maintain a ledger at
`docs/superpowers/plans/f2-progress.md` (mirror `f1-progress.md`): mark each task done with a one-line
note on what changed and the verification result (`dotnet build` = 0 errors; migration applied).

## Final verification (after all tasks)

- [ ] `dotnet build MyApp.slnx` → 0 errors / 0 warnings.
- [ ] `dotnet ef database update` → both migrations applied on the F1 database; `ProductVariants.Stock` column gone; `StockMovements` + `ProductStocks` present with opening balances.
- [ ] `/inventory/stock-levels` lists balances; warehouse filter + search work.
- [ ] `/inventory/adjustments/new` applies positive (HPP moves) and negative (clamped at 0) deltas through the ledger.
- [ ] Product create seeds opening stock into the default warehouse; product edit shows stock read-only.
- [ ] Home dashboard total stock + inventory value match `SUM(ProductStocks.Quantity)` and `SUM(Quantity × CostPrice)`.
- [ ] Tests compile (`dotnet build` includes test projects); not run as a gate.

## Out of scope (per spec §8)

Purchase/Sales/Transfer transactions (F4), `ReservedQty`, per-warehouse Moving Average, FIFO/LIFO, an adjustment-history Index page (deferred to F4).
```