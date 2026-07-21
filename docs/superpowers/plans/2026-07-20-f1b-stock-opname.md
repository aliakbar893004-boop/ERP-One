# Stock Opname (Physical Count) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a formal per-warehouse physical-count document (Stock Opname) with an approval workflow that posts stock variances so system stock equals the physical count.

**Architecture:** Mirrors the existing **Stock Transfer** module end-to-end (Domain → EF → Application → Infrastructure service → Blazor pages), reusing `IApprovalService`, `IDocumentNumberService`, `IStockService`, `StockMovement`, and `UpsertStockAsync`. Workflow is `Draft → PendingApproval → Posted`. On full approval, for each line `delta = PhysicalQty − liveOnHand(at post)` is posted as a `MovementType.Adjustment` `StockMovement` using the variant's current `CostPrice`; the moving-average is **not** changed (opname is a correction, not a purchase). No GL journal is created.

**Tech Stack:** .NET / C# (clean-arch layers), EF Core (SQL Server; SQLite for tests), FluentValidation, Blazor Server (`.pi`/`.cf`/`.pf` design system), xUnit integration tests.

## Global Constraints

- Domain entities: `private set`, private `// EF Core` ctor, backing `List<>` exposed as `IReadOnlyCollection<>`, invariants `throw`. All domain entities use namespace `ErpOne.Domain.Entities` regardless of folder.
- New entities live under `src/ErpOne.Domain/Entities/Inventory/` (namespace still `ErpOne.Domain.Entities`).
- `SystemQty` is a snapshot taken at draft creation and MUST stay stable across edits; only `PhysicalQty` / header (date, notes) change during Draft. Warehouse is locked after create.
- Variance is computed against **live** on-hand at Post time: `delta = PhysicalQty − liveOnHand`. `SystemQty` snapshot is for the variance report only.
- No GL posting (consistent with Stock Adjustment & Stock Transfer). Moving-average is never modified by opname.
- Only variants that already have a `ProductStock` row in the chosen warehouse are counted (v1 limitation).
- UI language is English; use existing `.pi`/`.cf`/`.pf` design classes and Bootstrap-icons already used by Stock Transfer pages. Do not introduce new CSS design systems.
- **Commits are manual:** the user commits/merges/pushes themselves. Do NOT run `git commit`/`git push`. Each task ends with a **Checkpoint** (build + relevant tests green) for the user to review and commit.
- Every new EF entity must be registered in `tablePrefixes` in `AppDbContext` (`T_` for transaction tables) or the model build fails by design.

---

### Task 1: Domain — status enum + entities

**Files:**
- Create: `src/ErpOne.Domain/Entities/Inventory/StockOpnameStatus.cs`
- Create: `src/ErpOne.Domain/Entities/Inventory/StockOpnameLine.cs`
- Create: `src/ErpOne.Domain/Entities/Inventory/StockOpname.cs`

**Interfaces:**
- Consumes: `AuditableEntity` (from `ErpOne.Domain.Common`).
- Produces (later tasks rely on these exact signatures):
  - `enum StockOpnameStatus { Draft, PendingApproval, Posted }`
  - `StockOpnameLine(int productVariantId, int systemQty, int physicalQty)`; props `Id`, `StockOpnameId`, `ProductVariantId`, `SystemQty`, `PhysicalQty`; method `void SetPhysicalQty(int qty)`.
  - `StockOpname(string opnameNumber, DateTime opnameDate, int warehouseId, string? notes)`; props `Id`, `OpnameNumber`, `OpnameDate`, `WarehouseId`, `Notes`, `Status`, `RejectionNote`, `IReadOnlyCollection<StockOpnameLine> Lines`; methods `SetLines(IEnumerable<(int VariantId, int SystemQty, int PhysicalQty)>)`, `SetPhysicalCounts(IEnumerable<(int LineId, int PhysicalQty)>)`, `UpdateHeader(DateTime opnameDate, string? notes)`, `Submit()`, `MarkPosted()`, `ReturnToDraft(string reason)`.

- [ ] **Step 1: Create the status enum**

Create `src/ErpOne.Domain/Entities/Inventory/StockOpnameStatus.cs`:

```csharp
namespace ErpOne.Domain.Entities;

public enum StockOpnameStatus { Draft, PendingApproval, Posted }
```

- [ ] **Step 2: Create the line entity**

Create `src/ErpOne.Domain/Entities/Inventory/StockOpnameLine.cs`:

```csharp
namespace ErpOne.Domain.Entities;

public class StockOpnameLine
{
    public int Id { get; private set; }
    public int StockOpnameId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int SystemQty { get; private set; }    // snapshot on-hand at draft creation (variance report)
    public int PhysicalQty { get; private set; }  // physical count result

    private StockOpnameLine() { } // EF Core

    public StockOpnameLine(int productVariantId, int systemQty, int physicalQty)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (systemQty < 0) throw new ArgumentException("SystemQty must be >= 0.", nameof(systemQty));
        if (physicalQty < 0) throw new ArgumentException("PhysicalQty must be >= 0.", nameof(physicalQty));
        ProductVariantId = productVariantId;
        SystemQty = systemQty;
        PhysicalQty = physicalQty;
    }

    public void SetPhysicalQty(int qty)
    {
        if (qty < 0) throw new ArgumentException("PhysicalQty must be >= 0.", nameof(qty));
        PhysicalQty = qty;
    }
}
```

- [ ] **Step 3: Create the aggregate entity**

Create `src/ErpOne.Domain/Entities/Inventory/StockOpname.cs`:

```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Physical count document per warehouse. Draft → PendingApproval → Posted (variance posted when fully approved).</summary>
public class StockOpname : AuditableEntity
{
    private readonly List<StockOpnameLine> _lines = [];

    public int Id { get; private set; }
    public string OpnameNumber { get; private set; } = default!;
    public DateTime OpnameDate { get; private set; }
    public int WarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public StockOpnameStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<StockOpnameLine> Lines => _lines;

    private StockOpname() { } // EF Core

    public StockOpname(string opnameNumber, DateTime opnameDate, int warehouseId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(opnameNumber)) throw new ArgumentException("OpnameNumber is required.", nameof(opnameNumber));
        if (warehouseId <= 0) throw new ArgumentException("Warehouse is required.", nameof(warehouseId));
        OpnameNumber = opnameNumber.Trim();
        OpnameDate = opnameDate;
        WarehouseId = warehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = StockOpnameStatus.Draft;
    }

    // Warehouse intentionally NOT updatable: lines are a warehouse-specific snapshot.
    public void UpdateHeader(DateTime opnameDate, string? notes)
    {
        EnsureDraft();
        OpnameDate = opnameDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<(int VariantId, int SystemQty, int PhysicalQty)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(new StockOpnameLine(l.VariantId, l.SystemQty, l.PhysicalQty));
    }

    // Updates PhysicalQty only; SystemQty snapshot stays stable.
    public void SetPhysicalCounts(IEnumerable<(int LineId, int PhysicalQty)> counts)
    {
        EnsureDraft();
        var map = counts.ToDictionary(c => c.LineId, c => c.PhysicalQty);
        foreach (var line in _lines)
            if (map.TryGetValue(line.Id, out var qty)) line.SetPhysicalQty(qty);
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit an opname without lines.");
        Status = StockOpnameStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != StockOpnameStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending opname can be posted.");
        Status = StockOpnameStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != StockOpnameStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending opname can be returned to draft.");
        Status = StockOpnameStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != StockOpnameStatus.Draft) throw new InvalidOperationException("Only a draft opname can be modified.");
    }
}
```

- [ ] **Step 4: Build the Domain project**

Run: `dotnet build src/ErpOne.Domain`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Checkpoint**

Domain builds clean. Pause for user review & manual commit.

---

### Task 2: Constants & shared enums

**Files:**
- Modify: `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs`
- Modify: `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`

**Interfaces:**
- Produces: `DocumentTypes.StockOpname == "StockOpname"`; `ApprovalDocumentType.StockOpname` enum member.

- [ ] **Step 1: Add the document-type key**

In `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs`, add after the `StockTransfer` line:

```csharp
    public const string StockTransfer = "StockTransfer";
    public const string StockOpname   = "StockOpname";
```

- [ ] **Step 2: Add the approval document type**

In `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`, add `StockOpname` after `StockTransfer`:

```csharp
public enum ApprovalDocumentType
{
    PurchaseOrder,
    SalesOrder,
    SupplierPayment,
    SupplierPaymentVoid,
    CustomerReceiptVoid,
    ExpenseVoid,
    StockTransfer,
    StockOpname
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Checkpoint** — pause for user review & manual commit.

---

### Task 3: EF wiring + migration + NumberSequence test bump

**Files:**
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`
- Create (generated): `src/ErpOne.Infrastructure/Persistence/Migrations/*_AddStockOpname.cs`

**Interfaces:**
- Consumes: `StockOpname`, `StockOpnameLine` (Task 1); `Warehouse`, `ProductVariant`, `NumberSequence`, `ResetPeriod` (existing).
- Produces: `db.StockOpnames`, `db.StockOpnameLines` DbSets; NumberSequence row Id=14 Code="StockOpname" Prefix="OPN".

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `StockTransferLines` DbSet (line ~41), add:

```csharp
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockOpname> StockOpnames => Set<StockOpname>();
    public DbSet<StockOpnameLine> StockOpnameLines => Set<StockOpnameLine>();
```

- [ ] **Step 2: Add the NumberSequence seed row (Id=14)**

In the `NumberSequence` `HasData(...)` block, add a comma after the Id=13 line and append:

```csharp
                new { Id = 13, Code = "StockTransfer", Prefix = "TRF", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 14, Code = "StockOpname", Prefix = "OPN", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 3: Add the entity configuration**

In `AppDbContext.cs`, immediately after the `StockTransferLine` config block (line ~515), add:

```csharp
        modelBuilder.Entity<StockOpname>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OpnameNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.OpnameNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.StockOpnameId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(StockOpname.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StockOpnameLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Register table prefixes**

In the `tablePrefixes` dictionary (line ~932), after the `StockTransferLine` entries, add:

```csharp
            [nameof(StockTransfer)] = "T_",
            [nameof(StockTransferLine)] = "T_",
            [nameof(StockOpname)] = "T_",
            [nameof(StockOpnameLine)] = "T_",
```

- [ ] **Step 5: Bump the NumberSequence count assertion**

In `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs` line 23, change:

```csharp
        Assert.Equal(14, all.Count);   // 6 core + AP invoice/payment + AR invoice/receipt + Expense + JournalEntry + StockTransfer + StockOpname
```

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add AddStockOpname --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web --output-dir Persistence/Migrations`
Expected: New `*_AddStockOpname.cs` + `.Designer.cs` created; migration body has two `CreateTable` calls (`T_StockOpname`, `T_StockOpnameLine`), the unique index on `OpnameNumber`, FKs, and an `InsertData` for NumberSequences Id=14. If the tool reports pending model changes only (no new file), verify Steps 1–4 saved correctly.

- [ ] **Step 7: Build + run the NumberSequence test**

Run: `dotnet build` then `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~NumberSequenceServiceTests"`
Expected: Build succeeded; NumberSequence tests PASS (count now 14).

- [ ] **Step 8: Checkpoint** — pause for user review & manual commit.

---

### Task 4: Application layer — DTOs, interface, validators

**Files:**
- Create: `src/ErpOne.Application/StockOpnames/StockOpnameDtos.cs`
- Create: `src/ErpOne.Application/StockOpnames/IStockOpnameService.cs`
- Create: `src/ErpOne.Application/StockOpnames/StockOpnameValidators.cs`

**Interfaces:**
- Consumes: `ApprovalStepDto` (`ErpOne.Application.Approvals`); `PagedResult<T>` (`ErpOne.Application.Common`); `StockOpnameStatus` (`ErpOne.Domain.Entities`).
- Produces (later tasks rely on these exact records/signatures):
  - `CreateStockOpnameRequest(DateTime OpnameDate, int WarehouseId, string? Notes)`
  - `StockOpnameCountInput(int LineId, int PhysicalQty)`
  - `UpdateStockOpnameRequest(DateTime OpnameDate, string? Notes, IReadOnlyList<StockOpnameCountInput> Counts)`
  - `StockOpnameLineDto(int Id, int ProductVariantId, string Sku, string ProductName, int SystemQty, int PhysicalQty, int Variance, int OnHandNow)`
  - `StockOpnameDto(int Id, string OpnameNumber, DateTime OpnameDate, int WarehouseId, string WarehouseName, string? Notes, string Status, string? RejectionNote, string? CreatedBy, IReadOnlyList<StockOpnameLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps)`
  - `StockOpnameListItemDto(int Id, string OpnameNumber, DateTime OpnameDate, string WarehouseName, int LineCount, int TotalVariance, string Status)`
  - `IStockOpnameService` with `GetPagedAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `SubmitAsync`, `ApproveAsync`, `RejectAsync`.

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/StockOpnames/StockOpnameDtos.cs`:

```csharp
using ErpOne.Application.Approvals;

namespace ErpOne.Application.StockOpnames;

public record CreateStockOpnameRequest(DateTime OpnameDate, int WarehouseId, string? Notes);

public record StockOpnameCountInput(int LineId, int PhysicalQty);

public record UpdateStockOpnameRequest(DateTime OpnameDate, string? Notes, IReadOnlyList<StockOpnameCountInput> Counts);

public record StockOpnameLineDto(int Id, int ProductVariantId, string Sku, string ProductName,
    int SystemQty, int PhysicalQty, int Variance, int OnHandNow);

public record StockOpnameDto(int Id, string OpnameNumber, DateTime OpnameDate,
    int WarehouseId, string WarehouseName, string? Notes, string Status, string? RejectionNote,
    string? CreatedBy, IReadOnlyList<StockOpnameLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record StockOpnameListItemDto(int Id, string OpnameNumber, DateTime OpnameDate,
    string WarehouseName, int LineCount, int TotalVariance, string Status);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/StockOpnames/IStockOpnameService.cs`:

```csharp
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.StockOpnames;

public interface IStockOpnameService
{
    Task<PagedResult<StockOpnameListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, StockOpnameStatus? status = null, CancellationToken ct = default);
    Task<StockOpnameDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<StockOpnameDto> CreateAsync(CreateStockOpnameRequest request, CancellationToken ct = default);
    Task<StockOpnameDto> UpdateAsync(int id, UpdateStockOpnameRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create the validators**

Create `src/ErpOne.Application/StockOpnames/StockOpnameValidators.cs`:

```csharp
using FluentValidation;

namespace ErpOne.Application.StockOpnames;

public class CreateStockOpnameValidator : AbstractValidator<CreateStockOpnameRequest>
{
    public CreateStockOpnameValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage("Warehouse is required.");
    }
}

public class UpdateStockOpnameValidator : AbstractValidator<UpdateStockOpnameRequest>
{
    public UpdateStockOpnameValidator()
    {
        RuleForEach(x => x.Counts).ChildRules(c =>
        {
            c.RuleFor(x => x.LineId).GreaterThan(0);
            c.RuleFor(x => x.PhysicalQty).GreaterThanOrEqualTo(0).WithMessage("Physical quantity must be >= 0.");
        });
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ErpOne.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Checkpoint** — pause for user review & manual commit.

---

### Task 5: Infrastructure service + DI + integration tests (TDD)

**Files:**
- Create: `tests/ErpOne.IntegrationTests/StockOpnameServiceTests.cs`
- Create: `src/ErpOne.Infrastructure/Services/Inventory/StockOpnameService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `IStockOpnameService` + DTOs (Task 4); `IApprovalService`, `IStockService`, `IDocumentNumberService`, `AppDbContext`, `UpsertStockAsync`, `StockMovement`, `MovementType.Adjustment` (existing); `DocumentTypes.StockOpname`, `ApprovalDocumentType.StockOpname` (Task 2).
- Produces: `StockOpnameService : IStockOpnameService`; DI registration.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/ErpOne.IntegrationTests/StockOpnameServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Stock;
using ErpOne.Application.StockOpnames;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockOpnameServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockOpnameServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds 1 warehouse, 1 product/variant, opening stock in that warehouse. Returns (warehouseId, variantId).
    private static async Task<(int warehouseId, int variantId)> SeedAsync(IServiceProvider sp, int opening)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Wh {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, wh.Id, opening));
        await db.SaveChangesAsync();
        return (wh.Id, v.Id);
    }

    // CustomWebApplicationFactory does NOT run BootstrapSeeder, so seed the approval chain manually.
    private static async Task SeedChainAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockOpname))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockOpname, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    private static async Task SetCountAsync(IServiceProvider sp, IStockOpnameService svc, int opnameId, int physicalQty)
    {
        var dto = await svc.GetByIdAsync(opnameId);
        var counts = dto!.Lines.Select(l => new StockOpnameCountInput(l.Id, physicalQty)).ToList();
        await svc.UpdateAsync(opnameId, new UpdateStockOpnameRequest(dto.OpnameDate, dto.Notes, counts));
    }

    [Fact]
    public async Task Approve_posts_surplus_variance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 120);
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);
        Assert.Equal(120, await stock.GetOnHandAsync(variantId, wh));

        var db = sp.GetRequiredService<AppDbContext>();
        var moves = await db.StockMovements.Where(m => m.RefType == "StockOpname" && m.RefId == created.Id).ToListAsync();
        Assert.Single(moves);
        Assert.Equal(20, moves[0].Quantity);
    }

    [Fact]
    public async Task Approve_posts_shortage_variance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 80);
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        Assert.Equal(80, await stock.GetOnHandAsync(variantId, wh));
    }

    [Fact]
    public async Task Zero_variance_line_is_a_noop()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 100); // equal to system/on-hand
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
        var db = sp.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StockMovements.Where(m => m.RefType == "StockOpname" && m.RefId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task Variance_is_computed_against_live_on_hand_at_post()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        // Draft snapshots SystemQty=100.
        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 100); // counted 100

        // Stock drifts down to 90 AFTER the snapshot, BEFORE post.
        await stock.RecordAdjustmentAsync(new StockAdjustmentRequest(wh, [new StockAdjustmentLine(variantId, -10)]));
        Assert.Equal(90, await stock.GetOnHandAsync(variantId, wh));

        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        // delta = 100 - 90 = +10 → on-hand ends at 100 (matches the count), not 100-100=0.
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
    }
}
```

> **Note on the drift test:** it uses `IStockService.RecordAdjustmentAsync`. Confirm the exact request/line record names (`StockAdjustmentRequest`, `StockAdjustmentLine`) in `src/ErpOne.Application/Inventory/Stock/` before running; if they differ, adjust the two lines that build the adjustment (the assertion contract is unchanged).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~StockOpnameServiceTests"`
Expected: FAIL — `IStockOpnameService` not registered / not resolvable (no implementation yet).

- [ ] **Step 3: Implement the service**

Create `src/ErpOne.Infrastructure/Services/Inventory/StockOpnameService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.Stock;
using ErpOne.Application.StockOpnames;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockOpnameService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    IValidator<CreateStockOpnameRequest> createValidator,
    IValidator<UpdateStockOpnameRequest> updateValidator,
    IDocumentNumberService docNumbers) : IStockOpnameService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.StockOpname;

    public async Task<PagedResult<StockOpnameListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, StockOpnameStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.StockOpnames.AsNoTracking();
        if (status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.OpnameNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockOpnameListItemDto(
                x.Id, x.OpnameNumber, x.OpnameDate,
                db.Warehouses.Where(w => w.Id == x.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                x.Lines.Count,
                x.Lines.Sum(l => l.PhysicalQty - l.SystemQty),
                x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<StockOpnameListItemDto>(items, total, page, pageSize);
    }

    public async Task<StockOpnameDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var o = await db.StockOpnames.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return null;
        var whName = await db.Warehouses.Where(w => w.Id == o.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var variantIds = o.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variantInfo = await (from v in db.ProductVariants.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                                 where variantIds.Contains(v.Id)
                                 select new { v.Id, v.Sku, p.Name }).ToListAsync(ct);
        var info = variantInfo.ToDictionary(x => x.Id, x => (x.Sku, x.Name));
        var lines = new List<StockOpnameLineDto>();
        foreach (var l in o.Lines.OrderBy(l => l.Id))
        {
            var onHand = await stock.GetOnHandAsync(l.ProductVariantId, o.WarehouseId, ct);
            var (sku, name) = info.TryGetValue(l.ProductVariantId, out var x) ? x : ("?", "(unknown)");
            lines.Add(new StockOpnameLineDto(l.Id, l.ProductVariantId, sku, name,
                l.SystemQty, l.PhysicalQty, l.PhysicalQty - l.SystemQty, onHand));
        }
        var steps = await approval.GetStepsAsync(DocType, o.Id, ct);
        return new StockOpnameDto(o.Id, o.OpnameNumber, o.OpnameDate, o.WarehouseId, whName,
            o.Notes, o.Status.ToString(), o.RejectionNote, o.CreatedBy, lines, steps);
    }

    public async Task<StockOpnameDto> CreateAsync(CreateStockOpnameRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var whExists = await db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId, ct);
        if (!whExists) throw Fail("Warehouse not found.");

        var number = await docNumbers.NextAsync(DocumentTypes.StockOpname, request.OpnameDate, ct);
        var o = new StockOpname(number, request.OpnameDate, request.WarehouseId, request.Notes);

        var stocks = await db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == request.WarehouseId)
            .Select(s => new { s.ProductVariantId, s.Quantity })
            .ToListAsync(ct);
        // PhysicalQty initialized to SystemQty; user edits it on the count sheet.
        o.SetLines(stocks.Select(s => (s.ProductVariantId, s.Quantity, s.Quantity)));

        db.StockOpnames.Add(o);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(o.Id, ct))!;
    }

    public async Task<StockOpnameDto> UpdateAsync(int id, UpdateStockOpnameRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        o.UpdateHeader(request.OpnameDate, request.Notes);
        o.SetPhysicalCounts(request.Counts.Select(c => (c.LineId, c.PhysicalQty)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var o = await db.StockOpnames.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Opname not found.");
        if (o.Status != StockOpnameStatus.Draft) throw Fail("Only a draft opname can be deleted.");
        db.StockOpnames.Remove(o);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        o.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, o.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, o.Id, ct);
        if (fullyApproved) await PostAsync(o, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Opname not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, o.Id, actingUserName, isInRole, o.CreatedBy, ct);
        if (fullyApproved) await PostAsync(o, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.StockOpnames.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Opname not found.");
        await approval.RejectAsync(DocType, o.Id, actingUserName, isInRole, o.CreatedBy, reason, ct);
        o.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, o.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Posts variance vs LIVE on-hand at post time; caller saves + commits. No moving-average change, no GL.
    private async Task PostAsync(StockOpname o, CancellationToken ct)
    {
        foreach (var line in o.Lines)
        {
            var onHand = await stock.GetOnHandAsync(line.ProductVariantId, o.WarehouseId, ct);
            var delta = line.PhysicalQty - onHand;
            if (delta == 0) continue;
            var cost = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                .Select(v => v.CostPrice).FirstOrDefaultAsync(ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, o.WarehouseId, MovementType.Adjustment,
                delta, cost, o.OpnameDate, "StockOpname", o.Id, o.OpnameNumber));
            await db.UpsertStockAsync(line.ProductVariantId, o.WarehouseId, delta, ct);
        }
        o.MarkPosted();
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("StockOpname", message)]);
}
```

- [ ] **Step 4: Register the service in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using near the other Application usings and the registration after the `StockTransferService` line (~105):

```csharp
using ErpOne.Application.StockOpnames;
```

```csharp
        services.AddScoped<IStockTransferService, StockTransferService>();
        services.AddScoped<IStockOpnameService, StockOpnameService>();
```

> Validators are auto-registered by the existing FluentValidation assembly scan in the Application project; if there is no scan, register `CreateStockOpnameValidator` and `UpdateStockOpnameValidator` alongside the existing validators (grep `AddScoped<IValidator` to confirm the convention).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~StockOpnameServiceTests"`
Expected: PASS — all 4 tests green.

- [ ] **Step 6: Checkpoint** — pause for user review & manual commit.

---

### Task 6: Menus + approval-chain seed

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs`

**Interfaces:**
- Consumes: existing `AppAction` action-set constants (`ActIndex`, `ActCreate`, `ActEdit`, `ActDelete`, `ActApprove`, `ActPost`); `ApprovalDocumentType.StockOpname` (Task 2).
- Produces: resource `inventory.stock-opname` with policies `inventory.stock-opname.index/create/edit/delete/approve/post` (auto-seeded from `AppMenus.AllPermissions`); default approval chain for `StockOpname`.

- [ ] **Step 1: Register the menu resource**

In `AppMenus.cs`, after the `StockTransferActions` field (line ~35) add an action-set (reuse the same actions as Stock Transfer):

```csharp
    private static AppAction[] StockTransferActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost];
    private static AppAction[] StockOpnameActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost];
```

Then in the Inventory menu group, after the `inventory.transfers` entry (line ~64) add:

```csharp
            new("inventory.transfers",    "Stock Transfer",   "bi-arrow-left-right",     StockTransferActions),
            new("inventory.stock-opname", "Stock Opname",     "bi-clipboard-data",       StockOpnameActions),
```

- [ ] **Step 2: Seed the default approval chain**

In `BootstrapSeeder.cs`, after the Stock Transfer chain block (line ~76), add:

```csharp
        // Seed rantai approval default untuk Stock Opname (idempotent), mengikuti pola Stock Transfer.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockOpname))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockOpname, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Checkpoint** — pause for user review & manual commit.

---

### Task 7: Web — Index page

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameIndex.razor`

**Interfaces:**
- Consumes: `IStockOpnameService.GetPagedAsync`; `StockOpnameListItemDto`; `StockOpnameStatus`; `PagedResult<T>`; `Pager` component; policies from Task 6.

- [ ] **Step 1: Create the index page**

Create `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameIndex.razor`:

```razor
@page "/inventory/stock-opname"
@attribute [Authorize(Policy = "inventory.stock-opname.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.StockOpnames
@using ErpOne.Domain.Entities
@inject IStockOpnameService Opnames
@inject NavigationManager Nav

<PageTitle>Stock Opname</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Inventory</span><span class="sep">·</span><span class="here">Stock Opname</span></nav>
            <h1>Stock Opname</h1>
            <p>Formal physical count per warehouse (approval required before posting).</p>
        </div>
        <AuthorizeView Policy="inventory.stock-opname.create">
            <Authorized>
                <div class="pi-actions"><a class="btn btn-primary" href="/inventory/stock-opname/new"><i class="bi bi-plus-lg"></i> New opname</a></div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <div class="chips">
            <button class="chip @(_status is null ? "on" : "")" @onclick="() => SetStatusAsync(null)">All</button>
            @foreach (var s in Enum.GetValues<StockOpnameStatus>())
            { <button class="chip @(_status == s ? "on" : "")" @onclick="() => SetStatusAsync(s)">@s</button> }
        </div>
    </div>

    @if (_page is null) { <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div> }
    else if (_page.Total == 0) { <div class="empty"><div class="empty-ic"><i class="bi bi-clipboard-data"></i></div><p>No opname documents yet.</p></div> }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th style="width:150px">Opname</th><th style="width:110px">Date</th><th>Warehouse</th><th class="text-end" style="width:80px">Lines</th><th class="text-end" style="width:110px">Variance</th><th style="width:130px" class="text-center">Status</th></tr></thead>
                    <tbody>
                        @foreach (var o in _page.Items)
                        {
                            <tr style="cursor:pointer" @onclick="@(() => Nav.NavigateTo($"/inventory/stock-opname/{o.Id}"))">
                                <td class="code mono">@o.OpnameNumber</td>
                                <td class="code">@o.OpnameDate.ToString("d MMM yyyy")</td>
                                <td>@o.WarehouseName</td>
                                <td class="text-end mono">@o.LineCount</td>
                                <td class="text-end mono">@(o.TotalVariance > 0 ? "+" : "")@o.TotalVariance</td>
                                <td class="text-center"><span class="badge @StatusClass(o.Status)">@o.Status</span></td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            @if (_page.TotalPages > 1) { <div class="card-foot"><Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" /></div> }
        </div>
    }
</div>

@code {
    private const int PageSize = 15;
    private PagedResult<StockOpnameListItemDto>? _page;
    private int _currentPage = 1;
    private StockOpnameStatus? _status;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync() => _page = await Opnames.GetPagedAsync(_currentPage, PageSize, null, _status);
    private async Task SetStatusAsync(StockOpnameStatus? s) { _status = s; _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int p) { _currentPage = p; await LoadAsync(); }

    private static string StatusClass(string s) => s switch
    {
        "Posted" => "bg-success-subtle text-success-emphasis",
        "PendingApproval" => "bg-warning-subtle text-warning-emphasis",
        _ => "bg-secondary-subtle text-secondary-emphasis"
    };
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ErpOne.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Checkpoint** — pause for user review & manual commit.

---

### Task 8: Web — Form page (create header)

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameForm.razor`

**Interfaces:**
- Consumes: `IStockOpnameService.CreateAsync`; `CreateStockOpnameRequest`; `IWarehouseService.GetAllAsync` / `WarehouseDto` (`ErpOne.Application.Warehouses`); `FluentValidation.ValidationException`.

- [ ] **Step 1: Create the form page**

Create `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameForm.razor`. It creates the header only; lines are generated server-side, then it redirects to the count sheet.

```razor
@page "/inventory/stock-opname/new"
@attribute [Authorize(Policy = "inventory.stock-opname.create")]
@rendermode InteractiveServer
@using ErpOne.Application.StockOpnames
@using ErpOne.Application.Warehouses
@using FluentValidation
@inject IStockOpnameService Opnames
@inject IWarehouseService WarehouseService
@inject NavigationManager Nav

<PageTitle>New Stock Opname</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs"><a href="/inventory/stock-opname">Stock Opname</a><i class="bi bi-chevron-right"></i><span class="here">New</span></div>
        <h1>New Stock Opname</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else
    {
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }
        <div class="cf-wrap">
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-clipboard-data"></i></span><div class="hd-tx"><h2>Opname</h2><p>Pick a warehouse; the count sheet is generated from its current stock.</p></div></div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c4"><label class="fl">Date <span class="req">*</span></label><input type="date" class="ctl" @bind="_date" /></div>
                        <div class="f c4"><label class="fl">Warehouse <span class="req">*</span></label>
                            <select class="ctl" @bind="_warehouseId"><option value="0">— select —</option>@foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }</select></div>
                        <div class="f c12"><label class="fl">Notes</label><input class="ctl" @bind="_notes" placeholder="Optional" /></div>
                    </div>
                    <p class="text-muted mt-2"><i class="bi bi-info-circle"></i> All variants with stock in the selected warehouse will be added to the count sheet.</p>
                </div>
            </section>
            <div class="pf-footer"><div class="in">
                <span class="note"><span class="req">*</span> required</span>
                <a class="btn btn-ghost" href="/inventory/stock-opname"><i class="bi bi-x-lg"></i> Cancel</a>
                <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving"><i class="bi bi-check2"></i> Create & count</button>
            </div></div>
        </div>
    }
</div>

@code {
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private DateTime _date = DateTime.Today;
    private int _warehouseId;
    private string? _notes, _error;
    private bool _loading = true, _saving;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            var saved = await Opnames.CreateAsync(new CreateStockOpnameRequest(_date, _warehouseId, _notes));
            Nav.NavigateTo($"/inventory/stock-opname/{saved.Id}/edit");
        }
        catch (ValidationException ex) { _error = ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed."; }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ErpOne.Web`
Expected: Build succeeded, 0 errors.

> If `IWarehouseService`/`WarehouseDto` live in a namespace other than `ErpOne.Application.Warehouses`, correct the `@using` (grep the Stock Transfer form — it uses the same service).

- [ ] **Step 3: Checkpoint** — pause for user review & manual commit.

---

### Task 9: Web — Count sheet page (edit physical counts)

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameCountSheet.razor`

**Interfaces:**
- Consumes: `IStockOpnameService.GetByIdAsync`, `UpdateAsync`; `UpdateStockOpnameRequest`, `StockOpnameCountInput`, `StockOpnameLineDto`; `FluentValidation.ValidationException`.

- [ ] **Step 1: Create the count sheet page**

Create `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameCountSheet.razor`:

```razor
@page "/inventory/stock-opname/{Id:int}/edit"
@attribute [Authorize(Policy = "inventory.stock-opname.edit")]
@rendermode InteractiveServer
@using ErpOne.Application.StockOpnames
@using FluentValidation
@inject IStockOpnameService Opnames
@inject NavigationManager Nav

<PageTitle>Count Sheet</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs"><a href="/inventory/stock-opname">Stock Opname</a><i class="bi bi-chevron-right"></i><span class="here">Count sheet</span></div>
        <h1>@(_o?.OpnameNumber ?? "Count Sheet")</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else if (_o is null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> Opname not found or not editable.</div> }
    else
    {
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }
        <div class="cf-wrap">
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-clipboard-data"></i></span><div class="hd-tx"><h2>@_o.WarehouseName</h2><p>Enter counted quantities. Variance = Physical − System.</p></div></div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c4"><label class="fl">Date</label><input type="date" class="ctl" @bind="_date" /></div>
                        <div class="f c8"><label class="fl">Notes</label><input class="ctl" @bind="_notes" placeholder="Optional" /></div>
                    </div>
                    <table class="table align-middle mt-3">
                        <thead class="table-light"><tr><th>Product / Variant</th><th class="text-end" style="width:110px">System</th><th class="text-end" style="width:150px">Physical</th><th class="text-end" style="width:110px">Variance</th></tr></thead>
                        <tbody>
                            @for (int i = 0; i < _rows.Count; i++)
                            {
                                var idx = i;
                                var variance = _rows[idx].Physical - _rows[idx].SystemQty;
                                <tr>
                                    <td><div class="prod"><span class="pn">@_rows[idx].ProductName</span> <span class="sku">@_rows[idx].Sku</span></div></td>
                                    <td class="text-end mono">@_rows[idx].SystemQty</td>
                                    <td><input class="ctl mono text-end" type="number" min="0" step="1" @bind="_rows[idx].Physical" /></td>
                                    <td class="text-end mono">@(variance > 0 ? "+" : "")@variance</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
            </section>
            <div class="pf-footer"><div class="in">
                <a class="btn btn-ghost" href="@($"/inventory/stock-opname/{Id}")"><i class="bi bi-x-lg"></i> Cancel</a>
                <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving"><i class="bi bi-check2"></i> Save counts</button>
            </div></div>
        </div>
    }
</div>

@code {
    [Parameter] public int Id { get; set; }

    private sealed class Row { public int LineId; public string Sku = ""; public string ProductName = ""; public int SystemQty; public int Physical; }

    private StockOpnameDto? _o;
    private readonly List<Row> _rows = [];
    private DateTime _date = DateTime.Today;
    private string? _notes, _error;
    private bool _loading = true, _saving;

    protected override async Task OnParametersSetAsync()
    {
        var o = await Opnames.GetByIdAsync(Id);
        if (o is null || o.Status != "Draft") { Nav.NavigateTo("/inventory/stock-opname"); return; }
        _o = o; _date = o.OpnameDate; _notes = o.Notes;
        _rows.Clear();
        foreach (var l in o.Lines)
            _rows.Add(new Row { LineId = l.Id, Sku = l.Sku, ProductName = l.ProductName, SystemQty = l.SystemQty, Physical = l.PhysicalQty });
        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            var counts = _rows.Select(r => new StockOpnameCountInput(r.LineId, r.Physical)).ToList();
            await Opnames.UpdateAsync(Id, new UpdateStockOpnameRequest(_date, _notes, counts));
            Nav.NavigateTo($"/inventory/stock-opname/{Id}");
        }
        catch (ValidationException ex) { _error = ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed."; }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ErpOne.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Checkpoint** — pause for user review & manual commit.

---

### Task 10: Web — Detail page (approval workflow)

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameDetail.razor`

**Interfaces:**
- Consumes: `IStockOpnameService.GetByIdAsync/SubmitAsync/ApproveAsync/RejectAsync`; `StockOpnameDto`, `StockOpnameLineDto`, `ApprovalStepDto`; `IAuthorizationService`; `SwalService`; `AuthenticationState` cascading param. Mirrors `StockTransferDetail.razor` plumbing exactly (creator-exclusion, inline reject card, `RunAsync`).

- [ ] **Step 1: Create the detail page**

Create `src/ErpOne.Web/Components/Pages/Inventory/StockOpname/StockOpnameDetail.razor`:

```razor
@page "/inventory/stock-opname/{Id:int}"
@attribute [Authorize(Policy = "inventory.stock-opname.index")]
@rendermode InteractiveServer
@using FluentValidation
@using ErpOne.Application.Approvals
@using ErpOne.Application.StockOpnames
@inject IStockOpnameService Opnames
@inject IAuthorizationService Auth
@inject SwalService Swal

<PageTitle>@(_o?.OpnameNumber ?? "Opname")</PageTitle>

@if (_loading)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else if (_o is null)
{
    <div class="pf pf-detail"><div class="pf-alert err"><i class="bi bi-exclamation-octagon"></i> Opname not found.</div></div>
}
else
{
    <div class="pf pf-detail">
        <div class="pf-top">
            <div class="crumbs"><a href="/inventory/stock-opname">Stock Opname</a><i class="bi bi-chevron-right"></i><span class="here">@_o.OpnameNumber</span></div>
        </div>
        <div class="pd-head">
            <h1>@_o.OpnameNumber</h1>
            <span class="badge @StatusClass(_o.Status)">@_o.Status</span>
            <div class="actions">
                @if (_o.Status == "Draft")
                {
                    <AuthorizeView Policy="inventory.stock-opname.edit"><Authorized>
                        <a class="btn btn-line btn-sm" href="@($"/inventory/stock-opname/{_o.Id}/edit")"><i class="bi bi-pencil"></i> Edit counts</a>
                    </Authorized></AuthorizeView>
                    <AuthorizeView Policy="inventory.stock-opname.post"><Authorized>
                        <button class="btn btn-primary btn-sm" @onclick="SubmitAsync" disabled="@_busy"><i class="bi bi-send"></i> Submit</button>
                    </Authorized></AuthorizeView>
                }
                @if (_o.Status == "PendingApproval" && _canApprove)
                {
                    <button class="btn btn-ok btn-sm" @onclick="ApproveAsync" disabled="@_busy"><i class="bi bi-check2-circle"></i> Approve</button>
                    <button class="btn btn-danger btn-sm" @onclick="@(() => _showReject = true)" disabled="@_busy"><i class="bi bi-x-circle"></i> Reject</button>
                }
            </div>
        </div>

        @if (_error is not null) { <div class="pf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        @if (_showReject)
        {
            <section class="card danger">
                <div class="card-b">
                    <textarea class="ctl" rows="2" placeholder="Rejection reason" @bind="_rejectReason"></textarea>
                    <div class="row-end">
                        <button class="btn btn-ghost btn-sm" @onclick="@(() => _showReject = false)">Cancel</button>
                        <button class="btn btn-danger btn-sm" @onclick="RejectAsync" disabled="@_busy">Reject opname</button>
                    </div>
                </div>
            </section>
        }

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-clipboard-data"></i></span><h2>Opname</h2></div>
            <div class="card-b">
                <div class="info-grid">
                    <div class="info-cell"><span class="k"><i class="bi bi-calendar3"></i> Date</span><span class="v">@_o.OpnameDate.ToString("d MMM yyyy")</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-building"></i> Warehouse</span><span class="v">@_o.WarehouseName</span></div>
                    @if (!string.IsNullOrWhiteSpace(_o.Notes)) { <div class="info-cell wide"><span class="k"><i class="bi bi-sticky"></i> Notes</span><span class="v">@_o.Notes</span></div> }
                    @if (!string.IsNullOrWhiteSpace(_o.RejectionNote)) { <div class="info-cell wide reject"><span class="k"><i class="bi bi-exclamation-triangle"></i> Rejection note</span><span class="v">@_o.RejectionNote</span></div> }
                </div>
            </div>
        </section>

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-list-ul"></i></span><h2>Count lines</h2></div>
            <div class="card-b">
                <table class="items">
                    <thead><tr><th>Product</th><th class="r">System</th><th class="r">Physical</th><th class="r">Variance</th><th class="r">On hand now</th></tr></thead>
                    <tbody>
                        @foreach (var l in _o.Lines)
                        {
                            <tr>
                                <td><div class="prod"><span class="pn">@l.ProductName</span><span class="sku">@l.Sku</span></div></td>
                                <td class="r mono">@l.SystemQty</td>
                                <td class="r mono">@l.PhysicalQty</td>
                                <td class="r mono">@(l.Variance > 0 ? "+" : "")@l.Variance</td>
                                <td class="r mono">@l.OnHandNow</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </section>

        @if (_steps.Count > 0)
        {
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-diagram-3"></i></span><h2>Approval</h2></div>
                <div class="card-b">
                    <ul class="tl">
                        @foreach (var s in _steps)
                        {
                            <li>
                                <span class="dot @StepDot(s.Status)"><i class="bi @StepIcon(s.Status)"></i></span>
                                <div>
                                    <div class="ti">Step @s.StepOrder · @s.RoleName</div>
                                    <div class="meta">@s.Status@(s.ActedByName is null ? "" : $" · {s.ActedByName}")</div>
                                    @if (!string.IsNullOrWhiteSpace(s.Note)) { <div class="note">@s.Note</div> }
                                </div>
                            </li>
                        }
                    </ul>
                </div>
            </section>
        }
    </div>
}

@code {
    [Parameter] public int Id { get; set; }
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private StockOpnameDto? _o;
    private IReadOnlyList<ApprovalStepDto> _steps = [];
    private bool _loading = true, _busy, _canApprove, _showReject;
    private string _rejectReason = string.Empty;
    private string? _error;
    private System.Security.Claims.ClaimsPrincipal _user = default!;

    protected override async Task OnInitializedAsync()
    {
        _user = (await AuthStateTask).User;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _o = await Opnames.GetByIdAsync(Id);
        _steps = _o?.ApprovalSteps ?? [];
        _canApprove = await EvaluateCanApproveAsync();
        _loading = false;
    }

    private async Task<bool> EvaluateCanApproveAsync()
    {
        if (_o is null || _o.Status != "PendingApproval") return false;
        if (!(await Auth.AuthorizeAsync(_user, "inventory.stock-opname.approve")).Succeeded) return false;
        if (string.Equals(_o.CreatedBy, _user.Identity?.Name, StringComparison.OrdinalIgnoreCase)) return false;
        var current = _steps.FirstOrDefault(s => s.Status == "Pending");
        return current is not null && _user.IsInRole(current.RoleName);
    }

    private async Task SubmitAsync() => await RunAsync(() => Opnames.SubmitAsync(Id), "Opname submitted");
    private async Task ApproveAsync() =>
        await RunAsync(() => Opnames.ApproveAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole), "Opname approved & posted");

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(_rejectReason)) { _error = "Rejection reason is required."; return; }
        var reason = _rejectReason;
        _showReject = false;
        _rejectReason = string.Empty;
        await RunAsync(() => Opnames.RejectAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole, reason), "Opname rejected, returned to Draft");
    }

    private async Task RunAsync(Func<Task> action, string okMsg)
    {
        _error = null; _busy = true;
        try
        {
            await action();
            await LoadAsync();
            await Swal.ToastAsync("success", okMsg);
        }
        catch (ValidationException ex) { _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)); }
        catch (InvalidOperationException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string StatusClass(string s) => s switch
    {
        "Draft" => "b-draft", "PendingApproval" => "b-warn", "Posted" => "b-done", _ => "b-dark"
    };
    private static string StepIcon(string s) => s switch { "Approved" => "bi-check-lg", "Rejected" => "bi-x-lg", _ => "bi-clock" };
    private static string StepDot(string s) => s switch { "Approved" => "ok", "Rejected" => "no", _ => "wait" };
}
```

- [ ] **Step 2: Build the full solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full integration test suite**

Run: `dotnet test tests/ErpOne.IntegrationTests`
Expected: All tests PASS (~318: prior baseline + the 4 new opname tests; the NumberSequence count assertion now expects 14).

- [ ] **Step 4: Manual smoke test (verify end-to-end)**

Run the app, log in as admin, then:
1. `/inventory/stock-opname` → **New opname** → pick a warehouse with stock → **Create & count**.
2. On the count sheet, change a Physical qty (e.g. system 100 → physical 120), **Save counts**.
3. On the detail page, **Submit**, then **Approve** (as a different-from-creator user in the approver role, or reconfigure the chain).
4. Confirm status → **Posted**, variance shown, and Stock Levels for that variant/warehouse now equals the counted quantity.

- [ ] **Step 5: Checkpoint** — pause for user review & manual commit.

---

## Self-Review Notes

- **Spec coverage:** Domain (Task 1), enums/constants (Task 2), EF + migration + NumberSequence Id=14 (Task 3), Application DTOs/interface/validators (Task 4), Infrastructure service with live-basis variance + no-MA + no-GL (Task 5), menu resource `inventory.stock-opname` + approval chain seed (Task 6), all four pages Index/Form/CountSheet/Detail (Tasks 7–10), and the three required test scenarios plus the count bump (Tasks 3 & 5). Permission seeding is automatic via `AppMenus.AllPermissions` (noted in Task 6).
- **Test count:** spec targets ~317 (314 + 3). This plan adds 4 opname tests (surplus, shortage, zero-variance no-op, live-basis) → ~318; the extra test is deliberate and harmless. Adjust the expectation if the baseline differs.
- **Type consistency:** `SetPhysicalCounts((int LineId, int PhysicalQty))` used by `UpdateAsync`; `SetLines((VariantId, SystemQty, PhysicalQty))` used by `CreateAsync`; `StockOpnameCountInput(LineId, PhysicalQty)` matches the count-sheet mapping; `MovementType.Adjustment` + `StockMovement(..., "StockOpname", o.Id, o.OpnameNumber)` ref matches the test's `RefType`/`RefId` filter.
- **Assumptions to verify during execution (grep first):** `IWarehouseService`/`WarehouseDto` namespace; FluentValidation auto-registration convention; `StockAdjustmentRequest`/`StockAdjustmentLine` record shapes used in the live-basis test; the `dotnet ef` startup project.
