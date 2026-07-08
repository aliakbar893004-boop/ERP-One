# B2 — Goods Receipt (GRN) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Goods Receipt (penerimaan barang) over a Confirmed Purchase Order — partial receipts, stock-in via the ledger, and moving-average HPP update.

**Architecture:** Clean Architecture, domain-rich entities (`GoodsReceipt`/`GoodsReceiptLine`) with a `GoodsReceiptService` orchestrating the post transaction (stock movement + `ProductStock` upsert + `ProductVariant.ApplyMovingAverage` + PO line receipt tracking + PO status), mirroring the existing `StockService` and `PurchaseOrderService`. Blazor Server UI mirrors the existing PO pages.

**Tech Stack:** .NET 10 / C#, EF Core 10, Blazor Server + Bootstrap 5 + Bootstrap Icons, FluentValidation, xUnit. SQL Server (app) / SQLite (integration tests via `CustomWebApplicationFactory`).

**Spec:** `docs/superpowers/specs/2026-06-29-b2-goods-receipt-design.md`

## Global Constraints

- **No git** — implementers do NOT commit. Each task ends by verifying `dotnet build` (0 warnings) + the relevant `dotnet test` filter is green. Reviewers read changed files directly.
- **Build/test from solution root** `F:\4. My Data\Project\MyApplication`.
- **Enums stored as string** via `.HasConversion<string>().HasMaxLength(20)`.
- **Decimals** `(18,2)` for money/cost via `.HasPrecision(18, 2)`.
- **Rounding** `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Entities** derive from `AuditableEntity` where audited, private setters, EF ctor `private Xxx() { }`, child collections via backing `List<>` + `PropertyAccessMode.Field`.
- **Validation errors** thrown as `FluentValidation.ValidationException`.
- **Moving average is global per variant**: `totalBefore = Σ ProductStock.Quantity` for the variant across ALL warehouses (match `StockService`). Stock movement & `ProductStock` rows are per `PurchaseOrder.WarehouseId`.
- **Over-receipt tolerance** default 10%, from config section `GoodsReceipt:OverReceiptTolerancePercent`.

---

### Task 1: Domain — PO receipt tracking + status transitions

**Files:**
- Modify: `src/MyApp.Domain/Entities/PurchaseOrderStatus.cs`
- Modify: `src/MyApp.Domain/Entities/PurchaseOrderLine.cs`
- Modify: `src/MyApp.Domain/Entities/PurchaseOrder.cs`
- Test: `tests/MyApp.UnitTests/PurchaseOrderLineTests.cs` (add cases)
- Test: `tests/MyApp.UnitTests/PurchaseOrderTests.cs` (add cases)

**Interfaces:**
- Produces:
  - `PurchaseOrderStatus.PartiallyReceived`, `.Received`, `.Closed`
  - `PurchaseOrderLine.ReceivedQuantity` (int, get), `.IsFullyReceived` (bool), `.DefaultUnitCost` (decimal), `.ApplyReceipt(int qty, int tolerancePercent)`
  - `PurchaseOrder.CanReceive` (bool), `.MarkPartiallyReceived()`, `.MarkReceived()`, `.Close()`

- [ ] **Step 1: Add failing unit tests for `PurchaseOrderLine`**

Append to `tests/MyApp.UnitTests/PurchaseOrderLineTests.cs` (inside the existing test class):

```csharp
    [Fact]
    public void DefaultUnitCost_is_net_of_discount_rounded()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 10m, null, 0m); // 1000 * 0.9 = 900
        Assert.Equal(900m, line.DefaultUnitCost);
    }

    [Fact]
    public void ApplyReceipt_accumulates_and_tracks_full_receipt()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m);
        Assert.Equal(0, line.ReceivedQuantity);
        Assert.False(line.IsFullyReceived);

        line.ApplyReceipt(4, 0);
        Assert.Equal(4, line.ReceivedQuantity);
        Assert.False(line.IsFullyReceived);

        line.ApplyReceipt(6, 0);
        Assert.Equal(10, line.ReceivedQuantity);
        Assert.True(line.IsFullyReceived);
    }

    [Fact]
    public void ApplyReceipt_allows_up_to_tolerance()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m); // tol 10% -> max 11
        line.ApplyReceipt(11, 10);
        Assert.Equal(11, line.ReceivedQuantity);
    }

    [Fact]
    public void ApplyReceipt_rejects_over_tolerance()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m); // tol 10% -> max 11
        Assert.Throws<InvalidOperationException>(() => line.ApplyReceipt(12, 10));
    }

    [Fact]
    public void ApplyReceipt_rejects_non_positive()
    {
        var line = new PurchaseOrderLine(5, 10, 1000m, 0m, null, 0m);
        Assert.Throws<ArgumentException>(() => line.ApplyReceipt(0, 10));
    }
```

- [ ] **Step 2: Run tests — verify they fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~PurchaseOrderLineTests"`
Expected: FAIL — build error, members `DefaultUnitCost`/`ApplyReceipt`/`ReceivedQuantity`/`IsFullyReceived` do not exist.

- [ ] **Step 3: Implement `PurchaseOrderLine` members**

Add to `src/MyApp.Domain/Entities/PurchaseOrderLine.cs` — a new property after `LineTotal` and methods after `Recompute`/`Round`:

```csharp
    public int ReceivedQuantity { get; private set; }

    public bool IsFullyReceived => ReceivedQuantity >= Quantity;

    /// <summary>HPP per unit default = harga netto setelah diskon (tanpa PPN), dibulatkan.</summary>
    public decimal DefaultUnitCost =>
        Math.Round(UnitPrice * (1 - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Catat penerimaan; tolak bila melebihi qty pesan × (1 + toleransi%).</summary>
    public void ApplyReceipt(int qty, int tolerancePercent)
    {
        if (qty <= 0) throw new ArgumentException("Receipt quantity must be > 0.", nameof(qty));
        if (tolerancePercent < 0) throw new ArgumentException("Tolerance percent must be >= 0.", nameof(tolerancePercent));
        var maxAllowed = (int)Math.Floor(Quantity * (1 + tolerancePercent / 100m));
        if (ReceivedQuantity + qty > maxAllowed)
            throw new InvalidOperationException(
                $"Receiving {qty} would exceed the allowed quantity ({maxAllowed}) for this line.");
        ReceivedQuantity += qty;
    }
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~PurchaseOrderLineTests"`
Expected: PASS (all, including the 5 new ones).

- [ ] **Step 5: Add the new `PurchaseOrderStatus` values**

Edit `src/MyApp.Domain/Entities/PurchaseOrderStatus.cs` — append three values after `Cancelled` (order is irrelevant; stored as string):

```csharp
public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled,
    PartiallyReceived,
    Received,
    Closed
}
```

- [ ] **Step 6: Add failing unit tests for `PurchaseOrder` transitions**

Append to `tests/MyApp.UnitTests/PurchaseOrderTests.cs` (inside the class). Helper to reach `Confirmed`:

```csharp
    private static PurchaseOrder Confirmed()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        return po;
    }

    [Fact]
    public void CanReceive_only_when_confirmed_or_partially_received()
    {
        Assert.False(Make().CanReceive);
        var po = Confirmed();
        Assert.True(po.CanReceive);
        po.MarkPartiallyReceived();
        Assert.True(po.CanReceive);
    }

    [Fact]
    public void MarkReceived_and_partial_require_receivable_status()
    {
        Assert.Throws<InvalidOperationException>(() => Make().MarkReceived());
        var po = Confirmed();
        po.MarkReceived();
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);
    }

    [Fact]
    public void Close_only_from_partially_received()
    {
        var po = Confirmed();
        Assert.Throws<InvalidOperationException>(() => po.Close()); // Confirmed cannot close
        po.MarkPartiallyReceived();
        po.Close();
        Assert.Equal(PurchaseOrderStatus.Closed, po.Status);
    }
```

- [ ] **Step 7: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~PurchaseOrderTests"`
Expected: FAIL — `CanReceive`/`MarkPartiallyReceived`/`MarkReceived`/`Close` do not exist.

- [ ] **Step 8: Implement `PurchaseOrder` transitions**

Add to `src/MyApp.Domain/Entities/PurchaseOrder.cs` after `MarkConfirmed()`:

```csharp
    public bool CanReceive =>
        Status is PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived;

    public void MarkPartiallyReceived()
    {
        if (!CanReceive)
            throw new InvalidOperationException("Only a confirmed or partially-received purchase order can record receipts.");
        Status = PurchaseOrderStatus.PartiallyReceived;
    }

    public void MarkReceived()
    {
        if (!CanReceive)
            throw new InvalidOperationException("Only a confirmed or partially-received purchase order can record receipts.");
        Status = PurchaseOrderStatus.Received;
    }

    public void Close()
    {
        if (Status != PurchaseOrderStatus.PartiallyReceived)
            throw new InvalidOperationException("Only a partially-received purchase order can be closed.");
        Status = PurchaseOrderStatus.Closed;
    }
```

- [ ] **Step 9: Run all unit tests — verify green**

Run: `dotnet test tests/MyApp.UnitTests`
Expected: PASS (all existing + new). Then `dotnet build` — 0 warnings.

---

### Task 2: Domain — GoodsReceipt + GoodsReceiptLine + status enum

**Files:**
- Create: `src/MyApp.Domain/Entities/GoodsReceiptStatus.cs`
- Create: `src/MyApp.Domain/Entities/GoodsReceiptLine.cs`
- Create: `src/MyApp.Domain/Entities/GoodsReceipt.cs`
- Test: `tests/MyApp.UnitTests/GoodsReceiptTests.cs`

**Interfaces:**
- Produces:
  - `enum GoodsReceiptStatus { Draft, Posted }`
  - `GoodsReceiptLine(int purchaseOrderLineId, int productVariantId, int quantityReceived, decimal unitCost)` with props `Id, GoodsReceiptId, PurchaseOrderLineId, ProductVariantId, QuantityReceived, UnitCost`
  - `GoodsReceipt(string grnNumber, int purchaseOrderId, DateTime receiptDate, string? notes)` with `Id, GrnNumber, PurchaseOrderId, ReceiptDate, Notes, Status, IReadOnlyCollection<GoodsReceiptLine> Lines`, methods `UpdateHeader(DateTime, string?)`, `SetLines(IEnumerable<GoodsReceiptLine>)`, `Post()`

- [ ] **Step 1: Write failing tests**

Create `tests/MyApp.UnitTests/GoodsReceiptTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class GoodsReceiptTests
{
    private static GoodsReceipt Make() =>
        new("GRN-202606-0001", purchaseOrderId: 1, receiptDate: new DateTime(2026, 6, 29), notes: "  catatan  ");

    private static GoodsReceiptLine Line() => new(purchaseOrderLineId: 7, productVariantId: 5, quantityReceived: 3, unitCost: 900m);

    [Fact]
    public void New_grn_is_draft_and_trims_notes()
    {
        var grn = Make();
        Assert.Equal(GoodsReceiptStatus.Draft, grn.Status);
        Assert.Equal("catatan", grn.Notes);
        Assert.Equal("GRN-202606-0001", grn.GrnNumber);
    }

    [Fact]
    public void Post_requires_lines()
    {
        var grn = Make();
        Assert.Throws<InvalidOperationException>(() => grn.Post());
        grn.SetLines([Line()]);
        grn.Post();
        Assert.Equal(GoodsReceiptStatus.Posted, grn.Status);
    }

    [Fact]
    public void Cannot_modify_after_post()
    {
        var grn = Make();
        grn.SetLines([Line()]);
        grn.Post();
        Assert.Throws<InvalidOperationException>(() => grn.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() => grn.UpdateHeader(DateTime.Today, null));
        Assert.Throws<InvalidOperationException>(() => grn.Post());
    }

    [Fact]
    public void Line_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(0, 5, 3, 900m));
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(7, 5, 0, 900m));
        Assert.Throws<ArgumentException>(() => new GoodsReceiptLine(7, 5, 3, -1m));
    }
}
```

- [ ] **Step 2: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~GoodsReceiptTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create the enum**

`src/MyApp.Domain/Entities/GoodsReceiptStatus.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup Goods Receipt: Draft (belum gerak stok) → Posted (stok & HPP final).</summary>
public enum GoodsReceiptStatus
{
    Draft,
    Posted
}
```

- [ ] **Step 4: Create `GoodsReceiptLine`**

`src/MyApp.Domain/Entities/GoodsReceiptLine.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Baris penerimaan: qty diterima + HPP per unit untuk satu baris PO.</summary>
public class GoodsReceiptLine
{
    public int Id { get; private set; }
    public int GoodsReceiptId { get; private set; }
    public int PurchaseOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int QuantityReceived { get; private set; }
    public decimal UnitCost { get; private set; }

    private GoodsReceiptLine() { } // EF Core

    public GoodsReceiptLine(int purchaseOrderLineId, int productVariantId, int quantityReceived, decimal unitCost)
    {
        if (purchaseOrderLineId <= 0) throw new ArgumentException("PurchaseOrderLineId is required.", nameof(purchaseOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantityReceived <= 0) throw new ArgumentException("QuantityReceived must be > 0.", nameof(quantityReceived));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        PurchaseOrderLineId = purchaseOrderLineId;
        ProductVariantId = productVariantId;
        QuantityReceived = quantityReceived;
        UnitCost = unitCost;
    }
}
```

- [ ] **Step 5: Create `GoodsReceipt`**

`src/MyApp.Domain/Entities/GoodsReceipt.cs`:

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Penerimaan barang atas satu PO. Baris hanya bisa diubah saat Draft; Post mengunci.</summary>
public class GoodsReceipt : AuditableEntity
{
    private readonly List<GoodsReceiptLine> _lines = [];

    public int Id { get; private set; }
    public string GrnNumber { get; private set; } = default!;
    public int PurchaseOrderId { get; private set; }
    public DateTime ReceiptDate { get; private set; }
    public string? Notes { get; private set; }
    public GoodsReceiptStatus Status { get; private set; }

    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines;

    private GoodsReceipt() { } // EF Core

    public GoodsReceipt(string grnNumber, int purchaseOrderId, DateTime receiptDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(grnNumber))
            throw new ArgumentException("GrnNumber is required.", nameof(grnNumber));
        if (purchaseOrderId <= 0)
            throw new ArgumentException("PurchaseOrderId is required.", nameof(purchaseOrderId));
        GrnNumber = grnNumber.Trim();
        PurchaseOrderId = purchaseOrderId;
        SetHeader(receiptDate, notes);
        Status = GoodsReceiptStatus.Draft;
    }

    public void UpdateHeader(DateTime receiptDate, string? notes)
    {
        EnsureDraft();
        SetHeader(receiptDate, notes);
    }

    private void SetHeader(DateTime receiptDate, string? notes)
    {
        ReceiptDate = receiptDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<GoodsReceiptLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
    }

    public void Post()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot post a goods receipt without lines.");
        Status = GoodsReceiptStatus.Posted;
    }

    private void EnsureDraft()
    {
        if (Status != GoodsReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft goods receipt can be modified.");
    }
}
```

- [ ] **Step 6: Run tests — verify pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~GoodsReceiptTests"`
Expected: PASS. Then `dotnet build` — 0 warnings.

---

### Task 3: Application — DTOs, service interface, validators, options

**Files:**
- Create: `src/MyApp.Application/GoodsReceipts/GoodsReceiptDtos.cs`
- Create: `src/MyApp.Application/GoodsReceipts/IGoodsReceiptService.cs`
- Create: `src/MyApp.Application/GoodsReceipts/GoodsReceiptValidators.cs`
- Create: `src/MyApp.Application/GoodsReceipts/GoodsReceiptOptions.cs`
- Modify: `src/MyApp.Application/PurchaseOrders/IPurchaseOrderService.cs` (add `CloseAsync`)
- Test: `tests/MyApp.UnitTests/GoodsReceiptValidatorTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` from `MyApp.Application.Common`; `GoodsReceiptStatus` from `MyApp.Domain.Entities`.
- Produces (DTOs and signatures exactly as below). Later tasks (5,6,7,10–13) consume these.

- [ ] **Step 1: Create DTOs**

`src/MyApp.Application/GoodsReceipts/GoodsReceiptDtos.cs`:

```csharp
namespace MyApp.Application.GoodsReceipts;

public record GoodsReceiptLineDto(
    int Id, int PurchaseOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int QuantityReceived, decimal UnitCost, decimal LineCost);

public record GoodsReceiptDto(
    int Id, string GrnNumber, int PurchaseOrderId, string PoNumber,
    int SupplierId, string SupplierName, int WarehouseId, string WarehouseName,
    DateTime ReceiptDate, string? Notes, string Status,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<GoodsReceiptLineDto> Lines);

public record GoodsReceiptListItemDto(
    int Id, string GrnNumber, int PurchaseOrderId, string PoNumber, string SupplierName,
    DateTime ReceiptDate, string Status, int TotalQuantity);

public record ReceivablePoDto(
    int Id, string PoNumber, string SupplierName, DateTime OrderDate, string Status);

public record PoForReceiptLineDto(
    int PurchaseOrderLineId, int ProductVariantId, string VariantSku, string ProductName,
    int OrderedQuantity, int AlreadyReceivedQuantity, int RemainingQuantity, decimal DefaultUnitCost);

public record PoForReceiptDto(
    int PurchaseOrderId, string PoNumber, int SupplierId, string SupplierName,
    int WarehouseId, string WarehouseName, string Currency,
    IReadOnlyList<PoForReceiptLineDto> Lines);

public record GoodsReceiptLineRequest(int PurchaseOrderLineId, int QuantityReceived, decimal UnitCost);

public record CreateGoodsReceiptRequest(
    int PurchaseOrderId, DateTime ReceiptDate, string? Notes,
    IReadOnlyList<GoodsReceiptLineRequest> Lines);

public record UpdateGoodsReceiptRequest(
    DateTime ReceiptDate, string? Notes,
    IReadOnlyList<GoodsReceiptLineRequest> Lines);
```

- [ ] **Step 2: Create the options class**

`src/MyApp.Application/GoodsReceipts/GoodsReceiptOptions.cs`:

```csharp
namespace MyApp.Application.GoodsReceipts;

/// <summary>Konfigurasi GRN (section "GoodsReceipt" di appsettings).</summary>
public class GoodsReceiptOptions
{
    public int OverReceiptTolerancePercent { get; set; } = 10;
}
```

- [ ] **Step 3: Create the service interface**

`src/MyApp.Application/GoodsReceipts/IGoodsReceiptService.cs`:

```csharp
using MyApp.Application.Common;
using MyApp.Domain.Entities;

namespace MyApp.Application.GoodsReceipts;

public interface IGoodsReceiptService
{
    Task<PagedResult<GoodsReceiptListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, GoodsReceiptStatus? status = null, CancellationToken ct = default);
    Task<GoodsReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ReceivablePoDto>> GetReceivablePosAsync(CancellationToken ct = default);
    Task<PoForReceiptDto?> GetPoForReceiptAsync(int purchaseOrderId, CancellationToken ct = default);

    Task<GoodsReceiptDto> CreateDraftAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateGoodsReceiptRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task<bool> PostAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 4: Add `CloseAsync` to `IPurchaseOrderService`**

In `src/MyApp.Application/PurchaseOrders/IPurchaseOrderService.cs`, add after `CancelAsync`:

```csharp
    Task<bool> CloseAsync(int id, CancellationToken ct = default);
```

- [ ] **Step 5: Write failing validator tests**

Create `tests/MyApp.UnitTests/GoodsReceiptValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using MyApp.Application.GoodsReceipts;
using Xunit;

namespace MyApp.UnitTests;

public class GoodsReceiptValidatorTests
{
    private static GoodsReceiptLineRequest Line() => new(PurchaseOrderLineId: 7, QuantityReceived: 3, UnitCost: 900m);

    [Fact]
    public void Create_requires_po_date_and_lines()
    {
        var v = new CreateGoodsReceiptValidator();
        var bad = new CreateGoodsReceiptRequest(0, default, null, []);
        var r = v.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.PurchaseOrderId);
        r.ShouldHaveValidationErrorFor(x => x.ReceiptDate);
        r.ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Create_valid_passes()
    {
        var v = new CreateGoodsReceiptValidator();
        var ok = new CreateGoodsReceiptRequest(1, new DateTime(2026, 6, 29), "ok", [Line()]);
        v.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Line_rejects_bad_values()
    {
        var v = new GoodsReceiptLineRequestValidator();
        v.TestValidate(new GoodsReceiptLineRequest(0, 0, -1m)).ShouldHaveValidationErrorFor(x => x.PurchaseOrderLineId);
        v.TestValidate(new GoodsReceiptLineRequest(7, 0, 900m)).ShouldHaveValidationErrorFor(x => x.QuantityReceived);
        v.TestValidate(new GoodsReceiptLineRequest(7, 3, -1m)).ShouldHaveValidationErrorFor(x => x.UnitCost);
    }
}
```

> Note: `FluentValidation.TestHelper` is already referenced by the existing validator tests (`PurchaseOrderValidatorTests`). If the build complains, mirror the using/imports of that file.

- [ ] **Step 6: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~GoodsReceiptValidatorTests"`
Expected: FAIL — validators do not exist.

- [ ] **Step 7: Create validators**

`src/MyApp.Application/GoodsReceipts/GoodsReceiptValidators.cs`:

```csharp
using FluentValidation;

namespace MyApp.Application.GoodsReceipts;

public class GoodsReceiptLineRequestValidator : AbstractValidator<GoodsReceiptLineRequest>
{
    public GoodsReceiptLineRequestValidator()
    {
        RuleFor(x => x.PurchaseOrderLineId).GreaterThan(0);
        RuleFor(x => x.QuantityReceived).GreaterThan(0);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
    }
}

public class CreateGoodsReceiptValidator : AbstractValidator<CreateGoodsReceiptRequest>
{
    public CreateGoodsReceiptValidator()
    {
        RuleFor(x => x.PurchaseOrderId).GreaterThan(0);
        RuleFor(x => x.ReceiptDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new GoodsReceiptLineRequestValidator());
    }
}

public class UpdateGoodsReceiptValidator : AbstractValidator<UpdateGoodsReceiptRequest>
{
    public UpdateGoodsReceiptValidator()
    {
        RuleFor(x => x.ReceiptDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new GoodsReceiptLineRequestValidator());
    }
}
```

- [ ] **Step 8: Run — verify pass + build**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~GoodsReceiptValidatorTests"`
Expected: PASS.
Run: `dotnet build` — Expected: FAIL (deliberate) because `PurchaseOrderService` and any DI now need `CloseAsync` — that lands in Task 4/7. If the build fails ONLY on `IPurchaseOrderService.CloseAsync` not implemented by `PurchaseOrderService`, that is expected and resolved in Task 7. To keep this task self-contained green, implement the `PurchaseOrderService.CloseAsync` stub now as part of Step 9.

- [ ] **Step 9: Add `PurchaseOrderService.CloseAsync` (real impl — small)**

In `src/MyApp.Infrastructure/Services/PurchaseOrderService.cs`, add after `CancelAsync`:

```csharp
    public async Task<bool> CloseAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;
        po.Close();
        await db.SaveChangesAsync(ct);
        return true;
    }
```

- [ ] **Step 10: Run build + unit tests — verify green**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test tests/MyApp.UnitTests` — PASS.

---

### Task 4: Infrastructure — DbContext mapping

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Produces: `db.GoodsReceipts`, `db.GoodsReceiptLines` DbSets; mapping for the new entities + `PurchaseOrderLine.ReceivedQuantity` column.

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `PurchaseOrderLines` DbSet (line ~30):

```csharp
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
```

- [ ] **Step 2: Map the entities**

In `OnModelCreating`, after the `PurchaseOrderLine` entity block (line ~281):

```csharp
        modelBuilder.Entity<GoodsReceipt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GrnNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.GrnNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(GoodsReceipt.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<GoodsReceiptLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });
```

> `PurchaseOrderLine.ReceivedQuantity` (int, non-null) is mapped by convention (default 0) — no fluent config needed. The migration in Task 8 will add the column with default 0.

- [ ] **Step 3: Verify build**

Run: `dotnet build` — Expected: 0 warnings. (No new tests here; mapping is exercised by Task 5+ integration tests.)

---

### Task 5: Infrastructure — GoodsReceiptService (read + draft CRUD) + DI

**Files:**
- Create: `src/MyApp.Infrastructure/Services/GoodsReceiptService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Modify: `src/MyApp.Web/appsettings.json`
- Test: `tests/MyApp.IntegrationTests/GoodsReceiptServiceTests.cs`

**Interfaces:**
- Consumes: `IValidator<CreateGoodsReceiptRequest>`, `IValidator<UpdateGoodsReceiptRequest>`, `IOptions<GoodsReceiptOptions>`, `AppDbContext`.
- Produces: full `IGoodsReceiptService` impl. `PostAsync` body is added in Task 6 (here it throws `NotImplementedException` so the class compiles and draft tests run).

- [ ] **Step 1: Register DI + config**

In `src/MyApp.Infrastructure/DependencyInjection.cs`:
- add `using MyApp.Application.GoodsReceipts;` (with the other `using`s),
- after `services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();`:

```csharp
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.Configure<GoodsReceiptOptions>(configuration.GetSection("GoodsReceipt"));
```

In `src/MyApp.Web/appsettings.json`, add a top-level section (sibling of existing sections):

```json
  "GoodsReceipt": {
    "OverReceiptTolerancePercent": 10
  }
```

- [ ] **Step 2: Create the service with draft/read methods (Post stubbed)**

`src/MyApp.Infrastructure/Services/GoodsReceiptService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyApp.Application.Common;
using MyApp.Application.GoodsReceipts;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class GoodsReceiptService(
    AppDbContext db,
    IValidator<CreateGoodsReceiptRequest> createValidator,
    IValidator<UpdateGoodsReceiptRequest> updateValidator,
    IOptions<GoodsReceiptOptions> options) : IGoodsReceiptService
{
    private int Tolerance => Math.Max(0, options.Value.OverReceiptTolerancePercent);

    public async Task<PagedResult<GoodsReceiptListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, GoodsReceiptStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query =
            from g in db.GoodsReceipts.AsNoTracking()
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            select new { g, po };

        if (status is { } st) query = query.Where(x => x.g.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.g.GrnNumber.Contains(search) || x.po.PoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.g.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.g.Id, x.g.GrnNumber, x.g.PurchaseOrderId, x.po.PoNumber, x.po.SupplierId,
                x.g.ReceiptDate, x.g.Status,
                TotalQuantity = db.GoodsReceiptLines.Where(l => l.GoodsReceiptId == x.g.Id).Sum(l => (int?)l.QuantityReceived) ?? 0
            })
            .ToListAsync(ct);

        var supplierIds = rows.Select(r => r.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToListAsync(ct);

        var items = rows.Select(r => new GoodsReceiptListItemDto(
            r.Id, r.GrnNumber, r.PurchaseOrderId, r.PoNumber,
            suppliers.FirstOrDefault(s => s.Id == r.SupplierId)?.Name ?? "—",
            r.ReceiptDate, r.Status.ToString(), r.TotalQuantity)).ToList();

        return new PagedResult<GoodsReceiptListItemDto>(items, total, page, pageSize);
    }

    public async Task<GoodsReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var grn = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return null;

        var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct);
        var supplierName = po is null ? "—"
            : await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = po is null ? "—"
            : await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var poLineIds = grn.Lines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => poLineIds.Contains(l.Id)).Select(l => new { l.Id, l.Quantity }).ToListAsync(ct);

        var variantIds = grn.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = grn.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var ordered = poLines.FirstOrDefault(x => x.Id == l.PurchaseOrderLineId)?.Quantity ?? 0;
            return new GoodsReceiptLineDto(l.Id, l.PurchaseOrderLineId, l.ProductVariantId, v?.Sku ?? "—", pn,
                ordered, l.QuantityReceived, l.UnitCost,
                Math.Round(l.QuantityReceived * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        }).ToList();

        return new GoodsReceiptDto(grn.Id, grn.GrnNumber, grn.PurchaseOrderId, po?.PoNumber ?? "—",
            po?.SupplierId ?? 0, supplierName, po?.WarehouseId ?? 0, warehouseName,
            grn.ReceiptDate, grn.Notes, grn.Status.ToString(), grn.CreatedAt, grn.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<ReceivablePoDto>> GetReceivablePosAsync(CancellationToken ct = default)
    {
        var statuses = new[] { PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.PartiallyReceived };
        return await db.PurchaseOrders.AsNoTracking()
            .Where(p => statuses.Contains(p.Status))
            .OrderByDescending(p => p.Id)
            .Select(p => new ReceivablePoDto(
                p.Id, p.PoNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<PoForReceiptDto?> GetPoForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct);
        if (po is null || !po.CanReceive) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = po.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = po.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var remaining = Math.Max(0, l.Quantity - l.ReceivedQuantity);
            return new PoForReceiptLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.ReceivedQuantity, remaining, l.DefaultUnitCost);
        }).ToList();

        return new PoForReceiptDto(po.Id, po.PoNumber, po.SupplierId, supplierName,
            po.WarehouseId, warehouseName, po.Currency, lines);
    }

    public async Task<GoodsReceiptDto> CreateDraftAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");
        if (!po.CanReceive) throw Fail("Only a confirmed or partially-received purchase order can be received.");

        var grnLines = BuildLines(po, request.Lines);
        var grn = new GoodsReceipt(await GenerateNumberAsync(request.ReceiptDate, ct),
            po.Id, request.ReceiptDate, request.Notes);
        grn.SetLines(grnLines);

        db.GoodsReceipts.Add(grn);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(grn.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grn = await db.GoodsReceipts.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft) throw Fail("Only a draft goods receipt can be modified.");

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");

        var oldLines = await db.GoodsReceiptLines.Where(l => l.GoodsReceiptId == id).ToListAsync(ct);
        db.GoodsReceiptLines.RemoveRange(oldLines);

        grn.UpdateHeader(request.ReceiptDate, request.Notes);
        grn.SetLines(BuildLines(po, request.Lines));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var grn = await db.GoodsReceipts.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft) throw Fail("Only a draft goods receipt can be deleted.");
        db.GoodsReceipts.Remove(grn);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> PostAsync(int id, CancellationToken ct = default) =>
        throw new NotImplementedException("Implemented in Task 6.");

    /// <summary>Validasi tiap baris terhadap baris PO &amp; toleransi (vs qty yang sudah diposting), bangun entitas line.</summary>
    private List<GoodsReceiptLine> BuildLines(PurchaseOrder po, IReadOnlyList<GoodsReceiptLineRequest> requests)
    {
        var lines = new List<GoodsReceiptLine>();
        foreach (var r in requests)
        {
            var poLine = po.Lines.FirstOrDefault(l => l.Id == r.PurchaseOrderLineId)
                ?? throw Fail($"PO line {r.PurchaseOrderLineId} does not belong to PO {po.PoNumber}.");
            var maxAllowed = (int)Math.Floor(poLine.Quantity * (1 + Tolerance / 100m));
            var remaining = maxAllowed - poLine.ReceivedQuantity;
            if (r.QuantityReceived > remaining)
                throw Fail($"Receiving {r.QuantityReceived} for {poLine.ProductVariantId} exceeds the remaining allowed quantity ({remaining}).");
            lines.Add(new GoodsReceiptLine(poLine.Id, poLine.ProductVariantId, r.QuantityReceived, r.UnitCost));
        }
        return lines;
    }

    private async Task<string> GenerateNumberAsync(DateTime receiptDate, CancellationToken ct)
    {
        var prefix = $"GRN-{receiptDate:yyyyMM}-";
        var last = await db.GoodsReceipts.AsNoTracking()
            .Where(g => g.GrnNumber.StartsWith(prefix))
            .OrderByDescending(g => g.GrnNumber)
            .Select(g => g.GrnNumber).FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("GoodsReceipt", message)]);
}
```

- [ ] **Step 3: Write failing integration tests (draft + read)**

Create `tests/MyApp.IntegrationTests/GoodsReceiptServiceTests.cs`. The seed helper mirrors `PurchaseOrderServiceTests` (using the REAL ctors noted there) and drives a PO to `Confirmed` with an empty approval chain:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Approvals;
using MyApp.Application.GoodsReceipts;
using MyApp.Application.PurchaseOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class GoodsReceiptServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public GoodsReceiptServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var sup = new Supplier($"SP{id}", $"PT GRN {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(sup); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    // Creates a Confirmed PO (empty approval chain) with one line: qty 10 @ 1000, no discount/tax.
    private static async Task<PurchaseOrderDto> ConfirmedPoAsync(IServiceProvider sp, int sup, int wh, int variant)
    {
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var po = await poSvc.CreateAsync(new CreatePurchaseOrderRequest(
            sup, wh, new DateTime(2026, 6, 29), null, "po",
            [new PurchaseOrderLineRequest(variant, 10, 1000m, 0m, null)]));
        await poSvc.SubmitAsync(po.Id);
        return (await poSvc.GetByIdAsync(po.Id))!;
    }

    [Fact]
    public async Task GetPoForReceipt_returns_remaining_and_default_cost()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();

        var dto = await svc.GetPoForReceiptAsync(po.Id);
        Assert.NotNull(dto);
        var line = Assert.Single(dto!.Lines);
        Assert.Equal(10, line.OrderedQuantity);
        Assert.Equal(10, line.RemainingQuantity);
        Assert.Equal(1000m, line.DefaultUnitCost);
    }

    [Fact]
    public async Task CreateDraft_generates_number_and_does_not_move_stock()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poLineId = po.Lines[0].Id;

        var grn = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), "terima sebagian",
            [new GoodsReceiptLineRequest(poLineId, 4, 1000m)]));

        Assert.StartsWith("GRN-202606-", grn.GrnNumber);
        Assert.Equal("Draft", grn.Status);
        Assert.Single(grn.Lines);

        var stockSvc = sp.GetRequiredService<MyApp.Application.Stock.IStockService>();
        Assert.Equal(0, await stockSvc.GetOnHandAsync(variant, wh)); // draft hasn't posted
    }

    [Fact]
    public async Task CreateDraft_rejects_over_tolerance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poLineId = po.Lines[0].Id;

        // qty 10, tol 10% -> max 11; 12 must fail
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
                po.Id, new DateTime(2026, 6, 29), null,
                [new GoodsReceiptLineRequest(poLineId, 12, 1000m)])));
    }

    [Fact]
    public async Task DeleteDraft_removes_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));

        Assert.True(await svc.DeleteDraftAsync(grn.Id));
        Assert.Null(await svc.GetByIdAsync(grn.Id));
    }
}
```

- [ ] **Step 4: Run integration tests — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~GoodsReceiptServiceTests"`
Expected: PASS (4 tests). Then `dotnet build` — 0 warnings.

> If `CustomWebApplicationFactory` does not exist with this exact name/method, mirror `PurchaseOrderServiceTests`'s fixture usage in the same project (it is the canonical integration pattern).

---

### Task 6: Infrastructure — GoodsReceiptService.PostAsync (stock + HPP + PO status)

**Files:**
- Modify: `src/MyApp.Infrastructure/Services/GoodsReceiptService.cs` (replace `PostAsync` stub + add helper)
- Test: `tests/MyApp.IntegrationTests/GoodsReceiptServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `StockMovement` ctor `(productVariantId, warehouseId, MovementType, quantity, unitCost, movementDate, refType, refId, note)`; `MovementType.In`; `ProductStock(variantId, warehouseId, quantity)` + `.ApplyDelta(int)`; `ProductVariant.ApplyMovingAverage(int totalQtyBefore, int inQty, decimal inUnitCost)`; `PurchaseOrderLine.ApplyReceipt(int, int)` / `.IsFullyReceived`; `PurchaseOrder.MarkReceived()` / `.MarkPartiallyReceived()`.

- [ ] **Step 1: Add failing integration tests (post effects)**

Append to `GoodsReceiptServiceTests`:

```csharp
    [Fact]
    public async Task Post_partial_then_full_moves_stock_updates_hpp_and_po_status()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant); // qty 10
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var stockSvc = sp.GetRequiredService<MyApp.Application.Stock.IStockService>();
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();
        var db = sp.GetRequiredService<AppDbContext>();
        var poLineId = po.Lines[0].Id;

        // Receive 4 @ 1000 (variant starts with CostPrice 800, 0 on hand → MA = 1000)
        var g1 = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(poLineId, 4, 1000m)]));
        Assert.True(await svc.PostAsync(g1.Id));

        Assert.Equal("Posted", (await svc.GetByIdAsync(g1.Id))!.Status);
        Assert.Equal(4, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("PartiallyReceived", (await poSvc.GetByIdAsync(po.Id))!.Status);
        var v1 = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(1000m, v1!.CostPrice); // (0*800 + 4*1000)/4

        // Receive remaining 6 @ 1000 → fully received
        var g2 = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(poLineId, 6, 1000m)]));
        Assert.True(await svc.PostAsync(g2.Id));

        Assert.Equal(10, await stockSvc.GetOnHandAsync(variant, wh));
        Assert.Equal("Received", (await poSvc.GetByIdAsync(po.Id))!.Status);
    }

    [Fact]
    public async Task Post_writes_grn_stock_movement()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var stockSvc = sp.GetRequiredService<MyApp.Application.Stock.IStockService>();

        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 5, 950m)]));
        await svc.PostAsync(g.Id);

        var movements = await stockSvc.GetMovementsByVariantAsync(variant);
        Assert.Contains(movements, m => m.RefType == "GRN" && m.Quantity == 5);
    }

    [Fact]
    public async Task Post_uses_overridden_unit_cost_for_moving_average()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant); // PO line UnitPrice 1000
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var db = sp.GetRequiredService<AppDbContext>();

        // Override cost to 1200; 0 on hand → MA becomes 1200
        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 5, 1200m)]));
        await svc.PostAsync(g.Id);

        var v = await db.ProductVariants.FindAsync(variant);
        Assert.Equal(1200m, v!.CostPrice);
    }

    [Fact]
    public async Task Post_twice_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));
        await svc.PostAsync(g.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(g.Id));
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~GoodsReceiptServiceTests"`
Expected: FAIL — `PostAsync` throws `NotImplementedException`.

- [ ] **Step 3: Implement `PostAsync`**

In `GoodsReceiptService.cs`, replace the `PostAsync` stub with:

```csharp
    public async Task<bool> PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grn = await db.GoodsReceipts.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft) throw Fail("Only a draft goods receipt can be posted.");

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");
        if (!po.CanReceive) throw Fail("Only a confirmed or partially-received purchase order can be received.");

        foreach (var line in grn.Lines)
        {
            var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId)
                ?? throw Fail($"PO line {line.PurchaseOrderLineId} not found on PO {po.PoNumber}.");

            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Variant {line.ProductVariantId} not found.");

            var totalBefore = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.ProductVariantId)
                .SumAsync(s => (int?)s.Quantity, ct) ?? 0;

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, po.WarehouseId, MovementType.In,
                line.QuantityReceived, line.UnitCost, grn.ReceiptDate, refType: "GRN", refId: grn.Id,
                note: grn.GrnNumber));

            await UpsertStockAsync(line.ProductVariantId, po.WarehouseId, line.QuantityReceived, ct);
            variant.ApplyMovingAverage(totalBefore, line.QuantityReceived, line.UnitCost);
            poLine.ApplyReceipt(line.QuantityReceived, Tolerance);
        }

        if (po.Lines.All(l => l.IsFullyReceived)) po.MarkReceived();
        else po.MarkPartiallyReceived();

        grn.Post();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Buat/Update baris ProductStock (varian+gudang) dengan delta; tolak hasil negatif.</summary>
    private async Task UpsertStockAsync(int variantId, int warehouseId, int delta, CancellationToken ct)
    {
        var stock = await db.ProductStocks
            .FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct);
        if (stock is null) db.ProductStocks.Add(new ProductStock(variantId, warehouseId, delta));
        else stock.ApplyDelta(delta);
    }
```

- [ ] **Step 4: Run integration tests — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~GoodsReceiptServiceTests"`
Expected: PASS (all). Then `dotnet build` — 0 warnings.

---

### Task 7: Infrastructure — verify PurchaseOrderService.CloseAsync via integration test

> The method itself was added in Task 3 Step 9. This task adds its integration coverage.

**Files:**
- Test: `tests/MyApp.IntegrationTests/GoodsReceiptServiceTests.cs` (add case)

- [ ] **Step 1: Add failing test**

Append:

```csharp
    [Fact]
    public async Task ClosePo_locks_a_partially_received_po()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var po = await ConfirmedPoAsync(sp, sup, wh, variant);
        var svc = sp.GetRequiredService<IGoodsReceiptService>();
        var poSvc = sp.GetRequiredService<IPurchaseOrderService>();

        var g = await svc.CreateDraftAsync(new CreateGoodsReceiptRequest(
            po.Id, new DateTime(2026, 6, 29), null, [new GoodsReceiptLineRequest(po.Lines[0].Id, 4, 1000m)]));
        await svc.PostAsync(g.Id); // PO now PartiallyReceived

        Assert.True(await poSvc.CloseAsync(po.Id));
        Assert.Equal("Closed", (await poSvc.GetByIdAsync(po.Id))!.Status);

        // After close, PO is no longer receivable
        Assert.Null(await svc.GetPoForReceiptAsync(po.Id));
    }
```

- [ ] **Step 2: Run — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~GoodsReceiptServiceTests"`
Expected: PASS. Then `dotnet build` — 0 warnings.

---

### Task 8: EF migration + database update

**Files:**
- Create (generated): `src/MyApp.Infrastructure/Migrations/*_AddGoodsReceipt.cs`

- [ ] **Step 1: Generate the migration**

Run from solution root:

```bash
dotnet ef migrations add AddGoodsReceipt --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: a new migration file created. (If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.)

- [ ] **Step 2: Review the migration content**

Open the generated file and confirm `Up()`:
- creates table `GoodsReceipts` (`Id` PK identity; `GrnNumber` nvarchar(30) NOT NULL + UNIQUE index; `PurchaseOrderId` int FK→`PurchaseOrders` `Restrict`; `ReceiptDate`; `Notes` nvarchar(500); `Status` nvarchar(20) NOT NULL; audit columns `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy`),
- creates table `GoodsReceiptLines` (`Id` PK; `GoodsReceiptId` FK→`GoodsReceipts` `Cascade`; `PurchaseOrderLineId` FK→`PurchaseOrderLines` `Restrict`; `ProductVariantId` FK→`ProductVariants` `Restrict`; `QuantityReceived` int; `UnitCost` decimal(18,2)),
- adds column `ReceivedQuantity` int NOT NULL default 0 to `PurchaseOrderLines`.

Confirm `Down()` drops both tables and the `ReceivedQuantity` column, and nothing else.

- [ ] **Step 3: Apply to the dev database**

```bash
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: ends with `Done.`

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test` — all unit + integration green.

---

### Task 9: Web — authorization (AppMenus), nav, appsettings

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/appsettings.json` (already done in Task 5 — verify present)
- Modify: NavMenu (the file rendering `AppMenus.Groups`; confirm it is data-driven)

- [ ] **Step 1: Add the `post` and `close` actions + GRN resource**

In `AppMenus.cs`:
- after `ActApprove`:

```csharp
    public static readonly AppAction ActPost  = new("post",  "Post",  "bi-box-arrow-in-down");
    public static readonly AppAction ActClose = new("close", "Close", "bi-lock-fill");
```

- change `PurchaseOrderActions` to include close:

```csharp
    private static AppAction[] PurchaseOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActClose];
```

- add GRN actions helper:

```csharp
    private static AppAction[] GoodsReceiptActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
```

- in the `"Transaksi"` group, add the resource after `transactions.purchase-orders`:

```csharp
            new("transactions.goods-receipts", "Goods Receipt", "bi-box-seam", GoodsReceiptActions),
```

- [ ] **Step 2: Verify nav is data-driven & admin auto-grant**

The nav renders from `AppMenus.Groups`, and admin permissions come from `AppMenus.AllPermissions` (used by `BootstrapSeeder`). Confirm no per-page hardcoding is needed. If `NavMenu.razor` lists items manually rather than iterating `Groups`, add a `transactions.goods-receipts` entry mirroring the `purchase-orders` line.

- [ ] **Step 3: Build + run app once to confirm seeding**

Run: `dotnet build` — 0 warnings.
(Manual, optional now) On next app start, `BootstrapSeeder` grants the new permissions to admin via `AllPermissions`. No test here.

---

### Task 10: Web — GrnIndex page

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/GoodsReceipts/GrnIndex.razor`

> Use the sibling `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoIndex.razor` as the structural template (same `Pager`, `SwalService`, search box, table card, `@attribute [Authorize(Policy = ...)]`, status-filter dropdown). Reproduce its layout, swapping in GRN data.

- [ ] **Step 1: Create the page**

Key specifics (match PoIndex styling/structure exactly):
- `@page "/transactions/goods-receipts"`
- `@attribute [Authorize(Policy = "transactions.goods-receipts.index")]`
- `@inject IGoodsReceiptService Grn`, `@inject NavigationManager Nav`, `@inject SwalService Swal` (match PoIndex injects).
- Title "Goods Receipt" + button "Penerimaan Baru" → `Nav.NavigateTo("/transactions/goods-receipts/new")`, shown under `<AuthorizeView Policy="transactions.goods-receipts.create">`.
- Search input (bound, debounced like PoIndex) + status `<select>` over `GoodsReceiptStatus` values (All/Draft/Posted).
- Table columns: GRN#, PO#, Supplier, Tanggal (`ReceiptDate:dd MMM yyyy`), Status (badge — Draft=secondary, Posted=success), Qty (`TotalQuantity`), Aksi.
- Row click / "Lihat" → `/transactions/goods-receipts/{id}`. Delete button only when `Status == "Draft"`, under `<AuthorizeView Policy="transactions.goods-receipts.delete">`, calling `Swal.ConfirmAsync(...)` then `Grn.DeleteDraftAsync(id)` then reload (mirror PoIndex delete).
- `Pager` with `pageSize = 15`; data via `Grn.GetPagedAsync(page, 15, search, statusFilter)`.

`@code` data-loading block:

```csharp
@code {
    private PagedResult<GoodsReceiptListItemDto>? _data;
    private int _page = 1;
    private string? _search;
    private GoodsReceiptStatus? _status;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _data = await Grn.GetPagedAsync(_page, 15, _search, _status);
    }

    private async Task OnSearch(string? term) { _search = term; _page = 1; await LoadAsync(); }
    private async Task OnStatus(GoodsReceiptStatus? s) { _status = s; _page = 1; await LoadAsync(); }
    private async Task OnPage(int p) { _page = p; await LoadAsync(); }

    private async Task DeleteAsync(int id)
    {
        if (!await Swal.ConfirmAsync("Hapus penerimaan draft ini?")) return;
        await Grn.DeleteDraftAsync(id);
        await LoadAsync();
    }
}
```

Add the matching `@using MyApp.Application.GoodsReceipts`, `@using MyApp.Application.Common`, `@using MyApp.Domain.Entities`, `@using Microsoft.AspNetCore.Authorization` (mirror PoIndex usings, or rely on `_Imports.razor`).

- [ ] **Step 2: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 11: Web — GrnForm page (create/edit draft)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/GoodsReceipts/GrnForm.razor`

> Use `PoForm.razor` as the structural template (`fs-card`, inline validation `_xxxError`, save spinner, `ValidationException` handling).

- [ ] **Step 1: Create the page**

Specifics:
- Routes: `@page "/transactions/goods-receipts/new"` and `@page "/transactions/goods-receipts/{Id:int}/edit"`.
- `@attribute [Authorize(Policy = "transactions.goods-receipts.create")]` (the edit route reuses create policy; acceptable for B2).
- Accept optional `?poId=` query param on the new route (`[SupplyParameterFromQuery] public int? PoId { get; set; }`).
- Injects: `IGoodsReceiptService Grn`, `NavigationManager Nav` (+ `SwalService` if you surface errors via toast like PoForm).
- **Create flow:**
  - Load `await Grn.GetReceivablePosAsync()` for the PO `<select>`. If `PoId` set, preselect & load its lines.
  - On PO change → `await Grn.GetPoForReceiptAsync(poId)`; build an editable row per line seeded with `QuantityReceived = RemainingQuantity`, `UnitCost = DefaultUnitCost`. Show Product, SKU, Ordered, Already received (read-only) and editable qty + unit cost.
  - `ReceiptDate` defaults to `DateTime.Today`. `Notes` textarea (≤500).
  - Save → build `CreateGoodsReceiptRequest` (only include lines with `QuantityReceived > 0`) → `Grn.CreateDraftAsync(...)` → navigate to `/transactions/goods-receipts/{newId}`.
- **Edit flow:** `Id` set → `await Grn.GetByIdAsync(Id)`; reject if `Status != "Draft"` (navigate to detail). Map lines into the editable rows (qty/cost editable; PO fixed). Save → `Grn.UpdateDraftAsync(Id, new UpdateGoodsReceiptRequest(...))` → navigate to detail.
- Wrap save in try/catch on `FluentValidation.ValidationException`; render `ex.Errors` inline / as alert (mirror PoForm error handling). Disable Save with spinner while in-flight.

Editable row view-model:

```csharp
@code {
    [Parameter] public int? Id { get; set; }
    [SupplyParameterFromQuery] public int? PoId { get; set; }

    private List<ReceivablePoDto> _pos = new();
    private int _selectedPoId;
    private DateTime _receiptDate = DateTime.Today;
    private string? _notes;
    private List<Row> _rows = new();
    private bool _saving;
    private string? _error;

    private sealed class Row
    {
        public int PurchaseOrderLineId { get; set; }
        public string ProductName { get; set; } = "";
        public string Sku { get; set; } = "";
        public int Ordered { get; set; }
        public int AlreadyReceived { get; set; }
        public int Qty { get; set; }
        public decimal UnitCost { get; set; }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Id is int gid) { await LoadForEditAsync(gid); return; }
        _pos = (await Grn.GetReceivablePosAsync()).ToList();
        if (PoId is int p) { _selectedPoId = p; await LoadPoLinesAsync(p); }
    }

    private async Task LoadPoLinesAsync(int poId)
    {
        var po = await Grn.GetPoForReceiptAsync(poId);
        _rows = po is null ? new() : po.Lines.Select(l => new Row {
            PurchaseOrderLineId = l.PurchaseOrderLineId, ProductName = l.ProductName, Sku = l.VariantSku,
            Ordered = l.OrderedQuantity, AlreadyReceived = l.AlreadyReceivedQuantity,
            Qty = l.RemainingQuantity, UnitCost = l.DefaultUnitCost
        }).ToList();
    }

    private async Task LoadForEditAsync(int gid)
    {
        var g = await Grn.GetByIdAsync(gid);
        if (g is null || g.Status != "Draft") { Nav.NavigateTo($"/transactions/goods-receipts/{gid}"); return; }
        _selectedPoId = g.PurchaseOrderId; _receiptDate = g.ReceiptDate; _notes = g.Notes;
        _rows = g.Lines.Select(l => new Row {
            PurchaseOrderLineId = l.PurchaseOrderLineId, ProductName = l.ProductName, Sku = l.VariantSku,
            Ordered = l.OrderedQuantity, AlreadyReceived = 0, Qty = l.QuantityReceived, UnitCost = l.UnitCost
        }).ToList();
    }

    private async Task OnPoChanged(ChangeEventArgs e)
    {
        _selectedPoId = int.TryParse(e.Value?.ToString(), out var v) ? v : 0;
        if (_selectedPoId > 0) await LoadPoLinesAsync(_selectedPoId); else _rows = new();
    }

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            var lines = _rows.Where(r => r.Qty > 0)
                .Select(r => new GoodsReceiptLineRequest(r.PurchaseOrderLineId, r.Qty, r.UnitCost)).ToList();
            if (Id is int gid)
            {
                await Grn.UpdateDraftAsync(gid, new UpdateGoodsReceiptRequest(_receiptDate, _notes, lines));
                Nav.NavigateTo($"/transactions/goods-receipts/{gid}");
            }
            else
            {
                var created = await Grn.CreateDraftAsync(new CreateGoodsReceiptRequest(_selectedPoId, _receiptDate, _notes, lines));
                Nav.NavigateTo($"/transactions/goods-receipts/{created.Id}");
            }
        }
        catch (FluentValidation.ValidationException ex)
        {
            _error = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
        }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 12: Web — GrnDetail page (view + post)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/GoodsReceipts/GrnDetail.razor`

> Use `PoDetail.razor` as the structural template (header card, lines table, action buttons + `SwalService` confirm).

- [ ] **Step 1: Create the page**

Specifics:
- `@page "/transactions/goods-receipts/{Id:int}"`, `@attribute [Authorize(Policy = "transactions.goods-receipts.index")]`.
- Injects: `IGoodsReceiptService Grn`, `NavigationManager Nav`, `SwalService Swal`.
- Header: GRN#, status badge, PO# (link to `/transactions/purchase-orders/{PurchaseOrderId}`), Supplier, Warehouse, ReceiptDate, Notes, CreatedAt/CreatedBy.
- Lines table: Produk, SKU, Dipesan (`OrderedQuantity`), Diterima (`QuantityReceived`), HPP/unit (`UnitCost`), Subtotal (`LineCost`).
- When `Status == "Draft"`: show **Edit** button → `/transactions/goods-receipts/{Id}/edit` (under `transactions.goods-receipts.edit`) and **Post** button (under `transactions.goods-receipts.post`) → confirm via `Swal` then `Grn.PostAsync(Id)`, reload; on `ValidationException`/`InvalidOperationException` show the message.
- When `Status == "Posted"`: read-only, no action buttons.

`@code`:

```csharp
@code {
    [Parameter] public int Id { get; set; }
    private GoodsReceiptDto? _grn;
    private bool _posting;
    private string? _error;

    protected override Task OnParametersSetAsync() => LoadAsync();
    private async Task LoadAsync() => _grn = await Grn.GetByIdAsync(Id);

    private async Task PostAsync()
    {
        if (!await Swal.ConfirmAsync("Posting penerimaan ini? Stok & HPP akan diperbarui dan tidak bisa dibatalkan.")) return;
        _error = null; _posting = true;
        try { await Grn.PostAsync(Id); await LoadAsync(); }
        catch (FluentValidation.ValidationException ex) { _error = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)); }
        catch (InvalidOperationException ex) { _error = ex.Message; }
        finally { _posting = false; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 13: Web — PoDetail receive progress + Buat GRN + Tutup PO

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoDetail.razor`

- [ ] **Step 1: Add received-progress column**

In the PO lines table, add a "Diterima" column showing `line.ReceivedQuantity / line.Quantity`.

> Requires `PurchaseOrderLineDto` to expose `ReceivedQuantity`. Add it: append `int ReceivedQuantity` to the `PurchaseOrderLineDto` record (`src/MyApp.Application/PurchaseOrders/PurchaseOrderDtos.cs`) and set it in `PurchaseOrderService.GetByIdAsync` line projection (`l.ReceivedQuantity`). Build the Application + Infrastructure projects after this change. (Existing PO tests still pass — record gets one extra positional field; update any construction sites that use the positional ctor — there is one, in `GetByIdAsync`.)

- [ ] **Step 2: Add action buttons**

In the PO detail action bar:
- **Buat GRN** button when `Status` is `Confirmed` or `PartiallyReceived`, under `<AuthorizeView Policy="transactions.goods-receipts.create">` → `Nav.NavigateTo($"/transactions/goods-receipts/new?poId={Id}")`.
- **Tutup PO** button when `Status == "PartiallyReceived"`, under `<AuthorizeView Policy="transactions.purchase-orders.close">` → `Swal.ConfirmAsync("Tutup PO ini? Sisa qty tidak akan bisa diterima lagi.")` then `Po.CloseAsync(Id)` then reload.

Inject `IPurchaseOrderService Po` if not already present; the close handler:

```csharp
    private async Task ClosePoAsync()
    {
        if (!await Swal.ConfirmAsync("Tutup PO ini? Sisa qty tidak akan bisa diterima lagi.")) return;
        await Po.CloseAsync(Id);
        await LoadAsync(); // existing reload method in PoDetail
    }
```

- [ ] **Step 3: Build + full test suite**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test` — all green.

---

### Task 14: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build` — Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: all unit + integration tests pass (the B1 baseline 101 + the new B2 unit & integration tests).

- [ ] **Step 3: Confirm no stock movement on draft / immutability**

Re-read `GoodsReceiptService` to confirm: `CreateDraftAsync`/`UpdateDraftAsync` never touch `StockMovements`/`ProductStocks`/`ApplyMovingAverage`; only `PostAsync` does, inside a transaction; `PostAsync` rejects a non-Draft GRN.

- [ ] **Step 4: Manual UI walkthrough (hand to user)**

Hand to the user (cannot be automated):
1. PO at `Confirmed` → open PO detail → **Buat GRN** → receive partial qty → **Post** → PO shows `PartiallyReceived`, stock & HPP updated.
2. Receive remaining → PO shows `Received`.
3. On a partially-received PO → **Tutup PO** → status `Closed`, no longer receivable.
4. Over-receipt beyond +10% is rejected with a clear message.

## Self-Review (done by plan author)

- **Spec coverage:** §1 Domain → Tasks 1–2; §2 Application → Task 3; §3 Infrastructure (DbContext/service/DI/appsettings/migration) → Tasks 4–8; §4 Web → Tasks 10–13; §5 Auth → Task 9; §6 Testing → embedded per task + Task 14. Close PO → Tasks 3/7/13. ✓
- **Type consistency:** DTO/record names and service signatures defined in Task 3 are reused verbatim in Tasks 5,6,10–13. `ApplyMovingAverage(totalBefore, qty, unitCost)`, `StockMovement(...)`, `ProductStock(...)`, `ApplyDelta`, `ApplyReceipt(qty, tol)` match the real signatures read from source. ✓
- **Placeholder scan:** Razor tasks reference sibling pages as templates (repo convention) but specify routes, policies, injects, columns, and full `@code` logic — no "TBD"/"add validation" placeholders. `PurchaseOrderLineDto.ReceivedQuantity` addition (needed by Task 13) is called out explicitly in Task 13 Step 1. ✓
- **Note:** Task 13 mutates `PurchaseOrderLineDto`; the only positional construction site is `PurchaseOrderService.GetByIdAsync`, updated in the same step.
