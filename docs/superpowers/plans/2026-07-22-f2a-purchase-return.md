# Fase 2a — Purchase Return (Debit Note) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return-to-supplier document with approval (`Draft → PendingApproval → Posted`) via two source paths — GRN (pre-invoice: `Dr GR-IR / Cr Inventory`) and Supplier Invoice (post-invoice debit note: `Dr AP / Cr Input Tax / Cr Inventory (+ GR-IR variance)`). Stock leaves **through the costing seam** (`GetOutboundUnitCostAsync`) so it is correct for all four HPP methods. Partial & multiple returns are guarded by per-GRN-line remaining qty.

**Architecture:** New `PurchaseReturn` aggregate (+`PurchaseReturnLine`) mirrors `StockTransfer`'s approval lifecycle. `PurchaseReturnService` mirrors `StockTransferService` (own tx per public method; private `PostAsync` mutates + `MarkPosted`, caller saves/commits). Posting: stock out via seam per line, then AP credit (Invoice path) via new `SupplierInvoice.ApplyCredit`, then GL via new `IJournalPostingService.PostPurchaseReturnAsync`. Web = Index `.pi` / Form `.cf` / Detail `.pf` mirroring the StockTransfer pages.

**Tech Stack:** .NET 10, EF Core (SQLite in-memory per test class via `EnsureCreated`), xUnit, Blazor Server, FluentValidation.

## Global Constraints

- **Namespace flat:** all domain entities use `namespace ErpOne.Domain.Entities;` regardless of subfolder. Entities extend `AuditableEntity` (provides `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy`, auto-stamped by `AppDbContext` on save — never set manually) and declare their own `public int Id { get; private set; }`.
- **Entity pattern:** `private readonly List<TLine> _lines = [];` exposed as `IReadOnlyCollection<TLine> Lines => _lines;`; two ctors — `private Ctor() { } // EF Core` + validating public ctor (`ArgumentException` + `nameof`); header mutation in private `SetHeader`; guard `private void EnsureDraft()` throwing `InvalidOperationException`.
- **Rounding everywhere:** `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **Costing seam (decision 2026-07-22):** Purchase Return stock-out routes through `ICostingService.GetOutboundUnitCostAsync(variantId, warehouseId, qty, ct)` (unit cost; caller multiplies by qty) — consistent with DO/POS/Transfer. `InventoryTotal` is the sum of actual seam costs, computed **at post time**. Do NOT recompute moving-average on outbound (seam handles per-method behavior).
- **Service pattern:** primary-ctor DI; `private const ApprovalDocumentType DocType = ApprovalDocumentType.PurchaseReturn;`; every mutating public method wraps `await using var tx = await db.Database.BeginTransactionAsync(ct);` ... `await tx.CommitAsync(ct);`; approval flow `ResetAsync`→`SubmitAsync(bool)` on submit, `ApproveAsync(bool)` on approve, `RejectAsync`+`ReturnToDraft`+`ResetAsync` on reject; `private static ValidationException Fail(string m) => new([new FluentValidation.Results.ValidationFailure("PurchaseReturn", m)]);`.
- **GL:** `JournalPostingService` enlists in caller tx (no own tx), idempotent on `(SourceType, SourceId)` = `("PurchaseReturn", r.Id)`; accounts from single-row `PostingConfiguration` via `RequireAccount(cfg.XId, "label")` (fail-hard). Keys: `InventoryAccountId`, `GrIrAccountId`, `ApAccountId`, `InputTaxAccountId`.
- **Numbering:** `DocumentTypes.PurchaseReturn` (namespace `ErpOne.Application.Numbering`); `NumberSequence` seed Id=16 Code="PurchaseReturn" Prefix="DN".
- **Table prefix:** `[nameof(PurchaseReturn)]="T_"`, `[nameof(PurchaseReturnLine)]="T_"` — else the model-build guard throws.
- **Remaining qty** tracked at GRN-line level across both paths (`Status ∈ {PendingApproval, Posted}` count against remaining). Invoice path additionally capped by `invoiceLine.Quantity − Σ returnedViaInvoiceLine`.
- **Tests:** SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`, seed a `PurchaseReturn` approval chain manually before Submit; `AccountingSeeder` runs so GL mapping exists. Default costing = MovingAverage (so seam cost = GRN cost for a single-GRN variant → spec's expected numbers hold).

---

### Task 1: Domain — `SupplierInvoice` credit-note support

**Files:**
- Modify: `src/ErpOne.Domain/Entities/Finance/SupplierInvoice.cs`
- Test: `tests/ErpOne.UnitTests/SupplierInvoiceCreditTests.cs`

**Interfaces:**
- Produces: `SupplierInvoice.CreditedAmount` (get); `Outstanding => GrandTotal - PaidAmount - CreditedAmount`; `void ApplyCredit(decimal)`, `void ReverseCredit(decimal)`; `ApplyPayment` guard now includes `CreditedAmount`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/ErpOne.UnitTests/SupplierInvoiceCreditTests.cs
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class SupplierInvoiceCreditTests
{
    private static SupplierInvoice InvoiceOf(decimal grand)
    {
        var inv = new SupplierInvoice("INV-1", 1, "IDR", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), null, null);
        inv.SetLines([new SupplierInvoiceLine(1, 1, 1, 1, grand, 0m, 0m)]); // qty1 × price=grand, no disc/tax
        return inv;
    }

    [Fact]
    public void ApplyCredit_reduces_outstanding_and_sets_status()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(400m);
        Assert.Equal(400m, inv.CreditedAmount);
        Assert.Equal(600m, inv.Outstanding);
        Assert.Equal(SupplierInvoiceStatus.PartiallyPaid, inv.Status);
    }

    [Fact]
    public void ApplyCredit_full_marks_paid()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(1000m);
        Assert.Equal(0m, inv.Outstanding);
        Assert.Equal(SupplierInvoiceStatus.Paid, inv.Status);
    }

    [Fact]
    public void ApplyCredit_rejects_over_outstanding()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyPayment(600m);
        Assert.Throws<InvalidOperationException>(() => inv.ApplyCredit(500m)); // 600 paid + 500 credit > 1000
    }

    [Fact]
    public void ApplyPayment_guard_accounts_for_credit()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(700m);
        Assert.Throws<InvalidOperationException>(() => inv.ApplyPayment(400m)); // 700 credit + 400 pay > 1000
        inv.ApplyPayment(300m); // exactly fills
        Assert.Equal(SupplierInvoiceStatus.Paid, inv.Status);
        Assert.Equal(0m, inv.Outstanding);
    }

    [Fact]
    public void ReverseCredit_restores_outstanding()
    {
        var inv = InvoiceOf(1000m);
        inv.ApplyCredit(400m);
        inv.ReverseCredit(400m);
        Assert.Equal(0m, inv.CreditedAmount);
        Assert.Equal(1000m, inv.Outstanding);
        Assert.Equal(SupplierInvoiceStatus.Open, inv.Status);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.UnitTests --filter SupplierInvoiceCreditTests`
Expected: FAIL — `CreditedAmount`/`ApplyCredit`/`ReverseCredit` do not exist.

- [ ] **Step 3: Add `CreditedAmount`, change `Outstanding`, add credit methods, tighten `ApplyPayment`**

In `SupplierInvoice.cs`, add the property after `PaidAmount`:

```csharp
    public decimal CreditedAmount { get; private set; }
```

Change the `Outstanding` expression from `GrandTotal - PaidAmount` to:

```csharp
    public decimal Outstanding => GrandTotal - PaidAmount - CreditedAmount;
```

In `ApplyPayment`, change the guard line from `if (PaidAmount + amount > GrandTotal)` to:

```csharp
        if (PaidAmount + CreditedAmount + amount > GrandTotal)
```

Add these two methods after `ReversePayment`:

```csharp
    /// <summary>Terapkan nota kredit (retur pembelian) — mengurangi Outstanding tanpa kas keluar.</summary>
    public void ApplyCredit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Credit amount must be > 0.", nameof(amount));
        if (Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot credit a cancelled invoice.");
        if (PaidAmount + CreditedAmount + amount > GrandTotal)
            throw new InvalidOperationException("Credit exceeds the invoice outstanding amount.");
        CreditedAmount += amount;
        Status = (PaidAmount + CreditedAmount) >= GrandTotal
            ? SupplierInvoiceStatus.Paid
            : (PaidAmount + CreditedAmount) > 0 ? SupplierInvoiceStatus.PartiallyPaid : SupplierInvoiceStatus.Open;
    }

    /// <summary>Balikkan nota kredit (kelengkapan; tak dipakai v1 — tanpa void retur).</summary>
    public void ReverseCredit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Reversal amount must be > 0.", nameof(amount));
        if (amount > CreditedAmount) throw new InvalidOperationException("Reversal exceeds the credited amount.");
        CreditedAmount -= amount;
        Status = (PaidAmount + CreditedAmount) <= 0
            ? SupplierInvoiceStatus.Open
            : (PaidAmount + CreditedAmount) >= GrandTotal ? SupplierInvoiceStatus.Paid : SupplierInvoiceStatus.PartiallyPaid;
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ErpOne.UnitTests --filter SupplierInvoiceCreditTests`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Domain/Entities/Finance/SupplierInvoice.cs tests/ErpOne.UnitTests/SupplierInvoiceCreditTests.cs
git commit -m "feat(finance): SupplierInvoice credit-note support (CreditedAmount + ApplyCredit)"
```

---

### Task 2: Domain — `PurchaseReturn` + `PurchaseReturnLine` + enums

**Files:**
- Create: `src/ErpOne.Domain/Entities/Transactions/PurchaseReturnStatus.cs`
- Create: `src/ErpOne.Domain/Entities/Transactions/PurchaseReturnSource.cs`
- Create: `src/ErpOne.Domain/Entities/Transactions/PurchaseReturnLine.cs`
- Create: `src/ErpOne.Domain/Entities/Transactions/PurchaseReturn.cs`
- Test: `tests/ErpOne.UnitTests/PurchaseReturnTests.cs`

**Interfaces:**
- Produces: `PurchaseReturnStatus { Draft, PendingApproval, Posted }`; `PurchaseReturnSource { GoodsReceipt, SupplierInvoice }`; `PurchaseReturnLine(int goodsReceiptLineId, int? supplierInvoiceLineId, int productVariantId, int warehouseId, string variantSku, string productName, int quantity, decimal unitCost, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)` + `void SetUnitCost(decimal)`; `PurchaseReturn(string returnNumber, int supplierId, PurchaseReturnSource sourceType, int? goodsReceiptId, int? supplierInvoiceId, DateTime returnDate, string? notes)` + `SetLines(IEnumerable<PurchaseReturnLine>)`, `UpdateHeader(DateTime, string?)`, `RecomputeInventoryTotal()`, `Submit()`, `MarkPosted()`, `ReturnToDraft(string)`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/ErpOne.UnitTests/PurchaseReturnTests.cs
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PurchaseReturnTests
{
    private static PurchaseReturnLine GrnLine(int qty, decimal cost) =>
        new(goodsReceiptLineId: 1, supplierInvoiceLineId: null, productVariantId: 1, warehouseId: 1,
            variantSku: "SKU", productName: "P", quantity: qty, unitCost: cost,
            unitPrice: cost, discountPercent: 0m, taxRateSnapshot: 0m);

    private static PurchaseReturn NewGrnReturn() =>
        new("DN-1", 1, PurchaseReturnSource.GoodsReceipt, goodsReceiptId: 10, supplierInvoiceId: null,
            new DateTime(2026, 1, 5), null);

    [Fact]
    public void SetLines_recomputes_totals_including_inventory()
    {
        var r = NewGrnReturn();
        r.SetLines([GrnLine(10, 100m)]); // GRN path: price=cost=100, no disc/tax
        Assert.Equal(1000m, r.Subtotal);
        Assert.Equal(0m, r.DiscountTotal);
        Assert.Equal(0m, r.TaxTotal);
        Assert.Equal(1000m, r.GrandTotal);
        Assert.Equal(1000m, r.InventoryTotal);
        Assert.Single(r.Lines);
    }

    [Fact]
    public void Invoice_line_recompute_applies_discount_and_tax()
    {
        var line = new PurchaseReturnLine(1, 5, 1, 1, "SKU", "P", quantity: 10,
            unitCost: 100m, unitPrice: 120m, discountPercent: 10m, taxRateSnapshot: 11m);
        Assert.Equal(1200m, line.LineSubtotal);       // 10 × 120
        Assert.Equal(120m, line.LineDiscount);        // 10%
        Assert.Equal(118.80m, line.LineTax);          // (1200-120) × 11%
        Assert.Equal(1198.80m, line.LineTotal);       // 1200 - 120 + 118.80
    }

    [Fact]
    public void RecomputeInventoryTotal_uses_updated_line_costs()
    {
        var r = NewGrnReturn();
        r.SetLines([GrnLine(10, 100m)]);
        foreach (var l in r.Lines) l.SetUnitCost(90m); // seam returns 90 at post
        r.RecomputeInventoryTotal();
        Assert.Equal(900m, r.InventoryTotal);
        Assert.Equal(1000m, r.GrandTotal); // billed total unchanged
    }

    [Fact]
    public void Submit_requires_lines_then_moves_to_pending()
    {
        var r = NewGrnReturn();
        Assert.Throws<InvalidOperationException>(() => r.Submit()); // no lines
        r.SetLines([GrnLine(1, 100m)]);
        r.Submit();
        Assert.Equal(PurchaseReturnStatus.PendingApproval, r.Status);
    }

    [Fact]
    public void Lifecycle_post_and_return_to_draft()
    {
        var r = NewGrnReturn();
        r.SetLines([GrnLine(1, 100m)]);
        r.Submit();
        r.MarkPosted();
        Assert.Equal(PurchaseReturnStatus.Posted, r.Status);

        var r2 = NewGrnReturn();
        r2.SetLines([GrnLine(1, 100m)]);
        r2.Submit();
        r2.ReturnToDraft("wrong qty");
        Assert.Equal(PurchaseReturnStatus.Draft, r2.Status);
        Assert.Equal("wrong qty", r2.RejectionNote);
    }

    [Fact]
    public void SetLines_and_UpdateHeader_blocked_when_not_draft()
    {
        var r = NewGrnReturn();
        r.SetLines([GrnLine(1, 100m)]);
        r.Submit();
        Assert.Throws<InvalidOperationException>(() => r.SetLines([GrnLine(1, 100m)]));
        Assert.Throws<InvalidOperationException>(() => r.UpdateHeader(new DateTime(2026, 2, 1), "x"));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.UnitTests --filter PurchaseReturnTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create the two enums**

```csharp
// src/ErpOne.Domain/Entities/Transactions/PurchaseReturnStatus.cs
namespace ErpOne.Domain.Entities;

public enum PurchaseReturnStatus { Draft, PendingApproval, Posted }
```

```csharp
// src/ErpOne.Domain/Entities/Transactions/PurchaseReturnSource.cs
namespace ErpOne.Domain.Entities;

public enum PurchaseReturnSource { GoodsReceipt, SupplierInvoice }
```

- [ ] **Step 4: Create `PurchaseReturnLine`**

```csharp
// src/ErpOne.Domain/Entities/Transactions/PurchaseReturnLine.cs
namespace ErpOne.Domain.Entities;

/// <summary>Baris retur pembelian; jangkar fisik = GoodsReceiptLineId (stok, sisa qty, HPP).</summary>
public class PurchaseReturnLine
{
    public int Id { get; private set; }
    public int PurchaseReturnId { get; private set; }
    public int GoodsReceiptLineId { get; private set; }
    public int? SupplierInvoiceLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int WarehouseId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }          // basis Cr Inventory; di-refresh dari seam saat post
    public decimal UnitPrice { get; private set; }         // jalur Invoice; jalur GRN: = UnitCost
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private PurchaseReturnLine() { } // EF Core

    public PurchaseReturnLine(int goodsReceiptLineId, int? supplierInvoiceLineId, int productVariantId,
        int warehouseId, string variantSku, string productName, int quantity, decimal unitCost,
        decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (goodsReceiptLineId <= 0) throw new ArgumentException("GoodsReceiptLineId is required.", nameof(goodsReceiptLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        GoodsReceiptLineId = goodsReceiptLineId;
        SupplierInvoiceLineId = supplierInvoiceLineId;
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

    /// <summary>Perbarui basis biaya (dari seam costing saat post). Tidak mengubah total tagih (UnitPrice).</summary>
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

- [ ] **Step 5: Create `PurchaseReturn`**

```csharp
// src/ErpOne.Domain/Entities/Transactions/PurchaseReturn.cs
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Dokumen retur barang ke supplier (debit note). Draft → PendingApproval → Posted.</summary>
public class PurchaseReturn : AuditableEntity
{
    private readonly List<PurchaseReturnLine> _lines = [];

    public int Id { get; private set; }
    public string ReturnNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public PurchaseReturnSource SourceType { get; private set; }
    public int? GoodsReceiptId { get; private set; }
    public int? SupplierInvoiceId { get; private set; }
    public DateTime ReturnDate { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseReturnStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal InventoryTotal { get; private set; }
    public IReadOnlyCollection<PurchaseReturnLine> Lines => _lines;

    private PurchaseReturn() { } // EF Core

    public PurchaseReturn(string returnNumber, int supplierId, PurchaseReturnSource sourceType,
        int? goodsReceiptId, int? supplierInvoiceId, DateTime returnDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(returnNumber)) throw new ArgumentException("ReturnNumber is required.", nameof(returnNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        if (sourceType == PurchaseReturnSource.GoodsReceipt && goodsReceiptId is not > 0)
            throw new ArgumentException("GoodsReceiptId is required for a GRN-sourced return.", nameof(goodsReceiptId));
        if (sourceType == PurchaseReturnSource.SupplierInvoice && supplierInvoiceId is not > 0)
            throw new ArgumentException("SupplierInvoiceId is required for an invoice-sourced return.", nameof(supplierInvoiceId));

        ReturnNumber = returnNumber.Trim();
        SupplierId = supplierId;
        SourceType = sourceType;
        GoodsReceiptId = goodsReceiptId;
        SupplierInvoiceId = supplierInvoiceId;
        SetHeader(returnDate, notes);
        Status = PurchaseReturnStatus.Draft;
    }

    public void SetLines(IEnumerable<PurchaseReturnLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        _lines.AddRange(lines);
        RecomputeTotals();
    }

    public void UpdateHeader(DateTime returnDate, string? notes)
    {
        EnsureDraft();
        SetHeader(returnDate, notes);
    }

    /// <summary>Hitung ulang InventoryTotal dari UnitCost baris terkini (dipanggil setelah refresh biaya seam saat post).</summary>
    public void RecomputeInventoryTotal() =>
        InventoryTotal = _lines.Sum(l => Round(l.Quantity * l.UnitCost));

    public void Submit()
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot submit a return without lines.");
        Status = PurchaseReturnStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != PurchaseReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be posted.");
        Status = PurchaseReturnStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != PurchaseReturnStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending return can be returned to draft.");
        Status = PurchaseReturnStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void SetHeader(DateTime returnDate, string? notes)
    {
        ReturnDate = returnDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
        InventoryTotal = _lines.Sum(l => Round(l.Quantity * l.UnitCost));
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseReturnStatus.Draft)
            throw new InvalidOperationException("Only a draft return can be modified.");
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/ErpOne.UnitTests --filter PurchaseReturnTests`
Expected: PASS (6).

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Domain/Entities/Transactions/PurchaseReturn*.cs tests/ErpOne.UnitTests/PurchaseReturnTests.cs
git commit -m "feat(purchasing): PurchaseReturn + PurchaseReturnLine domain entities"
```

---

### Task 3: EF mapping + migration + constants wiring

**Files:**
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (DbSets ~line 20-area; new entity configs after `SupplierInvoice` config; NumberSequence HasData ~line 273; tablePrefixes ~line 1092)
- Modify: `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`
- Modify: `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs`
- Create: migration `AddPurchaseReturn`
- Test: none (verified by build + migration inspection)

**Interfaces:**
- Produces: `db.PurchaseReturns`, `db.PurchaseReturnLines`; `ApprovalDocumentType.PurchaseReturn`; `DocumentTypes.PurchaseReturn`; NumberSequence row Id=16.

- [ ] **Step 1: Add the enum + constant**

In `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`, append after `PosSaleVoid` (add a comma to it):

```csharp
    PosSaleVoid,
    PurchaseReturn
```

In `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs` (namespace `ErpOne.Application.Numbering`), add after the `PosRefund` const:

```csharp
    public const string PurchaseReturn = "PurchaseReturn";
```

- [ ] **Step 2: Add DbSets**

In `AppDbContext.cs`, after `public DbSet<CostLayer> CostLayers => Set<CostLayer>();` (or near other transaction DbSets):

```csharp
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
```

- [ ] **Step 3: Add EF configs**

In `AppDbContext.cs` `OnModelCreating`, after the `SupplierInvoice`/`SupplierInvoiceLine` config block, add:

```csharp
        modelBuilder.Entity<PurchaseReturn>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.ReturnNumber).HasMaxLength(40).IsRequired();
            e.HasIndex(r => r.ReturnNumber).IsUnique();
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.RejectionNote).HasMaxLength(500);
            e.Property(r => r.SourceType).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Subtotal).HasPrecision(18, 2);
            e.Property(r => r.DiscountTotal).HasPrecision(18, 2);
            e.Property(r => r.TaxTotal).HasPrecision(18, 2);
            e.Property(r => r.GrandTotal).HasPrecision(18, 2);
            e.Property(r => r.InventoryTotal).HasPrecision(18, 2);
            e.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseReturnLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.VariantSku).HasMaxLength(60).IsRequired();
            e.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
            e.Property(l => l.UnitCost).HasPrecision(18, 2);
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
            e.Property(l => l.DiscountPercent).HasPrecision(5, 2);
            e.Property(l => l.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(l => l.LineSubtotal).HasPrecision(18, 2);
            e.Property(l => l.LineDiscount).HasPrecision(18, 2);
            e.Property(l => l.LineTax).HasPrecision(18, 2);
            e.Property(l => l.LineTotal).HasPrecision(18, 2);
            e.HasIndex(l => l.GoodsReceiptLineId);
        });
```

> Cascade delete of lines matches the `Draft`-only delete flow; the header→line relationship is owned. Check the exact precision/`HasConversion<string>()` convention against the `SupplierInvoice` config block and match it if it differs (some enums may be stored as int in this codebase — mirror the neighbor).

- [ ] **Step 4: Add the SupplierInvoice.CreditedAmount column mapping**

In the `SupplierInvoice` EF config block, add (mirroring the `PaidAmount` precision line):

```csharp
            e.Property(i => i.CreditedAmount).HasPrecision(18, 2);
```

- [ ] **Step 5: Register table prefixes**

In `AppDbContext.cs`, in the `tablePrefixes` `// Transaksi` group:

```csharp
            [nameof(PurchaseReturn)] = "T_",
            [nameof(PurchaseReturnLine)] = "T_",
```

- [ ] **Step 6: Seed the NumberSequence row**

In `AppDbContext.cs`, after the Id=15 `PosRefund` seed row (add a comma to it), add:

```csharp
            new { Id = 16, Code = "PurchaseReturn", Prefix = "DN", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 7: Create the migration**

Run: `dotnet ef migrations add AddPurchaseReturn --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Expected: `CreateTable("T_PurchaseReturns", ...)` + `CreateTable("T_PurchaseReturnLines", ...)` with cascade FK, `AddColumn<decimal>("CreditedAmount", "…SupplierInvoices…", ... defaultValue: 0m ...)`, and an `InsertData` row into the NumberSequence table (Id=16). Confirm the `S_`/`T_` prefixes and the `CreditedAmount` default 0.

- [ ] **Step 8: Build (model-build guard must accept the new tables)**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/ src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs
git commit -m "feat(purchasing): EF mapping + migration + constants for PurchaseReturn"
```

---

### Task 4: Application layer — DTOs, interface, validator

**Files:**
- Create: `src/ErpOne.Application/Purchasing/PurchaseReturns/PurchaseReturnDtos.cs`
- Create: `src/ErpOne.Application/Purchasing/PurchaseReturns/IPurchaseReturnService.cs`
- Create: `src/ErpOne.Application/Purchasing/PurchaseReturns/PurchaseReturnValidators.cs`
- Test: none (compile only; behavior covered in Task 5)

**Interfaces:**
- Produces: all DTOs + `IPurchaseReturnService` (consumed by Task 5 impl and Task 7 web) + `CreatePurchaseReturnValidator`.

- [ ] **Step 1: Create the DTOs**

```csharp
// src/ErpOne.Application/Purchasing/PurchaseReturns/PurchaseReturnDtos.cs
using ErpOne.Application.Approvals;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public record ReturnableLineDto(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, int WarehouseId, string WarehouseName, int SourceQty, int AlreadyReturnedQty,
    int RemainingQty, decimal UnitCost, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot);

public record ReturnableSourceDto(string SourceType, int? GoodsReceiptId, int? SupplierInvoiceId, string SourceNumber,
    int SupplierId, string SupplierName, IReadOnlyList<ReturnableLineDto> Lines);

public record ReturnableSourceOptionDto(string SourceType, int DocId, string DocNumber, DateTime DocDate, string SupplierName);

public record PurchaseReturnLineInput(int GoodsReceiptLineId, int? SupplierInvoiceLineId, int Quantity);

public record CreatePurchaseReturnRequest(string SourceType, int? GoodsReceiptId, int? SupplierInvoiceId,
    DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines);

public record UpdatePurchaseReturnRequest(DateTime ReturnDate, string? Notes, IReadOnlyList<PurchaseReturnLineInput> Lines);

public record PurchaseReturnLineDto(int Id, int GoodsReceiptLineId, int? SupplierInvoiceLineId, int ProductVariantId,
    string Sku, string ProductName, string WarehouseName, int Quantity, decimal UnitCost, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineTotal);

public record PurchaseReturnDto(int Id, string ReturnNumber, string SourceType, int? GoodsReceiptId, string? GrnNumber,
    int? SupplierInvoiceId, string? InvoiceNumber, int SupplierId, string SupplierName, DateTime ReturnDate, string? Notes,
    string Status, string? RejectionNote, string? CreatedBy, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrandTotal, decimal InventoryTotal, IReadOnlyList<PurchaseReturnLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record PurchaseReturnListItemDto(int Id, string ReturnNumber, DateTime ReturnDate, string SourceType,
    string SupplierName, int LineCount, decimal GrandTotal, string Status);
```

> Verify the `ApprovalStepDto` namespace/shape against how `StockTransferDto` imports it (Task-3 agent confirmed `ErpOne.Application.Approvals`); match exactly.

- [ ] **Step 2: Create the service interface**

```csharp
// src/ErpOne.Application/Purchasing/PurchaseReturns/IPurchaseReturnService.cs
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public interface IPurchaseReturnService
{
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableGrnsAsync(string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default);
    Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default);

    Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<PurchaseReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        PurchaseReturnStatus? status = null, CancellationToken ct = default);

    Task<PurchaseReturnDto> CreateAsync(CreatePurchaseReturnRequest request, CancellationToken ct = default);
    Task<PurchaseReturnDto> UpdateAsync(int id, UpdatePurchaseReturnRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
}
```

> Confirm `PagedResult<T>` lives in `ErpOne.Application.Common` (StockTransfer uses it) — match the using.

- [ ] **Step 3: Create the validator**

```csharp
// src/ErpOne.Application/Purchasing/PurchaseReturns/PurchaseReturnValidators.cs
using FluentValidation;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public class CreatePurchaseReturnValidator : AbstractValidator<CreatePurchaseReturnRequest>
{
    public CreatePurchaseReturnValidator()
    {
        RuleFor(x => x.SourceType).Must(s => s is "GoodsReceipt" or "SupplierInvoice")
            .WithMessage("SourceType must be GoodsReceipt or SupplierInvoice.");
        RuleFor(x => x.GoodsReceiptId).NotNull().When(x => x.SourceType == "GoodsReceipt")
            .WithMessage("GoodsReceiptId is required for a GRN-sourced return.");
        RuleFor(x => x.SupplierInvoiceId).NotNull().When(x => x.SourceType == "SupplierInvoice")
            .WithMessage("SupplierInvoiceId is required for an invoice-sourced return.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.GoodsReceiptLineId).GreaterThan(0);
        });
        RuleForEach(x => x.Lines).Must(l => l.SupplierInvoiceLineId is > 0)
            .When(x => x.SourceType == "SupplierInvoice")
            .WithMessage("SupplierInvoiceLineId is required on each line for an invoice-sourced return.");
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Application/Purchasing/PurchaseReturns/
git commit -m "feat(purchasing): PurchaseReturn DTOs, service interface, validator"
```

---

### Task 5: GL — `PostPurchaseReturnAsync`

**Files:**
- Modify: `src/ErpOne.Application/Accounting/IJournalPostingService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`
- Test: none standalone (exercised in Task 6 integration tests)

**Interfaces:**
- Consumes: `PurchaseReturn`, `PostBalancedAsync`, `ConfigAsync`, `RequireAccount`.
- Produces: `IJournalPostingService.PostPurchaseReturnAsync(PurchaseReturn r, CancellationToken)`.

- [ ] **Step 1: Add to the interface**

In `IJournalPostingService.cs`, add after `PostPosRefundAsync`:

```csharp
    Task PostPurchaseReturnAsync(PurchaseReturn r, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in `JournalPostingService`**

Add this method (mirrors `PostSupplierInvoiceAsync`/`PostGoodsReceiptAsync`; idempotent via `PostBalancedAsync`):

```csharp
    public async Task PostPurchaseReturnAsync(PurchaseReturn r, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");

        if (r.SourceType == PurchaseReturnSource.GoodsReceipt)
        {
            // Reverse the GR-IR/Inventory pair: Dr GR-IR / Cr Inventory.
            await PostBalancedAsync(r.ReturnDate, $"Purchase Return {r.ReturnNumber}", "PurchaseReturn", r.Id,
                [(grIr, r.InventoryTotal, 0m, "Return goods (pre-invoice)"),
                 (inventory, 0m, r.InventoryTotal, "Inventory returned")], ct);
            return;
        }

        // SupplierInvoice path (debit note): Dr AP / Cr Input Tax / Cr Inventory / +/- GR-IR variance.
        var ap = RequireAccount(cfg.ApAccountId, "Accounts Payable");
        var net = r.Subtotal - r.DiscountTotal;
        var grIrVariance = net - r.InventoryTotal; // Cr if net>inv, Dr if net<inv
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (ap, r.GrandTotal, 0m, "Debit note to supplier"),
            (inventory, 0m, r.InventoryTotal, "Inventory returned"),
        };
        if (r.TaxTotal > 0m)
            lines.Add((RequireAccount(cfg.InputTaxAccountId, "Input Tax"), 0m, r.TaxTotal, "Input VAT reversal"));
        if (grIrVariance != 0m)
            lines.Add((grIr, Math.Max(-grIrVariance, 0m), Math.Max(grIrVariance, 0m), "GR-IR variance"));
        await PostBalancedAsync(r.ReturnDate, $"Purchase Return {r.ReturnNumber}", "PurchaseReturn", r.Id, lines, ct);
    }
```

> Balance check (Invoice path): `Dr AP(net+tax)` vs `Cr Inventory(inv) + Cr Tax(tax) + Cr GR-IR(net−inv)` = `inv + tax + net − inv = net + tax` ✓. `PostBalancedAsync` filters zero lines, so a zero variance/tax simply drops out.

- [ ] **Step 3: Build**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Application/Accounting/IJournalPostingService.cs src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs
git commit -m "feat(accounting): PostPurchaseReturnAsync (GR-IR & debit-note journals)"
```

---

### Task 6: Infrastructure — `PurchaseReturnService`

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Purchasing/PurchaseReturnService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs` (after the last transaction-service registration)
- Test: `tests/ErpOne.IntegrationTests/PurchaseReturnServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `IApprovalService`, `IStockService`, `ICostingService`, `IValidator<CreatePurchaseReturnRequest>`, `IDocumentNumberService`, `IJournalPostingService`.
- Produces: `PurchaseReturnService : IPurchaseReturnService`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
// tests/ErpOne.IntegrationTests/PurchaseReturnServiceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Purchasing.PurchaseReturns;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PurchaseReturnServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseReturnServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seed a PurchaseReturn approval chain (one manager step) so Submit leaves the doc PendingApproval.
    private static async Task SeedChainAsync(AppDbContext db)
    {
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PurchaseReturn))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PurchaseReturn, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    // Seed Supplier + PO + posted GRN of (qty @ unitCost); returns (supplierId, grnId, grnLineId, variantId, warehouseId).
    // MIRROR the helper in StockTransferServiceTests / GoodsReceiptServiceTests for the exact PO→GRN post path.
    private static async Task<(int supplierId, int grnId, int grnLineId, int variantId, int warehouseId)>
        SeedPostedGrnAsync(IServiceProvider sp, int qty, decimal unitCost)
    {
        // TODO(impl): build Supplier, Warehouse, Product+variant, PurchaseOrder(WarehouseId), then GRN posted via
        // GoodsReceiptService so stock + MA CostPrice are set. Return the ids. Follow GoodsReceiptServiceTests seed.
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Grn_path_full_return_reduces_stock_and_posts_grir_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SeedChainAsync(db);

        var (supplierId, grnId, grnLineId, variantId, whId) = await SeedPostedGrnAsync(sp, 10, 100m);

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest(
            "GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);
        Assert.Equal(0, await stock.GetOnHandAsync(variantId, whId)); // 10 - 10

        var je = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(x => x.SourceType == "PurchaseReturn" && x.SourceId == created.Id);
        Assert.Equal(1000m, je.Lines.Sum(l => l.Debit)); // Dr GR-IR 1000 = Cr Inventory 1000
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Partial_returns_track_remaining_and_reject_over_return()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (_, grnId, grnLineId, _, _) = await SeedPostedGrnAsync(sp, 10, 100m);

        var first = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 6)]));
        await svc.SubmitAsync(first.Id); await svc.ApproveAsync(first.Id, "admin", _ => true);

        var src = await svc.GetReturnableSourceAsync("GoodsReceipt", grnId);
        Assert.Equal(4, src!.Lines.Single(l => l.GoodsReceiptLineId == grnLineId).RemainingQty);

        var second = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 4)]));
        await svc.SubmitAsync(second.Id); await svc.ApproveAsync(second.Id, "admin", _ => true);

        // Third return over remaining -> rejected at create.
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
                [new PurchaseReturnLineInput(grnLineId, null, 1)])));
    }

    [Fact]
    public async Task Insufficient_on_hand_is_rejected_on_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        await SeedChainAsync(db);
        var (_, grnId, grnLineId, variantId, whId) = await SeedPostedGrnAsync(sp, 10, 100m);

        // Draw stock down below the return qty via an adjustment out.
        await stock.RecordAdjustmentAsync(new ErpOne.Application.Stock.StockAdjustmentRequest(
            whId, DateTime.Today, "draw", [new ErpOne.Application.Stock.StockAdjustmentLine(variantId, -7, 0m, null)]));

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("GoodsReceipt", grnId, null, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, null, 10)]));
        await svc.SubmitAsync(created.Id);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));
    }
}
```

> **Verify-before-embed:** the `SeedPostedGrnAsync` helper and the `StockAdjustmentRequest`/`StockAdjustmentLine` ctor shapes must be aligned to the existing `GoodsReceiptServiceTests` / `StockServiceTests` helpers — copy their exact construction. The Invoice-path test (path 2 + over-Outstanding, spec §172, §175) is added in Task 6 Step 6 once the GRN path is green, to keep this step's red state focused.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter PurchaseReturnServiceTests`
Expected: FAIL — `IPurchaseReturnService` has no registered implementation.

- [ ] **Step 3: Implement `PurchaseReturnService`**

```csharp
// src/ErpOne.Infrastructure/Services/Purchasing/PurchaseReturnService.cs
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Application.Purchasing.PurchaseReturns;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PurchaseReturnService(
    AppDbContext db,
    IApprovalService approval,
    IStockService stock,
    ICostingService costing,
    IValidator<CreatePurchaseReturnRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IPurchaseReturnService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.PurchaseReturn;

    // ---- Returnable source discovery ------------------------------------------------

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableGrnsAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);
        var q =
            from grn in db.GoodsReceipts.AsNoTracking()
            where grn.Status == GoodsReceiptStatus.Posted
            join po in db.PurchaseOrders.AsNoTracking() on grn.PurchaseOrderId equals po.Id
            join sup in db.Suppliers.AsNoTracking() on po.SupplierId equals sup.Id
            select new { grn.Id, grn.GrnNumber, grn.ReceiptDate, po.SupplierId, SupplierName = sup.Name, grn.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.GrnNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        // Keep only GRNs with at least one line still returnable.
        return rows.Where(x => x.Lines.Any(l => l.QuantityReceived - returnedByGrnLine.GetValueOrDefault(l.Id) > 0))
            .Select(x => new ReturnableSourceOptionDto("GoodsReceipt", x.Id, x.GrnNumber, x.ReceiptDate, x.SupplierName))
            .ToList();
    }

    public async Task<IReadOnlyList<ReturnableSourceOptionDto>> GetReturnableInvoicesAsync(string? search = null, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);
        var q =
            from inv in db.SupplierInvoices.AsNoTracking()
            where inv.Status != SupplierInvoiceStatus.Cancelled && (inv.GrandTotal - inv.PaidAmount - inv.CreditedAmount) > 0
            join sup in db.Suppliers.AsNoTracking() on inv.SupplierId equals sup.Id
            select new { inv.Id, inv.InvoiceNumber, inv.InvoiceDate, inv.SupplierId, SupplierName = sup.Name, inv.Lines };
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.InvoiceNumber.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);

        return rows.Where(x => x.Lines.Any(l => l.Quantity - returnedByGrnLine.GetValueOrDefault(l.GoodsReceiptLineId) > 0))
            .Select(x => new ReturnableSourceOptionDto("SupplierInvoice", x.Id, x.InvoiceNumber, x.InvoiceDate, x.SupplierName))
            .ToList();
    }

    public async Task<ReturnableSourceDto?> GetReturnableSourceAsync(string sourceType, int docId, CancellationToken ct = default)
    {
        var returnedByGrnLine = await ReturnedQtyByGrnLineAsync(ct);

        if (sourceType == "GoodsReceipt")
        {
            var grn = await db.GoodsReceipts.AsNoTracking().FirstOrDefaultAsync(g => g.Id == docId && g.Status == GoodsReceiptStatus.Posted, ct);
            if (grn is null) return null;
            var po = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == grn.PurchaseOrderId, ct);
            var sup = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == po.SupplierId, ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var gl in grn.Lines)
            {
                var (sku, name) = await VariantInfoAsync(gl.ProductVariantId, ct);
                var remaining = gl.QuantityReceived - returnedByGrnLine.GetValueOrDefault(gl.Id);
                if (remaining <= 0) continue;
                lines.Add(new ReturnableLineDto(gl.Id, null, gl.ProductVariantId, sku, name, po.WarehouseId,
                    await WarehouseNameAsync(po.WarehouseId, ct), gl.QuantityReceived,
                    returnedByGrnLine.GetValueOrDefault(gl.Id), remaining, gl.UnitCost, gl.UnitCost, 0m, 0m));
            }
            return new ReturnableSourceDto("GoodsReceipt", grn.Id, null, grn.GrnNumber, po.SupplierId, sup.Name, lines);
        }

        if (sourceType == "SupplierInvoice")
        {
            var inv = await db.SupplierInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == docId, ct);
            if (inv is null) return null;
            var sup = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == inv.SupplierId, ct);
            var returnedByInvLine = await ReturnedQtyByInvoiceLineAsync(ct);
            var lines = new List<ReturnableLineDto>();
            foreach (var il in inv.Lines)
            {
                var grnLine = await db.GoodsReceiptLines.AsNoTracking().FirstAsync(g => g.Id == il.GoodsReceiptLineId, ct);
                var grn = await db.GoodsReceipts.AsNoTracking().FirstAsync(g => g.Id == grnLine.GoodsReceiptId, ct);
                var po = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == grn.PurchaseOrderId, ct);
                var (sku, name) = await VariantInfoAsync(il.ProductVariantId, ct);
                var grnRemaining = grnLine.QuantityReceived - returnedByGrnLine.GetValueOrDefault(il.GoodsReceiptLineId);
                var invRemaining = il.Quantity - returnedByInvLine.GetValueOrDefault(il.Id);
                var remaining = Math.Min(grnRemaining, invRemaining);
                if (remaining <= 0) continue;
                lines.Add(new ReturnableLineDto(il.GoodsReceiptLineId, il.Id, il.ProductVariantId, sku, name,
                    po.WarehouseId, await WarehouseNameAsync(po.WarehouseId, ct), il.Quantity,
                    il.Quantity - invRemaining, remaining, grnLine.UnitCost, il.UnitPrice, il.DiscountPercent, il.TaxRateSnapshot));
            }
            return new ReturnableSourceDto("SupplierInvoice", null, inv.Id, inv.InvoiceNumber, inv.SupplierId, sup.Name, lines);
        }

        return null;
    }

    // ---- CRUD -----------------------------------------------------------------------

    public async Task<PurchaseReturnDto> CreateAsync(CreatePurchaseReturnRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var docId = request.SourceType == "GoodsReceipt" ? request.GoodsReceiptId!.Value : request.SupplierInvoiceId!.Value;
        var source = await GetReturnableSourceAsync(request.SourceType, docId, ct)
            ?? throw Fail("Source document not found or not returnable.");

        var number = await docNumbers.NextAsync(DocumentTypes.PurchaseReturn, request.ReturnDate, ct);
        var sourceType = Enum.Parse<PurchaseReturnSource>(request.SourceType);
        var pr = new PurchaseReturn(number, source.SupplierId, sourceType, source.GoodsReceiptId, source.SupplierInvoiceId,
            request.ReturnDate, request.Notes);
        pr.SetLines(BuildLines(request.Lines, source));
        db.PurchaseReturns.Add(pr);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(pr.Id, ct))!;
    }

    public async Task<PurchaseReturnDto> UpdateAsync(int id, UpdatePurchaseReturnRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Return not found.");
        var docId = pr.SourceType == PurchaseReturnSource.GoodsReceipt ? pr.GoodsReceiptId!.Value : pr.SupplierInvoiceId!.Value;
        var source = await GetReturnableSourceForUpdateAsync(pr.SourceType.ToString(), docId, id, ct)
            ?? throw Fail("Source document not found or not returnable.");

        pr.UpdateHeader(request.ReturnDate, request.Notes);
        pr.SetLines(BuildLines(request.Lines, source));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var pr = await db.PurchaseReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        if (pr.Status != PurchaseReturnStatus.Draft) throw Fail("Only a draft return can be deleted.");
        db.PurchaseReturns.Remove(pr);
        await db.SaveChangesAsync(ct);
    }

    // ---- Approval lifecycle (mirror StockTransferService) ---------------------------

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        pr.Submit();
        await db.SaveChangesAsync(ct);
        await approval.ResetAsync(DocType, pr.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, pr.Id, ct);
        if (fullyApproved) await PostAsync(pr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, pr.Id, actingUserName, isInRole, pr.CreatedBy, ct);
        if (fullyApproved) await PostAsync(pr, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pr = await db.PurchaseReturns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Fail("Return not found.");
        await approval.RejectAsync(DocType, pr.Id, actingUserName, isInRole, pr.CreatedBy, reason, ct);
        pr.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, pr.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Posting: stock out via seam, AP credit (Invoice), GL. Caller saves + commits.
    private async Task PostAsync(PurchaseReturn r, CancellationToken ct)
    {
        // Phase 1: validate on-hand for every line (DB on-hand − accumulated taken).
        var taken = new Dictionary<(int, int), int>();
        foreach (var line in r.Lines)
        {
            var key = (line.ProductVariantId, line.WarehouseId);
            var onHand = await stock.GetOnHandAsync(line.ProductVariantId, line.WarehouseId, ct);
            var already = taken.GetValueOrDefault(key);
            if (onHand - already < line.Quantity)
                throw Fail($"Stok tidak cukup untuk retur varian {line.ProductVariantId} (butuh {line.Quantity}, tersedia {onHand - already}).");
            taken[key] = already + line.Quantity;
        }
        // Phase 2: mutate — cost from seam, stock out, refresh line cost.
        foreach (var line in r.Lines)
        {
            var unitCost = await costing.GetOutboundUnitCostAsync(line.ProductVariantId, line.WarehouseId, line.Quantity, ct);
            db.StockMovements.Add(new StockMovement(line.ProductVariantId, line.WarehouseId, MovementType.Out,
                -line.Quantity, unitCost, r.ReturnDate, "PurchaseReturn", r.Id, r.ReturnNumber));
            await db.UpsertStockAsync(line.ProductVariantId, line.WarehouseId, -line.Quantity, ct);
            line.SetUnitCost(unitCost); // COGS/inventory basis snapshot
        }
        r.RecomputeInventoryTotal();

        // AP credit (Invoice path).
        if (r.SourceType == PurchaseReturnSource.SupplierInvoice)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == r.SupplierInvoiceId, ct)
                ?? throw Fail("Supplier invoice not found.");
            if (r.GrandTotal > inv.Outstanding) throw Fail("Retur melebihi Outstanding invoice.");
            inv.ApplyCredit(r.GrandTotal);
        }

        await journalPoster.PostPurchaseReturnAsync(r, ct);
        r.MarkPosted();
    }

    // ---- Queries (GetByIdAsync / GetPagedAsync) -------------------------------------

    public async Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await db.PurchaseReturns.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var supplierName = await db.Suppliers.AsNoTracking().Where(s => s.Id == r.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        string? grnNumber = r.GoodsReceiptId is int gid
            ? await db.GoodsReceipts.AsNoTracking().Where(g => g.Id == gid).Select(g => g.GrnNumber).FirstOrDefaultAsync(ct) : null;
        string? invNumber = r.SupplierInvoiceId is int iid
            ? await db.SupplierInvoices.AsNoTracking().Where(i => i.Id == iid).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct) : null;
        var steps = await approval.GetStepsAsync(DocType, r.Id, ct);
        var lines = r.Lines.Select(l => new PurchaseReturnLineDto(l.Id, l.GoodsReceiptLineId, l.SupplierInvoiceLineId,
            l.ProductVariantId, l.VariantSku, l.ProductName, "", l.Quantity, l.UnitCost, l.UnitPrice,
            l.DiscountPercent, l.TaxRateSnapshot, l.LineTotal)).ToList();
        // Fill WarehouseName per line.
        var whIds = lines.Select(_ => 0).ToList(); // replaced below
        var lineDtos = new List<PurchaseReturnLineDto>();
        foreach (var l in r.Lines)
            lineDtos.Add(new PurchaseReturnLineDto(l.Id, l.GoodsReceiptLineId, l.SupplierInvoiceLineId, l.ProductVariantId,
                l.VariantSku, l.ProductName, await WarehouseNameAsync(l.WarehouseId, ct), l.Quantity, l.UnitCost,
                l.UnitPrice, l.DiscountPercent, l.TaxRateSnapshot, l.LineTotal));

        return new PurchaseReturnDto(r.Id, r.ReturnNumber, r.SourceType.ToString(), r.GoodsReceiptId, grnNumber,
            r.SupplierInvoiceId, invNumber, r.SupplierId, supplierName, r.ReturnDate, r.Notes, r.Status.ToString(),
            r.RejectionNote, r.CreatedBy, r.Subtotal, r.DiscountTotal, r.TaxTotal, r.GrandTotal, r.InventoryTotal, lineDtos, steps);
    }

    public async Task<PagedResult<PurchaseReturnListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        PurchaseReturnStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.PurchaseReturns.AsNoTracking();
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.ReturnNumber.Contains(search));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PurchaseReturnListItemDto(x.Id, x.ReturnNumber, x.ReturnDate, x.SourceType.ToString(),
                db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                x.Lines.Count, x.GrandTotal, x.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<PurchaseReturnListItemDto>(items, total, page, pageSize);
    }

    // ---- Helpers --------------------------------------------------------------------

    private IEnumerable<PurchaseReturnLine> BuildLines(IReadOnlyList<PurchaseReturnLineInput> inputs, ReturnableSourceDto source)
    {
        foreach (var input in inputs)
        {
            var cand = source.Lines.FirstOrDefault(l => l.GoodsReceiptLineId == input.GoodsReceiptLineId
                && l.SupplierInvoiceLineId == input.SupplierInvoiceLineId)
                ?? throw Fail($"Line {input.GoodsReceiptLineId} is not returnable on this source.");
            if (input.Quantity <= 0 || input.Quantity > cand.RemainingQty)
                throw Fail($"Return quantity {input.Quantity} exceeds remaining {cand.RemainingQty} for line {input.GoodsReceiptLineId}.");
            yield return new PurchaseReturnLine(cand.GoodsReceiptLineId, cand.SupplierInvoiceLineId, cand.ProductVariantId,
                cand.WarehouseId, cand.Sku, cand.ProductName, input.Quantity, cand.UnitCost, cand.UnitPrice,
                cand.DiscountPercent, cand.TaxRateSnapshot);
        }
    }

    // returned qty grouped by GRN line, counting PendingApproval + Posted returns.
    private async Task<Dictionary<int, int>> ReturnedQtyByGrnLineAsync(CancellationToken ct) =>
        await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => db.PurchaseReturns.Any(r => r.Id == l.PurchaseReturnId
                && (r.Status == PurchaseReturnStatus.PendingApproval || r.Status == PurchaseReturnStatus.Posted)))
            .GroupBy(l => l.GoodsReceiptLineId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<Dictionary<int, int>> ReturnedQtyByInvoiceLineAsync(CancellationToken ct) =>
        await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => l.SupplierInvoiceLineId != null && db.PurchaseReturns.Any(r => r.Id == l.PurchaseReturnId
                && (r.Status == PurchaseReturnStatus.PendingApproval || r.Status == PurchaseReturnStatus.Posted)))
            .GroupBy(l => l.SupplierInvoiceLineId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    // Update variant: recompute returnable EXCLUDING this document's own lines.
    private async Task<ReturnableSourceDto?> GetReturnableSourceForUpdateAsync(string sourceType, int docId, int excludeReturnId, CancellationToken ct)
    {
        var basis = await GetReturnableSourceAsync(sourceType, docId, ct);
        if (basis is null) return basis;
        var mine = await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => l.PurchaseReturnId == excludeReturnId)
            .GroupBy(l => l.GoodsReceiptLineId).Select(g => new { g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var lines = basis.Lines.Select(l => l with { RemainingQty = l.RemainingQty + mine.GetValueOrDefault(l.GoodsReceiptLineId) }).ToList();
        return basis with { Lines = lines };
    }

    private async Task<(string sku, string name)> VariantInfoAsync(int variantId, CancellationToken ct)
    {
        var row = await (from v in db.ProductVariants.AsNoTracking()
                         join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                         where v.Id == variantId select new { v.Sku, p.Name }).FirstAsync(ct);
        return (row.Sku, row.Name);
    }

    private async Task<string> WarehouseNameAsync(int warehouseId, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().Where(w => w.Id == warehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PurchaseReturn", message)]);
}
```

> **Verify-before-embed:** (a) `db.Suppliers`/`Supplier.Name`, `db.Warehouses`, `db.ProductVariants.ProductId`, `db.GoodsReceiptLines` DbSet names — confirm against `AppDbContext`. (b) `IApprovalService.GetStepsAsync/ResetAsync/SubmitAsync/ApproveAsync/RejectAsync` signatures — copy from `StockTransferService`. (c) `StockMovement` ctor arg order (`variantId, warehouseId, MovementType, qty, unitCost, date, refType, refId, refNumber`) — confirm from `DeliveryOrderService`/`StockTransferService`. (d) `MovementType.Out` exists. (e) the `GetByIdAsync` line-DTO assembly has a dead `whIds`/`lines` pair left as a guide — delete it; keep the `lineDtos` loop. Simplify per the code-simplifier pass in Task 8.

- [ ] **Step 4: Register DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, after the last transaction service registration (e.g. after `IPosRefundService`):

```csharp
        services.AddScoped<IPurchaseReturnService, PurchaseReturnService>();
```

- [ ] **Step 5: Run the GRN-path tests**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter PurchaseReturnServiceTests`
Expected: PASS (3 GRN-path tests). Fix seed helper / arg shapes flagged above until green.

- [ ] **Step 6: Add + pass the Invoice-path tests**

Append to `PurchaseReturnServiceTests` (seed a `SupplierInvoice` from the GRN first — mirror `SupplierInvoiceServiceTests` seed):

```csharp
    [Fact]
    public async Task Invoice_path_return_credits_outstanding_and_posts_ap_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, grnLineId, variantId, whId, grandTotal) = await SeedSupplierInvoiceAsync(sp, 10, 100m);

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("SupplierInvoice", null, invoiceId, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var inv = await db.SupplierInvoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(grandTotal, inv.CreditedAmount);
        Assert.Equal(0m, inv.Outstanding);

        var je = await db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.SourceType == "PurchaseReturn" && x.SourceId == created.Id);
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit)); // balanced
    }

    [Fact]
    public async Task Return_over_invoice_outstanding_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<IPurchaseReturnService>();
        await SeedChainAsync(db);
        var (invoiceId, invLineId, grnLineId, _, _, grandTotal) = await SeedSupplierInvoiceAsync(sp, 10, 100m);

        // Pay the invoice down so Outstanding < a full return.
        var inv = await db.SupplierInvoices.FirstAsync(i => i.Id == invoiceId);
        inv.ApplyPayment(grandTotal - 100m);
        await db.SaveChangesAsync();

        var created = await svc.CreateAsync(new CreatePurchaseReturnRequest("SupplierInvoice", null, invoiceId, DateTime.Today, null,
            [new PurchaseReturnLineInput(grnLineId, invLineId, 10)]));
        await svc.SubmitAsync(created.Id);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));
    }
```

Run: `dotnet test tests/ErpOne.IntegrationTests --filter PurchaseReturnServiceTests`
Expected: PASS (5). Implement `SeedSupplierInvoiceAsync` mirroring the existing supplier-invoice test seed.

- [ ] **Step 7: Update the SupplierPayment outstanding guard for credits**

The invoice `Outstanding` now subtracts `CreditedAmount`, but `SupplierPaymentService.ValidateAsync` computes outstanding inline as `inv.GrandTotal - inv.PaidAmount`. In `src/ErpOne.Infrastructure/Services/Finance/SupplierPaymentService.cs`, change that inline calc to `inv.Outstanding` (or subtract `CreditedAmount`). Add a regression assertion to `SupplierPaymentServiceTests`: an invoice with a credit cannot be over-paid past the reduced Outstanding.

Run: `dotnet test tests/ErpOne.IntegrationTests --filter SupplierPaymentServiceTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Purchasing/PurchaseReturnService.cs src/ErpOne.Infrastructure/DependencyInjection.cs src/ErpOne.Infrastructure/Services/Finance/SupplierPaymentService.cs tests/ErpOne.IntegrationTests/PurchaseReturnServiceTests.cs tests/ErpOne.IntegrationTests/SupplierPaymentServiceTests.cs
git commit -m "feat(purchasing): PurchaseReturnService (returnable, CRUD, approval, seam-costed posting)"
```

---

### Task 7: Web — Index / Form / Detail + menu + seeder

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs`
- Create: `src/ErpOne.Web/Components/Pages/Transactions/PurchaseReturns/PurchaseReturnIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Transactions/PurchaseReturns/PurchaseReturnForm.razor`
- Create: `src/ErpOne.Web/Components/Pages/Transactions/PurchaseReturns/PurchaseReturnDetail.razor`
- Test: none (manual smoke; logic covered in Task 6)

**Interfaces:**
- Consumes: `IPurchaseReturnService`, `AppMenus`, `ApprovalChainStep`.

- [ ] **Step 1: Register the menu resource**

In `AppMenus.cs`, add the actions property near `StockTransferActions`:

```csharp
    private static AppAction[] PurchaseReturnActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost];
```

In the **Transaksi** group, add:

```csharp
        new("transactions.purchase-returns", "Purchase Return", "bi-arrow-return-left", PurchaseReturnActions),
```

- [ ] **Step 2: Seed the approval chain**

In `BootstrapSeeder.cs`, after the POS Void chain block (and before `AccountingSeeder.SeedAsync`):

```csharp
        // Seed rantai approval default untuk Purchase Return (idempotent).
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PurchaseReturn))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PurchaseReturn, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 3: Create `PurchaseReturnIndex.razor`**

Mirror `Components/Pages/Inventory/Transfers/StockTransferIndex.razor` markup (root `<div class="pi">`, status chips from `Enum.GetValues<PurchaseReturnStatus>()`, "New" button gated by `transactions.purchase-returns.create`, rows navigate to detail). Top of file:

```razor
@page "/transactions/purchase-returns"
@attribute [Authorize(Policy = "transactions.purchase-returns.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Common
@using ErpOne.Application.Purchasing.PurchaseReturns
@using ErpOne.Domain.Entities
@inject IPurchaseReturnService Returns
@inject NavigationManager Nav
```

Table columns: No (ReturnNumber), Tgl (ReturnDate), Sumber (SourceType), Supplier (SupplierName), #Baris (LineCount), Grand Total (GrandTotal), Status. Load via `Returns.GetPagedAsync(page, pageSize, search, statusFilter)`. Row click → `Nav.NavigateTo($"/transactions/purchase-returns/{r.Id}")`.

- [ ] **Step 4: Create `PurchaseReturnForm.razor`**

Mirror `StockTransferForm.razor` (root `<div class="cf">`, dual `@page` new/edit, indexed `@for` line table). Top of file:

```razor
@page "/transactions/purchase-returns/new"
@page "/transactions/purchase-returns/{Id:int}/edit"
@attribute [Authorize(Policy = "transactions.purchase-returns.create")]
@rendermode InteractiveServer
@using ErpOne.Application.Purchasing.PurchaseReturns
@using FluentValidation
@inject IPurchaseReturnService Returns
@inject NavigationManager Nav
```

Flow: (1) radio/select **jalur** GoodsReceipt vs SupplierInvoice → (2) source dropdown from `Returns.GetReturnableGrnsAsync()` / `GetReturnableInvoicesAsync()` → (3) on select, `Returns.GetReturnableSourceAsync(sourceType, docId)` populates a table: Produk · Gudang · Sisa (RemainingQty) · Qty retur (bound int input, `max=RemainingQty`) → (4) ReturnDate + Notes → (5) Save calls `CreateAsync`/`UpdateAsync` with only the lines whose qty > 0. On edit, prefill from `GetByIdAsync`.

- [ ] **Step 5: Create `PurchaseReturnDetail.razor`**

Mirror `StockTransferDetail.razor` **exactly** for the approval plumbing (this is the copy-paste-critical part). Top of file:

```razor
@page "/transactions/purchase-returns/{Id:int}"
@attribute [Authorize(Policy = "transactions.purchase-returns.index")]
@rendermode InteractiveServer
@using FluentValidation
@using ErpOne.Application.Approvals
@using ErpOne.Application.Purchasing.PurchaseReturns
@inject IPurchaseReturnService Returns
@inject IAuthorizationService Auth
@inject SwalService Swal
```

Root `<div class="pf pf-detail">`. Header (ReturnNumber, Source, Supplier, dates), lines table, summary block (Subtotal/DiscountTotal/TaxTotal/GrandTotal/InventoryTotal), approval timeline. `@code` block — copy from `StockTransferDetail` and rename the service calls; keep the exact structure:
- `[Parameter] public int Id`, `[CascadingParameter] Task<AuthenticationState> AuthStateTask`.
- Fields: `_t` (PurchaseReturnDto?), `_steps`, `_loading/_busy/_canApprove/_showReject`, `_rejectReason`, `_error`, `_user`.
- `OnInitializedAsync`: `_user = (await AuthStateTask).User;` then `LoadAsync`.
- `LoadAsync`: `_t = await Returns.GetByIdAsync(Id); _steps = _t?.ApprovalSteps ?? []; _canApprove = await EvaluateCanApproveAsync();`.
- `EvaluateCanApproveAsync`: same as StockTransfer but with policy `"transactions.purchase-returns.approve"` and `_t.Status != "PendingApproval"` guard, creator-exclusion via `_t.CreatedBy`, `current.RoleName` role check.
- Submit gated by `Status == "Draft"` + policy `transactions.purchase-returns.post`; Approve/Reject gated by `Status == "PendingApproval" && _canApprove`.
- `SubmitAsync`/`ApproveAsync`/`RejectAsync` via `RunAsync(...)` wrapper (copy verbatim, catching `ValidationException`/`InvalidOperationException` into `_error`, toasting via `Swal.ToastAsync`).

Also add an Edit/Delete affordance for `Status == "Draft"` (Edit → Form route; Delete → `Returns.DeleteAsync` with a confirm), gated by the respective policies.

- [ ] **Step 6: Build + smoke**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings. (Manual UI smoke optional — logic is covered by Task 6.)

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs src/ErpOne.Web/Components/Pages/Transactions/PurchaseReturns/
git commit -m "feat(purchasing): Purchase Return pages (index/form/detail) + menu + approval seed"
```

---

### Task 8: Final regression + self-review + simplify

- [ ] **Step 1: Bump NumberSequence count assertion**

`tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs` asserts the seeded row count (currently 15). Update the expected count 15→16.

- [ ] **Step 2: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors/0 warnings; all green. Baseline (355) + Task-1 (5) + Task-2 (6) + Task-6 (5) + guard/seq tests ≈ **371+**.

- [ ] **Step 3: Confirm existing finance/GL untouched**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "SupplierInvoiceServiceTests|SupplierPaymentServiceTests|GoodsReceiptServiceTests|JournalPostingServiceTests|ArApAgingReportServiceTests"`
Expected: PASS — Outstanding formula change is additive (CreditedAmount=0 for pre-existing invoices → identical numbers).

- [ ] **Step 4: Simplify the new code**

Run the `code-simplifier` (or `/simplify`) over the new files, especially `PurchaseReturnService.GetByIdAsync` (remove the dead `whIds`/`lines` scaffold) and the returnable-source N+1 loops (batch the variant/warehouse lookups if straightforward). Re-run `dotnet test` after.

- [ ] **Step 5: Straggler grep**

Run: `git grep -n "PurchaseReturn" -- src/ErpOne.Infrastructure src/ErpOne.Application`
Expected: references confined to the new service, DTOs, GL method, and DI registration.

- [ ] **Step 6: Final commit (if fixes)**

```bash
git add -A
git commit -m "chore(purchasing): Purchase Return (Fase 2a) complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 Domain (PurchaseReturn/Line + enums, SupplierInvoice credit) → Tasks 1,2 ✓; §2 enums/constants/NumberSequence/prefix/CreditedAmount column → Task 3 ✓; §3 Application (DTOs, interface, validator) → Task 4 ✓; §4 Infrastructure service (returnable, remaining-qty both paths, CRUD, PostAsync) → Task 6 ✓; §5 GL PostPurchaseReturnAsync (both source branches, balanced) → Task 5 ✓; §6 Web (menu, seeder, 3 pages) → Task 7 ✓; §7 tests (GRN full, Invoice full, partial+remaining, on-hand short, over-Outstanding) → Task 6 ✓; §catatan SupplierPayment guard → Task 6 Step 7 ✓; NumberSequence assert bump → Task 8 ✓.

**Deviation from spec (approved 2026-07-22):** stock-out routes through `ICostingService.GetOutboundUnitCostAsync` (not raw GRN `UnitCost`), so `InventoryTotal` is seam-based and computed at post via `line.SetUnitCost` + `RecomputeInventoryTotal`. GL formulas still balance for any InventoryTotal (GR-IR variance absorbs the difference). Under the default MovingAverage method, seam cost == GRN cost for a single-GRN variant, so the spec's expected test numbers hold.

**Type consistency:** `PurchaseReturn`/`PurchaseReturnLine` ctors + `SetLines`/`SetUnitCost`/`RecomputeInventoryTotal`/lifecycle methods consistent between Task 2 (definition) and Tasks 6-7 (use). `IPurchaseReturnService` signatures identical across Tasks 4 (def), 6 (impl), 7 (web). `PostPurchaseReturnAsync` signature consistent Tasks 5↔6. Account keys (`InventoryAccountId`/`GrIrAccountId`/`ApAccountId`/`InputTaxAccountId`) match `PostingConfiguration`. `ApprovalDocumentType.PurchaseReturn`, `DocumentTypes.PurchaseReturn`, NumberSequence Id=16 consistent across Tasks 3,6,7.

**Verify-before-embed flags (must confirm during impl):** DbSet names (`Suppliers`, `Warehouses`, `GoodsReceiptLines`, `SupplierInvoices`), `Supplier.Name`, `ProductVariant.ProductId`; `IApprovalService` method signatures (copy from StockTransferService); `StockMovement` ctor arg order + `MovementType.Out`; `IStockService.GetOnHandAsync`/`RecordAdjustmentAsync` + `StockAdjustmentRequest`/`StockAdjustmentLine` shapes; `PagedResult<T>` + `ApprovalStepDto` namespaces; `SupplierInvoice`/`GoodsReceipt` EF-config precision & enum-storage convention (int vs string) — mirror the neighbor; test seed helpers (`SeedPostedGrnAsync`, `SeedSupplierInvoiceAsync`) copied from existing GRN/invoice tests; NumberSequence seed count in `NumberSequenceServiceTests`.
