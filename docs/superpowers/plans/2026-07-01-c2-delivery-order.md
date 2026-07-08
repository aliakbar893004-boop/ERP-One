# C2 — Delivery Order (DO) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Delivery Order (pengiriman barang) over a Confirmed Sales Order — partial deliveries, stock-OUT via the ledger, and COGS (harga pokok) snapshotted per line. C2 is the near-verbatim mirror of GRN (B2) for the SELL side: stock goes OUT (negative delta), COGS is taken automatically from `ProductVariant.CostPrice` (not user-entered), moving-average HPP is NEVER touched, over-delivery is STRICT (no tolerance, no config), and there is NO approval on a DO.

**Architecture:** Clean Architecture, domain-rich entities (`DeliveryOrder`/`DeliveryOrderLine`) with a `DeliveryOrderService` orchestrating the post transaction (stock-out check + `StockMovement` Out + `ProductStock` upsert via the shared `db.UpsertStockAsync` helper with a NEGATIVE delta + COGS snapshot onto the line + SO line delivery tracking + SO status), mirroring the existing `GoodsReceiptService` and the OUTBOUND branch of `StockService.RecordAdjustmentAsync`. Blazor Server UI mirrors the existing GRN pages.

**Tech Stack:** .NET 10 / C#, EF Core 10, Blazor Server + Bootstrap 5 + Bootstrap Icons, FluentValidation, xUnit. SQL Server (app) / SQLite (integration tests via `CustomWebApplicationFactory`).

**Spec:** `docs/superpowers/specs/2026-07-01-c2-delivery-order-design.md`

## Global Constraints

- **No git** — implementers do NOT commit. Each task ends by verifying `dotnet build` (0 warnings) + the relevant `dotnet test` filter is green. Reviewers read changed files directly.
- **Build/test from solution root** `F:\4. My Data\Project\MyApplication`.
- **Enums stored as string** via `.HasConversion<string>().HasMaxLength(20)`.
- **Decimals** `(18,2)` for money/cost via `.HasPrecision(18, 2)`.
- **Rounding** `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Entities** derive from `AuditableEntity` where audited, private setters, EF ctor `private Xxx() { }`, child collections via backing `List<>` + `PropertyAccessMode.Field`.
- **Validation errors** thrown as `FluentValidation.ValidationException`.
- **Stock-out uses the shared** `AppDbContext.UpsertStockAsync(variantId, warehouseId, delta, ct)` (`src/MyApp.Infrastructure/Persistence/StockWriteExtensions.cs`) with a **NEGATIVE** delta; it is `.Local`-aware and throws `InvalidOperationException` on a negative result. Stock movement & `ProductStock` rows are per `SalesOrder.WarehouseId` (the source warehouse).
- **Moving average is NEVER touched by DO** (stock-out). Do NOT call `ProductVariant.ApplyMovingAverage`. The DO `StockMovement` uses `variant.CostPrice` as its unit cost — exactly like the OUTBOUND branch of `StockService.RecordAdjustmentAsync`.
- **Over-delivery is STRICT**: per-line cap = `Quantity − DeliveredQuantity(posted)`. No tolerance, no config, no appsettings section.

---

### Task 1: Domain — SalesOrder delivery tracking + status transitions

**Files:**
- Modify: `src/MyApp.Domain/Entities/SalesOrderStatus.cs`
- Modify: `src/MyApp.Domain/Entities/SalesOrderLine.cs`
- Modify: `src/MyApp.Domain/Entities/SalesOrder.cs`
- Test: `tests/MyApp.UnitTests/SalesOrderTests.cs` (add cases)

**Interfaces:**
- Produces:
  - `SalesOrderStatus.PartiallyDelivered`, `.Delivered`, `.Closed`
  - `SalesOrderLine.DeliveredQuantity` (int, get), `.IsFullyDelivered` (bool), `.ApplyDelivery(int qty)`
  - `SalesOrder.CanDeliver` (bool), `.MarkPartiallyDelivered()`, `.MarkDelivered()`, `.Close()`

- [ ] **Step 1: Add the new `SalesOrderStatus` values**

Edit `src/MyApp.Domain/Entities/SalesOrderStatus.cs` — append three values after `Cancelled` (order is irrelevant; stored as string):

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup Sales Order (C1; status pengiriman ditambahkan di C2).</summary>
public enum SalesOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled,
    PartiallyDelivered,
    Delivered,
    Closed
}
```

- [ ] **Step 2: Add failing unit tests for `SalesOrderLine.ApplyDelivery`**

Append to `tests/MyApp.UnitTests/SalesOrderTests.cs` (inside the existing `SalesOrderTests` class — `Line()` already returns `new(5, 10, 1000m, 0m, null, 0m)`, qty 10):

```csharp
    [Fact]
    public void ApplyDelivery_accumulates_and_tracks_full_delivery()
    {
        var line = Line(); // qty 10
        Assert.Equal(0, line.DeliveredQuantity);
        Assert.False(line.IsFullyDelivered);

        line.ApplyDelivery(4);
        Assert.Equal(4, line.DeliveredQuantity);
        Assert.False(line.IsFullyDelivered);

        line.ApplyDelivery(6);
        Assert.Equal(10, line.DeliveredQuantity);
        Assert.True(line.IsFullyDelivered);
    }

    [Fact]
    public void ApplyDelivery_allows_up_to_ordered_qty_exactly()
    {
        var line = Line(); // qty 10, strict cap = 10
        line.ApplyDelivery(10);
        Assert.Equal(10, line.DeliveredQuantity);
    }

    [Fact]
    public void ApplyDelivery_rejects_over_ordered_qty_strict()
    {
        var line = Line(); // qty 10 — no tolerance
        Assert.Throws<InvalidOperationException>(() => line.ApplyDelivery(11));
    }

    [Fact]
    public void ApplyDelivery_rejects_non_positive()
    {
        var line = Line();
        Assert.Throws<ArgumentException>(() => line.ApplyDelivery(0));
        Assert.Throws<ArgumentException>(() => line.ApplyDelivery(-1));
    }
```

- [ ] **Step 3: Run tests — verify they fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderTests"`
Expected: FAIL — build error, members `DeliveredQuantity`/`IsFullyDelivered`/`ApplyDelivery` do not exist.

- [ ] **Step 4: Implement `SalesOrderLine` delivery members**

Add to `src/MyApp.Domain/Entities/SalesOrderLine.cs` — a property after `LineTotal` and a method after `Recompute`/`Round` (mirror `PurchaseOrderLine.ReceivedQuantity`/`ApplyReceipt` but STRICT — the cap is the ordered qty with no tolerance):

```csharp
    public int DeliveredQuantity { get; private set; }

    public bool IsFullyDelivered => DeliveredQuantity >= Quantity;

    /// <summary>Catat pengiriman; tolak bila melebihi qty dipesan (STRICT, tanpa toleransi).</summary>
    public void ApplyDelivery(int qty)
    {
        if (qty <= 0) throw new ArgumentException("Delivery quantity must be > 0.", nameof(qty));
        if (DeliveredQuantity + qty > Quantity)
            throw new InvalidOperationException(
                $"Delivering {qty} would exceed the ordered quantity ({Quantity}) for this line.");
        DeliveredQuantity += qty;
    }
```

- [ ] **Step 5: Run tests — verify they pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderTests"`
Expected: PASS (all existing + the 4 new ones).

- [ ] **Step 6: Add failing unit tests for `SalesOrder` transitions**

Append to `tests/MyApp.UnitTests/SalesOrderTests.cs`. Helper to reach `Confirmed` (mirrors the existing `Make()`/`Line()` helpers):

```csharp
    private static SalesOrder Confirmed()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        return so;
    }

    [Fact]
    public void CanDeliver_only_when_confirmed_or_partially_delivered()
    {
        Assert.False(Make().CanDeliver);
        var so = Confirmed();
        Assert.True(so.CanDeliver);
        so.MarkPartiallyDelivered();
        Assert.True(so.CanDeliver);
    }

    [Fact]
    public void MarkDelivered_and_partial_require_deliverable_status()
    {
        Assert.Throws<InvalidOperationException>(() => Make().MarkDelivered());
        Assert.Throws<InvalidOperationException>(() => Make().MarkPartiallyDelivered());
        var so = Confirmed();
        so.MarkDelivered();
        Assert.Equal(SalesOrderStatus.Delivered, so.Status);
    }

    [Fact]
    public void Close_only_from_partially_delivered()
    {
        var so = Confirmed();
        Assert.Throws<InvalidOperationException>(() => so.Close()); // Confirmed cannot close
        so.MarkPartiallyDelivered();
        so.Close();
        Assert.Equal(SalesOrderStatus.Closed, so.Status);
    }
```

- [ ] **Step 7: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderTests"`
Expected: FAIL — `CanDeliver`/`MarkPartiallyDelivered`/`MarkDelivered`/`Close` do not exist.

- [ ] **Step 8: Implement `SalesOrder` transitions**

Add to `src/MyApp.Domain/Entities/SalesOrder.cs` after `Cancel()` (mirror `PurchaseOrder.CanReceive`/`MarkPartiallyReceived`/`MarkReceived`/`Close`):

```csharp
    public bool CanDeliver =>
        Status is SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyDelivered;

    public void MarkPartiallyDelivered()
    {
        if (!CanDeliver)
            throw new InvalidOperationException("Only a confirmed or partially-delivered sales order can record deliveries.");
        Status = SalesOrderStatus.PartiallyDelivered;
    }

    public void MarkDelivered()
    {
        if (!CanDeliver)
            throw new InvalidOperationException("Only a confirmed or partially-delivered sales order can record deliveries.");
        Status = SalesOrderStatus.Delivered;
    }

    public void Close()
    {
        if (Status != SalesOrderStatus.PartiallyDelivered)
            throw new InvalidOperationException("Only a partially-delivered sales order can be closed.");
        Status = SalesOrderStatus.Closed;
    }
```

- [ ] **Step 9: Run all unit tests — verify green**

Run: `dotnet test tests/MyApp.UnitTests`
Expected: PASS (all existing + new). Then `dotnet build` — 0 warnings.

---

### Task 2: Domain — DeliveryOrder + DeliveryOrderLine + status enum

**Files:**
- Create: `src/MyApp.Domain/Entities/DeliveryOrderStatus.cs`
- Create: `src/MyApp.Domain/Entities/DeliveryOrderLine.cs`
- Create: `src/MyApp.Domain/Entities/DeliveryOrder.cs`
- Test: `tests/MyApp.UnitTests/DeliveryOrderTests.cs`

**Interfaces:**
- Produces:
  - `enum DeliveryOrderStatus { Draft, Posted }`
  - `DeliveryOrderLine(int salesOrderLineId, int productVariantId, int quantityDelivered)` with props `Id, DeliveryOrderId, SalesOrderLineId, ProductVariantId, QuantityDelivered, UnitCost` (UnitCost defaults to 0) and method `SetUnitCost(decimal cost)`
  - `DeliveryOrder(string doNumber, int salesOrderId, DateTime deliveryDate, string? notes)` with `Id, DoNumber, SalesOrderId, DeliveryDate, Notes, Status, IReadOnlyCollection<DeliveryOrderLine> Lines`, methods `UpdateHeader(DateTime, string?)`, `SetLines(IEnumerable<DeliveryOrderLine>)`, `Post()`

- [ ] **Step 1: Write failing tests**

Create `tests/MyApp.UnitTests/DeliveryOrderTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class DeliveryOrderTests
{
    private static DeliveryOrder Make() =>
        new("DO-202607-0001", salesOrderId: 1, deliveryDate: new DateTime(2026, 7, 1), notes: "  catatan  ");

    private static DeliveryOrderLine Line() => new(salesOrderLineId: 7, productVariantId: 5, quantityDelivered: 3);

    [Fact]
    public void New_do_is_draft_and_trims_notes()
    {
        var d = Make();
        Assert.Equal(DeliveryOrderStatus.Draft, d.Status);
        Assert.Equal("catatan", d.Notes);
        Assert.Equal("DO-202607-0001", d.DoNumber);
    }

    [Fact]
    public void New_line_defaults_unit_cost_to_zero()
    {
        var l = Line();
        Assert.Equal(0m, l.UnitCost);
        Assert.Equal(3, l.QuantityDelivered);
    }

    [Fact]
    public void SetUnitCost_sets_cogs_snapshot()
    {
        var l = Line();
        l.SetUnitCost(800m);
        Assert.Equal(800m, l.UnitCost);
        Assert.Throws<ArgumentException>(() => l.SetUnitCost(-1m));
    }

    [Fact]
    public void Post_requires_lines()
    {
        var d = Make();
        Assert.Throws<InvalidOperationException>(() => d.Post());
        d.SetLines([Line()]);
        d.Post();
        Assert.Equal(DeliveryOrderStatus.Posted, d.Status);
    }

    [Fact]
    public void Cannot_modify_after_post()
    {
        var d = Make();
        d.SetLines([Line()]);
        d.Post();
        Assert.Throws<InvalidOperationException>(() => d.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() => d.UpdateHeader(DateTime.Today, null));
        Assert.Throws<InvalidOperationException>(() => d.Post());
    }

    [Fact]
    public void Line_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(0, 5, 3));
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(7, 0, 3));
        Assert.Throws<ArgumentException>(() => new DeliveryOrderLine(7, 5, 0));
    }
}
```

- [ ] **Step 2: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~DeliveryOrderTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create the enum**

`src/MyApp.Domain/Entities/DeliveryOrderStatus.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup Delivery Order: Draft (belum gerak stok) → Posted (stok keluar & COGS final).</summary>
public enum DeliveryOrderStatus
{
    Draft,
    Posted
}
```

- [ ] **Step 4: Create `DeliveryOrderLine`**

`src/MyApp.Domain/Entities/DeliveryOrderLine.cs` (note: `UnitCost` is NOT a ctor arg — it defaults to 0 and is set at Post via `SetUnitCost`; mirror `GoodsReceiptLine` shape):

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Baris pengiriman: qty terkirim untuk satu baris SO. UnitCost = COGS di-snapshot saat Post.</summary>
public class DeliveryOrderLine
{
    public int Id { get; private set; }
    public int DeliveryOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int QuantityDelivered { get; private set; }
    public decimal UnitCost { get; private set; }

    private DeliveryOrderLine() { } // EF Core

    public DeliveryOrderLine(int salesOrderLineId, int productVariantId, int quantityDelivered)
    {
        if (salesOrderLineId <= 0) throw new ArgumentException("SalesOrderLineId is required.", nameof(salesOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantityDelivered <= 0) throw new ArgumentException("QuantityDelivered must be > 0.", nameof(quantityDelivered));

        SalesOrderLineId = salesOrderLineId;
        ProductVariantId = productVariantId;
        QuantityDelivered = quantityDelivered;
        UnitCost = 0m; // COGS ditetapkan saat Post
    }

    /// <summary>Snapshot COGS per unit (dari ProductVariant.CostPrice) saat Post.</summary>
    public void SetUnitCost(decimal cost)
    {
        if (cost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(cost));
        UnitCost = cost;
    }
}
```

- [ ] **Step 5: Create `DeliveryOrder`**

`src/MyApp.Domain/Entities/DeliveryOrder.cs` (mirror `GoodsReceipt`):

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pengiriman barang atas satu SO. Baris hanya bisa diubah saat Draft; Post mengunci.</summary>
public class DeliveryOrder : AuditableEntity
{
    private readonly List<DeliveryOrderLine> _lines = [];

    public int Id { get; private set; }
    public string DoNumber { get; private set; } = default!;
    public int SalesOrderId { get; private set; }
    public DateTime DeliveryDate { get; private set; }
    public string? Notes { get; private set; }
    public DeliveryOrderStatus Status { get; private set; }

    public IReadOnlyCollection<DeliveryOrderLine> Lines => _lines;

    private DeliveryOrder() { } // EF Core

    public DeliveryOrder(string doNumber, int salesOrderId, DateTime deliveryDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(doNumber))
            throw new ArgumentException("DoNumber is required.", nameof(doNumber));
        if (salesOrderId <= 0)
            throw new ArgumentException("SalesOrderId is required.", nameof(salesOrderId));
        DoNumber = doNumber.Trim();
        SalesOrderId = salesOrderId;
        SetHeader(deliveryDate, notes);
        Status = DeliveryOrderStatus.Draft;
    }

    public void UpdateHeader(DateTime deliveryDate, string? notes)
    {
        EnsureDraft();
        SetHeader(deliveryDate, notes);
    }

    private void SetHeader(DateTime deliveryDate, string? notes)
    {
        DeliveryDate = deliveryDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<DeliveryOrderLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
    }

    public void Post()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot post a delivery order without lines.");
        Status = DeliveryOrderStatus.Posted;
    }

    private void EnsureDraft()
    {
        if (Status != DeliveryOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft delivery order can be modified.");
    }
}
```

- [ ] **Step 6: Run tests — verify pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~DeliveryOrderTests"`
Expected: PASS. Then `dotnet build` — 0 warnings.

---

### Task 3: Application — DTOs, service interface, validators; `ISalesOrderService.CloseAsync`

**Files:**
- Create: `src/MyApp.Application/DeliveryOrders/DeliveryOrderDtos.cs`
- Create: `src/MyApp.Application/DeliveryOrders/IDeliveryOrderService.cs`
- Create: `src/MyApp.Application/DeliveryOrders/DeliveryOrderValidators.cs`
- Modify: `src/MyApp.Application/SalesOrders/ISalesOrderService.cs` (add `CloseAsync`)
- Test: `tests/MyApp.UnitTests/DeliveryOrderValidatorTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` from `MyApp.Application.Common`; `DeliveryOrderStatus` from `MyApp.Domain.Entities`.
- Produces (DTOs and signatures exactly as below). Later tasks (5,6,7,10–13) consume these. **`DeliveryOrderLineRequest` has NO UnitCost field** — COGS is set automatically at Post.

- [ ] **Step 1: Create DTOs**

`src/MyApp.Application/DeliveryOrders/DeliveryOrderDtos.cs` (mirror `GoodsReceiptDtos.cs`, swapping Supplier→Customer, "Received"→"Delivered", and DROPPING UnitCost from the line request):

```csharp
namespace MyApp.Application.DeliveryOrders;

public record DeliveryOrderLineDto(
    int Id, int SalesOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int QuantityDelivered, decimal UnitCost, decimal LineCost);

public record DeliveryOrderDto(
    int Id, string DoNumber, int SalesOrderId, string SoNumber,
    int CustomerId, string CustomerName, int WarehouseId, string WarehouseName,
    DateTime DeliveryDate, string? Notes, string Status,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<DeliveryOrderLineDto> Lines);

public record DeliveryOrderListItemDto(
    int Id, string DoNumber, int SalesOrderId, string SoNumber, string CustomerName,
    DateTime DeliveryDate, string Status, int TotalQuantity);

public record DeliveryOrderDashboardDto(
    int TotalCount, int DraftCount, int PostedCount);

public record DeliverableSoDto(
    int Id, string SoNumber, string CustomerName, DateTime OrderDate, string Status);

public record SoForDeliveryLineDto(
    int SalesOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int AlreadyDeliveredQuantity, int RemainingQuantity);

public record SoForDeliveryDto(
    int SalesOrderId, string SoNumber, int CustomerId, string CustomerName,
    int WarehouseId, string WarehouseName, string Currency,
    IReadOnlyList<SoForDeliveryLineDto> Lines);

public record DeliveryOrderLineRequest(int SalesOrderLineId, int QuantityDelivered);

public record CreateDeliveryOrderRequest(
    int SalesOrderId, DateTime DeliveryDate, string? Notes,
    IReadOnlyList<DeliveryOrderLineRequest> Lines);

public record UpdateDeliveryOrderRequest(
    DateTime DeliveryDate, string? Notes,
    IReadOnlyList<DeliveryOrderLineRequest> Lines);
```

> Note: unlike GRN there is NO `DeliveryOrderOptions` class and NO tolerance — over-delivery is strict.

- [ ] **Step 2: Create the service interface**

`src/MyApp.Application/DeliveryOrders/IDeliveryOrderService.cs` (mirror `IGoodsReceiptService`):

```csharp
using MyApp.Application.Common;
using MyApp.Domain.Entities;

namespace MyApp.Application.DeliveryOrders;

public interface IDeliveryOrderService
{
    Task<PagedResult<DeliveryOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, DeliveryOrderStatus? status = null, CancellationToken ct = default);
    Task<DeliveryOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<DeliveryOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DeliverableSoDto>> GetDeliverableSosAsync(CancellationToken ct = default);
    Task<SoForDeliveryDto?> GetSoForDeliveryAsync(int salesOrderId, CancellationToken ct = default);

    Task<DeliveryOrderDto> CreateDraftAsync(CreateDeliveryOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateDeliveryOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task<bool> PostAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3: Add `CloseAsync` to `ISalesOrderService`**

In `src/MyApp.Application/SalesOrders/ISalesOrderService.cs`, add after `CancelAsync`:

```csharp
    Task<bool> CloseAsync(int id, CancellationToken ct = default);
```

- [ ] **Step 4: Write failing validator tests**

Create `tests/MyApp.UnitTests/DeliveryOrderValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using MyApp.Application.DeliveryOrders;
using Xunit;

namespace MyApp.UnitTests;

public class DeliveryOrderValidatorTests
{
    private static DeliveryOrderLineRequest Line() => new(SalesOrderLineId: 7, QuantityDelivered: 3);

    [Fact]
    public void Create_requires_so_date_and_lines()
    {
        var v = new CreateDeliveryOrderValidator();
        var bad = new CreateDeliveryOrderRequest(0, default, null, []);
        var r = v.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.SalesOrderId);
        r.ShouldHaveValidationErrorFor(x => x.DeliveryDate);
        r.ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Create_valid_passes()
    {
        var v = new CreateDeliveryOrderValidator();
        var ok = new CreateDeliveryOrderRequest(1, new DateTime(2026, 7, 1), "ok", [Line()]);
        v.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Line_rejects_bad_values()
    {
        var v = new DeliveryOrderLineRequestValidator();
        v.TestValidate(new DeliveryOrderLineRequest(0, 3)).ShouldHaveValidationErrorFor(x => x.SalesOrderLineId);
        v.TestValidate(new DeliveryOrderLineRequest(7, 0)).ShouldHaveValidationErrorFor(x => x.QuantityDelivered);
    }
}
```

> Note: `FluentValidation.TestHelper` is already referenced by the existing validator tests. If the build complains, mirror the using/imports of `GoodsReceiptValidatorTests`.

- [ ] **Step 5: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~DeliveryOrderValidatorTests"`
Expected: FAIL — validators do not exist.

- [ ] **Step 6: Create validators**

`src/MyApp.Application/DeliveryOrders/DeliveryOrderValidators.cs` (mirror `GoodsReceiptValidators.cs`; no UnitCost rule):

```csharp
using FluentValidation;

namespace MyApp.Application.DeliveryOrders;

public class DeliveryOrderLineRequestValidator : AbstractValidator<DeliveryOrderLineRequest>
{
    public DeliveryOrderLineRequestValidator()
    {
        RuleFor(x => x.SalesOrderLineId).GreaterThan(0);
        RuleFor(x => x.QuantityDelivered).GreaterThan(0);
    }
}

public class CreateDeliveryOrderValidator : AbstractValidator<CreateDeliveryOrderRequest>
{
    public CreateDeliveryOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.DeliveryDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new DeliveryOrderLineRequestValidator());
    }
}

public class UpdateDeliveryOrderValidator : AbstractValidator<UpdateDeliveryOrderRequest>
{
    public UpdateDeliveryOrderValidator()
    {
        RuleFor(x => x.DeliveryDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new DeliveryOrderLineRequestValidator());
    }
}
```

- [ ] **Step 7: Add `SalesOrderService.CloseAsync` (real impl — small)**

`ISalesOrderService.CloseAsync` (added Step 3) must be implemented by `SalesOrderService` or the solution won't build. In `src/MyApp.Infrastructure/Services/SalesOrderService.cs`, add after `CancelAsync` (mirror `PurchaseOrderService.CloseAsync` — wrap in a transaction like the other SO mutators):

```csharp
    public async Task<bool> CloseAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        so.Close();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
```

> `GetCreditInfoAsync` widening lands in Task 7 (with its integration test); leave it as-is for now.

- [ ] **Step 8: Run build + unit tests — verify green**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test tests/MyApp.UnitTests` — PASS.

---

### Task 4: Infrastructure — DbContext mapping + SalesOrderLine.DeliveredQuantity

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Produces: `db.DeliveryOrders`, `db.DeliveryOrderLines` DbSets; mapping for the new entities + `SalesOrderLine.DeliveredQuantity` column.

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `GoodsReceiptLines` DbSet (line ~34):

```csharp
    public DbSet<DeliveryOrder> DeliveryOrders => Set<DeliveryOrder>();
    public DbSet<DeliveryOrderLine> DeliveryOrderLines => Set<DeliveryOrderLine>();
```

- [ ] **Step 2: Map the entities**

In `OnModelCreating`, after the `GoodsReceiptLine` entity block (line ~350), mirror the GRN mapping — `SalesOrder` FK `Restrict`, lines `Cascade`, `ProductVariant` `Restrict`, `SalesOrderLine` `Restrict`, nav `Lines` field-access:

```csharp
        modelBuilder.Entity<DeliveryOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.DoNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.DeliveryOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(DeliveryOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<DeliveryOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });
```

> `SalesOrderLine.DeliveredQuantity` (int, non-null) is mapped by convention (default 0) — no fluent config is needed. The migration in Task 8 adds the column with default 0.

- [ ] **Step 3: Verify build**

Run: `dotnet build` — Expected: 0 warnings. (No new tests here; mapping is exercised by Task 5+ integration tests.)

---

### Task 5: Infrastructure — DeliveryOrderService (read + draft CRUD) + DI

**Files:**
- Create: `src/MyApp.Infrastructure/Services/DeliveryOrderService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/DeliveryOrderServiceTests.cs`

**Interfaces:**
- Consumes: `IValidator<CreateDeliveryOrderRequest>`, `IValidator<UpdateDeliveryOrderRequest>`, `AppDbContext`.
- Produces: full `IDeliveryOrderService` impl. `PostAsync` body is added in Task 6 (here it throws `NotImplementedException` so the class compiles and draft/read tests run).

- [ ] **Step 1: Register DI**

In `src/MyApp.Infrastructure/DependencyInjection.cs`:
- add `using MyApp.Application.DeliveryOrders;` (with the other `using`s),
- after `services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();`:

```csharp
        services.AddScoped<IDeliveryOrderService, DeliveryOrderService>();
```

> No `services.Configure<...>` line — DO has no options section.

- [ ] **Step 2: Create the service with draft/read methods (Post stubbed)**

`src/MyApp.Infrastructure/Services/DeliveryOrderService.cs` (mirror `GoodsReceiptService`; note: NO `IOptions`/`Tolerance`; the DoNumber prefix is `DO-`; `BuildLines` uses the STRICT remaining cap `ordered − DeliveredQuantity`; deliverable SO statuses are `Confirmed`/`PartiallyDelivered`):

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.DeliveryOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class DeliveryOrderService(
    AppDbContext db,
    IValidator<CreateDeliveryOrderRequest> createValidator,
    IValidator<UpdateDeliveryOrderRequest> updateValidator) : IDeliveryOrderService
{
    public async Task<PagedResult<DeliveryOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, DeliveryOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query =
            from d in db.DeliveryOrders.AsNoTracking()
            join so in db.SalesOrders.AsNoTracking() on d.SalesOrderId equals so.Id
            select new { d, so };

        if (status is { } st) query = query.Where(x => x.d.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.d.DoNumber.Contains(search) || x.so.SoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.d.Id, x.d.DoNumber, x.d.SalesOrderId, x.so.SoNumber, x.so.CustomerId,
                x.d.DeliveryDate, x.d.Status,
                TotalQuantity = db.DeliveryOrderLines.Where(l => l.DeliveryOrderId == x.d.Id).Sum(l => (int?)l.QuantityDelivered) ?? 0
            })
            .ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct);

        var items = rows.Select(r => new DeliveryOrderListItemDto(
            r.Id, r.DoNumber, r.SalesOrderId, r.SoNumber,
            customers.FirstOrDefault(c => c.Id == r.CustomerId)?.Name ?? "—",
            r.DeliveryDate, r.Status.ToString(), r.TotalQuantity)).ToList();

        return new PagedResult<DeliveryOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<DeliveryOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.DeliveryOrders
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(DeliveryOrderStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new DeliveryOrderDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(DeliveryOrderStatus.Draft),
            CountOf(DeliveryOrderStatus.Posted));
    }

    public async Task<DeliveryOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.DeliveryOrders.AsNoTracking().Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        var so = await db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct);
        var customerName = so is null ? "—"
            : await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = so is null ? "—"
            : await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var soLineIds = doc.Lines.Select(l => l.SalesOrderLineId).Distinct().ToList();
        var soLines = await db.SalesOrderLines.AsNoTracking()
            .Where(l => soLineIds.Contains(l.Id)).Select(l => new { l.Id, l.Quantity }).ToListAsync(ct);

        var variantIds = doc.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = doc.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var ordered = soLines.FirstOrDefault(x => x.Id == l.SalesOrderLineId)?.Quantity ?? 0;
            return new DeliveryOrderLineDto(l.Id, l.SalesOrderLineId, l.ProductVariantId, v?.Sku ?? "—", pn,
                ordered, l.QuantityDelivered, l.UnitCost,
                Math.Round(l.QuantityDelivered * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        }).ToList();

        return new DeliveryOrderDto(doc.Id, doc.DoNumber, doc.SalesOrderId, so?.SoNumber ?? "—",
            so?.CustomerId ?? 0, customerName, so?.WarehouseId ?? 0, warehouseName,
            doc.DeliveryDate, doc.Notes, doc.Status.ToString(), doc.CreatedAt, doc.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<DeliverableSoDto>> GetDeliverableSosAsync(CancellationToken ct = default)
    {
        var statuses = new[] { SalesOrderStatus.Confirmed, SalesOrderStatus.PartiallyDelivered };
        return await db.SalesOrders.AsNoTracking()
            .Where(s => statuses.Contains(s.Status))
            .OrderByDescending(s => s.Id)
            .Select(s => new DeliverableSoDto(
                s.Id, s.SoNumber,
                db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                s.OrderDate, s.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<SoForDeliveryDto?> GetSoForDeliveryAsync(int salesOrderId, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == salesOrderId, ct);
        if (so is null || !so.CanDeliver) return null;

        var customerName = await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = so.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = so.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var remaining = Math.Max(0, l.Quantity - l.DeliveredQuantity);
            return new SoForDeliveryLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.DeliveredQuantity, remaining);
        }).ToList();

        return new SoForDeliveryDto(so.Id, so.SoNumber, so.CustomerId, customerName,
            so.WarehouseId, warehouseName, so.Currency, lines);
    }

    public async Task<DeliveryOrderDto> CreateDraftAsync(CreateDeliveryOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");
        if (!so.CanDeliver) throw Fail("Only a confirmed or partially-delivered sales order can be delivered.");

        var doLines = BuildLines(so, request.Lines);
        var doc = new DeliveryOrder(await GenerateNumberAsync(request.DeliveryDate, ct),
            so.Id, request.DeliveryDate, request.Notes);
        doc.SetLines(doLines);

        db.DeliveryOrders.Add(doc);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(doc.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateDeliveryOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = await db.DeliveryOrders.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft) throw Fail("Only a draft delivery order can be modified.");

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");

        var oldLines = await db.DeliveryOrderLines.Where(l => l.DeliveryOrderId == id).ToListAsync(ct);
        db.DeliveryOrderLines.RemoveRange(oldLines);

        doc.UpdateHeader(request.DeliveryDate, request.Notes);
        doc.SetLines(BuildLines(so, request.Lines));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.DeliveryOrders.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft) throw Fail("Only a draft delivery order can be deleted.");
        db.DeliveryOrders.Remove(doc);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> PostAsync(int id, CancellationToken ct = default) =>
        throw new NotImplementedException("Implemented in Task 6.");

    /// <summary>Validasi tiap baris terhadap baris SO &amp; sisa qty (STRICT vs qty terposting), bangun entitas line.</summary>
    private List<DeliveryOrderLine> BuildLines(SalesOrder so, IReadOnlyList<DeliveryOrderLineRequest> requests)
    {
        var lines = new List<DeliveryOrderLine>();
        foreach (var r in requests)
        {
            var soLine = so.Lines.FirstOrDefault(l => l.Id == r.SalesOrderLineId)
                ?? throw Fail($"SO line {r.SalesOrderLineId} does not belong to SO {so.SoNumber}.");
            var remaining = soLine.Quantity - soLine.DeliveredQuantity;
            if (r.QuantityDelivered > remaining)
                throw Fail($"Delivering {r.QuantityDelivered} for variant {soLine.ProductVariantId} exceeds the remaining quantity ({remaining}).");
            lines.Add(new DeliveryOrderLine(soLine.Id, soLine.ProductVariantId, r.QuantityDelivered));
        }
        return lines;
    }

    private async Task<string> GenerateNumberAsync(DateTime deliveryDate, CancellationToken ct)
    {
        var prefix = $"DO-{deliveryDate:yyyyMM}-";
        var last = await db.DeliveryOrders.AsNoTracking()
            .Where(d => d.DoNumber.StartsWith(prefix))
            .OrderByDescending(d => d.DoNumber)
            .Select(d => d.DoNumber).FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("DeliveryOrder", message)]);
}
```

- [ ] **Step 3: Write failing integration tests (draft + read)**

Create `tests/MyApp.IntegrationTests/DeliveryOrderServiceTests.cs`. The seed helper mirrors `SalesOrderServiceTests` (using the REAL ctors noted there) and drives an SO to `Confirmed` with an empty approval chain. Note the OUTBOUND wrinkle: to Post later we need stock on hand, so `SeedMastersAsync` also records opening stock via `IStockService.RecordOpeningAsync`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Approvals;
using MyApp.Application.DeliveryOrders;
using MyApp.Application.SalesOrders;
using MyApp.Application.Stock;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class DeliveryOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DeliveryOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Customer ctor: (code, name, contactPerson, phone, email, address, taxId, paymentTermDays, defaultCurrency, creditLimit, isActive)
    // ProductVariant ctor via product.AddVariant: (sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive)
    private static async Task<(int cust, int wh, int variant)> SeedMastersAsync(IServiceProvider sp, int openingQty = 100)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT DO {id}", null, null, null, null, null, 30, "IDR", 0m, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true); // CostPrice 800
        await db.SaveChangesAsync();

        // Opening stock so a later Post has on-hand to draw down (mutasi masuk → CostPrice becomes 800 via MA on 0 base).
        if (openingQty > 0)
            await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, openingQty, 800m);

        return (cust.Id, wh.Id, variant.Id);
    }

    // Creates a Confirmed SO (empty approval chain) with one line: qty 10 @ 1000, no discount/tax.
    private static async Task<SalesOrderDto> ConfirmedSoAsync(IServiceProvider sp, int cust, int wh, int variant)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(
            cust, wh, new DateTime(2026, 7, 1), null, "so",
            [new SalesOrderLineRequest(variant, 10, 1000m, 0m, null)]));
        await soSvc.SubmitAsync(so.Id);
        return (await soSvc.GetByIdAsync(so.Id))!;
    }

    [Fact]
    public async Task GetSoForDelivery_returns_remaining()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();

        var dto = await svc.GetSoForDeliveryAsync(so.Id);
        Assert.NotNull(dto);
        var line = Assert.Single(dto!.Lines);
        Assert.Equal(10, line.OrderedQuantity);
        Assert.Equal(0, line.AlreadyDeliveredQuantity);
        Assert.Equal(10, line.RemainingQuantity);
    }

    [Fact]
    public async Task CreateDraft_generates_number_and_does_not_move_stock()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soLineId = so.Lines[0].Id;

        var doc = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), "kirim sebagian",
            [new DeliveryOrderLineRequest(soLineId, 4)]));

        Assert.StartsWith("DO-202607-", doc.DoNumber);
        Assert.Equal("Draft", doc.Status);
        Assert.Single(doc.Lines);
        Assert.Equal(0m, doc.Lines[0].UnitCost); // COGS belum di-set sebelum Post

        var stockSvc = sp.GetRequiredService<IStockService>();
        Assert.Equal(100, await stockSvc.GetOnHandAsync(variant, wh)); // draft belum mengurangi stok
    }

    [Fact]
    public async Task CreateDraft_rejects_over_delivery_strict()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soLineId = so.Lines[0].Id;

        // qty 10, STRICT → 11 must fail (no tolerance)
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                so.Id, new DateTime(2026, 7, 1), null,
                [new DeliveryOrderLineRequest(soLineId, 11)])));
    }

    [Fact]
    public async Task DeleteDraft_removes_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var doc = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));

        Assert.True(await svc.DeleteDraftAsync(doc.Id));
        Assert.Null(await svc.GetByIdAsync(doc.Id));
    }
}
```

> If `CustomWebApplicationFactory` does not exist with this exact name/method, mirror `SalesOrderServiceTests`'s fixture usage in the same project (it is the canonical integration pattern).

- [ ] **Step 4: Run integration tests — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~DeliveryOrderServiceTests"`
Expected: PASS (4 tests). Then `dotnet build` — 0 warnings.

---

### Task 6: Infrastructure — DeliveryOrderService.PostAsync (stock-out + COGS + SO status)

**Files:**
- Modify: `src/MyApp.Infrastructure/Services/DeliveryOrderService.cs` (replace `PostAsync` stub)
- Test: `tests/MyApp.IntegrationTests/DeliveryOrderServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `StockMovement` ctor `(productVariantId, warehouseId, MovementType, quantity, unitCost, movementDate, refType, refId, note)`; `MovementType.Out`; `AppDbContext.UpsertStockAsync(variantId, warehouseId, delta, ct)` with a NEGATIVE delta; `DeliveryOrderLine.SetUnitCost(decimal)`; `SalesOrderLine.ApplyDelivery(int)` / `.IsFullyDelivered`; `SalesOrder.MarkDelivered()` / `.MarkPartiallyDelivered()`.
- **Does NOT** call `ProductVariant.ApplyMovingAverage` — MA is untouched on stock-out.

- [ ] **Step 1: Add failing integration tests (post effects)**

Append to `DeliveryOrderServiceTests`:

```csharp
    [Fact]
    public async Task Post_partial_then_full_moves_stock_out_and_updates_so_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp); // 100 on hand @ CostPrice 800
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // qty 10
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var soLineId = so.Lines[0].Id;

        // Deliver 4 → stock 100 - 4 = 96; SO PartiallyDelivered
        var d1 = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(soLineId, 4)]));
        Assert.True(await svc.PostAsync(d1.Id));

        Assert.Equal("Posted", (await svc.GetByIdAsync(d1.Id))!.Status);
        Assert.Equal(96, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("PartiallyDelivered", (await soSvc.GetByIdAsync(so.Id))!.Status);

        // Deliver remaining 6 → stock 90; SO Delivered
        var d2 = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(soLineId, 6)]));
        Assert.True(await svc.PostAsync(d2.Id));

        Assert.Equal(90, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Delivered", (await soSvc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task Post_writes_out_stock_movement_and_snapshots_cogs_without_touching_ma()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp); // CostPrice 800
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 5)]));
        await svc.PostAsync(d.Id);

        // Movement: Out, RefType "DO", qty is stored signed negative on the ledger, unit cost = CostPrice 800.
        var movements = await stockSvc.GetMovementsByVariantAsync(variant);
        Assert.Contains(movements, m => m.RefType == "DO" && m.Type == MovementType.Out && m.Quantity == -5 && m.UnitCost == 800m);

        // COGS snapshot on the DO line = CostPrice at Post.
        var line = (await svc.GetByIdAsync(d.Id))!.Lines[0];
        Assert.Equal(800m, line.UnitCost);
        Assert.Equal(4000m, line.LineCost); // 5 * 800

        // MA is untouched by a stock-out: variant CostPrice stays 800.
        var v = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(800m, v!.CostPrice);
    }

    [Fact]
    public async Task Post_rejected_when_stock_insufficient_no_mutation()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, openingQty: 3); // only 3 on hand
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // wants to deliver 5
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var stockSvc = sp.GetRequiredService<IStockService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 5)]));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.PostAsync(d.Id));

        // No mutation: stock unchanged, DO still Draft.
        Assert.Equal(3, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Draft", (await svc.GetByIdAsync(d.Id))!.Status);
    }

    [Fact]
    public async Task Post_twice_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(d.Id));
    }
```

> `StockMovementDto` exposes `Type` and `Quantity` (signed) — confirmed from `StockService.GetMovementsByVariantAsync`. The `Out` movement stores a NEGATIVE quantity (consistent with the ledger's signed-quantity convention and `RecordAdjustmentAsync`). If the movement is written with a positive magnitude in your implementation, adjust the assertion accordingly — but the plan writes it signed-negative (see Step 3).

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~DeliveryOrderServiceTests"`
Expected: FAIL — `PostAsync` throws `NotImplementedException`.

- [ ] **Step 3: Implement `PostAsync`**

In `DeliveryOrderService.cs`, replace the `PostAsync` stub with the following. It mirrors `GoodsReceiptService.PostAsync` but: (a) checks on-hand at `SO.WarehouseId` BEFORE any mutation, accumulating per-variant within the post so multi-line same-variant deliveries don't over-draw un-flushed stock; (b) writes an `Out` movement with a NEGATIVE quantity at `variant.CostPrice`; (c) upserts stock with a NEGATIVE delta; (d) snapshots COGS via `line.SetUnitCost`; (e) NEVER calls `ApplyMovingAverage`:

```csharp
    public async Task<bool> PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = await db.DeliveryOrders.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft delivery order can be posted.");

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");
        if (!so.CanDeliver) throw Fail("Only a confirmed or partially-delivered sales order can be delivered.");

        // Akumulasi qty KELUAR per varian dalam post ini: UpsertStockAsync hanya mengubah entitas di memori
        // (tanpa flush), jadi on-hand dari DB akan basi untuk baris ke-2 dengan varian sama. Cek stok memakai
        // (on-hand DB − akumulasiKeluar) SEBELUM mutasi apa pun.
        var takenPerVariant = new Dictionary<int, int>();

        // Fase 1: validasi ketersediaan stok untuk SEMUA baris sebelum menulis apa pun.
        foreach (var line in doc.Lines)
        {
            var onHand = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.ProductVariantId && s.WarehouseId == so.WarehouseId)
                .SumAsync(s => (int?)s.Quantity, ct) ?? 0;
            var alreadyTaken = takenPerVariant.TryGetValue(line.ProductVariantId, out var t) ? t : 0;
            var available = onHand - alreadyTaken;
            if (line.QuantityDelivered > available)
            {
                var sku = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                    .Select(v => v.Sku).FirstOrDefaultAsync(ct) ?? line.ProductVariantId.ToString();
                throw Fail($"Delivering {line.QuantityDelivered} of {sku} exceeds available stock ({available}) at the source warehouse.");
            }
            takenPerVariant[line.ProductVariantId] = alreadyTaken + line.QuantityDelivered;
        }

        // Fase 2: mutasi (stok keluar + COGS + tracking SO line).
        foreach (var line in doc.Lines)
        {
            var soLine = so.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId)
                ?? throw Fail($"SO line {line.SalesOrderLineId} not found on SO {so.SoNumber}.");

            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Variant {line.ProductVariantId} not found.");

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, so.WarehouseId, MovementType.Out,
                -line.QuantityDelivered, variant.CostPrice, doc.DeliveryDate, refType: "DO", refId: doc.Id,
                note: doc.DoNumber));

            await db.UpsertStockAsync(line.ProductVariantId, so.WarehouseId, -line.QuantityDelivered, ct);
            line.SetUnitCost(variant.CostPrice); // COGS snapshot; MA TIDAK diubah
            soLine.ApplyDelivery(line.QuantityDelivered);
        }

        if (so.Lines.All(l => l.IsFullyDelivered)) so.MarkDelivered();
        else so.MarkPartiallyDelivered();

        doc.Post();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
```

> Why two phases: the strict availability check must be authoritative and reject with a clear message BEFORE mutating, so a partially-writable multi-line DO never leaves stock half-decremented. `db.UpsertStockAsync` also guards negative results (`InvalidOperationException`) as a backstop, but the explicit phase-1 check is what produces the friendly `ValidationException` message the spec requires. `StockMovement` forbids a zero quantity (`quantity == 0` throws) — DO lines always have `QuantityDelivered > 0`, so `-QuantityDelivered` is always negative and valid.

- [ ] **Step 4: Run integration tests — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~DeliveryOrderServiceTests"`
Expected: PASS (all). Then `dotnet build` — 0 warnings.

---

### Task 7: Infrastructure — SalesOrderService.CloseAsync coverage + widen GetCreditInfoAsync

> `CloseAsync` itself was added in Task 3 Step 7. This task adds its integration coverage AND widens `GetCreditInfoAsync` to count PartiallyDelivered/Delivered SOs toward outstanding.

**Files:**
- Modify: `src/MyApp.Infrastructure/Services/SalesOrderService.cs` (widen `GetCreditInfoAsync`)
- Test: `tests/MyApp.IntegrationTests/DeliveryOrderServiceTests.cs` (add cases)

- [ ] **Step 1: Add failing tests (close + credit widening)**

Append to `DeliveryOrderServiceTests`:

```csharp
    [Fact]
    public async Task CloseSo_locks_a_partially_delivered_so()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant);
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id); // SO now PartiallyDelivered

        Assert.True(await soSvc.CloseAsync(so.Id));
        Assert.Equal("Closed", (await soSvc.GetByIdAsync(so.Id))!.Status);

        // After close, SO is no longer deliverable.
        Assert.Null(await svc.GetSoForDeliveryAsync(so.Id));
    }

    [Fact]
    public async Task GetCreditInfo_counts_partially_delivered_so_as_outstanding()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var so = await ConfirmedSoAsync(sp, cust, wh, variant); // GrandTotal 10000
        var svc = sp.GetRequiredService<IDeliveryOrderService>();
        var soSvc = sp.GetRequiredService<ISalesOrderService>();

        var d = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            so.Id, new DateTime(2026, 7, 1), null, [new DeliveryOrderLineRequest(so.Lines[0].Id, 4)]));
        await svc.PostAsync(d.Id); // SO → PartiallyDelivered

        var info = await soSvc.GetCreditInfoAsync(cust, thisOrderTotal: 0m, excludeSoId: null, default);
        Assert.Equal(10000m, info.EstimatedOutstanding); // PartiallyDelivered still counts
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~DeliveryOrderServiceTests"`
Expected: `CloseSo_locks...` PASSES (Close was implemented in Task 3); `GetCreditInfo_counts_partially_delivered...` FAILS — outstanding is still `Confirmed`-only, so a PartiallyDelivered SO returns 0.

- [ ] **Step 3: Widen `GetCreditInfoAsync`**

In `src/MyApp.Infrastructure/Services/SalesOrderService.cs`, change the outstanding filter in `GetCreditInfoAsync` from the `Confirmed`-only predicate to include `PartiallyDelivered` and `Delivered` (EF-translatable OR form — do NOT use `is`/pattern-matching which EF can't translate). Replace:

```csharp
        var estimatedOutstanding = await db.SalesOrders.AsNoTracking()
            .Where(s => s.CustomerId == customerId
                        && s.Status == SalesOrderStatus.Confirmed
                        && (excludeSoId == null || s.Id != excludeSoId))
            .SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;
```

with:

```csharp
        var estimatedOutstanding = await db.SalesOrders.AsNoTracking()
            .Where(s => s.CustomerId == customerId
                        && (s.Status == SalesOrderStatus.Confirmed
                            || s.Status == SalesOrderStatus.PartiallyDelivered
                            || s.Status == SalesOrderStatus.Delivered)
                        && (excludeSoId == null || s.Id != excludeSoId))
            .SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;
```

(Update the adjacent comment to note the committed set now includes PartiallyDelivered/Delivered.)

- [ ] **Step 4: Run — verify pass (incl. existing C1 credit tests)**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~DeliveryOrderServiceTests"` — PASS.
Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~SalesOrderServiceTests"` — PASS (existing C1 credit tests only use Confirmed SOs, so they are unaffected).
Then `dotnet build` — 0 warnings.

---

### Task 8: EF migration + database update

**Files:**
- Create (generated): `src/MyApp.Infrastructure/Migrations/*_AddDeliveryOrder.cs`

- [ ] **Step 1: Generate the migration**

Run from solution root:

```bash
dotnet ef migrations add AddDeliveryOrder --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: a new migration file created. (If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.)

- [ ] **Step 2: Review the migration content**

Open the generated file and confirm `Up()`:
- creates table `DeliveryOrders` (`Id` PK identity; `DoNumber` nvarchar(30) NOT NULL + UNIQUE index; `SalesOrderId` int FK→`SalesOrders` `Restrict`; `DeliveryDate`; `Notes` nvarchar(500); `Status` nvarchar(20) NOT NULL; audit columns `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy`),
- creates table `DeliveryOrderLines` (`Id` PK; `DeliveryOrderId` FK→`DeliveryOrders` `Cascade`; `SalesOrderLineId` FK→`SalesOrderLines` `Restrict`; `ProductVariantId` FK→`ProductVariants` `Restrict`; `QuantityDelivered` int; `UnitCost` decimal(18,2)),
- adds column `DeliveredQuantity` int NOT NULL default 0 to `SalesOrderLines`.

Confirm `Down()` drops both tables and the `DeliveredQuantity` column, and nothing else.

- [ ] **Step 3: Apply to the dev database**

```bash
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: ends with `Done.`

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test` — all unit + integration green.

---

### Task 9: Web — authorization (AppMenus), nav entry, admin auto-grant

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`

> `ActPost` and `ActClose` already exist in `AppMenus.cs` (added in B2) — reuse them. No appsettings change (DO has no options).

- [ ] **Step 1: Add the `delivery-orders` resource + `ActClose` on sales-orders**

In `AppMenus.cs`:
- add a DO actions helper next to `GoodsReceiptActions`:

```csharp
    private static AppAction[] DeliveryOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
```

- add `ActClose` to `SalesOrderActions` (so a partially-delivered SO can be closed):

```csharp
    private static AppAction[] SalesOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActClose];
```

- in the `"Transaksi"` group, add the resource after `transactions.sales-orders`:

```csharp
            new("transactions.delivery-orders", "Delivery Order", "bi-truck", DeliveryOrderActions),
```

- [ ] **Step 2: Add the NavMenu entry**

`NavMenu.razor` is hardcoded (not data-driven). Add a Delivery Order entry inside the `Policy="transactions.any"` block, after the `transactions.sales-orders.index` `<AuthorizeView>` block (mirror the GRN entry):

```razor
                <AuthorizeView Policy="transactions.delivery-orders.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="transactions/delivery-orders" title="Delivery Order">
                                <i class="bi bi-truck nav-icon" aria-hidden="true"></i> <span class="nav-label">Delivery Order</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
```

- [ ] **Step 3: Build + confirm admin auto-grant**

Run: `dotnet build` — 0 warnings.
Admin permissions come from `AppMenus.AllPermissions` (used by `BootstrapSeeder`); the new `transactions.delivery-orders.*` and `transactions.sales-orders.close` permissions are auto-granted to admin on next app start. No chain seed is needed (DO has no approval). No test here.

---

### Task 10: Web — DoIndex page (+ scoped css)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoIndex.razor.css`

> Mirror `src/MyApp.Web/Components/Pages/Transactions/GoodsReceipts/GrnIndex.razor` (KPI cards, search box, status chips, table card, `Pager`, `SwalService` delete, `@rendermode InteractiveServer`). Blazor scoped CSS is per-component: you MUST copy `GrnIndex.razor.css` to `DoIndex.razor.css` verbatim (styles are keyed to the `pi`/`kpi`/`toolbar`/`card` classes reused here).

- [ ] **Step 1: Copy the scoped CSS**

Copy `GrnIndex.razor.css` → `DoIndex.razor.css` unchanged (same class names/layout).

- [ ] **Step 2: Create the page**

Create `DoIndex.razor` by copying `GrnIndex.razor` and applying these exact renames/edits (nothing else changes):
- `@page "/transactions/delivery-orders"`
- `@attribute [Authorize(Policy = "transactions.delivery-orders.index")]`
- usings → `@using MyApp.Application.DeliveryOrders` (drop the GoodsReceipts using); keep `@using MyApp.Application.Common` and `@using MyApp.Domain.Entities`.
- `@inject IDeliveryOrderService Do` (was `IGoodsReceiptService Grn`); keep `NavigationManager Nav`, `SwalService Swal`.
- `<PageTitle>Delivery Order</PageTitle>`; breadcrumb "here" → "Delivery Order"; H1 "Delivery Order"; subtitle "Pengiriman barang atas Sales Order."
- Create button label "Pengiriman Baru" → `href="/transactions/delivery-orders/new"`, under `Policy="transactions.delivery-orders.create"`.
- KPI cards: "Total DO" / "Draft" / "Posted" bound to `_dash.TotalCount` / `_dash.DraftCount` / `_dash.PostedCount`; use icon `bi-truck` for the total card.
- Search placeholder "Cari nomor DO atau SO…".
- Status chips: `foreach (var s in Enum.GetValues<DeliveryOrderStatus>())`.
- Table columns: `DO#`, `SO#`, `Customer`, `Tanggal` (`item.DeliveryDate.ToString("dd MMM yyyy")`), `Status`, `Qty` (`item.TotalQuantity`), actions. Row `@onclick="() => Open(item.Id)"` → `Open` navigates to `/transactions/delivery-orders/{id}`.
- Cell values: `@item.DoNumber`, `@item.SoNumber`, `@item.CustomerName`.
- Delete action: only when `item.Status == "Draft"`, under `Policy="transactions.delivery-orders.delete"`, `@onclick="() => DeleteAsync(item.Id, item.DoNumber)"`.
- `@code` block: rename the model type to `PagedResult<DeliveryOrderListItemDto>`, dash type to `DeliveryOrderDashboardDto`, status field to `DeliveryOrderStatus?`, and the service field to `Do`. Method bodies are identical to GrnIndex (call `Do.GetDashboardAsync`, `Do.GetPagedAsync`, `Do.DeleteDraftAsync`). Delete confirm text "Hapus pengiriman draft ini?"; toast "Delivery Order dihapus". Keep `StatusClass` (Draft→`b-draft`, Posted→`b-done`).

- [ ] **Step 3: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 11: Web — DoForm page (create/edit draft; qty-only, no cost) (+ scoped css)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoForm.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoForm.razor.css`

> Mirror `GrnForm.razor` (Atlas layout: `pf`/`pf-nav` progress + `pf-main` section cards + `pf-footer`; inline `_error` alert; save spinner; `ValidationException` handling; `pfScrollTo` JS interop). Copy `GrnForm.razor.css` → `DoForm.razor.css` verbatim. **The DO items table edits qty-delivered ONLY — there is NO Unit Cost column** (COGS is set automatically at Post).

- [ ] **Step 1: Copy the scoped CSS**

Copy `GrnForm.razor.css` → `DoForm.razor.css` unchanged.

- [ ] **Step 2: Create the page**

Create `DoForm.razor` by copying `GrnForm.razor` and applying these exact edits:
- Routes: `@page "/transactions/delivery-orders/new"` and `@page "/transactions/delivery-orders/{Id:int}/edit"`.
- `@attribute [Authorize(Policy = "transactions.delivery-orders.create")]`.
- usings → `@using FluentValidation`, `@using MyApp.Application.DeliveryOrders`.
- `@inject IDeliveryOrderService Do` (keep `NavigationManager Nav`, `IJSRuntime JS`).
- Breadcrumb links → `/transactions/delivery-orders`, text "Delivery Orders".
- Section 1 header "Delivery information" / "The sales order delivered and the delivery date."; the PO `<select>`/readonly input becomes the **SO** selector bound to `_selectedSoId`/`_soNumber` and populated from `_sos` (`DeliverableSoDto`: `@so.SoNumber — @so.CustomerName`); label "Sales Order". Receipt Date → **Delivery Date** bound to `_deliveryDate`.
- Section 2 header "Delivered Items" / "Enter the delivered quantity per line." The items table has columns: `Product`, `SKU`, `Ordered`, `Already Delivered`, `Qty Delivered` — and **NO Unit Cost column** (delete that `<th>` and its `<td><input ... @bind="row.UnitCost" /></td>`). Keep the qty input `@bind="row.Qty"`.
- Nav-progress "Received Items" label → "Delivered Items"; `ReceivedLineCount` → `DeliveredLineCount` (same `_rows.Count(r => r.Qty > 0)` logic).
- `@code` renames:
  - `[SupplyParameterFromQuery] public int? SoId { get; set; }` (was `PoId`).
  - `_sos` is `List<DeliverableSoDto>`; `_selectedSoId`; `_soNumber`; `_deliveryDate = DateTime.Today`.
  - `Row` view-model DROPS `UnitCost` and `AlreadyReceived`→`AlreadyDelivered`; keeps `SalesOrderLineId`, `ProductName`, `Sku`, `Ordered`, `AlreadyDelivered`, `Qty`.
  - `Title` → "Add Delivery Order"/"Edit Delivery Order".
  - `OnInitializedAsync`: `if (Id is int did) { await LoadForEditAsync(did); return; }` `_sos = (await Do.GetDeliverableSosAsync()).ToList();` `if (SoId is int s) { _selectedSoId = s; await LoadSoLinesAsync(s); }`.
  - `LoadSoLinesAsync(int soId)`: `var so = await Do.GetSoForDeliveryAsync(soId);` then map `so.Lines` → rows with `SalesOrderLineId = l.SalesOrderLineId, ProductName = l.ProductName, Sku = l.VariantSku, Ordered = l.OrderedQuantity, AlreadyDelivered = l.AlreadyDeliveredQuantity, Qty = l.RemainingQuantity` (no UnitCost).
  - `LoadForEditAsync(int did)`: `var g = await Do.GetByIdAsync(did); if (g is null || g.Status != "Draft") { Nav.NavigateTo($"/transactions/delivery-orders/{did}"); return; }` set `_selectedSoId = g.SalesOrderId; _soNumber = g.SoNumber; _deliveryDate = g.DeliveryDate; _notes = g.Notes;` map `g.Lines` → rows (`AlreadyDelivered = 0`, `Qty = l.QuantityDelivered`).
  - `OnSoChanged` (was `OnPoChanged`): parse `_selectedSoId`, load lines or clear rows.
  - `SaveAsync`: build `lines = _rows.Where(r => r.Qty > 0).Select(r => new DeliveryOrderLineRequest(r.SalesOrderLineId, r.Qty)).ToList();` (NO cost). If `Id is int did` → `Do.UpdateDraftAsync(did, new UpdateDeliveryOrderRequest(_deliveryDate, _notes, lines))` → navigate to detail; else guard `_selectedSoId <= 0` ("Select a sales order first.") and `lines.Count == 0` ("Enter a delivered quantity on at least one line.") then `Do.CreateDraftAsync(new CreateDeliveryOrderRequest(_selectedSoId, _deliveryDate, _notes, lines))` → navigate to `/transactions/delivery-orders/{created.Id}`. Keep the `catch (ValidationException ex)` inline-error handling and `_saving` spinner.

- [ ] **Step 3: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 12: Web — DoDetail page (view + post; shows COGS after post) (+ scoped css)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoDetail.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoDetail.razor.css`

> Mirror `GrnDetail.razor` (header card + status badge + action bar; info `dl`; items table; Post button under policy with `SwalService` confirm; read-only after Post). Copy `GrnDetail.razor.css` → `DoDetail.razor.css` verbatim.

- [ ] **Step 1: Copy the scoped CSS**

Copy `GrnDetail.razor.css` → `DoDetail.razor.css` unchanged.

- [ ] **Step 2: Create the page**

Create `DoDetail.razor` by copying `GrnDetail.razor` and applying these exact edits:
- `@page "/transactions/delivery-orders/{Id:int}"`, `@attribute [Authorize(Policy = "transactions.delivery-orders.index")]`.
- usings → `@using FluentValidation`, `@using MyApp.Application.DeliveryOrders`; `@inject IDeliveryOrderService Do` (keep `NavigationManager Nav`, `SwalService Swal`).
- `<PageTitle>@(_do?.DoNumber ?? "Delivery Order")</PageTitle>`; breadcrumb links → `/transactions/delivery-orders`, text "Delivery Orders"; "here" → `@(_do?.DoNumber ?? "Detail")`.
- Header H1 `@_do.DoNumber`; the action bar's Edit link → `/transactions/delivery-orders/{_do.Id}/edit` under `Policy="transactions.delivery-orders.edit"`; the Post button under `Policy="transactions.delivery-orders.post"`.
- Info `dl`: `DO#` → `@_do.DoNumber`; `SO#` → `<a href="/transactions/sales-orders/@_do.SalesOrderId">@_do.SoNumber</a>`; `Customer` → `@_do.CustomerName`; `Gudang` (source) → `@_do.WarehouseName`; `Tanggal Kirim` → `@_do.DeliveryDate.ToString("dd MMM yyyy")`; Catatan/Dibuat/Oleh identical.
- Items table columns: `Produk`, `SKU`, `Dipesan` (`OrderedQuantity`), `Terkirim` (`QuantityDelivered`), `HPP/unit` (`UnitCost.ToString("N2")`), `Subtotal` (`LineCost.ToString("N2")`). (HPP/unit shows 0.00 while Draft and the snapshotted COGS after Post — this is expected and matches the spec.)
- `@code` renames: `_do` is `DeliveryOrderDto?`; `LoadAsync` → `_do = await Do.GetByIdAsync(Id);`. `PostAsync` confirm text "Post Delivery Order?" / "Stok akan dikurangi dan COGS dicatat; tidak bisa dibatalkan." then `await Do.PostAsync(Id)`; keep both `catch` blocks (`ValidationException` → joined messages; `InvalidOperationException` → message). Keep `StatusClass` (Draft→`b-draft`, Posted→`b-done`).

- [ ] **Step 3: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 13: Web — SoDetail delivered-progress + Buat DO + Tutup SO

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Transactions/SalesOrders/SoDetail.razor`
- Modify: `src/MyApp.Web/Components/Pages/Transactions/SalesOrders/SoIndex.razor` (status-badge parity)
- Modify: `src/MyApp.Application/SalesOrders/SalesOrderDtos.cs` (add `DeliveredQuantity` to line DTO)
- Modify: `src/MyApp.Infrastructure/Services/SalesOrderService.cs` (project `DeliveredQuantity` in `GetByIdAsync`)

- [ ] **Step 1: Expose `DeliveredQuantity` on the SO line DTO**

`SoDetail` needs per-line delivered progress. Append `int DeliveredQuantity` to the `SalesOrderLineDto` record (`src/MyApp.Application/SalesOrders/SalesOrderDtos.cs`):

```csharp
public record SalesOrderLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal,
    int DeliveredQuantity);
```

Then set it in `SalesOrderService.GetByIdAsync`'s line projection (the ONLY positional construction site) — add `l.DeliveredQuantity` as the final argument:

```csharp
            return new SalesOrderLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, l.TaxRateSnapshot,
                l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal,
                l.DeliveredQuantity);
```

Build the Application + Infrastructure projects after this change. (Existing C1 tests still pass — the record just gains one trailing positional field and there is a single positional construction site, updated here.)

- [ ] **Step 2: Add the delivered-progress column**

In `SoDetail.razor`'s Item table, add a "Terkirim" column showing `@l.DeliveredQuantity / @l.Quantity` (mirror how `PoDetail` shows `@l.ReceivedQuantity / @l.Quantity`). Add the `<th class="r">Terkirim</th>` header after `Qty` and a `<td class="r mono">@l.DeliveredQuantity / @l.Quantity</td>` cell after the Qty cell; bump the `<tfoot>` grand-total `colspan` from `5` to `6`.

- [ ] **Step 3: Add Buat DO + Tutup SO buttons**

Inject the service if not present (already `@inject ISalesOrderService SoService`). Add a new branch to the header `actions` block after the `PendingApproval` branch (mirror `PoDetail`'s `Confirmed`/`PartiallyReceived` branch, but for delivery):

```razor
                else if (_so.Status == "Confirmed" || _so.Status == "PartiallyDelivered")
                {
                    <AuthorizeView Policy="transactions.delivery-orders.create">
                        <button class="btn btn-primary" @onclick='() => Nav.NavigateTo($"/transactions/delivery-orders/new?soId={_so.Id}")' disabled="@_busy"><i class="bi bi-truck"></i> Buat DO</button>
                    </AuthorizeView>
                    @if (_so.Status == "PartiallyDelivered")
                    {
                        <AuthorizeView Policy="transactions.sales-orders.close">
                            <button class="btn btn-line" @onclick="CloseSoAsync" disabled="@_busy">Tutup SO</button>
                        </AuthorizeView>
                    }
                }
```

Add the close handler in `@code` (mirror `PoDetail.ClosePoAsync`, reusing the existing `RunAsync` helper):

```csharp
    private async Task CloseSoAsync()
    {
        if (!await Swal.ConfirmAsync("Tutup SO ini?", "Sisa qty tidak akan bisa dikirim lagi.")) return;
        await RunAsync(() => SoService.CloseAsync(Id), "SO ditutup");
    }
```

Extend the `StatusClass` switch to cover the new statuses (the scoped `.b-info`/`.b-done`/`.b-closed` classes already exist in `SoDetail.razor.css`):

```csharp
    private static string StatusClass(string s) => s switch
    {
        "Draft" => "b-draft",
        "PendingApproval" => "b-warn",
        "Confirmed" => "b-ok",
        "PartiallyDelivered" => "b-info",
        "Delivered" => "b-done",
        "Closed" => "b-closed",
        "Rejected" => "b-danger",
        "Cancelled" => "b-cancel",
        _ => "b-dark"
    };
```

- [ ] **Step 3b: Badge parity in the SO list**

`SoIndex.razor`'s `StatusClass` was written in C1 for the 5 C1 statuses only, so `PartiallyDelivered`/`Delivered`/`Closed` SOs would render with the fallback `b-dark` badge. The `.b-info`/`.b-done`/`.b-closed` classes already exist in the copied `SoIndex.razor.css`. Update the `StatusClass` switch in `SoIndex.razor`'s `@code` to the full set (same arms as SoDetail in Step 3):

```csharp
    private static string StatusClass(string status) => status switch
    {
        "Draft" => "b-draft",
        "PendingApproval" => "b-warn",
        "Confirmed" => "b-ok",
        "PartiallyDelivered" => "b-info",
        "Delivered" => "b-done",
        "Closed" => "b-closed",
        "Rejected" => "b-danger",
        "Cancelled" => "b-cancel",
        _ => "b-dark"
    };
```

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test` — all green.

---

### Task 14: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build` — Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: all unit + integration tests pass (the C1 baseline + the new C2 unit & integration tests).

- [ ] **Step 3: Confirm invariants by re-reading the service**

Re-read `DeliveryOrderService` to confirm:
- `CreateDraftAsync`/`UpdateDraftAsync` never touch `StockMovements`/`ProductStocks`; only `PostAsync` does, inside a transaction.
- `PostAsync` performs the phase-1 availability check BEFORE any mutation, uses `MovementType.Out` with a NEGATIVE quantity at `variant.CostPrice`, calls `db.UpsertStockAsync(..., -qty, ...)`, snapshots COGS via `line.SetUnitCost(variant.CostPrice)`, and NEVER calls `ApplyMovingAverage`.
- `PostAsync` rejects a non-Draft DO (`InvalidOperationException`).

- [ ] **Step 4: Manual UI walkthrough (hand to user)**

Hand to the user (cannot be automated):
1. SO at `Confirmed` (with on-hand stock in the source warehouse) → open SO detail → **Buat DO** → deliver a partial qty → **Post** → SO shows `PartiallyDelivered`, stock decremented, DO line shows COGS.
2. Deliver the remaining qty → SO shows `Delivered`.
3. On a partially-delivered SO → **Tutup SO** → status `Closed`, no longer deliverable.
4. Over-delivery beyond ordered qty is rejected with a clear message (STRICT, no tolerance).
5. Posting a DO when stock is insufficient is rejected with a clear per-item message, and nothing is mutated.

## Self-Review (done by plan author)

- **Spec coverage:** §1 Domain (`SalesOrderStatus`/`SalesOrderLine`/`SalesOrder` + `DeliveryOrderStatus`/`DeliveryOrder`/`DeliveryOrderLine`) → Tasks 1–2; §2 Application (DTOs/interface/validators + `ISalesOrderService.CloseAsync` + `GetCreditInfoAsync` widening) → Tasks 3 & 7; §3 Infrastructure (DbContext/service/DI/migration; no appsettings) → Tasks 4–8; §4 Web (DoIndex/DoForm/DoDetail + SoDetail) → Tasks 10–13; §5 Menu & auth → Task 9; §6 Testing → embedded per task + Task 14. Close SO → Tasks 3/7/13. ✓
- **Type consistency:** DTO/record names and service signatures defined in Task 3 are reused verbatim in Tasks 5,6,7,10–13. `DeliveryOrderLineRequest(int SalesOrderLineId, int QuantityDelivered)` has NO UnitCost anywhere. Domain method names `ApplyDelivery`/`SetUnitCost`/`MarkPartiallyDelivered`/`MarkDelivered`/`Close`/`CanDeliver` defined in Tasks 1–2 match all later consumers. `StockMovement(variantId, warehouseId, MovementType.Out, -qty, variant.CostPrice, deliveryDate, refType:"DO", refId, note:DoNumber)` matches the real ctor read from source. `SalesOrderLine`/`SalesOrder`/`Customer.ctor`/`Product.AddVariant`/`RecordOpeningAsync` shapes used in tests match the real sources (`SalesOrderServiceTests`). ✓
- **Mirror fidelity vs current sources:** `DeliveryOrderService` mirrors the CURRENT `GoodsReceiptService` (which has `GetDashboardAsync` and calls the shared `db.UpsertStockAsync`), and `PostAsync`'s per-variant accumulation mirrors the GRN `addedPerVariant` pattern (adapted to OUT with a pre-mutation availability check). The web pages mirror the CURRENT Grn*/Po*/So* pages (KPI cards, Atlas `pf` layout, `SwalService`, `@rendermode InteractiveServer`) — richer than the spec's prose — and each task copies the sibling scoped `.razor.css`. ✓
- **MA untouched / no MA call:** Task 6 Step 3 explicitly omits `ApplyMovingAverage`; Task 14 Step 3 re-verifies. Movement unit cost = `variant.CostPrice`, matching the OUTBOUND branch of `StockService.RecordAdjustmentAsync`. ✓
- **No approval / no tolerance / no config:** DO has no approval flow, no `DeliveryOrderOptions`, no appsettings section, and `ApplyDelivery`/`BuildLines` cap at the exact ordered qty (STRICT). ✓
- **Placeholder scan:** Domain/Application/Infrastructure steps show REAL code. Razor tasks reference the concrete sibling Grn*/So* file as template (repo convention) but specify routes, policies, injects, exact column/label renames, and the full `@code` deltas — plus the mandatory scoped-CSS copy — no "TBD"/"similar to"/"add validation" placeholders. ✓
- **Resolved ambiguities (noted for the user):**
  1. **Stock movement sign.** The spec says "delta negatif" for the stock helper but does not pin the ledger `Quantity` sign. The ledger's `Quantity` is documented as signed (`+ masuk / − keluar`) and `StockMovement` forbids `Quantity == 0`; `RecordAdjustmentAsync` writes the signed `DeltaQuantity`. So DO writes `MovementType.Out` with `-QuantityDelivered`. The post-effect test asserts `m.Quantity == -5`.
  2. **Opening stock in tests.** A DO Post needs on-hand stock; the integration seed helper records opening stock via `IStockService.RecordOpeningAsync` (real API) so Post tests have inventory to draw down and the insufficient-stock test can seed exactly 3.
  3. **`GetSoForDeliveryAsync` returns all SO lines** (with `RemainingQuantity`), including fully-delivered ones (RemainingQuantity 0). The form pre-fills `Qty = RemainingQuantity`; lines with `Qty == 0` are dropped on save. This mirrors GRN's `GetPoForReceiptAsync` behavior rather than filtering server-side.
  4. **`CloseAsync` transaction.** Mirrors `PurchaseOrderService.CloseAsync` (wrapped in a transaction), consistent with the other SO mutators; the spec only said "load SO; `so.Close()`; save".
