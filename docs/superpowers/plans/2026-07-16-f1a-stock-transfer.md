# Fase 1a — Stock Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transfer inventory quantity between warehouses via an approval-gated document (Draft → PendingApproval → Posted); on full approval, stock moves from source to destination in one step.

**Architecture:** `StockTransfer` aggregate (+ lines) mirrors `SupplierPayment`'s approval flow: `Submit` → `IApprovalService` chain → when fully approved, a private post writes two `StockMovement` legs (Out at source, In at destination, at `ProductVariant.CostPrice`) and two `UpsertStockAsync` deltas. No cost change (moving-average is global per variant) and **no GL journal** (internal move; 5b untouched).

**Tech Stack:** .NET 10, Blazor Server, EF Core (tests SQLite `EnsureCreated`+`AccountingSeeder`), FluentValidation, xUnit. Reuses `IApprovalService`, `IStockService.GetOnHandAsync`, `UpsertStockAsync`, `IDocumentNumberService`, `.pi/.cf/.pf`. Solution `ErpOne.slnx`.

## Global Constraints

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`. **App di VS di-stop** sebelum build/test.
- Namespaces (verbatim): entities/enums → `ErpOne.Domain.Entities`; new Application service → `ErpOne.Application.StockTransfers`; `IApprovalService`/`ApprovalStepDto` → `ErpOne.Application.Approvals`; `IStockService` → `ErpOne.Application.Stock`; `DocumentTypes` → `ErpOne.Application.Numbering`; infra service → `ErpOne.Infrastructure.Services`; `UpsertStockAsync` → `ErpOne.Infrastructure.Persistence`.
- Entity pattern: `private set`, private ctor `// EF Core`, backing `List<>` as `IReadOnlyCollection`, invariants throw.
- Service: primary-ctor DI; money-movement wraps `await using var tx = await db.Database.BeginTransactionAsync(ct)`; `private static ValidationException Fail(string)`.
- `StockMovement` ctor: `(int productVariantId, int warehouseId, MovementType type, int quantity, decimal unitCost, DateTime movementDate, string? refType = null, int? refId = null, string? note = null)` — `quantity != 0` required (never pass 0). Use `MovementType.Transfer` for both legs.
- `UpsertStockAsync(this AppDbContext db, int variantId, int warehouseId, int delta, CancellationToken ct)` — throws `InvalidOperationException("Stock cannot go negative.")` if it would.
- `IStockService.GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct)` → current qty (0 if none).
- Register new business tables in `tablePrefixes` (T_) or model build fails. NumberSequence next Id = **13** (max is 12 JournalEntry).
- Commit MANUAL — "Commit" steps are markers; JANGAN `git commit/merge/push`. Boleh `git add`. Branch `Development`.

---

## File Structure
- Create Domain: `src/ErpOne.Domain/Entities/Transactions/StockTransfer.cs`, `StockTransferLine.cs`, `StockTransferStatus.cs`
- Create Application: `src/ErpOne.Application/StockTransfers/StockTransferDtos.cs`, `IStockTransferService.cs`, `StockTransferValidators.cs`
- Create Infra: `src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs`; migration `*_AddStockTransfer.cs`
- Create Web: `src/ErpOne.Web/Components/Pages/Inventory/Transfers/StockTransferIndex.razor`, `StockTransferForm.razor`, `StockTransferDetail.razor`
- Create Tests: `tests/ErpOne.IntegrationTests/StockTransferServiceTests.cs`
- Modify: `ApprovalDocumentType.cs`, `AppDbContext.cs`, `DocumentTypes.cs`, `DependencyInjection.cs`, `AppMenus.cs`, `BootstrapSeeder.cs`, `NumberSequenceServiceTests.cs`

---

## Task 1: Domain — StockTransfer + line + status

**Files:** Create `StockTransferStatus.cs`, `StockTransferLine.cs`, `StockTransfer.cs` under `src/ErpOne.Domain/Entities/Transactions/`.

**Interfaces:**
- Produces: `StockTransferStatus { Draft, PendingApproval, Posted }`; `StockTransferLine(int productVariantId, int quantity)`; `StockTransfer` with `SetLines`, `UpdateHeader`, `Submit`, `MarkPosted`, `ReturnToDraft`.

- [ ] **Step 1: Status enum**

Create `StockTransferStatus.cs`:
```csharp
namespace ErpOne.Domain.Entities;

public enum StockTransferStatus { Draft, PendingApproval, Posted }
```

- [ ] **Step 2: Line entity**

Create `StockTransferLine.cs`:
```csharp
namespace ErpOne.Domain.Entities;

public class StockTransferLine
{
    public int Id { get; private set; }
    public int StockTransferId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }

    private StockTransferLine() { } // EF Core

    public StockTransferLine(int productVariantId, int quantity)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        ProductVariantId = productVariantId;
        Quantity = quantity;
    }
}
```

- [ ] **Step 3: Aggregate**

Create `StockTransfer.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Transfer stok antar gudang. Draft → PendingApproval → Posted (stok pindah saat fully-approved).</summary>
public class StockTransfer : AuditableEntity
{
    private readonly List<StockTransferLine> _lines = [];

    public int Id { get; private set; }
    public string TransferNumber { get; private set; } = default!;
    public DateTime TransferDate { get; private set; }
    public int SourceWarehouseId { get; private set; }
    public int DestinationWarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public StockTransferStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<StockTransferLine> Lines => _lines;

    private StockTransfer() { } // EF Core

    public StockTransfer(string transferNumber, DateTime transferDate, int sourceWarehouseId,
        int destinationWarehouseId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(transferNumber)) throw new ArgumentException("TransferNumber is required.", nameof(transferNumber));
        if (sourceWarehouseId <= 0) throw new ArgumentException("Source warehouse is required.", nameof(sourceWarehouseId));
        if (destinationWarehouseId <= 0) throw new ArgumentException("Destination warehouse is required.", nameof(destinationWarehouseId));
        if (sourceWarehouseId == destinationWarehouseId) throw new ArgumentException("Source and destination must differ.");
        TransferNumber = transferNumber.Trim();
        TransferDate = transferDate;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = StockTransferStatus.Draft;
    }

    public void UpdateHeader(DateTime transferDate, int sourceWarehouseId, int destinationWarehouseId, string? notes)
    {
        EnsureDraft();
        if (sourceWarehouseId == destinationWarehouseId) throw new ArgumentException("Source and destination must differ.");
        TransferDate = transferDate;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<(int VariantId, int Quantity)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(new StockTransferLine(l.VariantId, l.Quantity));
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit a transfer without lines.");
        Status = StockTransferStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != StockTransferStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending transfer can be posted.");
        Status = StockTransferStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != StockTransferStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending transfer can be returned to draft.");
        Status = StockTransferStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != StockTransferStatus.Draft) throw new InvalidOperationException("Only a draft transfer can be modified.");
    }
}
```

- [ ] **Step 4: Build Domain**

Run: `dotnet build src/ErpOne.Domain/ErpOne.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Transactions/StockTransfer.cs src/ErpOne.Domain/Entities/Transactions/StockTransferLine.cs src/ErpOne.Domain/Entities/Transactions/StockTransferStatus.cs
```

---

## Task 2: EF wiring + approval type + numbering + migration

**Files:** Modify `ApprovalDocumentType.cs`, `DocumentTypes.cs`, `AppDbContext.cs`, `NumberSequenceServiceTests.cs`; create migration.

- [ ] **Step 1: ApprovalDocumentType**

In `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`, add `StockTransfer` before the closing brace:
```csharp
    ExpenseVoid,
    StockTransfer
```
(Add a comma after `ExpenseVoid` if missing.)

- [ ] **Step 2: DocumentTypes constant**

In `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs`, after `JournalEntry`:
```csharp
    public const string StockTransfer = "StockTransfer";
```

- [ ] **Step 3: DbSets**

In `AppDbContext.cs`, near the other transaction DbSets (after `DeliveryOrderLines`):
```csharp
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
```

- [ ] **Step 4: Config blocks**

In `OnModelCreating`, after the `DeliveryOrderLine` config block, add:
```csharp
        modelBuilder.Entity<StockTransfer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TransferNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.TransferNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.StockTransferId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(StockTransfer.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StockTransferLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 5: NumberSequence seed row Id=13**

In the `NumberSequence` `HasData(...)`, add after the `Id = 12` row (add a comma to it):
```csharp
                ,new { Id = 13, Code = "StockTransfer", Prefix = "TRF", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 6: tablePrefixes**

In the Transaksi section of `tablePrefixes`:
```csharp
            [nameof(StockTransfer)] = "T_",
            [nameof(StockTransferLine)] = "T_",
```

- [ ] **Step 7: Bump NumberSequence test**

In `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`, change `Assert.Equal(12, all.Count)` to:
```csharp
        Assert.Equal(13, all.Count);   // ... + JournalEntry + StockTransfer
```

- [ ] **Step 8: Build + migration**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj` → succeeded.
App di VS di-stop. Run: `dotnet ef migrations add AddStockTransfer -p src/ErpOne.Infrastructure -s src/ErpOne.Web` → creates `T_StockTransfers`, `T_StockTransferLines` + NumberSequence Id=13.
**Fallback** if `dotnet ef` unavailable: hand-write migration (2 CreateTable + InsertData Id=13 + FKs/indexes; pattern like existing `AddAccountingLedger`). Then `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`.

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs
```

---

## Task 3: StockTransferService (DTO + interface + validator + impl + DI) — TDD

**Files:** Create `StockTransferDtos.cs`, `IStockTransferService.cs`, `StockTransferValidators.cs` (Application/StockTransfers); `StockTransferService.cs` (Infrastructure/Services/Transactions); modify `DependencyInjection.cs`; create `StockTransferServiceTests.cs`.

**Interfaces:**
- Consumes: `StockTransfer`, `IApprovalService`, `IStockService`, `UpsertStockAsync`, `IDocumentNumberService`, `PagedResult<T>`.
- Produces: `IStockTransferService`, DTOs, validator.

- [ ] **Step 1: DTOs**

Create `src/ErpOne.Application/StockTransfers/StockTransferDtos.cs`:
```csharp
using ErpOne.Application.Approvals;

namespace ErpOne.Application.StockTransfers;

public record StockTransferLineInput(int ProductVariantId, int Quantity);

public record CreateStockTransferRequest(DateTime TransferDate, int SourceWarehouseId, int DestinationWarehouseId,
    string? Notes, IReadOnlyList<StockTransferLineInput> Lines);

public record StockTransferLineDto(int Id, int ProductVariantId, string Sku, string ProductName, int Quantity, int OnHandSource);

public record StockTransferDto(int Id, string TransferNumber, DateTime TransferDate,
    int SourceWarehouseId, string SourceWarehouseName, int DestinationWarehouseId, string DestinationWarehouseName,
    string? Notes, string Status, string? RejectionNote, IReadOnlyList<StockTransferLineDto> Lines,
    IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record StockTransferListItemDto(int Id, string TransferNumber, DateTime TransferDate,
    string SourceWarehouseName, string DestinationWarehouseName, int TotalQuantity, string Status);
```

- [ ] **Step 2: Interface**

Create `src/ErpOne.Application/StockTransfers/IStockTransferService.cs`:
```csharp
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.StockTransfers;

public interface IStockTransferService
{
    Task<PagedResult<StockTransferListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, StockTransferStatus? status = null, CancellationToken ct = default);
    Task<StockTransferDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<StockTransferDto> CreateAsync(CreateStockTransferRequest request, CancellationToken ct = default);
    Task<StockTransferDto> UpdateAsync(int id, CreateStockTransferRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
```

- [ ] **Step 3: Validator**

Create `src/ErpOne.Application/StockTransfers/StockTransferValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.StockTransfers;

public class CreateStockTransferValidator : AbstractValidator<CreateStockTransferRequest>
{
    public CreateStockTransferValidator()
    {
        RuleFor(x => x.SourceWarehouseId).GreaterThan(0);
        RuleFor(x => x.DestinationWarehouseId).GreaterThan(0)
            .NotEqual(x => x.SourceWarehouseId).WithMessage("Source and destination warehouses must differ.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductVariantId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
```

- [ ] **Step 4: Write failing tests**

Create `tests/ErpOne.IntegrationTests/StockTransferServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.StockTransfers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockTransferServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockTransferServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds 2 warehouses, 1 product/variant, and opening stock at source. Returns (src, dst, variantId).
    private static async Task<(int src, int dst, int variantId)> SeedAsync(IServiceProvider sp, int openingAtSource)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var src = new Warehouse($"SR{id}", $"Src {id}", null, true, false);
        var dst = new Warehouse($"DS{id}", $"Dst {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        db.Warehouses.AddRange(src, dst); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, src.Id, openingAtSource));
        await db.SaveChangesAsync();
        return (src.Id, dst.Id, v.Id);
    }

    [Fact]
    public async Task Approve_moves_stock_source_to_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (src, dst, variantId) = await SeedAsync(sp, 100);
        var svc = sp.GetRequiredService<IStockTransferService>();

        var created = await svc.CreateAsync(new CreateStockTransferRequest(
            DateTime.Today, src, dst, null, [new StockTransferLineInput(variantId, 30)]));
        await svc.SubmitAsync(created.Id);
        // Default seeded chain (admin) → one Pending step; approve as admin role.
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);

        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        Assert.Equal(70, await stock.GetOnHandAsync(variantId, src));
        Assert.Equal(30, await stock.GetOnHandAsync(variantId, dst));
    }

    [Fact]
    public async Task Insufficient_source_stock_is_rejected_on_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (src, dst, variantId) = await SeedAsync(sp, 10);
        var svc = sp.GetRequiredService<IStockTransferService>();

        var created = await svc.CreateAsync(new CreateStockTransferRequest(
            DateTime.Today, src, dst, null, [new StockTransferLineInput(variantId, 50)]));
        await svc.SubmitAsync(created.Id);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));

        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        Assert.Equal(10, await stock.GetOnHandAsync(variantId, src)); // unchanged
    }

    [Fact]
    public async Task Same_source_and_destination_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (src, _, variantId) = await SeedAsync(sp, 100);
        var svc = sp.GetRequiredService<IStockTransferService>();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreateStockTransferRequest(DateTime.Today, src, src, null, [new StockTransferLineInput(variantId, 5)])));
    }
}
```

- [ ] **Step 5: Run — verify fail**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~StockTransferServiceTests"`
Expected: FAIL (service not registered).

- [ ] **Step 6: Implementation**

Create `src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.Stock;
using ErpOne.Application.StockTransfers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockTransferService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    IValidator<CreateStockTransferRequest> validator,
    IDocumentNumberService docNumbers) : IStockTransferService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.StockTransfer;

    public async Task<PagedResult<StockTransferListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, StockTransferStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.StockTransfers.AsNoTracking();
        if (status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.TransferNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockTransferListItemDto(
                x.Id, x.TransferNumber, x.TransferDate,
                db.Warehouses.Where(w => w.Id == x.SourceWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                db.Warehouses.Where(w => w.Id == x.DestinationWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "—",
                x.Lines.Sum(l => l.Quantity), x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<StockTransferListItemDto>(items, total, page, pageSize);
    }

    public async Task<StockTransferDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;
        var srcName = await db.Warehouses.Where(w => w.Id == t.SourceWarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var dstName = await db.Warehouses.Where(w => w.Id == t.DestinationWarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";
        var variantIds = t.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variantInfo = await (from v in db.ProductVariants.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                                 where variantIds.Contains(v.Id)
                                 select new { v.Id, v.Sku, p.Name }).ToListAsync(ct);
        var info = variantInfo.ToDictionary(x => x.Id, x => (x.Sku, x.Name));
        var lines = new List<StockTransferLineDto>();
        foreach (var l in t.Lines)
        {
            var onHand = await stock.GetOnHandAsync(l.ProductVariantId, t.SourceWarehouseId, ct);
            var (sku, name) = info.TryGetValue(l.ProductVariantId, out var x) ? x : ("?", "(unknown)");
            lines.Add(new StockTransferLineDto(l.Id, l.ProductVariantId, sku, name, l.Quantity, onHand));
        }
        var steps = await approval.GetStepsAsync(DocType, t.Id, ct);
        return new StockTransferDto(t.Id, t.TransferNumber, t.TransferDate, t.SourceWarehouseId, srcName,
            t.DestinationWarehouseId, dstName, t.Notes, t.Status.ToString(), t.RejectionNote, lines, steps);
    }

    public async Task<StockTransferDto> CreateAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateWarehousesAsync(request, ct);
        var number = await docNumbers.NextAsync(DocumentTypes.StockTransfer, request.TransferDate, ct);
        var t = new StockTransfer(number, request.TransferDate, request.SourceWarehouseId, request.DestinationWarehouseId, request.Notes);
        t.SetLines(request.Lines.Select(l => (l.ProductVariantId, l.Quantity)));
        db.StockTransfers.Add(t);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(t.Id, ct))!;
    }

    public async Task<StockTransferDto> UpdateAsync(int id, CreateStockTransferRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await ValidateWarehousesAsync(request, ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        t.UpdateHeader(request.TransferDate, request.SourceWarehouseId, request.DestinationWarehouseId, request.Notes);
        t.SetLines(request.Lines.Select(l => (l.ProductVariantId, l.Quantity)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var t = await db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Transfer not found.");
        if (t.Status != StockTransferStatus.Draft) throw Fail("Only a draft transfer can be deleted.");
        db.StockTransfers.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        t.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, t.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, t.Id, ct);
        if (fullyApproved) await PostAsync(t, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Transfer not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, t.Id, actingUserName, isInRole, t.CreatedBy, ct);
        if (fullyApproved) await PostAsync(t, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Transfer not found.");
        await approval.RejectAsync(DocType, t.Id, actingUserName, isInRole, t.CreatedBy, reason, ct);
        t.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, t.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Moves stock; caller saves + commits.
    private async Task PostAsync(StockTransfer t, CancellationToken ct)
    {
        foreach (var g in t.Lines.GroupBy(l => l.ProductVariantId))
        {
            var need = g.Sum(l => l.Quantity);
            var onHand = await stock.GetOnHandAsync(g.Key, t.SourceWarehouseId, ct);
            if (onHand < need) throw Fail($"Insufficient stock at source for variant {g.Key} (need {need}, have {onHand}).");
        }
        foreach (var line in t.Lines)
        {
            var cost = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId).Select(v => v.CostPrice).FirstOrDefaultAsync(ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.SourceWarehouseId, MovementType.Transfer,
                -line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, t.DestinationWarehouseId, MovementType.Transfer,
                line.Quantity, cost, t.TransferDate, "StockTransfer", t.Id, t.TransferNumber));
            await db.UpsertStockAsync(line.ProductVariantId, t.SourceWarehouseId, -line.Quantity, ct);
            await db.UpsertStockAsync(line.ProductVariantId, t.DestinationWarehouseId, line.Quantity, ct);
        }
        t.MarkPosted();
    }

    private async Task ValidateWarehousesAsync(CreateStockTransferRequest r, CancellationToken ct)
    {
        var count = await db.Warehouses.CountAsync(w => (w.Id == r.SourceWarehouseId || w.Id == r.DestinationWarehouseId), ct);
        if (count < 2) throw Fail("Source or destination warehouse not found.");
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("StockTransfer", message)]);
}
```

- [ ] **Step 7: DI**

In `DependencyInjection.cs`, add `using ErpOne.Application.StockTransfers;` near the other usings and register after `IDeliveryOrderService`:
```csharp
        services.AddScoped<IStockTransferService, StockTransferService>();
```

- [ ] **Step 8: Run tests — pass**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~StockTransferServiceTests"`
Expected: PASS (3 tests). (Chain seeded via BootstrapSeeder is NOT run in tests → chain empty → Submit auto-posts. So the approve test: after Submit with empty chain the transfer is already Posted; `ApproveAsync` on a non-pending doc — adjust: with empty chain, `SubmitAsync` posts immediately, so drop the explicit ApproveAsync call OR seed a chain step in the test. **Use:** in the approve test, after `CreateAsync`, seed a chain step directly: `db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockTransfer, 1, "Administrators")); await db.SaveChangesAsync();` BEFORE `SubmitAsync`, so a Pending step exists and `ApproveAsync` posts. Update the two approval tests accordingly.)

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Application/StockTransfers/ src/ErpOne.Infrastructure/Services/Transactions/StockTransferService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/StockTransferServiceTests.cs
```

> **Test note (chain seeding):** `CustomWebApplicationFactory` does not run `BootstrapSeeder`, so no default StockTransfer chain exists in tests. For `Approve_moves_stock...` and `Insufficient_source_stock...`, seed a chain step before submitting:
> ```csharp
> db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockTransfer, 1, "Administrators"));
> await db.SaveChangesAsync();
> ```
> Then `SubmitAsync` leaves it PendingApproval and `ApproveAsync(id, "admin", _ => true)` posts (the `isInRole` stub returns true).

---

## Task 4: BootstrapSeeder chain + menu

**Files:** Modify `BootstrapSeeder.cs`, `AppMenus.cs`.

- [ ] **Step 1: Seed default chain**

In `BootstrapSeeder.cs`, after the Supplier Payment chain block:
```csharp
        // Seed rantai approval default untuk Stock Transfer (idempotent), mengikuti pola PO/SO.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockTransfer))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockTransfer, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 2: Menu resource + action set**

In `AppMenus.cs`, add an action-set helper near the others:
```csharp
    private static AppAction[] StockTransferActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost];
```
In the `Inventory` group, after `inventory.low-stock`:
```csharp
            new("inventory.transfers", "Stock Transfer", "bi-arrow-left-right", StockTransferActions),
```

- [ ] **Step 3: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj` → succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 5: Web pages (Index + Form + Detail)

**Files:** Create `StockTransferIndex.razor`, `StockTransferForm.razor`, `StockTransferDetail.razor` under `src/ErpOne.Web/Components/Pages/Inventory/Transfers/`.

> **Alignment (do at write time):** open `src/ErpOne.Web/Components/Pages/Inventory/StockAdjustments/StockAdjustmentForm.razor` and copy its **variant dropdown population** (`_variants` from the product service) verbatim into the Form; open `src/ErpOne.Web/Components/Pages/Finance/ApPayments/ApPaymentDetail.razor` and copy its **approval-steps rendering + `_canApprove` + Submit/Approve/Reject plumbing** (UserManager/RoleManager/IAuthorizationService) verbatim into the Detail. Below are complete pages using those same shapes; reconcile the variant-source and approve-permission details with the templates.

- [ ] **Step 1: Index page**

Create `StockTransferIndex.razor`:
```razor
@page "/inventory/transfers"
@attribute [Authorize(Policy = "inventory.transfers.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.StockTransfers
@using ErpOne.Domain.Entities
@inject IStockTransferService Transfers
@inject NavigationManager Nav

<PageTitle>Stock Transfer</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Inventory</span><span class="sep">·</span><span class="here">Stock Transfer</span></nav>
            <h1>Stock Transfer</h1>
            <p>Move inventory between warehouses (approval required before posting).</p>
        </div>
        <AuthorizeView Policy="inventory.transfers.create">
            <Authorized>
                <div class="pi-actions"><a class="btn btn-primary" href="/inventory/transfers/new"><i class="bi bi-plus-lg"></i> New transfer</a></div>
            </Authorized>
        </AuthorizeView>
    </div>

    <div class="toolbar">
        <div class="chips">
            <button class="chip @(_status is null ? "on" : "")" @onclick="() => SetStatusAsync(null)">All</button>
            @foreach (var s in Enum.GetValues<StockTransferStatus>())
            { <button class="chip @(_status == s ? "on" : "")" @onclick="() => SetStatusAsync(s)">@s</button> }
        </div>
    </div>

    @if (_page is null) { <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div> }
    else if (_page.Total == 0) { <div class="empty"><div class="empty-ic"><i class="bi bi-arrow-left-right"></i></div><p>No transfers yet.</p></div> }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th style="width:150px">Transfer</th><th style="width:110px">Date</th><th>From</th><th>To</th><th class="text-end" style="width:90px">Qty</th><th style="width:130px" class="text-center">Status</th></tr></thead>
                    <tbody>
                        @foreach (var t in _page.Items)
                        {
                            <tr style="cursor:pointer" @onclick="@(() => Nav.NavigateTo($"/inventory/transfers/{t.Id}"))">
                                <td class="code mono">@t.TransferNumber</td>
                                <td class="code">@t.TransferDate.ToString("d MMM yyyy")</td>
                                <td>@t.SourceWarehouseName</td>
                                <td>@t.DestinationWarehouseName</td>
                                <td class="text-end mono">@t.TotalQuantity</td>
                                <td class="text-center"><span class="badge @StatusClass(t.Status)">@t.Status</span></td>
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
    private PagedResult<StockTransferListItemDto>? _page;
    private int _currentPage = 1;
    private StockTransferStatus? _status;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync() => _page = await Transfers.GetPagedAsync(_currentPage, PageSize, null, _status);
    private async Task SetStatusAsync(StockTransferStatus? s) { _status = s; _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int p) { _currentPage = p; await LoadAsync(); }

    private static string StatusClass(string s) => s switch
    {
        "Posted" => "bg-success-subtle text-success-emphasis",
        "PendingApproval" => "bg-warning-subtle text-warning-emphasis",
        _ => "bg-secondary-subtle text-secondary-emphasis"
    };
}
```

- [ ] **Step 2: Form page** — model on `StockAdjustmentForm.razor` (warehouse dropdowns + variant `<select>` lines). Create `StockTransferForm.razor`:
```razor
@page "/inventory/transfers/new"
@page "/inventory/transfers/{Id:int}/edit"
@attribute [Authorize(Policy = "inventory.transfers.create")]
@rendermode InteractiveServer
@using ErpOne.Application.StockTransfers
@using ErpOne.Application.Warehouses
@using ErpOne.Application.Products
@using FluentValidation
@inject IStockTransferService Transfers
@inject IWarehouseService WarehouseService
@inject IProductService ProductService
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs"><a href="/inventory/transfers">Stock Transfer</a><i class="bi bi-chevron-right"></i><span class="here">@(Id is null ? "New" : "Edit")</span></div>
        <h1>@Title</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else
    {
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }
        <div class="cf-wrap">
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-arrow-left-right"></i></span><div class="hd-tx"><h2>Transfer</h2><p>Move stock between warehouses.</p></div></div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c4"><label class="fl">Date <span class="req">*</span></label><input type="date" class="ctl" @bind="_date" /></div>
                        <div class="f c4"><label class="fl">From warehouse <span class="req">*</span></label>
                            <select class="ctl" @bind="_sourceId"><option value="0">— select —</option>@foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }</select></div>
                        <div class="f c4"><label class="fl">To warehouse <span class="req">*</span></label>
                            <select class="ctl" @bind="_destId"><option value="0">— select —</option>@foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }</select></div>
                        <div class="f c12"><label class="fl">Notes</label><input class="ctl" @bind="_notes" placeholder="Optional" /></div>
                    </div>

                    <table class="table align-middle mt-3">
                        <thead class="table-light"><tr><th>Product / Variant</th><th class="text-end" style="width:160px">Quantity</th><th style="width:44px"></th></tr></thead>
                        <tbody>
                            @for (int i = 0; i < _lines.Count; i++)
                            {
                                var idx = i;
                                <tr>
                                    <td><select class="ctl" @bind="_lines[idx].VariantId"><option value="0">— select —</option>@foreach (var v in _variants) { <option value="@v.Id">@v.Label</option> }</select></td>
                                    <td><input class="ctl mono text-end" type="number" min="1" step="1" @bind="_lines[idx].Quantity" /></td>
                                    <td><button type="button" class="btn btn-sm btn-outline-danger" @onclick="() => _lines.RemoveAt(idx)"><i class="bi bi-x"></i></button></td>
                                </tr>
                            }
                        </tbody>
                    </table>
                    <button type="button" class="btn btn-outline-secondary btn-sm" @onclick="() => _lines.Add(new())"><i class="bi bi-plus"></i> Add line</button>
                </div>
            </section>
            <div class="pf-footer"><div class="in">
                <span class="note"><span class="req">*</span> required</span>
                <a class="btn btn-ghost" href="/inventory/transfers"><i class="bi bi-x-lg"></i> Cancel</a>
                <button class="btn btn-primary" @onclick="SaveAsync" disabled="@_saving"><i class="bi bi-check2"></i> Save draft</button>
            </div></div>
        </div>
    }
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private sealed class Line { public int VariantId { get; set; } public int Quantity { get; set; } = 1; }
    private sealed record VariantOption(int Id, string Label);

    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private List<VariantOption> _variants = [];
    private readonly List<Line> _lines = [new()];
    private DateTime _date = DateTime.Today;
    private int _sourceId, _destId;
    private string? _notes, _error;
    private bool _loading = true, _saving;

    private string Title => Id is null ? "New Stock Transfer" : "Edit Stock Transfer";

    protected override async Task OnParametersSetAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        // Align with StockAdjustmentForm: flatten products → variants.
        var products = await ProductService.GetAllAsync();
        _variants = products.SelectMany(p => p.Variants.Select(v => new VariantOption(v.Id, $"{p.Name} · {v.Sku}"))).ToList();

        if (Id is int id)
        {
            var t = await Transfers.GetByIdAsync(id);
            if (t is null || t.Status != "Draft") { Nav.NavigateTo("/inventory/transfers"); return; }
            _date = t.TransferDate; _sourceId = t.SourceWarehouseId; _destId = t.DestinationWarehouseId; _notes = t.Notes;
            _lines.Clear();
            foreach (var l in t.Lines) _lines.Add(new Line { VariantId = l.ProductVariantId, Quantity = l.Quantity });
            if (_lines.Count == 0) _lines.Add(new());
        }
        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            var req = new CreateStockTransferRequest(_date, _sourceId, _destId, _notes,
                _lines.Where(l => l.VariantId > 0 && l.Quantity > 0).Select(l => new StockTransferLineInput(l.VariantId, l.Quantity)).ToList());
            var saved = Id is int id ? await Transfers.UpdateAsync(id, req) : await Transfers.CreateAsync(req);
            Nav.NavigateTo($"/inventory/transfers/{saved.Id}");
        }
        catch (ValidationException ex) { _error = ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed."; }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```
> **Verify at write time:** `IProductService.GetAllAsync()` returns items exposing `.Variants` with `.Id`/`.Sku` and `.Name` — confirm the exact shape against `StockAdjustmentForm.razor` (it builds the same `_variants` list); adjust the projection if the DTO differs.

- [ ] **Step 3: Detail page** — model on `ApPaymentDetail.razor` (approval steps + Submit/Approve/Reject). Create `StockTransferDetail.razor`:
```razor
@page "/inventory/transfers/{Id:int}"
@attribute [Authorize(Policy = "inventory.transfers.index")]
@rendermode InteractiveServer
@using ErpOne.Application.StockTransfers
@using Microsoft.AspNetCore.Identity
@using ErpOne.Infrastructure.Identity
@inject IStockTransferService Transfers
@inject NavigationManager Nav
@inject SwalService Swal
@inject IAuthorizationService Auth
@inject UserManager<ApplicationUser> UserManager

<PageTitle>@(_t?.TransferNumber ?? "Transfer")</PageTitle>

@if (_t is null) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
else
{
    <div class="pf pf-detail">
        <div class="pf-top"><div class="crumbs"><a href="/inventory/transfers">Stock Transfer</a><i class="bi bi-chevron-right"></i><span class="here">@_t.TransferNumber</span></div></div>
        <div class="pd-head">
            <h1>@_t.TransferNumber</h1>
            <span class="badge @StatusClass(_t.Status)">@_t.Status</span>
            <div class="actions">
                @if (_t.Status == "Draft")
                {
                    <AuthorizeView Policy="inventory.transfers.edit"><Authorized><a class="btn btn-line btn-sm" href="/inventory/transfers/@_t.Id/edit"><i class="bi bi-pencil"></i> Edit</a></Authorized></AuthorizeView>
                    <AuthorizeView Policy="inventory.transfers.post"><Authorized><button class="btn btn-primary btn-sm" @onclick="SubmitAsync" disabled="@_busy"><i class="bi bi-send"></i> Submit</button></Authorized></AuthorizeView>
                }
                else if (_t.Status == "PendingApproval" && _canApprove)
                {
                    <button class="btn btn-primary btn-sm" @onclick="ApproveAsync" disabled="@_busy"><i class="bi bi-check2-circle"></i> Approve</button>
                    <button class="btn btn-danger btn-sm" @onclick="RejectAsync" disabled="@_busy"><i class="bi bi-x-circle"></i> Reject</button>
                }
            </div>
        </div>

        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-arrow-left-right"></i></span><h2>Transfer</h2></div>
            <div class="card-b">
                <div class="info-grid">
                    <div class="info-cell"><span class="k"><i class="bi bi-calendar3"></i> Date</span><span class="v">@_t.TransferDate.ToString("d MMM yyyy")</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-box-arrow-up"></i> From</span><span class="v">@_t.SourceWarehouseName</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-box-arrow-in-down"></i> To</span><span class="v">@_t.DestinationWarehouseName</span></div>
                    @if (!string.IsNullOrWhiteSpace(_t.Notes)) { <div class="info-cell wide"><span class="k"><i class="bi bi-sticky"></i> Notes</span><span class="v">@_t.Notes</span></div> }
                    @if (!string.IsNullOrWhiteSpace(_t.RejectionNote)) { <div class="info-cell wide"><span class="k"><i class="bi bi-exclamation-triangle"></i> Rejection</span><span class="v">@_t.RejectionNote</span></div> }
                </div>
            </div>
        </section>

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-list-ul"></i></span><h2>Lines</h2></div>
            <div class="card-b">
                <table class="items">
                    <thead><tr><th>Product</th><th class="r">Qty</th><th class="r">On hand (source)</th></tr></thead>
                    <tbody>
                        @foreach (var l in _t.Lines)
                        {
                            <tr><td><div class="prod"><span class="pn">@l.ProductName</span><span class="sku">@l.Sku</span></div></td>
                                <td class="r mono">@l.Quantity</td><td class="r mono">@l.OnHandSource</td></tr>
                        }
                    </tbody>
                </table>
            </div>
        </section>

        @if (_t.ApprovalSteps.Count > 0)
        {
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-diagram-3"></i></span><h2>Approval</h2></div>
                <div class="card-b">
                    <ul class="list-unstyled mb-0">
                        @foreach (var s in _t.ApprovalSteps)
                        {
                            <li class="d-flex justify-content-between border-bottom py-2">
                                <span>Step @s.StepOrder · <strong>@s.RoleName</strong> @(s.ActedByName is not null ? $"— {s.ActedByName}" : "")</span>
                                <span class="badge @(s.Status == "Approved" ? "bg-success" : s.Status == "Rejected" ? "bg-danger" : "bg-warning text-dark")">@s.Status</span>
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
    private StockTransferDto? _t;
    private bool _busy, _canApprove;
    private string? _error;

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _t = await Transfers.GetByIdAsync(Id);
        _canApprove = false;
        if (_t?.Status == "PendingApproval")
        {
            var authState = await ((AuthenticationStateProvider)_asp!).GetAuthenticationStateAsync();
            var user = authState.User;
            var pending = _t.ApprovalSteps.FirstOrDefault(s => s.Status == "Pending");
            var approveOk = (await Auth.AuthorizeAsync(user, "inventory.transfers.approve")).Succeeded;
            _canApprove = approveOk && pending is not null && user.IsInRole(pending.RoleName)
                          && !string.Equals(user.Identity?.Name, _creator, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Inject] private AuthenticationStateProvider? _asp { get; set; }
    private string? _creator; // reserved; see alignment note

    private async Task SubmitAsync() => await RunAsync(() => Transfers.SubmitAsync(Id), "Submitted");
    private async Task ApproveAsync() => await RunAsync(async () =>
    {
        var st = await ((AuthenticationStateProvider)_asp!).GetAuthenticationStateAsync();
        await Transfers.ApproveAsync(Id, st.User.Identity?.Name ?? "", st.User.IsInRole);
    }, "Approved");
    private async Task RejectAsync()
    {
        var reason = await Swal.PromptAsync("Reject transfer", "Reason");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await RunAsync(async () =>
        {
            var st = await ((AuthenticationStateProvider)_asp!).GetAuthenticationStateAsync();
            await Transfers.RejectAsync(Id, st.User.Identity?.Name ?? "", st.User.IsInRole, reason);
        }, "Rejected");
    }

    private async Task RunAsync(Func<Task> action, string ok)
    {
        _busy = true; _error = null;
        try { await action(); await Swal.ToastAsync("success", ok); await ReloadAsync(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string StatusClass(string s) => s switch
    {
        "Posted" => "b-done", "PendingApproval" => "b-warn", _ => "b-draft"
    };
}
```
> **Alignment (do at write time):** reconcile the `_canApprove`/creator logic and any `Swal.PromptAsync` signature against `ApPaymentDetail.razor` — copy its exact approval plumbing (it already solves acting-user, is-in-role, creator-exclusion, and the reject-reason prompt). The version above follows that shape but confirm helper names (`Swal.PromptAsync`, creator field) match the real `SwalService` + detail template.

- [ ] **Step 4: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj` → succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Inventory/Transfers/
```

---

## Task 6: Full suite + verification

- [ ] **Step 1: Full suite**

App di VS di-stop. Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA PASS. Baseline 311 + 3 baru = **314**; `NumberSequenceServiceTests` hijau (assert 13).

- [ ] **Step 2: Manual verification (skill `run`/`verify`)**

Run app; sign out/in (permission `inventory.transfers.*`). Verify:
1. `/inventory/transfers` → New → pilih gudang asal/tujuan berbeda + baris varian → Save draft.
2. Detail → Submit → status PendingApproval + langkah approval tampil.
3. Approve (sebagai admin) → status Posted; cek Stock Levels: qty gudang asal turun, gudang tujuan naik.
4. Coba transfer qty > stok asal → Approve ditolak "Insufficient stock".
5. Smoke headless: `/inventory/transfers` tanpa login → 302 ke login.

- [ ] **Step 3: Done marker** — beritahu user Fase 1a selesai, siap commit manual.

---

## Self-Review

**Spec coverage:** StockTransfer + line + status → Task 1. Approval type + numbering + EF + migration → Task 2. Service (draft CRUD + submit/approve/reject + post moves stock + availability check) → Task 3. Default chain + menu → Task 4. Index/Form/Detail → Task 5. Tests (move stock, insufficient, same-warehouse, chain) → Task 3. No GL/5b touch → confirmed. ✓

**Placeholder scan:** No TBD. Two explicit alignment notes (variant source in Form; approval plumbing in Detail) flagged to reconcile with `StockAdjustmentForm.razor` / `ApPaymentDetail.razor` at write time — these are Razor-wiring details, not logic gaps.

**Type consistency:** `StockTransfer` methods (`SetLines`/`Submit`/`MarkPosted`/`ReturnToDraft`) consistent Task 1↔3. `IStockTransferService` signatures consistent Task 3↔5. `StockMovement` ctor + `MovementType.Transfer` + `UpsertStockAsync` + `GetOnHandAsync` verbatim from source. `IApprovalService.SubmitAsync/ApproveAsync(...,creatorUserName,...)/RejectAsync/ResetAsync/GetStepsAsync` verbatim; `ApprovalStepDto(Id,StepOrder,RoleName,Status,ActedByName,ActedAt,Note)`. `ApprovalChainStep(docType, stepOrder, roleName)`. NumberSequence Id=13 / `DocumentTypes.StockTransfer` consistent Task 2↔3. Permission keys `inventory.transfers.*` consistent Task 4↔5.

**Test-isolation note:** BootstrapSeeder chain not present in tests → approval tests seed their own `ApprovalChainStep(StockTransfer,1,"Administrators")` before Submit (see Task 3 note), else empty chain auto-posts on Submit.
