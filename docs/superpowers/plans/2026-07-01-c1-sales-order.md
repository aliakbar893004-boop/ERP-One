# C1 — Sales Order Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Sales Order module (Draft → approval → Confirmed) as a near-verbatim mirror of the existing Purchase Order (B1): Supplier→Customer, buy price→sell price, warehouse = SOURCE. Reuse the B1 approval engine verbatim via `ApprovalDocumentType.SalesOrder`. Add a soft credit-limit warning. **No stock movement** in C1 (that is C2, Delivery Order).

**Architecture:** Clean Architecture, domain-rich entities (`SalesOrder`/`SalesOrderLine`) with a `SalesOrderService` orchestrating create/update/submit/approve/reject/cancel inside a transaction, mirroring `PurchaseOrderService`. Blazor Server UI mirrors the existing PO pages plus a non-blocking credit-limit banner.

**Tech Stack:** .NET 10 / C#, EF Core 10, Blazor Server + Bootstrap 5 + Bootstrap Icons, FluentValidation, xUnit. SQL Server (app) / SQLite (integration tests via `CustomWebApplicationFactory`).

**Spec:** `docs/superpowers/specs/2026-07-01-c1-sales-order-design.md`

## Global Constraints

- **No git** — implementers do NOT commit. Each task ends by verifying `dotnet build` (0 warnings) + the relevant `dotnet test` filter is green. Reviewers read changed files directly.
- **Build/test from solution root** `F:\4. My Data\Project\MyApplication`.
- **Enums stored as string** via `.HasConversion<string>().HasMaxLength(20)`.
- **Decimals** `(18,2)` for money, `(5,2)` for percent, via `.HasPrecision`.
- **Rounding** `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Entities** derive from `AuditableEntity` where audited, private setters, EF ctor `private Xxx() { }`, child collections via backing `List<>` + `PropertyAccessMode.Field`.
- **Validation errors** thrown as `FluentValidation.ValidationException`.
- **C1 mirrors B1 exactly.** Where this plan says "mirror the PO X", it means the same structure with the renames Supplier→Customer, PO→SO, buy→sell. Line math is IDENTICAL to `PurchaseOrderLine` (tax exclusive).
- **Approval engine is reused unchanged.** No edits to anything under `Approvals/`. `ApprovalDocumentType.SalesOrder` already exists.
- **Credit limit is a soft warning only.** It never gates a transition.

---

### Task 1: Domain — SalesOrderStatus + SalesOrderLine + SalesOrder + unit tests

**Files:**
- Create: `src/MyApp.Domain/Entities/SalesOrderStatus.cs`
- Create: `src/MyApp.Domain/Entities/SalesOrderLine.cs`
- Create: `src/MyApp.Domain/Entities/SalesOrder.cs`
- Test: `tests/MyApp.UnitTests/SalesOrderLineTests.cs`
- Test: `tests/MyApp.UnitTests/SalesOrderTests.cs`

**Interfaces:**
- Produces:
  - `enum SalesOrderStatus { Draft, PendingApproval, Confirmed, Rejected, Cancelled }`
  - `SalesOrderLine(int productVariantId, int quantity, decimal unitPrice, decimal discountPercent, int? taxId, decimal taxRateSnapshot)` with props `Id, SalesOrderId, ProductVariantId, Quantity, UnitPrice, DiscountPercent, TaxId, TaxRateSnapshot, LineSubtotal, LineDiscount, LineTax, LineTotal`
  - `SalesOrder(string soNumber, int customerId, int warehouseId, DateTime orderDate, DateTime? expectedDate, string? currency, string? notes)` with props `Id, SoNumber, CustomerId, WarehouseId, OrderDate, ExpectedDate, Currency, Notes, Status, RejectionNote, Subtotal, DiscountTotal, TaxTotal, GrandTotal, IReadOnlyCollection<SalesOrderLine> Lines`; methods `UpdateHeader(...)`, `SetLines(...)`, `Submit()`, `MarkConfirmed()`, `ReturnToDraft(reason)`, `Cancel()`

- [ ] **Step 1: Write failing tests for `SalesOrderLine`**

Create `tests/MyApp.UnitTests/SalesOrderLineTests.cs` (mirror `PurchaseOrderLineTests`, same math):

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class SalesOrderLineTests
{
    [Fact]
    public void Computes_amounts_with_discount_and_tax()
    {
        // 10 x 1000 = 10000; diskon 10% = 1000; setelah diskon 9000; pajak 11% = 990; total 9990
        var l = new SalesOrderLine(5, 10, 1000m, 10m, taxId: 1, taxRateSnapshot: 11m);
        Assert.Equal(10000m, l.LineSubtotal);
        Assert.Equal(1000m, l.LineDiscount);
        Assert.Equal(990m, l.LineTax);
        Assert.Equal(9990m, l.LineTotal);
    }

    [Fact]
    public void No_tax_when_taxId_null_even_if_rate_passed()
    {
        var l = new SalesOrderLine(5, 2, 500m, 0m, taxId: null, taxRateSnapshot: 11m);
        Assert.Equal(0m, l.TaxRateSnapshot);
        Assert.Equal(0m, l.LineTax);
        Assert.Equal(1000m, l.LineTotal);
    }

    [Fact]
    public void Rejects_non_positive_quantity() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 0, 100m, 0m, null, 0m));

    [Fact]
    public void Rejects_negative_price() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 1, -1m, 0m, null, 0m));

    [Fact]
    public void Rejects_discount_out_of_range() =>
        Assert.Throws<ArgumentException>(() => new SalesOrderLine(5, 1, 100m, 150m, null, 0m));
}
```

- [ ] **Step 2: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderLineTests"`
Expected: FAIL — type `SalesOrderLine` does not exist.

- [ ] **Step 3: Create `SalesOrderStatus`**

`src/MyApp.Domain/Entities/SalesOrderStatus.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup Sales Order (C1; status pengiriman ditambahkan di C2).</summary>
public enum SalesOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled
}
```

> `PartiallyDelivered`, `Delivered`, `Closed` are **added in C2** (analog to how B2 added receipt statuses to `PurchaseOrderStatus`). Do not add them here.

- [ ] **Step 4: Create `SalesOrderLine`** (identical math to `PurchaseOrderLine`)

`src/MyApp.Domain/Entities/SalesOrderLine.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Baris item pada Sales Order. Amount dihitung di domain (pajak exclusive).</summary>
public class SalesOrderLine
{
    public int Id { get; private set; }
    public int SalesOrderId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public int? TaxId { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private SalesOrderLine() { } // EF Core

    public SalesOrderLine(int productVariantId, int quantity, decimal unitPrice,
        decimal discountPercent, int? taxId, decimal taxRateSnapshot)
    {
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxId = taxId;
        TaxRateSnapshot = taxId is null ? 0m : taxRateSnapshot;
        Recompute();
    }

    private void Recompute()
    {
        LineSubtotal = Round(Quantity * UnitPrice);
        LineDiscount = Round(LineSubtotal * DiscountPercent / 100m);
        LineTax = Round((LineSubtotal - LineDiscount) * TaxRateSnapshot / 100m);
        LineTotal = LineSubtotal - LineDiscount + LineTax;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 5: Run — verify `SalesOrderLineTests` pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderLineTests"`
Expected: PASS (all 5).

- [ ] **Step 6: Write failing tests for `SalesOrder`**

Create `tests/MyApp.UnitTests/SalesOrderTests.cs` (mirror `PurchaseOrderTests`, minus receipt cases):

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class SalesOrderTests
{
    private static SalesOrder Make() =>
        new("SO-202607-0001", customerId: 1, warehouseId: 2,
            orderDate: new DateTime(2026, 7, 1), expectedDate: null, currency: "idr", notes: null);

    private static SalesOrderLine Line() => new(5, 10, 1000m, 0m, null, 0m);

    [Fact]
    public void New_so_is_draft_and_normalizes_currency()
    {
        var so = Make();
        Assert.Equal(SalesOrderStatus.Draft, so.Status);
        Assert.Equal("IDR", so.Currency);
    }

    [Fact]
    public void SetLines_recomputes_totals()
    {
        var so = Make();
        so.SetLines([Line(), Line()]);
        Assert.Equal(20000m, so.Subtotal);
        Assert.Equal(20000m, so.GrandTotal);
        Assert.Equal(2, so.Lines.Count);
    }

    [Fact]
    public void Submit_requires_lines()
    {
        var so = Make();
        Assert.Throws<InvalidOperationException>(() => so.Submit());
        so.SetLines([Line()]);
        so.Submit();
        Assert.Equal(SalesOrderStatus.PendingApproval, so.Status);
    }

    [Fact]
    public void Cannot_edit_lines_unless_draft()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        Assert.Throws<InvalidOperationException>(() => so.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() =>
            so.UpdateHeader(1, 2, DateTime.Today, null, "IDR", null));
    }

    [Fact]
    public void Confirm_only_from_pending()
    {
        var so = Make();
        Assert.Throws<InvalidOperationException>(() => so.MarkConfirmed());
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        Assert.Equal(SalesOrderStatus.Confirmed, so.Status);
    }

    [Fact]
    public void ReturnToDraft_stores_reason()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.ReturnToDraft("stok tidak cukup");
        Assert.Equal(SalesOrderStatus.Draft, so.Status);
        Assert.Equal("stok tidak cukup", so.RejectionNote);
    }

    [Fact]
    public void Cancel_allowed_from_draft_and_pending_only()
    {
        var so = Make();
        so.SetLines([Line()]);
        so.Submit();
        so.MarkConfirmed();
        Assert.Throws<InvalidOperationException>(() => so.Cancel()); // confirmed tak bisa cancel di C1

        var so2 = Make();
        so2.Cancel();
        Assert.Equal(SalesOrderStatus.Cancelled, so2.Status);
    }

    [Fact]
    public void ExpectedDate_must_be_on_or_after_order_date() =>
        Assert.Throws<ArgumentException>(() =>
            new SalesOrder("SO-1", 1, 2, new DateTime(2026, 7, 1),
                expectedDate: new DateTime(2026, 6, 1), currency: "IDR", notes: null));
}
```

- [ ] **Step 7: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderTests"`
Expected: FAIL — type `SalesOrder` does not exist.

- [ ] **Step 8: Create `SalesOrder`** (mirror `PurchaseOrder`, no receipt/close transitions)

`src/MyApp.Domain/Entities/SalesOrder.cs`:

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pesanan penjualan ke customer. Baris hanya bisa diubah saat Draft. Gudang = sumber pengiriman.</summary>
public class SalesOrder : AuditableEntity
{
    private readonly List<SalesOrderLine> _lines = [];

    public int Id { get; private set; }
    public string SoNumber { get; private set; } = default!;
    public int CustomerId { get; private set; }
    public int WarehouseId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public string? Notes { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;

    private SalesOrder() { } // EF Core

    public SalesOrder(string soNumber, int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (string.IsNullOrWhiteSpace(soNumber))
            throw new ArgumentException("SoNumber is required.", nameof(soNumber));
        SoNumber = soNumber.Trim();
        SetHeader(customerId, warehouseId, orderDate, expectedDate, currency, notes);
        Status = SalesOrderStatus.Draft;
    }

    public void UpdateHeader(int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        EnsureDraft();
        SetHeader(customerId, warehouseId, orderDate, expectedDate, currency, notes);
    }

    private void SetHeader(int customerId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (customerId <= 0) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (expectedDate is { } ed && ed.Date < orderDate.Date)
            throw new ArgumentException("ExpectedDate cannot be before OrderDate.", nameof(expectedDate));

        CustomerId = customerId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<SalesOrderLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
        RecomputeTotals();
    }

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot submit a sales order without lines.");
        Status = SalesOrderStatus.PendingApproval;
    }

    public void MarkConfirmed()
    {
        if (Status != SalesOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending sales order can be confirmed.");
        Status = SalesOrderStatus.Confirmed;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != SalesOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending sales order can be returned to draft.");
        Status = SalesOrderStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (SalesOrderStatus.Draft or SalesOrderStatus.PendingApproval))
            throw new InvalidOperationException("Only draft or pending sales orders can be cancelled.");
        Status = SalesOrderStatus.Cancelled;
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft sales order can be modified.");
    }
}
```

- [ ] **Step 9: Run all unit tests — verify green**

Run: `dotnet test tests/MyApp.UnitTests`
Expected: PASS (all existing + new). Then `dotnet build` — 0 warnings.

---

### Task 2: Application — DTOs, service interface, validators + validator unit tests

**Files:**
- Create: `src/MyApp.Application/SalesOrders/SalesOrderDtos.cs`
- Create: `src/MyApp.Application/SalesOrders/ISalesOrderService.cs`
- Create: `src/MyApp.Application/SalesOrders/SalesOrderValidators.cs`
- Test: `tests/MyApp.UnitTests/SalesOrderValidatorTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` from `MyApp.Application.Common`; `ApprovalStepDto` from `MyApp.Application.Approvals`; `SalesOrderStatus` from `MyApp.Domain.Entities`.
- Produces the DTOs and service signatures exactly as below. Tasks 4 and 7–9 consume these verbatim.

> **Signature note (resolved against the real `IPurchaseOrderService`):** the PO service exposes `SubmitAsync`/`CancelAsync` returning `Task` (not `Task<bool>`), `ApproveAsync(int id, string actingUserName, Func<string,bool> isInRole, CancellationToken)` and `RejectAsync(int id, string actingUserName, Func<string,bool> isInRole, string reason, CancellationToken)`. The SO service mirrors these exact shapes (the spec's looser sketch is superseded by the real code for consistency). It also adds `SearchVariantsAsync` and `GetDashboardAsync` mirrors, plus `GetCreditInfoAsync`.

- [ ] **Step 1: Create DTOs**

`src/MyApp.Application/SalesOrders/SalesOrderDtos.cs`:

```csharp
namespace MyApp.Application.SalesOrders;

public record SalesOrderLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal);

public record SalesOrderDto(
    int Id, string SoNumber, int CustomerId, string CustomerName, int WarehouseId, string WarehouseName,
    DateTime OrderDate, DateTime? ExpectedDate, string Currency, string? Notes,
    string Status, string? RejectionNote,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<SalesOrderLineDto> Lines);

public record SalesOrderListItemDto(
    int Id, string SoNumber, string CustomerName, DateTime OrderDate,
    string Currency, decimal GrandTotal, string Status);

public record SalesOrderDashboardDto(
    int TotalCount, int DraftCount, int PendingApprovalCount, int ConfirmedCount);

public record SalesOrderVariantOptionDto(
    int VariantId, string Sku, string ProductName, decimal Price, decimal? DiscountPrice);

public record SalesOrderCreditInfoDto(
    decimal CreditLimit, decimal EstimatedOutstanding, decimal ThisOrderTotal, bool ExceedsLimit);

public record SalesOrderLineRequest(
    int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId);

public record CreateSalesOrderRequest(
    int CustomerId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<SalesOrderLineRequest> Lines);

public record UpdateSalesOrderRequest(
    int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<SalesOrderLineRequest> Lines);
```

> `UpdateSalesOrderRequest` intentionally omits `CustomerId` (the customer — and its currency snapshot — is fixed at create time, matching how PO update keeps `po.Currency`). `SalesOrderVariantOptionDto` carries `Price` + `DiscountPrice` so the form can default `UnitPrice = DiscountPrice ?? Price`.

- [ ] **Step 2: Create the service interface**

`src/MyApp.Application/SalesOrders/ISalesOrderService.cs`:

```csharp
using MyApp.Application.Approvals;
using MyApp.Application.Common;
using MyApp.Domain.Entities;

namespace MyApp.Application.SalesOrders;

public interface ISalesOrderService
{
    Task<PagedResult<SalesOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SalesOrderStatus? status = null, CancellationToken ct = default);
    Task<SalesOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SalesOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SalesOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default);
    Task<SalesOrderCreditInfoDto> GetCreditInfoAsync(int customerId, decimal thisOrderTotal, int? excludeSoId, CancellationToken ct = default);

    Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing validator tests**

Create `tests/MyApp.UnitTests/SalesOrderValidatorTests.cs` (mirror `PurchaseOrderValidatorTests`):

```csharp
using FluentValidation.TestHelper;
using MyApp.Application.SalesOrders;
using Xunit;

namespace MyApp.UnitTests;

public class SalesOrderValidatorTests
{
    private readonly CreateSalesOrderValidator _v = new();

    private static CreateSalesOrderRequest Valid() =>
        new(CustomerId: 1, WarehouseId: 2, OrderDate: new DateTime(2026, 7, 1),
            ExpectedDate: null, Notes: null,
            Lines: [new SalesOrderLineRequest(5, 10, 1000m, 0m, null)]);

    [Fact]
    public void Valid_passes() => _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Requires_customer() =>
        _v.TestValidate(Valid() with { CustomerId = 0 }).ShouldHaveValidationErrorFor(x => x.CustomerId);

    [Fact]
    public void Requires_warehouse() =>
        _v.TestValidate(Valid() with { WarehouseId = 0 }).ShouldHaveValidationErrorFor(x => x.WarehouseId);

    [Fact]
    public void Requires_at_least_one_line() =>
        _v.TestValidate(Valid() with { Lines = [] }).ShouldHaveValidationErrorFor(x => x.Lines);

    [Fact]
    public void Line_quantity_must_be_positive() =>
        _v.TestValidate(Valid() with { Lines = [new SalesOrderLineRequest(5, 0, 1000m, 0m, null)] })
          .ShouldHaveValidationErrorFor("Lines[0].Quantity");

    [Fact]
    public void Expected_before_order_fails() =>
        _v.TestValidate(Valid() with { ExpectedDate = new DateTime(2026, 6, 1) })
          .ShouldHaveValidationErrorFor(x => x.ExpectedDate);
}
```

- [ ] **Step 4: Run — verify fail to compile**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderValidatorTests"`
Expected: FAIL — validators do not exist.

- [ ] **Step 5: Create validators**

`src/MyApp.Application/SalesOrders/SalesOrderValidators.cs`:

```csharp
using FluentValidation;

namespace MyApp.Application.SalesOrders;

public class SalesOrderLineRequestValidator : AbstractValidator<SalesOrderLineRequest>
{
    public SalesOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new SalesOrderLineRequestValidator());
    }
}

public class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderRequest>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new SalesOrderLineRequestValidator());
    }
}
```

- [ ] **Step 6: Run — verify pass**

Run: `dotnet test tests/MyApp.UnitTests --filter "FullyQualifiedName~SalesOrderValidatorTests"`
Expected: PASS (all 6).
Run: `dotnet build` — Expected: FAIL is acceptable ONLY if it is because nothing yet implements `ISalesOrderService` — but no class references it yet, so the build should be **0 warnings, 0 errors** (the interface is not yet consumed anywhere). Confirm the build is clean.

---

### Task 3: Infrastructure — DbContext DbSets + mapping

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Produces: `db.SalesOrders`, `db.SalesOrderLines` DbSets; fluent mapping for the new entities (mirror the PurchaseOrder/PurchaseOrderLine blocks).

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `PurchaseOrderLines` DbSet (line ~30):

```csharp
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
```

- [ ] **Step 2: Map the entities**

In `OnModelCreating`, after the `PurchaseOrderLine` entity block (line ~283, i.e. immediately before the `GoodsReceipt` block):

```csharp
        modelBuilder.Entity<SalesOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.SoNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);

            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.SalesOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SalesOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SalesOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tax>().WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.SetNull);
        });
```

- [ ] **Step 3: Verify build**

Run: `dotnet build` — Expected: 0 warnings. (No new tests here; mapping is exercised by Task 4 integration tests.)

---

### Task 4: Infrastructure — SalesOrderService + DI + integration tests

**Files:**
- Create: `src/MyApp.Infrastructure/Services/SalesOrderService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/SalesOrderServiceTests.cs`

**Interfaces:**
- Consumes: `IApprovalService`, `IValidator<CreateSalesOrderRequest>`, `IValidator<UpdateSalesOrderRequest>`, `AppDbContext`.
- Produces: full `ISalesOrderService` implementation. Orchestration mirrors `PurchaseOrderService` verbatim with the SO renames; adds `GetCreditInfoAsync`.

- [ ] **Step 1: Register DI**

In `src/MyApp.Infrastructure/DependencyInjection.cs`:
- add `using MyApp.Application.SalesOrders;` with the other `using`s,
- after `services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();`:

```csharp
        services.AddScoped<ISalesOrderService, SalesOrderService>();
```

> Validators are auto-registered by the existing `services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();` scan (same assembly), so no extra validator registration is needed.

- [ ] **Step 2: Create the service** (mirror `PurchaseOrderService`; `DocType = ApprovalDocumentType.SalesOrder`)

`src/MyApp.Infrastructure/Services/SalesOrderService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Approvals;
using MyApp.Application.Common;
using MyApp.Application.SalesOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class SalesOrderService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreateSalesOrderRequest> createValidator,
    IValidator<UpdateSalesOrderRequest> updateValidator) : ISalesOrderService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.SalesOrder;

    public async Task<PagedResult<SalesOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SalesOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.SalesOrders.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.SoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SalesOrderListItemDto(
                p.Id, p.SoNumber,
                db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Currency, p.GrandTotal, p.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<SalesOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<SalesOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.SalesOrders
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(SalesOrderStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new SalesOrderDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(SalesOrderStatus.Draft),
            CountOf(SalesOrderStatus.PendingApproval),
            CountOf(SalesOrderStatus.Confirmed));
    }

    public async Task<SalesOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.AsNoTracking()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return null;

        var customerName = await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = so.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = so.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            return new SalesOrderLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, l.TaxRateSnapshot,
                l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal);
        }).ToList();

        return new SalesOrderDto(so.Id, so.SoNumber, so.CustomerId, customerName, so.WarehouseId, warehouseName,
            so.OrderDate, so.ExpectedDate, so.Currency, so.Notes, so.Status.ToString(), so.RejectionNote,
            so.Subtotal, so.DiscountTotal, so.TaxTotal, so.GrandTotal, so.CreatedAt, so.CreatedBy, lines);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<SalesOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, ProductName = p.Name, v.Price, v.DiscountPrice };
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Sku.Contains(term) || x.ProductName.Contains(term));

        return await q.OrderBy(x => x.ProductName).Take(50)
            .Select(x => new SalesOrderVariantOptionDto(x.Id, x.Sku, x.ProductName, x.Price, x.DiscountPrice))
            .ToListAsync(ct);
    }

    public async Task<SalesOrderCreditInfoDto> GetCreditInfoAsync(
        int customerId, decimal thisOrderTotal, int? excludeSoId, CancellationToken ct = default)
    {
        var creditLimit = await db.Customers.Where(c => c.Id == customerId)
            .Select(c => c.CreditLimit).FirstOrDefaultAsync(ct);

        // Outstanding proxy: Σ GrandTotal of this customer's Confirmed SOs, excluding excludeSoId.
        // C2 will widen the committed set to include PartiallyDelivered/Delivered.
        var estimatedOutstanding = await db.SalesOrders.AsNoTracking()
            .Where(s => s.CustomerId == customerId
                        && s.Status == SalesOrderStatus.Confirmed
                        && (excludeSoId == null || s.Id != excludeSoId))
            .SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;

        var exceedsLimit = creditLimit > 0 && (estimatedOutstanding + thisOrderTotal) > creditLimit;
        return new SalesOrderCreditInfoDto(creditLimit, estimatedOutstanding, thisOrderTotal, exceedsLimit);
    }

    public async Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await db.Customers.Where(c => c.Id == request.CustomerId)
            .Select(c => c.DefaultCurrency).FirstOrDefaultAsync(ct) ?? "IDR";
        var soNumber = await GenerateNumberAsync(request.OrderDate, ct);

        var so = new SalesOrder(soNumber, request.CustomerId, request.WarehouseId,
            request.OrderDate, request.ExpectedDate, currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, ct));

        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(so.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;

        var oldLines = await db.SalesOrderLines.Where(l => l.SalesOrderId == id).ToListAsync(ct);
        db.SalesOrderLines.RemoveRange(oldLines);

        so.UpdateHeader(so.CustomerId, request.WarehouseId, request.OrderDate, request.ExpectedDate, so.Currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, ct));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;
        if (so.Status != SalesOrderStatus.Draft)
            throw Fail("Only a draft sales order can be deleted.");
        db.SalesOrders.Remove(so);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        so.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, so.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, so.Id, ct);
        if (fullyApproved) so.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        var fullyApproved = await approval.ApproveAsync(DocType, so.Id, actingUserName, isInRole, so.CreatedBy, ct);
        if (fullyApproved) so.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        await approval.RejectAsync(DocType, so.Id, actingUserName, isInRole, so.CreatedBy, reason, ct);
        so.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, so.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Sales order not found.");

        so.Cancel();
        await approval.ResetAsync(DocType, so.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<string> GenerateNumberAsync(DateTime orderDate, CancellationToken ct)
    {
        var prefix = $"SO-{orderDate:yyyyMM}-";
        var last = await db.SalesOrders.AsNoTracking()
            .Where(p => p.SoNumber.StartsWith(prefix))
            .OrderByDescending(p => p.SoNumber)
            .Select(p => p.SoNumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private async Task<List<SalesOrderLine>> BuildLinesAsync(
        IReadOnlyList<SalesOrderLineRequest> requests, CancellationToken ct)
    {
        var taxIds = requests.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        var rates = taxIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.Taxes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Rate, ct);

        var lines = new List<SalesOrderLine>();
        foreach (var l in requests)
        {
            var rate = l.TaxId.HasValue && rates.TryGetValue(l.TaxId.Value, out var r) ? r : 0m;
            lines.Add(new SalesOrderLine(l.ProductVariantId, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, rate));
        }
        return lines;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SalesOrder", message)]);
}
```

- [ ] **Step 3: Write failing integration tests**

Create `tests/MyApp.IntegrationTests/SalesOrderServiceTests.cs`. The seed helper mirrors `PurchaseOrderServiceTests` (using the REAL ctors: `Customer` and `Product.AddVariant`) and drives an SO through the approval flow:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Approvals;
using MyApp.Application.SalesOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class SalesOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seeds a customer (with credit limit), warehouse, and one active variant.
    // Customer ctor: (code, name, contactPerson, phone, email, address, taxId, paymentTermDays, defaultCurrency, creditLimit, isActive)
    private static async Task<(int cust, int wh, int variant)> SeedMastersAsync(IServiceProvider sp, decimal creditLimit = 0m)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT SO {id}", null, null, null, null, null, 30, "IDR", creditLimit, true);
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(cust);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // ProductVariant ctor: (sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive)
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        return (cust.Id, wh.Id, variant.Id);
    }

    private static CreateSalesOrderRequest New(int cust, int wh, int variant) =>
        new(cust, wh, new DateTime(2026, 7, 1), null, "test",
            [new SalesOrderLineRequest(variant, 10, 1000m, 0m, null)]);

    [Fact]
    public async Task Create_generates_number_and_totals()
    {
        using var scope = _factory.Services.CreateScope();
        var (cust, wh, variant) = await SeedMastersAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        Assert.StartsWith("SO-202607-", so.SoNumber);
        Assert.Equal(10000m, so.GrandTotal);
        Assert.Equal("Draft", so.Status);
        Assert.Single(so.Lines);
    }

    [Fact]
    public async Task Submit_with_empty_chain_confirms_immediately()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);

        Assert.Equal("Confirmed", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task Submit_approve_chain_confirms()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.SalesOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);
        Assert.Equal("PendingApproval", (await svc.GetByIdAsync(so.Id))!.Status);

        // CreatedBy null in test context (NullCurrentUser) → acting "approver" is not the creator
        await svc.ApproveAsync(so.Id, "approver", _ => true);
        Assert.Equal("Confirmed", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    // NOTE: segregation-of-duties (creator cannot approve own document) is enforced entirely
    // by the reused, document-agnostic approval engine and is already covered by
    // ApprovalServiceTests — which apply to SalesOrder verbatim. It is NOT re-tested here:
    // the integration test factory uses NullCurrentUser (so.CreatedBy is null), and the engine's
    // check `ApprovalService.EnsureCanAct` is guarded by `!string.IsNullOrEmpty(creatorUserName)`,
    // so with a null creator the rule is (correctly) inert. B1's PurchaseOrderServiceTests omits
    // this test for the same reason. Do not add a service-level creator-cannot-approve test unless
    // you first plumb a non-null ICurrentUser into the factory; if you do, assert ValidationException
    // (the engine throws ValidationException via Fail(...), NOT InvalidOperationException).

    [Fact]
    public async Task Reject_returns_to_draft_with_note()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.SalesOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(so.Id);
        await svc.RejectAsync(so.Id, "approver", _ => true, "stok tidak cukup");

        var fetched = await svc.GetByIdAsync(so.Id);
        Assert.Equal("Draft", fetched!.Status);
        Assert.Equal("stok tidak cukup", fetched.RejectionNote);
        Assert.Empty(await svc.GetApprovalStepsAsync(so.Id));
    }

    [Fact]
    public async Task Cancel_marks_cancelled()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var so = await svc.CreateAsync(New(cust, wh, variant));
        await svc.CancelAsync(so.Id);
        Assert.Equal("Cancelled", (await svc.GetByIdAsync(so.Id))!.Status);
    }

    [Fact]
    public async Task So_numbers_are_unique_within_month()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var a = await svc.CreateAsync(New(cust, wh, variant));
        var b = await svc.CreateAsync(New(cust, wh, variant));
        Assert.NotEqual(a.SoNumber, b.SoNumber);
    }

    [Fact]
    public async Task GetCreditInfo_sums_confirmed_excludes_id_and_flags_over_limit()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, creditLimit: 15000m);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        // One Confirmed SO (empty chain → confirms on submit): GrandTotal 10000.
        var confirmed = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(confirmed.Id);

        // Outstanding counts the Confirmed SO (10000); a new 4000 order stays within 15000.
        var within = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 4000m, excludeSoId: null, default);
        Assert.Equal(15000m, within.CreditLimit);
        Assert.Equal(10000m, within.EstimatedOutstanding);
        Assert.False(within.ExceedsLimit); // 10000 + 4000 = 14000 <= 15000

        // Boundary: exactly at limit does NOT exceed (strictly greater-than).
        var atLimit = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5000m, excludeSoId: null, default);
        Assert.False(atLimit.ExceedsLimit); // 10000 + 5000 = 15000, not > 15000

        // Just over the limit exceeds.
        var over = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5001m, excludeSoId: null, default);
        Assert.True(over.ExceedsLimit); // 10000 + 5001 = 15001 > 15000

        // Excluding the confirmed SO drops outstanding to 0.
        var excluded = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 5001m, excludeSoId: confirmed.Id, default);
        Assert.Equal(0m, excluded.EstimatedOutstanding);
        Assert.False(excluded.ExceedsLimit);
    }

    [Fact]
    public async Task GetCreditInfo_zero_limit_never_exceeds()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cust, wh, variant) = await SeedMastersAsync(sp, creditLimit: 0m);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.SalesOrder, []);
        var svc = sp.GetRequiredService<ISalesOrderService>();

        var confirmed = await svc.CreateAsync(New(cust, wh, variant));
        await svc.SubmitAsync(confirmed.Id);

        var info = await svc.GetCreditInfoAsync(cust, thisOrderTotal: 999999m, excludeSoId: null, default);
        Assert.False(info.ExceedsLimit); // CreditLimit == 0 disables the check
    }
}
```

> Segregation-of-duties is covered by the reused engine's own `ApprovalServiceTests` (see the NOTE in the test file above); it is deliberately not re-tested at the SO service level, matching B1.

- [ ] **Step 4: Run integration tests — verify pass**

Run: `dotnet test tests/MyApp.IntegrationTests --filter "FullyQualifiedName~SalesOrderServiceTests"`
Expected: PASS. Then `dotnet build` — 0 warnings.

---

### Task 5: EF migration + database update

**Files:**
- Create (generated): `src/MyApp.Infrastructure/Migrations/*_AddSalesOrder.cs`

- [ ] **Step 1: Generate the migration**

Run from solution root:

```bash
dotnet ef migrations add AddSalesOrder --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: a new migration file created. (If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.)

- [ ] **Step 2: Review the migration content**

Open the generated file and confirm `Up()`:
- creates table `SalesOrders` (`Id` PK identity; `SoNumber` nvarchar(30) NOT NULL + UNIQUE index; `CustomerId` int FK→`Customers` `Restrict`; `WarehouseId` int FK→`Warehouses` `Restrict`; `OrderDate`; `ExpectedDate` nullable; `Currency` nvarchar(3) NOT NULL; `Notes` nvarchar(500); `Status` nvarchar(20) NOT NULL; `RejectionNote` nvarchar(500); `Subtotal`/`DiscountTotal`/`TaxTotal`/`GrandTotal` decimal(18,2); audit columns `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy`),
- creates table `SalesOrderLines` (`Id` PK; `SalesOrderId` FK→`SalesOrders` `Cascade`; `ProductVariantId` FK→`ProductVariants` `Restrict`; `TaxId` int nullable FK→`Taxes` `SetNull`; `Quantity` int; `UnitPrice` decimal(18,2); `DiscountPercent` decimal(5,2); `TaxRateSnapshot` decimal(5,2); `LineSubtotal`/`LineDiscount`/`LineTax`/`LineTotal` decimal(18,2)).

Confirm `Down()` drops **both** tables and nothing else. Confirm the migration touches **no** stock/product/approval tables.

- [ ] **Step 3: Apply to the dev database**

```bash
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```

Expected: ends with `Done.`

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build` — 0 warnings.
Run: `dotnet test` — all unit + integration green.

---

### Task 6: Web — BootstrapSeeder default chain + AppMenus permissions + Settings verification

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Infrastructure/BootstrapSeeder.cs`
- Verify (no change expected): `src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainsIndex.razor`

- [ ] **Step 1: Raise the `transactions.sales-orders` resource to CRUD + approve**

In `AppMenus.cs`:
- add a helper next to `PurchaseOrderActions` / `GoodsReceiptActions`:

```csharp
    private static AppAction[] SalesOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove];
```

- change the `transactions.sales-orders` line in the `"Transaksi"` group from `ViewOnly` to `SalesOrderActions`:

```csharp
            new("transactions.sales-orders",    "Sales Order",    "bi-bag-check-fill",     SalesOrderActions),
```

> `ActApprove` already exists (added in B1). New permissions (`transactions.sales-orders.create/edit/delete/approve`) are auto-granted to admin via `AppMenus.AllPermissions` in `BootstrapSeeder` on next startup — no per-page wiring needed. The `NavMenu` renders from `AppMenus.Groups`, so the SO entry already appears.

- [ ] **Step 2: Seed the default SalesOrder approval chain**

In `BootstrapSeeder.cs`, after the PurchaseOrder chain seed block (right after its closing `}` at line ~53), add the mirror block:

```csharp
        // Seed rantai approval default untuk Sales Order (idempotent), mengikuti pola PO.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SalesOrder))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SalesOrder, 1, roleName));
            await db.SaveChangesAsync();
        }
```

> `roleName` is the same `Identity:ManagerRole` (default `Administrators`) local already in scope. An empty chain would auto-Confirm on submit; the default seeds one step so the flow is visible, exactly like PO. Admins reconfigure the real approver role in Settings → Approval Chain.

- [ ] **Step 3: Verify Settings enumerates SalesOrder**

Open `src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainsIndex.razor` and confirm it iterates `Enum.GetValues<ApprovalDocumentType>()` (it does — lines 24 and 52). `SalesOrder` therefore already appears with an Edit link to `/settings/approval-chains/{dt}`. **No change required.**

- [ ] **Step 4: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 7: Web — SoIndex page (replaces SalesOrderPlaceholder)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/SalesOrders/SoIndex.razor`
- Delete: `src/MyApp.Web/Components/Pages/Transactions/SalesOrderPlaceholder.razor`

> Use `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoIndex.razor` as the exact structural template (KPI cards, `search` + status chips toolbar, table card, `Pager`, `StatusClass`). Reproduce its markup and `@code` verbatim with the renames below. There is **no delete button** in PoIndex's table (delete lives on the form flow / not surfaced); keep SoIndex consistent with PoIndex — do not invent a delete button beyond what PoIndex shows.

- [ ] **Step 1: Delete the placeholder**

Delete `src/MyApp.Web/Components/Pages/Transactions/SalesOrderPlaceholder.razor` (it owns `/transactions/sales-orders`; SoIndex takes that route).

- [ ] **Step 2: Create `SoIndex.razor`**

Copy `PoIndex.razor` and apply these renames/edits:
- Route/policy: `@page "/transactions/sales-orders"`, `@attribute [Authorize(Policy = "transactions.sales-orders.index")]`.
- Usings: `@using MyApp.Application.SalesOrders` (replace `...PurchaseOrders`); keep `@using MyApp.Application.Common` and `@using MyApp.Domain.Entities`.
- Injects: `@inject ISalesOrderService SoService` (replace `IPurchaseOrderService PoService`); keep `NavigationManager Nav` and `SwalService Swal`.
- Titles/labels: "Purchase Orders"→"Sales Orders", "Buat PO"→"Buat SO", "Total PO"→"Total SO", crumb "Purchase Orders"→"Sales Orders", "Cari nomor PO…"→"Cari nomor SO…", `/transactions/purchase-orders/new`→`/transactions/sales-orders/new`.
- Table: `No. PO`→`No. SO`, `Supplier`→`Customer`; bind `item.SoNumber`, `item.CustomerName`, `item.OrderDate`, `item.Currency`+`item.GrandTotal`, `item.Status`. Row nav + eye link → `/transactions/sales-orders/{item.Id}`.
- `@code`: rename `PurchaseOrderListItemDto`→`SalesOrderListItemDto`, `PurchaseOrderDashboardDto`→`SalesOrderDashboardDto`, `PurchaseOrderStatus`→`SalesOrderStatus`, `PoService`→`SoService`, `Open()` target to `/transactions/sales-orders/{id}`.
- `StatusClass`: keep only the C1 statuses (drop `PartiallyReceived`/`Received`/`Closed`):

```csharp
    private static string StatusClass(string status) => status switch
    {
        "Draft" => "b-draft",
        "PendingApproval" => "b-warn",
        "Confirmed" => "b-ok",
        "Rejected" => "b-danger",
        "Cancelled" => "b-cancel",
        _ => "b-dark"
    };
```

- [ ] **Step 3: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 8: Web — SoForm page (create/edit draft + credit-limit banner)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/SalesOrders/SoForm.razor`

> Use `PoForm.razor` as the exact structural template (Atlas layout: left section navigator with required-field progress, right info + items cards, footer grand total + Save Draft spinner, `ValidationException` handling). Reproduce it verbatim with the renames below, PLUS two SO-specific additions: (a) default `UnitPrice = DiscountPrice ?? Price` on variant change, and (b) a live, non-blocking credit-limit banner.

- [ ] **Step 1: Create `SoForm.razor`** — renames from PoForm

- Routes: `@page "/transactions/sales-orders/new"` and `@page "/transactions/sales-orders/{Id:int}/edit"`.
- Usings: replace `@using MyApp.Application.PurchaseOrders` with `@using MyApp.Application.SalesOrders`; replace `@using MyApp.Application.Suppliers` with `@using MyApp.Application.Customers`; keep Warehouses, Taxes, `MyApp.Web.Authorization`, `FluentValidation`.
- Injects: `@inject ISalesOrderService SoService` (replace `IPurchaseOrderService PoService`); `@inject ICustomerService CustomerService` (replace `ISupplierService SupplierService`); keep `IWarehouseService`, `ITaxService`, `IAuthorizationService Auth`, `NavigationManager Nav`, `IJSRuntime JS`.
- Info card: "Supplier"→"Customer" (bind `_customerId`, options over `_customers` = `ICustomerService.GetAllAsync()` → `CustomerDto` with `Code`/`Name`); "Destination Warehouse"→"Source Warehouse". Titles "Purchase order information"→"Sales order information", crumbs "Purchase Orders"→"Sales Orders", nav `/transactions/purchase-orders`→`/transactions/sales-orders`.
- `@code` field renames: `_supplierId`→`_customerId`; `IReadOnlyList<SupplierDto> _suppliers`→`IReadOnlyList<CustomerDto> _customers`; `IReadOnlyList<PurchaseOrderVariantOptionDto> _variants`→`IReadOnlyList<SalesOrderVariantOptionDto> _variants`; permission keys `"transactions.purchase-orders"`→`"transactions.sales-orders"`; `PoService`→`SoService`. `ReqFilled`/`InfoComplete` use `_customerId`.
- Load in edit mode: `SoService.GetByIdAsync(id)`, guard `so.Status != "Draft"`, map `_customerId = so.CustomerId`, `_warehouseId`, `_orderDate`, `_expectedDate`, `_notes`, and rows from `so.Lines`.
- `SaveAsync`: build `SalesOrderLineRequest`; create → `SoService.CreateAsync(new CreateSalesOrderRequest(_customerId, _warehouseId, _orderDate, _expectedDate, _notes, lines))`; update → `SoService.UpdateAsync(id, new UpdateSalesOrderRequest(_warehouseId, _orderDate, _expectedDate, _notes, lines))` (no `_customerId` in update). Navigate to `/transactions/sales-orders`.

- [ ] **Step 2: Default price = `DiscountPrice ?? Price` on variant change**

Replace PoForm's `OnVariantChanged` (which used `v.CostPrice`) with the SO version:

```csharp
    private void OnVariantChanged(Row row, string? value)
    {
        row.VariantId = int.TryParse(value, out var id) ? id : 0;
        var v = _variants.FirstOrDefault(x => x.VariantId == row.VariantId);
        if (v is not null && row.UnitPrice == 0) row.UnitPrice = v.DiscountPrice ?? v.Price; // saran harga jual
    }
```

`LineTotal(Row)` and `GrandTotal()` are copied verbatim from PoForm (identical math).

- [ ] **Step 3: Add the credit-limit banner (non-blocking)**

Add a debounced credit check that runs on customer change and whenever lines change, and render an informational banner above the footer. Add fields to `@code`:

```csharp
    private SalesOrderCreditInfoDto? _credit;

    private async Task RefreshCreditAsync()
    {
        if (_customerId <= 0) { _credit = null; return; }
        var excludeId = Id;   // in edit mode, exclude this SO from the outstanding sum
        _credit = await SoService.GetCreditInfoAsync(_customerId, GrandTotal(), excludeId, default);
    }
```

Call `await RefreshCreditAsync();` at the end of `OnInitializedAsync` (after data + rows are loaded), and invoke it from the customer `<select>` change and after `AddRow`/`OnVariantChanged`/line edits. Simplest robust approach mirroring PoForm's binding style: change the Customer select to `@onchange` and recompute, e.g.:

```razor
                                <select class="ctl sel" value="@_customerId"
                                        @onchange="OnCustomerChanged">
                                    <option value="0">— select customer —</option>
                                    @foreach (var c in _customers)
                                    {
                                        <option value="@c.Id">@c.Code — @c.Name</option>
                                    }
                                </select>
```

```csharp
    private async Task OnCustomerChanged(ChangeEventArgs e)
    {
        _customerId = int.TryParse(e.Value?.ToString(), out var v) ? v : 0;
        await RefreshCreditAsync();
    }
```

Render the banner in the footer area (just above the existing `pf-footer` block), shown only when the limit is exceeded — informational, never disables Save:

```razor
        @if (_credit is { ExceedsLimit: true })
        {
            <div class="pf-alert warn">
                <i class="bi bi-exclamation-triangle"></i>
                Peringatan kredit: perkiraan outstanding @_credit.EstimatedOutstanding.ToString("N2")
                + order ini @_credit.ThisOrderTotal.ToString("N2")
                melebihi limit @_credit.CreditLimit.ToString("N2"). SO tetap dapat disimpan.
            </div>
        }
```

> The banner is purely advisory. Do not gate `SaveAsync`, `Submit`, or any transition on `ExceedsLimit`. The `.pf-alert.warn` style follows the existing `.pf-alert.err` convention already in the PoForm stylesheet; if a `warn` variant does not exist, use `class="pf-alert err"` or add a small inline amber style — do not block on styling.

- [ ] **Step 4: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 9: Web — SoDetail page (view + approval timeline + actions + credit banner)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/SalesOrders/SoDetail.razor`

> Use `PoDetail.razor` as the exact structural template (head with status badge + contextual action buttons, reject-reason card, info card, items table, approval timeline). Reproduce verbatim with the renames below, drop the Confirmed/PartiallyReceived receipt branch (that is C2), and add a read-only credit-limit banner.

- [ ] **Step 1: Create `SoDetail.razor`** — renames from PoDetail

- Route/policy: `@page "/transactions/sales-orders/{Id:int}"`, `@attribute [Authorize(Policy = "transactions.sales-orders.index")]`.
- Usings: replace `@using MyApp.Application.PurchaseOrders` with `@using MyApp.Application.SalesOrders`; keep `FluentValidation`, `MyApp.Application.Approvals`.
- Injects: `@inject ISalesOrderService SoService` (replace `IPurchaseOrderService PoService`); keep `IAuthorizationService Auth`, `NavigationManager Nav`, `SwalService Swal`.
- Labels/crumbs: "Purchase Orders"→"Sales Orders", `/transactions/purchase-orders`→`/transactions/sales-orders`, "Purchase order tidak ditemukan."→"Sales order tidak ditemukan.", "Supplier"→"Customer" (`_so.CustomerName`), "Gudang Tujuan"→"Gudang Sumber".
- `@code` renames: `PurchaseOrderDto _po`→`SalesOrderDto _so` (rename all `_po` usages to `_so`), `PoService`→`SoService`, permission `"transactions.purchase-orders.approve"`→`"transactions.sales-orders.approve"`.

- [ ] **Step 2: Action bar — keep only C1 branches**

Replace PoDetail's action block. Keep Draft (Edit/Submit/Batalkan) and PendingApproval (Approve/Reject when `_canApprove`, + Batalkan) branches verbatim (renamed). **Remove** the `Confirmed || PartiallyReceived` branch entirely (GRN/Tutup PO belong to B2/C2):

```razor
            <div class="actions">
                @if (_so.Status == "Draft")
                {
                    <a class="btn btn-line" href="@($"/transactions/sales-orders/{_so.Id}/edit")"><i class="bi bi-pencil"></i> Edit</a>
                    <button class="btn btn-primary" @onclick="SubmitAsync" disabled="@_busy"><i class="bi bi-send"></i> Submit</button>
                    <button class="btn btn-line" @onclick="CancelAsync" disabled="@_busy">Batalkan</button>
                }
                else if (_so.Status == "PendingApproval")
                {
                    @if (_canApprove)
                    {
                        <button class="btn btn-ok" @onclick="ApproveAsync" disabled="@_busy"><i class="bi bi-check2-circle"></i> Approve</button>
                        <button class="btn btn-danger" @onclick="() => _showReject = true" disabled="@_busy"><i class="bi bi-x-circle"></i> Reject</button>
                    }
                    <button class="btn btn-line" @onclick="CancelAsync" disabled="@_busy">Batalkan</button>
                }
            </div>
```

- [ ] **Step 3: Items table — drop the "Diterima" column**

The SO line table has no receipt tracking in C1. Use these headers/cells (remove PoDetail's `Diterima` column):

```razor
                                <thead>
                                    <tr><th>Produk</th><th class="r">Qty</th><th class="r">Harga</th><th class="r">Disk</th><th class="r">Pajak</th><th class="r">Total</th></tr>
                                </thead>
                                <tbody>
                                    @foreach (var l in _so.Lines)
                                    {
                                        <tr>
                                            <td><span class="sku">@l.VariantSku</span> <span class="pn">@l.ProductName</span></td>
                                            <td class="r mono">@l.Quantity</td>
                                            <td class="r mono">@l.UnitPrice.ToString("N2")</td>
                                            <td class="r mono">@l.LineDiscount.ToString("N2")</td>
                                            <td class="r mono">@l.LineTax.ToString("N2")</td>
                                            <td class="r mono">@l.LineTotal.ToString("N2")</td>
                                        </tr>
                                    }
                                </tbody>
                                <tfoot>
                                    <tr><td colspan="5" class="r">Grand Total (@_so.Currency)</td><td class="r grand">@_so.GrandTotal.ToString("N2")</td></tr>
                                </tfoot>
```

- [ ] **Step 4: Approve/Reject/Submit/Cancel handlers**

Copy PoDetail's `LoadAsync`, `EvaluateCanApproveAsync`, `SubmitAsync`, `CancelAsync`, `ApproveAsync`, `RejectAsync`, `RunAsync`, `StatusClass`, `StepIcon`, `StepDot` verbatim with `_po`→`_so`, `PoService`→`SoService`, and the approve-permission string `"transactions.sales-orders.approve"`. **Remove** `ClosePoAsync` (C2). `StatusClass` keeps only the C1 statuses (same switch as Task 7 Step 2). The approval timeline `<section>` is copied verbatim (uses `_steps` / `ApprovalStepDto`).

Approve/Reject signatures match the service (identical to PO):

```csharp
    private async Task ApproveAsync() =>
        await RunAsync(() => SoService.ApproveAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole), "SO di-approve");

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(_rejectReason)) { _error = "Alasan reject wajib diisi."; return; }
        var reason = _rejectReason;
        _showReject = false;
        _rejectReason = string.Empty;
        await RunAsync(() => SoService.RejectAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole, reason), "SO ditolak, kembali ke Draft");
    }
```

- [ ] **Step 5: Read-only credit-limit banner**

Load credit info in `LoadAsync` (after `_so` is fetched) and render an advisory banner near the head. Add to `@code`:

```csharp
    private SalesOrderCreditInfoDto? _credit;
```

In `LoadAsync`, after `_so` is set (and only when not null), add:

```csharp
        _credit = _so is null ? null
            : await SoService.GetCreditInfoAsync(_so.CustomerId, _so.GrandTotal, _so.Id, default);
```

> `excludeSoId = _so.Id` so a Confirmed SO does not count its own total against itself. Render, e.g. right after the `pd-head` block:

```razor
        @if (_credit is { ExceedsLimit: true })
        {
            <div class="pf-alert warn">
                <i class="bi bi-exclamation-triangle"></i>
                Peringatan kredit: outstanding customer @_credit.EstimatedOutstanding.ToString("N2")
                (limit @_credit.CreditLimit.ToString("N2")) — hanya informasi, tidak memblokir.
            </div>
        }
```

- [ ] **Step 6: Build**

Run: `dotnet build` — 0 warnings.

---

### Task 10: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build` — Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: all unit + integration tests pass (B1/B2 baseline + the new C1 unit & integration tests).

- [ ] **Step 3: Confirm no stock movement in C1**

Re-read `SalesOrderService` to confirm it never touches `StockMovements`, `ProductStocks`, or `ProductVariant.ApplyMovingAverage` — C1 is order-only; stock-out is C2. Confirm the `AddSalesOrder` migration created only `SalesOrders` + `SalesOrderLines` and altered no other table.

- [ ] **Step 4: Manual UI walkthrough (hand to user)**

Hand to the user (cannot be automated):
1. Sales Order list at `/transactions/sales-orders` (placeholder gone) → **Buat SO** → pick customer (currency snapshots), source warehouse, add lines (price defaults to `DiscountPrice ?? Price`), live grand total → Save Draft.
2. If the customer's outstanding + this order exceeds their credit limit, the amber banner shows on the form and detail but never blocks Save/Submit.
3. Open SO detail → **Submit**. With the default seeded chain → status `PendingApproval` and a Level-1 timeline step. With an empty chain (configured in Settings) → immediately `Confirmed`.
4. As a user in the step role (and NOT the creator) → **Approve** → `Confirmed`; or **Reject** with a reason → back to `Draft` with the rejection note, chain reset.
5. **Batalkan** from Draft/PendingApproval → `Cancelled`. Confirmed SO shows no edit/cancel actions.
6. Settings → Approval Chain lists `SalesOrder` and its chain is editable.

## Self-Review (done by plan author)

- **Spec coverage:** §1 Domain (SalesOrderStatus/SalesOrderLine/SalesOrder) → Task 1; §2 Approval reuse → Tasks 4 (service orchestration) + 6 (default chain seed + Settings verify); §3 Application (DTOs/interface/validators) → Task 2; §4 Infrastructure (DbContext → Task 3, service+DI → Task 4, migration → Task 5); §5 Web (SoIndex → Task 7, SoForm → Task 8, SoDetail → Task 9, AppMenus/NavMenu → Task 6); §6 Testing → embedded per task + Task 10. Credit limit (soft warning) → service in Task 4 + banners in Tasks 8/9. Placeholder deletion → Task 7. ✓
- **Type consistency:** DTO/record names and service signatures defined in Task 2 (`SalesOrderListItemDto`, `SalesOrderDto`, `SalesOrderLineDto`, `SalesOrderDashboardDto`, `SalesOrderVariantOptionDto` with `Price`+`DiscountPrice`, `SalesOrderCreditInfoDto`, `CreateSalesOrderRequest`, `UpdateSalesOrderRequest`, `SalesOrderLineRequest`, and `ISalesOrderService` methods) are reused verbatim in Tasks 4, 7, 8, 9. Service signatures (`SubmitAsync`/`CancelAsync` → `Task`; `ApproveAsync(id, actingUserName, isInRole, ct)`; `RejectAsync(id, actingUserName, isInRole, reason, ct)`) mirror the real `IPurchaseOrderService` exactly. Domain ctors (`SalesOrder(soNumber, customerId, warehouseId, orderDate, expectedDate, currency, notes)`, `SalesOrderLine(productVariantId, quantity, unitPrice, discountPercent, taxId, taxRateSnapshot)`) match `PurchaseOrder`/`PurchaseOrderLine`. `Customer` and `ProductVariant`/`Product.AddVariant` ctor shapes used in tests match the real source. ✓
- **Placeholder scan:** Every code step shows real code. Razor tasks (7–9) reference the concrete sibling `Po*.razor` as the template per repo convention, but specify exact routes, policies, injects, renames, and the non-obvious additions (default price `DiscountPrice ?? Price`, credit banner markup + `RefreshCreditAsync`/`OnCustomerChanged`, C1-only status switch, dropped columns/branches) in full. No "TBD"/"similar to"/"add validation" placeholders. ✓
- **Ambiguities resolved:** (a) Service signatures follow the real PO code, not the spec's looser sketch. (b) `ApprovalChainsIndex` already enumerates all `ApprovalDocumentType`, so Settings needs no change (Task 6 Step 3 verifies). (c) `ApprovalDocumentType.SalesOrder` already exists. (d) `UpdateSalesOrderRequest` omits `CustomerId` (currency snapshot is fixed at create, matching PO update keeping `po.Currency`). (e) Segregation-of-duties (creator≠approver) is enforced by the reused engine and already covered by `ApprovalServiceTests` (throws `ValidationException`, guarded by non-null creator); it is intentionally NOT re-tested at the SO service level because the test factory uses `NullCurrentUser` (null creator makes the rule inert) — matching B1's `PurchaseOrderServiceTests`. The engine is never modified. ✓
- **No git:** every task ends with `dotnet build` (0 warnings) + the relevant `dotnet test --filter` (FAIL→PASS); reviewers read changed files directly. ✓
