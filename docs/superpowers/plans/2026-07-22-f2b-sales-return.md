# Fase 2b — Sales Return (Credit Note) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return-from-customer document with approval (`Draft → PendingApproval → Posted`), two source paths — Delivery Order (pre-invoice: `Dr Inventory / Cr COGS`) and Customer Invoice (post-invoice credit note: adds `Dr Sales / Dr Output Tax / Cr AR`). Returned goods re-enter stock **through the costing seam** (`OnInboundAsync`) at the DO's COGS snapshot, so it is correct for all four HPP methods. Partial & multiple returns are guarded by per-DO-line remaining qty.

**Architecture:** New `SalesReturn` aggregate (+`SalesReturnLine`) mirrors `PurchaseReturn` (committed Fase 2a), reversed. `SalesReturnService` mirrors `PurchaseReturnService` (own tx per public method; private `PostAsync` mutates + `MarkPosted`, caller saves/commits). Posting: stock IN per line via `OnInboundAsync(variantId, warehouseId, qty, doLineUnitCost)`, then AR credit (Invoice path) via new `CustomerInvoice.ApplyCredit`, then GL via new `IJournalPostingService.PostSalesReturnAsync`. Physical anchor is `DeliveryOrderLine`; the invoice path links to the invoice via `SalesOrderLineId`. Web = Index `.pi` / Form `.cf` / Detail `.pf` mirroring the Sales... the committed PurchaseReturns pages.

**Tech Stack:** .NET 10, EF Core (SQLite in-memory per test class via `EnsureCreated`), xUnit, Blazor Server, FluentValidation.

## Global Constraints

- **Reference implementation:** Fase 2a Purchase Return is committed and is the exact mirror. Reuse its file shapes: `src/ErpOne.Infrastructure/Services/Purchasing/PurchaseReturnService.cs`, `src/ErpOne.Application/Purchasing/PurchaseReturns/*`, `src/ErpOne.Web/Components/Pages/Transactions/PurchaseReturns/*`, and the `SupplierInvoice` credit changes. Where this plan says "mirror PurchaseReturnX", copy that committed file and rename Purchase→Sales, GRN→DO, Supplier→Customer, AP→AR, GR-IR→(n/a), grIr→cogs, adapting the differences called out below.
- **Namespace flat:** `namespace ErpOne.Domain.Entities;`. Entities extend `AuditableEntity`, declare own `int Id { get; private set; }`, `private readonly List<TLine> _lines = [];` exposed as `IReadOnlyCollection`, private EF ctor + validating ctor, private `EnsureDraft()`.
- **Rounding:** `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Costing seam (inbound):** returned stock re-enters via `ICostingService.OnInboundAsync(variantId, warehouseId, qty, doLineUnitCost, ct)` called AFTER `UpsertStockAsync(+qty)`. Cost = `DeliveryOrderLine.UnitCost` (COGS snapshot), carried onto `SalesReturnLine.UnitCost` at create. `InventoryTotal = Σ round(qty × UnitCost)`. No on-hand guard (inbound). Do NOT call `GetOutboundUnitCostAsync`.
- **Stock movement:** `new StockMovement(variantId, warehouseId, MovementType.In, +qty, unitCost, r.ReturnDate, "SalesReturn", r.Id, r.ReturnNumber)` (arg order per `PurchaseReturnService`/`DeliveryOrderService`). `MovementType.In` exists.
- **Service pattern:** primary-ctor DI; `private const ApprovalDocumentType DocType = ApprovalDocumentType.SalesReturn;`; every mutating public method wraps `await using var tx = await db.Database.BeginTransactionAsync(ct);` … `await tx.CommitAsync(ct);`; approval flow identical to `PurchaseReturnService`; `private static ValidationException Fail(string m) => new([new FluentValidation.Results.ValidationFailure("SalesReturn", m)]);`.
- **GL:** enlists in caller tx, idempotent on `(SourceType, SourceId)` = `("SalesReturn", r.Id)`; accounts via `RequireAccount(cfg.XId, "label")`. Keys: `InventoryAccountId`, `CogsAccountId`, `SalesAccountId`, `OutputTaxAccountId`, `ArAccountId`.
- **Numbering:** `DocumentTypes.SalesReturn` (namespace `ErpOne.Application.Numbering`); `NumberSequence` seed **Id=17** Code="SalesReturn" Prefix="CN"; monthly, padding 4, sep "-".
- **Table prefix:** `[nameof(SalesReturn)]="T_"`, `[nameof(SalesReturnLine)]="T_"`.
- **Anchor:** physical = `DeliveryOrderLine` (UnitCost, QuantityDelivered, ProductVariantId, SalesOrderLineId). Warehouse = `DeliveryOrder → SalesOrder.WarehouseId` (single per SO; no warehouse on DO). Invoice path links `DeliveryOrderLine.SalesOrderLineId ↔ CustomerInvoiceLine.SalesOrderLineId`.
- **Remaining qty** tracked at DO-line level (`Status ∈ {PendingApproval, Posted}` count). Invoice path additionally capped by `CustomerInvoiceLine.Quantity − Σ returnedViaCustomerInvoiceLine`.
- **Tests:** SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`, seed a `SalesReturn` approval chain manually before Submit; `AccountingSeeder` runs. Default costing = MovingAverage.
- **Enum values (verified):** `MovementType { In, Out, Transfer, Adjustment }`; `DeliveryOrderStatus` has `Draft, Posted`; `CustomerInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }` (mirror Supplier).

---

### Task 1: Domain — `CustomerInvoice` credit-note support

**Files:**
- Modify: `src/ErpOne.Domain/Entities/Finance/CustomerInvoice.cs`
- Test: `tests/ErpOne.UnitTests/CustomerInvoiceCreditTests.cs`

**Interfaces:**
- Produces: `CustomerInvoice.CreditedAmount` (get); `Outstanding => GrandTotal - PaidAmount - CreditedAmount`; `void ApplyCredit(decimal)`, `void ReverseCredit(decimal)`; `ApplyPayment` guard includes `CreditedAmount`.

This is the exact mirror of the committed `SupplierInvoice` credit change (Fase 2a Task 1).

- [ ] **Step 1: Write the failing unit tests** — copy `tests/ErpOne.UnitTests/SupplierInvoiceCreditTests.cs`, rename class → `CustomerInvoiceCreditTests`, and change the invoice factory to build a `CustomerInvoice`. Confirm the `CustomerInvoice` ctor + a line ctor (`CustomerInvoiceLine`) shapes:

```csharp
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class CustomerInvoiceCreditTests
{
    private static CustomerInvoice InvoiceOf(decimal grand)
    {
        // Verify CustomerInvoice ctor signature against the entity (mirror SupplierInvoice).
        var inv = new CustomerInvoice("CINV-1", 1, "IDR", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), null, null);
        // CustomerInvoiceLine(salesOrderId, salesOrderLineId, variantId, qty, unitPrice, discountPercent, taxRate)
        inv.SetLines([new CustomerInvoiceLine(1, 1, 1, 1, grand, 0m, 0m)]);
        return inv;
    }

    [Fact] public void ApplyCredit_reduces_outstanding_and_sets_status()
    { var inv = InvoiceOf(1000m); inv.ApplyCredit(400m); Assert.Equal(400m, inv.CreditedAmount); Assert.Equal(600m, inv.Outstanding); Assert.Equal(CustomerInvoiceStatus.PartiallyPaid, inv.Status); }

    [Fact] public void ApplyCredit_full_marks_paid()
    { var inv = InvoiceOf(1000m); inv.ApplyCredit(1000m); Assert.Equal(0m, inv.Outstanding); Assert.Equal(CustomerInvoiceStatus.Paid, inv.Status); }

    [Fact] public void ApplyCredit_rejects_over_outstanding()
    { var inv = InvoiceOf(1000m); inv.ApplyPayment(600m); Assert.Throws<InvalidOperationException>(() => inv.ApplyCredit(500m)); }

    [Fact] public void ApplyPayment_guard_accounts_for_credit()
    { var inv = InvoiceOf(1000m); inv.ApplyCredit(700m); Assert.Throws<InvalidOperationException>(() => inv.ApplyPayment(400m)); inv.ApplyPayment(300m); Assert.Equal(CustomerInvoiceStatus.Paid, inv.Status); Assert.Equal(0m, inv.Outstanding); }

    [Fact] public void ReverseCredit_restores_outstanding()
    { var inv = InvoiceOf(1000m); inv.ApplyCredit(400m); inv.ReverseCredit(400m); Assert.Equal(0m, inv.CreditedAmount); Assert.Equal(1000m, inv.Outstanding); Assert.Equal(CustomerInvoiceStatus.Open, inv.Status); }
}
```

> **Verify-before-embed:** the `CustomerInvoice` ctor param order (from `SupplierInvoice` it is `(invoiceNumber, supplierId, currency, invoiceDate, dueDate, supplierInvoiceNo, notes)` — Customer's is `(invoiceNumber, customerId, currency, invoiceDate, dueDate, customerRef, notes)`; confirm and adjust). `CustomerInvoiceLine` ctor confirmed: `(salesOrderId, salesOrderLineId, productVariantId, quantity, unitPrice, discountPercent, taxRateSnapshot)`.

- [ ] **Step 2: Run to verify fail** — `dotnet test tests/ErpOne.UnitTests --filter CustomerInvoiceCreditTests` → FAIL (members missing).

- [ ] **Step 3: Add `CreditedAmount`, change `Outstanding`, add credit methods, tighten `ApplyPayment`** — apply the identical change made to `SupplierInvoice`:
  - after `PaidAmount`: `public decimal CreditedAmount { get; private set; }`
  - `Outstanding => GrandTotal - PaidAmount - CreditedAmount;`
  - `ApplyPayment` guard: `if (PaidAmount + CreditedAmount + amount > GrandTotal)` and status: `(PaidAmount + CreditedAmount) >= GrandTotal ? Paid : PartiallyPaid`
  - `ReversePayment` status recompute using `(PaidAmount + CreditedAmount)`
  - add `ApplyCredit(decimal)` and `ReverseCredit(decimal)` (copy from `SupplierInvoice`, swap the enum type to `CustomerInvoiceStatus`).

- [ ] **Step 4: Run tests** — PASS (5).

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Finance/CustomerInvoice.cs tests/ErpOne.UnitTests/CustomerInvoiceCreditTests.cs
git commit -m "feat(finance): CustomerInvoice credit-note support (CreditedAmount + ApplyCredit)"
```

---

### Task 2: Domain — `SalesReturn` + `SalesReturnLine` + enums

**Files:**
- Create: `src/ErpOne.Domain/Entities/Transactions/SalesReturnStatus.cs`, `SalesReturnSource.cs`, `SalesReturnLine.cs`, `SalesReturn.cs`
- Test: `tests/ErpOne.UnitTests/SalesReturnTests.cs`

**Interfaces:**
- Produces: `SalesReturnStatus { Draft, PendingApproval, Posted }`; `SalesReturnSource { DeliveryOrder, CustomerInvoice }`; `SalesReturnLine(int deliveryOrderLineId, int? customerInvoiceLineId, int productVariantId, int warehouseId, string variantSku, string productName, int quantity, decimal unitCost, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)` + `SetUnitCost(decimal)`; `SalesReturn(string returnNumber, int customerId, SalesReturnSource sourceType, int? deliveryOrderId, int? customerInvoiceId, DateTime returnDate, string? notes)` + `SetLines`, `UpdateHeader`, `RecomputeInventoryTotal`, `Submit`, `MarkPosted`, `ReturnToDraft`.

This is the exact mirror of the committed `PurchaseReturn`/`PurchaseReturnLine`/enums (Fase 2a Task 2), with the field renames:
`GoodsReceiptLineId → DeliveryOrderLineId`, `SupplierInvoiceLineId → CustomerInvoiceLineId`, `SupplierId → CustomerId`, `GoodsReceiptId → DeliveryOrderId`, `SupplierInvoiceId → CustomerInvoiceId`, `PurchaseReturnSource.GoodsReceipt/SupplierInvoice → SalesReturnSource.DeliveryOrder/CustomerInvoice`.

- [ ] **Step 1: Write the failing unit tests** — copy `tests/ErpOne.UnitTests/PurchaseReturnTests.cs` → `SalesReturnTests.cs`, apply the renames. The `GrnLine` helper becomes `DoLine(int qty, decimal cost)` building a `SalesReturnLine(deliveryOrderLineId: 1, customerInvoiceLineId: null, productVariantId: 1, warehouseId: 1, "SKU", "P", qty, cost, cost, 0m, 0m)`. `NewGrnReturn()` becomes `NewDoReturn()` building `new SalesReturn("CN-1", 1, SalesReturnSource.DeliveryOrder, deliveryOrderId: 10, customerInvoiceId: null, new DateTime(2026,1,5), null)`. Assertions identical (totals, inventory, recompute, submit/lifecycle/draft-guard).

- [ ] **Step 2: Run to verify fail** — FAIL (types missing).

- [ ] **Step 3: Create the two enums**

```csharp
// SalesReturnStatus.cs
namespace ErpOne.Domain.Entities;
public enum SalesReturnStatus { Draft, PendingApproval, Posted }
```
```csharp
// SalesReturnSource.cs
namespace ErpOne.Domain.Entities;
public enum SalesReturnSource { DeliveryOrder, CustomerInvoice }
```

- [ ] **Step 4: Create `SalesReturnLine`** — copy `PurchaseReturnLine.cs`, rename `GoodsReceiptLineId → DeliveryOrderLineId` and `SupplierInvoiceLineId → CustomerInvoiceLineId`. Full content:

```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Baris retur penjualan; jangkar fisik = DeliveryOrderLineId (stok, sisa qty, COGS).</summary>
public class SalesReturnLine
{
    public int Id { get; private set; }
    public int SalesReturnId { get; private set; }
    public int DeliveryOrderLineId { get; private set; }
    public int? CustomerInvoiceLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }          // COGS snapshot from DO line (Dr Inventory / Cr COGS)
    public decimal UnitPrice { get; private set; }         // invoice path; DO path: = UnitCost
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private SalesReturnLine() { } // EF Core

    public SalesReturnLine(int deliveryOrderLineId, int? customerInvoiceLineId, int productVariantId,
        int warehouseId, string variantSku, string productName, int quantity, decimal unitCost,
        decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (deliveryOrderLineId <= 0) throw new ArgumentException("DeliveryOrderLineId is required.", nameof(deliveryOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        DeliveryOrderLineId = deliveryOrderLineId;
        CustomerInvoiceLineId = customerInvoiceLineId;
        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        VariantSku = variantSku;
        ProductName = productName;
        Quantity = quantity;
        UnitCost = unitCost;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxRateSnapshot = taxRateSnapshot;
        Recompute();
    }

    public void SetUnitCost(decimal unitCost)
    {
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        UnitCost = unitCost;
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

- [ ] **Step 5: Create `SalesReturn`** — copy `PurchaseReturn.cs`, apply renames (`SupplierId→CustomerId`, `GoodsReceiptId→DeliveryOrderId`, `SupplierInvoiceId→CustomerInvoiceId`, `PurchaseReturnSource→SalesReturnSource`, `PurchaseReturnStatus→SalesReturnStatus`, `PurchaseReturnLine→SalesReturnLine`). The ctor guards: `sourceType == SalesReturnSource.DeliveryOrder ⇒ deliveryOrderId > 0`; `== CustomerInvoice ⇒ customerInvoiceId > 0`. Everything else (SetLines/RecomputeTotals/RecomputeInventoryTotal/Submit/MarkPosted/ReturnToDraft/EnsureDraft/SetHeader) is identical.

- [ ] **Step 6: Run tests** — PASS (6).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Domain/Entities/Transactions/SalesReturn*.cs tests/ErpOne.UnitTests/SalesReturnTests.cs
git commit -m "feat(sales): SalesReturn + SalesReturnLine domain entities"
```

---

### Task 3: EF mapping + migration + constants wiring

**Files:**
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (DbSets; new entity configs after `CustomerInvoiceLine` config; `CustomerInvoice.CreditedAmount` mapping; tablePrefixes; NumberSequence seed Id=17)
- Modify: `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs` (+ `SalesReturn`)
- Modify: `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs` (+ `SalesReturn`)
- Create: migration `AddSalesReturn`

- [ ] **Step 1: Enum + constant** — `ApprovalDocumentType` append `SalesReturn` (after `PurchaseReturn`); `DocumentTypes` add `public const string SalesReturn = "SalesReturn";`.

- [ ] **Step 2: DbSets** — after the PurchaseReturn DbSets:
```csharp
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
```

- [ ] **Step 3: EF configs** — after the `PurchaseReturnLine` config block, add configs mirroring PurchaseReturn/Line (enum-as-string via `HasConversion<string>().HasMaxLength(20)`, decimals `HasPrecision(18,2)`, `DiscountPercent`/`TaxRateSnapshot` `HasPrecision(5,2)`, `ReturnNumber` maxlen 40 unique, `VariantSku` 60, `ProductName` 200, FK to `Customer` restrict, `HasMany(Lines).WithOne().HasForeignKey(l => l.SalesReturnId).OnDelete(Cascade)` + `SetPropertyAccessMode(Field)`, index `HasIndex(l => l.DeliveryOrderLineId)`):

```csharp
        modelBuilder.Entity<SalesReturn>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ReturnNumber).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.ReturnNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.InventoryTotal).HasPrecision(18, 2);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SalesReturn.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SalesReturnLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.VariantSku).HasMaxLength(60).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DeliveryOrderLineId);
        });
```

- [ ] **Step 4: `CustomerInvoice.CreditedAmount` mapping** — in the `CustomerInvoice` config block (unique context: `e.HasOne<Customer>()...`), after `e.Property(x => x.PaidAmount).HasPrecision(18, 2);` add `e.Property(x => x.CreditedAmount).HasPrecision(18, 2);` (keep `e.Ignore(x => x.Outstanding);`).

- [ ] **Step 5: tablePrefixes** — in the `// Transaksi` group add `[nameof(SalesReturn)] = "T_",` and `[nameof(SalesReturnLine)] = "T_",`.

- [ ] **Step 6: NumberSequence seed** — after the Id=16 `PurchaseReturn` row (add a comma), add:
```csharp
                new { Id = 17, Code = "SalesReturn", Prefix = "CN", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 7: Migration** — `dotnet ef migrations add AddSalesReturn --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`. Expect `CreateTable("T_SalesReturns")` + `CreateTable("T_SalesReturnLines")`, `AddColumn CreditedAmount` on the customer-invoices table (default 0), and `InsertData` NumberSequence Id=17.

- [ ] **Step 8: Build** — `dotnet build -clp:ErrorsOnly` → 0/0 (model-build prefix guard accepts the new tables).

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/ src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs
git commit -m "feat(sales): EF mapping + migration + constants for SalesReturn"
```

---

### Task 4: Application layer — DTOs, interface, validator

**Files:**
- Create: `src/ErpOne.Application/Sales/SalesReturns/SalesReturnDtos.cs`, `ISalesReturnService.cs`, `SalesReturnValidators.cs`

Mirror `src/ErpOne.Application/Purchasing/PurchaseReturns/*` with renames: `GoodsReceiptLineId → DeliveryOrderLineId`, `SupplierInvoiceLineId → CustomerInvoiceLineId`, `GoodsReceiptId → DeliveryOrderId`, `SupplierInvoiceId → CustomerInvoiceId`, `GrnNumber → DoNumber`, `SupplierName → CustomerName`, `SupplierId → CustomerId`, `"GoodsReceipt"/"SupplierInvoice" → "DeliveryOrder"/"CustomerInvoice"`, `PurchaseReturn* → SalesReturn*`, and the service methods `GetReturnableGrnsAsync → GetReturnableDeliveryOrdersAsync`, `GetReturnableInvoicesAsync` (same name).

- [ ] **Step 1: DTOs** — copy `PurchaseReturnDtos.cs` (namespace `ErpOne.Application.Sales.SalesReturns`, `using ErpOne.Application.Approvals;`) with the renames. Records: `ReturnableLineDto`, `ReturnableSourceDto`, `ReturnableSourceOptionDto`, `SalesReturnLineInput(int DeliveryOrderLineId, int? CustomerInvoiceLineId, int Quantity)`, `CreateSalesReturnRequest(string SourceType, int? DeliveryOrderId, int? CustomerInvoiceId, DateTime ReturnDate, string? Notes, IReadOnlyList<SalesReturnLineInput> Lines)`, `UpdateSalesReturnRequest`, `SalesReturnLineDto`, `SalesReturnDto` (with `DoNumber`/`InvoiceNumber`/`CustomerName`), `SalesReturnListItemDto`.

- [ ] **Step 2: Interface** — copy `IPurchaseReturnService.cs` → `ISalesReturnService` (namespace `ErpOne.Application.Sales.SalesReturns`, `using ErpOne.Application.Common; using ErpOne.Domain.Entities;`), rename `GetReturnableGrnsAsync → GetReturnableDeliveryOrdersAsync`, `PurchaseReturnStatus → SalesReturnStatus`, DTO types.

- [ ] **Step 3: Validator** — copy `PurchaseReturnValidators.cs` → `CreateSalesReturnValidator`: `SourceType ∈ {DeliveryOrder, CustomerInvoice}`; `DeliveryOrderId` NotNull when DeliveryOrder; `CustomerInvoiceId` NotNull when CustomerInvoice; Lines NotEmpty; per line `Quantity > 0` & `DeliveryOrderLineId > 0`; `CustomerInvoiceLineId is > 0` when CustomerInvoice path.

- [ ] **Step 4: Build** — `dotnet build src/ErpOne.Application -clp:ErrorsOnly` → 0/0.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Application/Sales/SalesReturns/
git commit -m "feat(sales): SalesReturn DTOs, service interface, validator"
```

---

### Task 5: GL — `PostSalesReturnAsync`

**Files:**
- Modify: `src/ErpOne.Application/Accounting/IJournalPostingService.cs` (+ method)
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs` (+ impl)

**Interfaces:**
- Produces: `IJournalPostingService.PostSalesReturnAsync(SalesReturn r, CancellationToken)`.

- [ ] **Step 1: Interface** — after `PostPurchaseReturnAsync`, add `Task PostSalesReturnAsync(SalesReturn r, CancellationToken ct = default);`.

- [ ] **Step 2: Implement** — add after `PostPurchaseReturnAsync`:

```csharp
    public async Task PostSalesReturnAsync(SalesReturn r, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");

        // Goods back into stock (reverse of the delivery COGS entry): Dr Inventory / Cr COGS.
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (inventory, r.InventoryTotal, 0m, "Inventory returned"),
            (cogs, 0m, r.InventoryTotal, "COGS reversed"),
        };

        // Invoice path: credit note reduces receivable — Dr Sales / Dr Output Tax / Cr AR.
        if (r.SourceType == SalesReturnSource.CustomerInvoice)
        {
            var ar = RequireAccount(cfg.ArAccountId, "Accounts Receivable");
            var sales = RequireAccount(cfg.SalesAccountId, "Sales");
            var net = r.Subtotal - r.DiscountTotal;
            lines.Add((sales, net, 0m, "Revenue reversed (credit note)"));
            if (r.TaxTotal > 0m)
                lines.Add((RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), r.TaxTotal, 0m, "Output VAT reversed"));
            lines.Add((ar, 0m, r.GrandTotal, "Credit note to customer"));
        }
        await PostBalancedAsync(r.ReturnDate, $"Sales Return {r.ReturnNumber}", "SalesReturn", r.Id, lines, ct);
    }
```

> Balance (invoice path): `Dr = InventoryTotal + net + tax`; `Cr = InventoryTotal + GrandTotal = InventoryTotal + net + tax`. ✓ `PostBalancedAsync` drops zero lines.

- [ ] **Step 3: Build** — `dotnet build src/ErpOne.Infrastructure -clp:ErrorsOnly` → 0/0.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Application/Accounting/IJournalPostingService.cs src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs
git commit -m "feat(accounting): PostSalesReturnAsync (COGS reversal & credit-note journals)"
```

---

### Task 6: Infrastructure — `SalesReturnService`

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Sales/SalesReturnService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs` (+ `using ErpOne.Application.Sales.SalesReturns;` + registration)
- Modify: `src/ErpOne.Infrastructure/Services/Finance/CustomerReceiptService.cs` (outstanding guard)
- Test: `tests/ErpOne.IntegrationTests/SalesReturnServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `IApprovalService`, `IStockService`, `ICostingService`, `IValidator<CreateSalesReturnRequest>`, `IDocumentNumberService`, `IJournalPostingService`.

Mirror `PurchaseReturnService`. The DIFFERENCES from the purchase mirror (implement carefully):

**(a) Returnable discovery — DO path** (`GetReturnableDeliveryOrdersAsync` / source): join `DeliveryOrders` (Status Posted) → `SalesOrders` (for `WarehouseId` + `CustomerId`) → `Customers`. Remaining per DO line = `QuantityDelivered − returnedByDoLine`. Load DO with `.Include(d => d.Lines)`. WarehouseId = `SalesOrder.WarehouseId`. UnitCost = `DeliveryOrderLine.UnitCost`; UnitPrice/Disc/Tax = 0 (DO path).

**(b) Returnable discovery — Invoice path:** load `CustomerInvoice` with `.Include(i => i.Lines)`. For each invoice line (a SalesOrderLine `L`), find the `DeliveryOrderLine`(s) with `SalesOrderLineId == L`. Emit one candidate per DO line: `DeliveryOrderLineId`, `CustomerInvoiceLineId = invoiceLine.Id`, variant, warehouse (`SO.WarehouseId`), `UnitCost = doLine.UnitCost`, `UnitPrice/Disc/Tax = invoiceLine.*`, `RemainingQty = min(doLineDelivered − returnedByDoLine, invoiceLine.Quantity − returnedByInvoiceLine)`. Use helpers `ReturnedQtyByDoLineAsync` and `ReturnedQtyByInvoiceLineAsync` (mirror the purchase helpers, grouping `SalesReturnLine.DeliveryOrderLineId` / `CustomerInvoiceLineId` where parent status ∈ {PendingApproval, Posted}).

> Known limitation (spec §Batasan): if one SO line has multiple DO lines, each candidate shows the same invoice-line remaining. `BuildLines` MUST additionally enforce the per-`CustomerInvoiceLineId` aggregate cap within the request (sum requested qty per invoice line ≤ its returnable remaining) to prevent cross-sibling over-return. Add that check in `BuildLines`.

**(c) PostAsync — INBOUND, no on-hand guard:**

```csharp
    private async Task PostAsync(SalesReturn r, CancellationToken ct)
    {
        foreach (var line in r.Lines)
        {
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, line.WarehouseId, MovementType.In,
                line.Quantity, line.UnitCost, r.ReturnDate, "SalesReturn", r.Id, r.ReturnNumber));
            await db.UpsertStockAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, ct);
            await costing.OnInboundAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, line.UnitCost, ct);
        }
        r.RecomputeInventoryTotal();

        if (r.SourceType == SalesReturnSource.CustomerInvoice)
        {
            var inv = await db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == r.CustomerInvoiceId, ct)
                ?? throw Fail("Customer invoice not found.");
            if (r.GrandTotal > inv.Outstanding) throw Fail("Retur melebihi Outstanding invoice.");
            inv.ApplyCredit(r.GrandTotal);
        }
        await journalPoster.PostSalesReturnAsync(r, ct);
        r.MarkPosted();
    }
```

> `OnInboundAsync` is called AFTER `UpsertStockAsync` (seam contract). Cost is the explicit `line.UnitCost` (DO COGS snapshot) — no seam outbound call.

**(d) Everything else** (Create/Update/Delete, Submit/Approve/Reject, GetById/GetPaged, `BuildLines`, `GetReturnableSourceForUpdateAsync`, `VariantInfoAsync`, `WarehouseNameAsync`, `Fail`) mirrors `PurchaseReturnService` with type/name renames. `GetByIdAsync` resolves `DoNumber` from `DeliveryOrders` and `InvoiceNumber` from `CustomerInvoices`; `CustomerName` from `Customers`.

- [ ] **Step 1: Write the failing integration tests** — mirror `tests/ErpOne.IntegrationTests/PurchaseReturnServiceTests.cs`. Seed helper `SeedPostedDoAsync(sp, qty, unitCost)` returns `(customerId, doId, doLineId, variantId, warehouseId)`: build Customer + Warehouse + Product/variant, a confirmed `SalesOrder`, then a `DeliveryOrder` posted via `IDeliveryOrderService.PostAsync` (mirror `DeliveryOrderServiceTests` seed) so stock goes OUT and the DO line's `UnitCost` COGS snapshot is set; query the `doLineId`. `SeedCustomerInvoiceAsync(sp, qty, unitCost)` chains `SeedPostedDoAsync` then creates a `CustomerInvoice` from the SO via `ICustomerInvoiceService` and returns invoice + line ids + grandTotal.

Tests (mirror the 5 purchase tests):
1. `Do_path_full_return_increases_stock_and_posts_cogs_journal` — DO qty10@100 → return 10 → approve → on-hand +10, JE `Dr Inventory 1000 = Cr COGS 1000`, AR untouched.
2. `Partial_returns_track_remaining_and_reject_over_return`.
3. (No on-hand guard — replace the purchase "insufficient on-hand" test with) `Fifo_return_creates_layer_at_do_cost` (optional): under FIFO, after return the seam has a layer at the DO cost — assert a subsequent `GetOutboundUnitCostAsync` sees it. If skipped, keep 4 tests.
4. `Invoice_path_return_credits_outstanding_and_posts_balanced_journal` — `CustomerInvoice.CreditedAmount == grandTotal`, `Outstanding == 0`, JE balanced.
5. `Return_over_invoice_outstanding_is_rejected` — receive against invoice down to a small Outstanding (via `ICustomerReceiptService` or `inv.ApplyPayment`) then full return → `ValidationException` on approve.

> **Verify-before-embed:** the DO seed path (SO create + confirm + DO create + post), `IDeliveryOrderService`/`ICustomerInvoiceService` method shapes, and `SalesOrder` creation — copy from `DeliveryOrderServiceTests` / `CustomerInvoiceServiceTests`. `StockMovement` ctor arg order + `MovementType.In`. `db.DeliveryOrders`/`db.SalesOrders`/`db.Customers`/`db.CustomerInvoices` DbSet names.

- [ ] **Step 2: Run to verify fail** — `dotnet test tests/ErpOne.IntegrationTests --filter SalesReturnServiceTests` → FAIL (no registered impl).

- [ ] **Step 3: Implement `SalesReturnService`** — copy `PurchaseReturnService.cs` into `src/ErpOne.Infrastructure/Services/Sales/SalesReturnService.cs`, apply the renames and the DO/invoice-path differences (a)–(d). Remember `.Include(Lines)` on the DO and CustomerInvoice loads in `GetReturnableSourceAsync` (this was the bug fixed in Fase 2a).

- [ ] **Step 4: Register DI** — add `using ErpOne.Application.Sales.SalesReturns;` and `services.AddScoped<ISalesReturnService, SalesReturnService>();` (near `IPurchaseReturnService`).

- [ ] **Step 5: Update `CustomerReceiptService` outstanding guard** — mirror the Fase 2a `SupplierPaymentService` change at its three spots: the open-invoice filter `i.GrandTotal - i.PaidAmount > 0` → `- i.CreditedAmount`, the `OpenInvoiceDto` projection outstanding, and the allocation guard `var outstanding = inv.GrandTotal - inv.PaidAmount;` → `var outstanding = inv.Outstanding;`.

- [ ] **Step 6: Run** — `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter "SalesReturnServiceTests|CustomerReceiptServiceTests"` → PASS. Fix seed/arg shapes flagged above until green.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Sales/SalesReturnService.cs src/ErpOne.Infrastructure/DependencyInjection.cs src/ErpOne.Infrastructure/Services/Finance/CustomerReceiptService.cs tests/ErpOne.IntegrationTests/SalesReturnServiceTests.cs
git commit -m "feat(sales): SalesReturnService (returnable, CRUD, approval, seam inbound, credit note)"
```

---

### Task 7: Web — Index / Form / Detail + menu + seeder

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`, `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs`
- Create: `src/ErpOne.Web/Components/Pages/Transactions/SalesReturns/SalesReturnIndex.razor`, `SalesReturnForm.razor`, `SalesReturnDetail.razor`

Mirror the committed `Components/Pages/Transactions/PurchaseReturns/*` pages with renames: routes `/transactions/sales-returns`, policy prefix `transactions.sales-returns`, service `ISalesReturnService`, DTO namespace `ErpOne.Application.Sales.SalesReturns`, labels Purchase→Sales, GRN→Delivery Order, Supplier→Customer, path options `DeliveryOrder`/`CustomerInvoice`, source loaders `GetReturnableDeliveryOrdersAsync`/`GetReturnableInvoicesAsync`, icon `bi-arrow-return-right`.

- [ ] **Step 1: Menu** — in `AppMenus.cs` add `private static AppAction[] SalesReturnActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost];` and in the Transaksi group `new("transactions.sales-returns", "Sales Return", "bi-arrow-return-right", SalesReturnActions),` (after purchase-returns).

- [ ] **Step 2: Approval-chain seed** — in `BootstrapSeeder.cs`, after the Purchase Return chain block:
```csharp
        // Seed rantai approval default untuk Sales Return (idempotent).
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SalesReturn))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SalesReturn, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 3: `SalesReturnIndex.razor`** — copy `PurchaseReturnIndex.razor`, apply renames; columns: Return, Date, Source, Customer, Lines, Grand Total, Status. Status chips from `Enum.GetValues<SalesReturnStatus>()`.

- [ ] **Step 4: `SalesReturnForm.razor`** — copy `PurchaseReturnForm.razor`, apply renames. Path options `DeliveryOrder`/`CustomerInvoice`; source loaders `GetReturnableDeliveryOrdersAsync()` / `GetReturnableInvoicesAsync()`; `LineRow` field `GoodsReceiptLineId → DeliveryOrderLineId`; `CreateSalesReturnRequest(_sourceType, _sourceType=="DeliveryOrder" ? _sourceDocId : null, _sourceType=="CustomerInvoice" ? _sourceDocId : null, _date, _notes, lines)`.

- [ ] **Step 5: `SalesReturnDetail.razor`** — copy `PurchaseReturnDetail.razor`, apply renames (service `ISalesReturnService`, policies, labels, source line "@_r.DoNumber"/"@_r.InvoiceNumber"). Approval plumbing (CascadingParameter, `EvaluateCanApproveAsync` with policy `transactions.sales-returns.approve`, `RunAsync`, Submit/Approve/Reject, Edit/Delete on Draft) identical.

- [ ] **Step 6: Build** — `dotnet build -clp:ErrorsOnly` → 0/0.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs src/ErpOne.Web/Components/Pages/Transactions/SalesReturns/
git commit -m "feat(sales): Sales Return web pages + menu + approval seed"
```

---

### Task 8: Final regression + self-review

- [ ] **Step 1: Bump NumberSequence count assertion** — `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`: expected count 16 → 17.

- [ ] **Step 2: Full build + test** — `dotnet build -clp:ErrorsOnly` then `dotnet test`. Expect 0/0 and all green. Baseline (371) + Task-1 (5) + Task-2 (6) + Task-6 (4–5) ≈ **386+**.

- [ ] **Step 3: Confirm existing finance/GL untouched** — `dotnet test tests/ErpOne.IntegrationTests --filter "CustomerInvoiceServiceTests|CustomerReceiptServiceTests|DeliveryOrderServiceTests|JournalPostingServiceTests|ArApAgingReportServiceTests"` → PASS (Outstanding change additive; CreditedAmount=0 for pre-existing invoices).

- [ ] **Step 4: Straggler grep** — `git grep -n "SalesReturn" -- src/ErpOne.Infrastructure/Services src/ErpOne.Application` → confined to the new service, DTOs, GL method, DI.

- [ ] **Step 5: Final commit (if fixes)**

```bash
git add -A
git commit -m "chore(sales): Sales Return (Fase 2b) complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 Domain (SalesReturn/Line + enums, CustomerInvoice credit) → Tasks 1,2 ✓; §2 enums/constants/NumberSequence/prefix/CreditedAmount column → Task 3 ✓; §3 Application → Task 4 ✓; §4 Infrastructure service (returnable both paths, remaining, inbound-costed PostAsync) → Task 6 ✓; §5 GL PostSalesReturnAsync (both branches, balanced) → Task 5 ✓; §6 Web → Task 7 ✓; §7 tests → Tasks 1,2,6 ✓; CustomerReceipt guard → Task 6 Step 5 ✓; NumberSequence bump → Task 8 ✓.

**Key asymmetry handled:** physical anchor `DeliveryOrderLine`, invoice-path link via `SalesOrderLineId` (Task 6 (b)); multi-DO-per-SO-line over-return prevented by the per-`CustomerInvoiceLineId` aggregate cap in `BuildLines` (Task 6 note). Inbound (no on-hand guard) with explicit DO COGS cost (Task 6 (c)).

**Type consistency:** `SalesReturn`/`SalesReturnLine` ctors + methods consistent Tasks 2↔6↔7. `ISalesReturnService` identical Tasks 4↔6↔7. `PostSalesReturnAsync` consistent Tasks 5↔6. Account keys match `PostingConfiguration`. `ApprovalDocumentType.SalesReturn`, `DocumentTypes.SalesReturn`, NumberSequence Id=17 consistent Tasks 3,6,7. `MovementType.In` verified.

**Verify-before-embed flags:** `CustomerInvoice` ctor param order; DO/SO/CustomerInvoice seed helpers + `IDeliveryOrderService`/`ICustomerInvoiceService` shapes (copy from existing tests); DbSet names (`DeliveryOrders`, `SalesOrders`, `Customers`, `CustomerInvoices`); `.Include(Lines)` on DO/invoice loads; `CustomerReceiptService` three outstanding spots; NumberSequence assert count.
