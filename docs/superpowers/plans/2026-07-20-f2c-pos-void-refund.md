# POS Void / Refund Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add POS refund/void — a `PosRefund` document referencing the original `PosSale` that reverses stock, shift totals (cash reconciliation), and the GL journal, supporting partial per-item and multiple refunds; full void = refund all remaining qty.

**Architecture:** New `PosRefund`/`PosRefundLine` aggregate (Domain) + `IPosRefundService` (Application) + `PosRefundService` (Infrastructure). `PosSale` stays immutable; refund status is derived. Reuses `IJournalPostingService` (new `PostPosRefundAsync`), `IDocumentNumberService`, `UpsertStockAsync`, `StockMovement`, and the finance void-authorization UI pattern. Refund is only allowed while the sale's shift is still Open, and reverses the sale's original payment method.

**Tech Stack:** .NET / C#, EF Core (SQL Server; SQLite for tests), FluentValidation, Blazor Server (`.pf` detail design), xUnit integration tests.

## Global Constraints

- Domain entities: `private set`, private `// EF Core` ctor, backing `List<>` as `IReadOnlyCollection<>`, invariants `throw`. Namespace flat `ErpOne.Domain.Entities`.
- New entities under `src/ErpOne.Domain/Entities/Cashier/`.
- Refund allowed **only** when the sale's `CashierShift.Status == Open`; refund is recorded against that same shift.
- Refund reverses the sale's **original** `PaymentMethodId`/`IsCashPayment`.
- Rounding everywhere: `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
- Moving-average is **never** recomputed by a refund.
- No GL change beyond the new `PostPosRefundAsync` (fail-hard on missing mapping, consistent with 5b); idempotent by `(sourceType="PosRefund", sourceId)`.
- UI copy in English; reuse `.pf`/`.card`/`.cr-*` and `auth-overlay`/`auth-modal` classes already used by POS + finance void pages.
- **Commits are manual:** the user commits/pushes. Do NOT run `git commit`/`git push`. Each task ends with a **Checkpoint**.
- Register every new EF entity in `tablePrefixes` (`T_`) or the model build fails by design.

---

### Task 1: Domain — refund entities + shift reversal methods

**Files:**
- Create: `src/ErpOne.Domain/Entities/Cashier/PosRefund.cs`
- Create: `src/ErpOne.Domain/Entities/Cashier/PosRefundLine.cs`
- Modify: `src/ErpOne.Domain/Entities/Cashier/CashierShift.cs`
- Modify: `src/ErpOne.Domain/Entities/Cashier/CashierShiftTotal.cs`

**Interfaces:**
- Consumes: `AuditableEntity`, `CashierShiftStatus`.
- Produces:
  - `PosRefundLine(int posSaleLineId, int productVariantId, string variantSku, string productName, int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost)`; props `Id, PosRefundId, PosSaleLineId, ProductVariantId, VariantSku, ProductName, Quantity, UnitPrice, DiscountPercent, UnitCost, LineTotal`.
  - `PosRefund(string refundNumber, int posSaleId, int cashierShiftId, DateTime refundDate, int paymentMethodId, bool isCashPayment, string reason, string authorizedBy, string cashierUserId, string cashierName)`; methods `AddLine(...)`, `SetTotals(decimal subtotal, decimal txnDiscount, decimal taxTotal, decimal grandTotal, decimal cogsTotal)`; props incl. `Lines`.
  - `CashierShift.RecordRefund(int paymentMethodId, bool isCash, decimal amount)`.
  - `CashierShiftTotal.SubtractRefund(decimal amount)`.

- [ ] **Step 1: Create the refund line entity**

Create `src/ErpOne.Domain/Entities/Cashier/PosRefundLine.cs`:

```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Baris refund POS — merujuk PosSaleLine asli; snapshot harga/COGS dari sale.</summary>
public class PosRefundLine
{
    public int Id { get; private set; }
    public int PosRefundId { get; private set; }
    public int PosSaleLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public string VariantSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal { get; private set; }

    private PosRefundLine() { } // EF Core

    public PosRefundLine(int posSaleLineId, int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost)
    {
        if (posSaleLineId <= 0) throw new ArgumentException("PosSaleLineId must be > 0.", nameof(posSaleLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (unitCost < 0) throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        PosSaleLineId = posSaleLineId;
        ProductVariantId = productVariantId;
        VariantSku = variantSku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        UnitCost = unitCost;
        LineTotal = Round(Round(quantity * unitPrice) * (100m - discountPercent) / 100m);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 2: Create the refund aggregate**

Create `src/ErpOne.Domain/Entities/Cashier/PosRefund.cs`:

```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Dokumen refund/void POS. Merujuk PosSale asli; full void = refund seluruh qty tersisa.</summary>
public class PosRefund : AuditableEntity
{
    private readonly List<PosRefundLine> _lines = [];

    public int Id { get; private set; }
    public string RefundNumber { get; private set; } = default!;
    public int PosSaleId { get; private set; }
    public int CashierShiftId { get; private set; }
    public DateTime RefundDate { get; private set; }
    public int PaymentMethodId { get; private set; }
    public bool IsCashPayment { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TransactionDiscount { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal CogsTotal { get; private set; }
    public string Reason { get; private set; } = default!;
    public string? AuthorizedBy { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;

    public IReadOnlyCollection<PosRefundLine> Lines => _lines;

    private PosRefund() { } // EF Core

    public PosRefund(string refundNumber, int posSaleId, int cashierShiftId, DateTime refundDate,
        int paymentMethodId, bool isCashPayment, string reason, string authorizedBy,
        string cashierUserId, string cashierName)
    {
        if (string.IsNullOrWhiteSpace(refundNumber)) throw new ArgumentException("RefundNumber is required.", nameof(refundNumber));
        if (posSaleId <= 0) throw new ArgumentException("PosSaleId is required.", nameof(posSaleId));
        if (cashierShiftId <= 0) throw new ArgumentException("CashierShiftId is required.", nameof(cashierShiftId));
        if (paymentMethodId <= 0) throw new ArgumentException("PaymentMethodId is required.", nameof(paymentMethodId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(cashierUserId)) throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName)) throw new ArgumentException("CashierName is required.", nameof(cashierName));

        RefundNumber = refundNumber.Trim();
        PosSaleId = posSaleId;
        CashierShiftId = cashierShiftId;
        RefundDate = refundDate;
        PaymentMethodId = paymentMethodId;
        IsCashPayment = isCashPayment;
        Reason = reason.Trim();
        AuthorizedBy = string.IsNullOrWhiteSpace(authorizedBy) ? null : authorizedBy.Trim();
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
    }

    public void AddLine(int posSaleLineId, int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost) =>
        _lines.Add(new PosRefundLine(posSaleLineId, productVariantId, variantSku, productName,
            quantity, unitPrice, discountPercent, unitCost));

    /// <summary>Set total teralokasi (dihitung service). Dipanggil sekali setelah semua AddLine.</summary>
    public void SetTotals(decimal subtotal, decimal transactionDiscount, decimal taxTotal, decimal grandTotal, decimal cogsTotal)
    {
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot total a refund without lines.");
        Subtotal = subtotal;
        TransactionDiscount = transactionDiscount;
        TaxTotal = taxTotal;
        GrandTotal = grandTotal;
        CogsTotal = cogsTotal;
    }
}
```

- [ ] **Step 3: Add `SubtractRefund` to CashierShiftTotal**

In `src/ErpOne.Domain/Entities/Cashier/CashierShiftTotal.cs`, add after `Add`:

```csharp
    public void Add(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        TotalAmount += amount;
        TransactionCount += 1;
    }

    /// <summary>Kurangi total karena refund; TransactionCount TIDAK berubah (refund bukan sale baru).</summary>
    public void SubtractRefund(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        if (amount > TotalAmount) throw new InvalidOperationException("Refund amount exceeds recorded sales for this method.");
        TotalAmount -= amount;
    }
```

- [ ] **Step 4: Add `RecordRefund` to CashierShift**

In `src/ErpOne.Domain/Entities/Cashier/CashierShift.cs`, add after `RecordSale`:

```csharp
    /// <summary>Catat refund: kurangi total metode + kas (bila tunai). Hanya shift Open.</summary>
    public void RecordRefund(int paymentMethodId, bool isCash, decimal amount)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Cannot record a refund on a closed shift.");
        if (paymentMethodId <= 0)
            throw new ArgumentException("PaymentMethodId must be > 0.", nameof(paymentMethodId));
        if (amount <= 0)
            throw new ArgumentException("Amount must be > 0.", nameof(amount));

        var total = _totals.FirstOrDefault(t => t.PaymentMethodId == paymentMethodId)
            ?? throw new InvalidOperationException("No sales recorded for this payment method in the shift.");
        total.SubtractRefund(amount);

        if (isCash) CashSalesTotal -= amount;
    }
```

- [ ] **Step 5: Build the Domain project**

Run: `dotnet build src/ErpOne.Domain`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Checkpoint** — pause for user review & manual commit.

---

### Task 2: Constants & shared enums

**Files:**
- Modify: `src/ErpOne.Application/Settings/Numbering/DocumentTypes.cs`
- Modify: `src/ErpOne.Domain/Entities/Settings/ApprovalDocumentType.cs`

**Interfaces:**
- Produces: `DocumentTypes.PosRefund == "PosRefund"`; `ApprovalDocumentType.PosSaleVoid`.

- [ ] **Step 1: Add the document-type key**

In `DocumentTypes.cs`, add after the `StockOpname` line:

```csharp
    public const string StockOpname   = "StockOpname";
    public const string PosRefund     = "PosRefund";
```

- [ ] **Step 2: Add the approval document type**

In `ApprovalDocumentType.cs`, add `PosSaleVoid` after `StockOpname`:

```csharp
    StockTransfer,
    StockOpname,
    PosSaleVoid
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
- Create (generated): `src/ErpOne.Infrastructure/Persistence/Migrations/*_AddPosRefund.cs`

**Interfaces:**
- Consumes: `PosRefund`, `PosRefundLine`, `PosSale`, `PosSaleLine`, `PaymentMethod`, `CashierShift`, `NumberSequence`, `ResetPeriod`.
- Produces: `db.PosRefunds`, `db.PosRefundLines`; NumberSequence Id=15 (OPN already 14) Code="PosRefund" Prefix="RFN".

- [ ] **Step 1: Add DbSets**

In `AppDbContext.cs`, after the `PosSaleLines` DbSet, add (locate the POS DbSets near `PosSales`):

```csharp
    public DbSet<PosRefund> PosRefunds => Set<PosRefund>();
    public DbSet<PosRefundLine> PosRefundLines => Set<PosRefundLine>();
```

> If `PosSaleLines` DbSet is not present, place these two lines beside the `PosSales` DbSet declaration instead.

- [ ] **Step 2: Add the NumberSequence seed row (Id=15)**

In the `NumberSequence` `HasData(...)` block, add a comma after the Id=14 line and append:

```csharp
                new { Id = 14, Code = "StockOpname", Prefix = "OPN", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 15, Code = "PosRefund", Prefix = "RFN", DateFormat = "yyyyMMdd", Padding = 4, ResetPeriod = ResetPeriod.Daily, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```

- [ ] **Step 3: Add the entity configuration**

In `AppDbContext.cs`, after the `StockOpnameLine` config block, add:

```csharp
        modelBuilder.Entity<PosRefund>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RefundNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.RefundNumber).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.AuthorizedBy).HasMaxLength(256);
            e.Property(x => x.CashierUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CashierName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.TransactionDiscount).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.CogsTotal).HasPrecision(18, 2);

            e.HasOne<PosSale>().WithMany().HasForeignKey(x => x.PosSaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.CashierShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.PosRefundId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(PosRefund.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PosRefundLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.VariantSku).HasMaxLength(60).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<PosSaleLine>().WithMany().HasForeignKey(x => x.PosSaleLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });
```

> Match precision/maxlength conventions to the existing `PosSale`/`PosSaleLine` config blocks if they differ; grep them first and mirror exactly.

- [ ] **Step 4: Register table prefixes**

In the `tablePrefixes` dictionary, after the `StockOpnameLine` entry, add:

```csharp
            [nameof(StockOpname)] = "T_",
            [nameof(StockOpnameLine)] = "T_",
            [nameof(PosRefund)] = "T_",
            [nameof(PosRefundLine)] = "T_",
```

- [ ] **Step 5: Bump the NumberSequence count assertion**

In `tests/ErpOne.IntegrationTests/NumberSequenceServiceTests.cs`, change the count assert:

```csharp
        Assert.Equal(15, all.Count);   // ... + StockTransfer + StockOpname + PosRefund
```

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add AddPosRefund --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web --output-dir Persistence/Migrations`
Expected: New `*_AddPosRefund.cs` created with two `CreateTable` calls (`T_PosRefunds`, `T_PosRefundLines`), unique index on `RefundNumber`, FKs, and `InsertData` for NumberSequences Id=15.

- [ ] **Step 7: Build + run NumberSequence test**

Run: `dotnet build` then `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~NumberSequenceServiceTests"`
Expected: Build succeeded; tests PASS (count now 15).

- [ ] **Step 8: Checkpoint** — pause for user review & manual commit.

---

### Task 4: Application layer — DTOs, interface, validators

**Files:**
- Create: `src/ErpOne.Application/Cashier/PosRefunds/PosRefundDtos.cs`
- Create: `src/ErpOne.Application/Cashier/PosRefunds/IPosRefundService.cs`
- Create: `src/ErpOne.Application/Cashier/PosRefunds/PosRefundValidators.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` (`ErpOne.Application.Common`).
- Produces (later tasks rely on these exact records/signatures):
  - `PosRefundLineInput(int PosSaleLineId, int Quantity)`
  - `CreatePosRefundRequest(string Reason, IReadOnlyList<PosRefundLineInput> Lines)`
  - `RefundableLineDto(int PosSaleLineId, int ProductVariantId, string Sku, string ProductName, int SoldQty, int AlreadyRefundedQty, int RemainingQty, decimal UnitPrice, decimal DiscountPercent)`
  - `RefundableSaleDto(int PosSaleId, string SaleNumber, int CashierShiftId, bool ShiftOpen, string RefundStatus, decimal GrandTotal, IReadOnlyList<RefundableLineDto> Lines)`
  - `PosRefundLineDto(int Id, int PosSaleLineId, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal)`
  - `PosRefundDto(int Id, string RefundNumber, int PosSaleId, string SaleNumber, DateTime RefundDate, int PaymentMethodId, string PaymentMethodName, bool IsCashPayment, decimal Subtotal, decimal TransactionDiscount, decimal TaxTotal, decimal GrandTotal, string Reason, string? AuthorizedBy, string CashierName, IReadOnlyList<PosRefundLineDto> Lines)`
  - `PosRefundListItemDto(int Id, string RefundNumber, DateTime RefundDate, string SaleNumber, string PaymentMethodName, decimal GrandTotal, string CashierName)`
  - `IPosRefundService` with `GetRefundableAsync`, `RefundAsync`, `GetBySaleAsync`, `GetPagedAsync`.

- [ ] **Step 1: Create the DTOs**

Create `src/ErpOne.Application/Cashier/PosRefunds/PosRefundDtos.cs`:

```csharp
namespace ErpOne.Application.PosRefunds;

public record PosRefundLineInput(int PosSaleLineId, int Quantity);

public record CreatePosRefundRequest(string Reason, IReadOnlyList<PosRefundLineInput> Lines);

public record RefundableLineDto(int PosSaleLineId, int ProductVariantId, string Sku, string ProductName,
    int SoldQty, int AlreadyRefundedQty, int RemainingQty, decimal UnitPrice, decimal DiscountPercent);

public record RefundableSaleDto(int PosSaleId, string SaleNumber, int CashierShiftId, bool ShiftOpen,
    string RefundStatus, decimal GrandTotal, IReadOnlyList<RefundableLineDto> Lines);

public record PosRefundLineDto(int Id, int PosSaleLineId, int ProductVariantId, string Sku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public record PosRefundDto(int Id, string RefundNumber, int PosSaleId, string SaleNumber, DateTime RefundDate,
    int PaymentMethodId, string PaymentMethodName, bool IsCashPayment,
    decimal Subtotal, decimal TransactionDiscount, decimal TaxTotal, decimal GrandTotal,
    string Reason, string? AuthorizedBy, string CashierName, IReadOnlyList<PosRefundLineDto> Lines);

public record PosRefundListItemDto(int Id, string RefundNumber, DateTime RefundDate, string SaleNumber,
    string PaymentMethodName, decimal GrandTotal, string CashierName);
```

- [ ] **Step 2: Create the service interface**

Create `src/ErpOne.Application/Cashier/PosRefunds/IPosRefundService.cs`:

```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.PosRefunds;

public interface IPosRefundService
{
    Task<RefundableSaleDto?> GetRefundableAsync(int posSaleId, CancellationToken ct = default);
    Task<PosRefundDto> RefundAsync(int posSaleId, CreatePosRefundRequest request,
        string cashierUserId, string cashierName, string authorizedBy, CancellationToken ct = default);
    Task<IReadOnlyList<PosRefundDto>> GetBySaleAsync(int posSaleId, CancellationToken ct = default);
    Task<PagedResult<PosRefundListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, int? shiftId = null, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create the validators**

Create `src/ErpOne.Application/Cashier/PosRefunds/PosRefundValidators.cs`:

```csharp
using FluentValidation;

namespace ErpOne.Application.PosRefunds;

public class CreatePosRefundValidator : AbstractValidator<CreatePosRefundRequest>
{
    public CreatePosRefundValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Reason is required.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.PosSaleLineId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ErpOne.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Checkpoint** — pause for user review & manual commit.

---

### Task 5: GL engine — `PostPosRefundAsync`

**Files:**
- Modify: `src/ErpOne.Application/Accounting/IJournalPostingService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`

**Interfaces:**
- Consumes: `PosRefund` (Task 1); `PostingConfiguration`, `PostBalancedAsync`, `RequireAccount`, `ConfigAsync` (existing private helpers).
- Produces: `IJournalPostingService.PostPosRefundAsync(PosRefund refund, CancellationToken ct = default)`.

- [ ] **Step 1: Add the interface method**

In `IJournalPostingService.cs`, add after `PostPosSaleAsync`:

```csharp
    Task PostPosSaleAsync(PosSale sale, CancellationToken ct = default);
    Task PostPosRefundAsync(PosRefund refund, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it (proportional reverse of the POS sale journal)**

In `JournalPostingService.cs`, add after `PostPosSaleAsync`:

```csharp
    public async Task PostPosRefundAsync(PosRefund refund, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var posCash = RequireAccount(cfg.PosCashAccountId, "POS Cash");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var net = refund.GrandTotal - refund.TaxTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (sales, net, 0m, "POS refund revenue"),
            (posCash, 0m, refund.GrandTotal, "POS cash out (refund)"),
        };
        if (refund.TaxTotal > 0m)
            lines.Insert(1, (RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), refund.TaxTotal, 0m, "Output VAT reversed"));
        if (refund.CogsTotal > 0m)
        {
            lines.Add((inventory, refund.CogsTotal, 0m, "Inventory returned"));
            lines.Add((cogs, 0m, refund.CogsTotal, "COGS reversed"));
        }
        await PostBalancedAsync(refund.RefundDate, $"POS Refund {refund.RefundNumber}", "PosRefund", refund.Id, lines, ct);
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Checkpoint** — pause for user review & manual commit.

---

### Task 6: Infrastructure service + DI + integration tests (TDD)

**Files:**
- Create: `tests/ErpOne.IntegrationTests/PosRefundServiceTests.cs`
- Create: `src/ErpOne.Infrastructure/Services/Cashier/PosRefundService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `IPosRefundService` + DTOs (Task 4); `PostPosRefundAsync` (Task 5); `AppDbContext`, `UpsertStockAsync`, `StockMovement`, `MovementType.In`, `IDocumentNumberService`, `DocumentTypes.PosRefund`.
- Produces: `PosRefundService : IPosRefundService`; DI registration.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/ErpOne.IntegrationTests/PosRefundServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.PosSales;
using ErpOne.Application.PosRefunds;
using ErpOne.Application.CashierShifts;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosRefundServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosRefundServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds warehouse, cash payment method, product/variant with stock, an open shift.
    // Returns (shiftId, warehouseId, variantId, cashMethodId, cardMethodId).
    private static async Task<(int shiftId, int whId, int variantId, int cashPm, int cardPm)> SeedAsync(IServiceProvider sp, int opening)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Wh {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        var cash = new PaymentMethod($"Cash {id}", PaymentType.Tunai, true);
        var card = new PaymentMethod($"Card {id}", PaymentType.NonTunai, true);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat); db.PaymentMethods.AddRange(cash, card);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, wh.Id, opening));
        await db.SaveChangesAsync();

        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shiftSvc.OpenAsync("cashier1", "Cashier One", new OpenShiftRequest(wh.Id, 0m));
        return (shift.Id, wh.Id, v.Id, cash.Id, card.Id);
    }

    private static async Task<PosSaleDto> SellAsync(IServiceProvider sp, int shiftId, int variantId, int pmId, int qty, decimal unitPrice)
    {
        var pos = sp.GetRequiredService<IPosSaleService>();
        return await pos.CreateSaleAsync("cashier1", "Cashier One", shiftId,
            new CreatePosSaleRequest(pmId, null, 0m, qty * unitPrice, [new PosSaleLineRequest(variantId, qty, unitPrice, 0m)]));
    }

    [Fact]
    public async Task Full_void_reverses_stock_shift_and_gl()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 3, 2000m); // sells 3 → stock 97
        Assert.Equal(97, await stock.GetOnHandAsync(variantId, wh));

        var refunds = sp.GetRequiredService<IPosRefundService>();
        var refundable = await refunds.GetRefundableAsync(sale.Id);
        var lines = refundable!.Lines.Select(l => new PosRefundLineInput(l.PosSaleLineId, l.RemainingQty)).ToList();
        var refund = await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("Customer changed mind", lines),
            "cashier1", "Cashier One", "supervisor");

        Assert.Equal(sale.GrandTotal, refund.GrandTotal);
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh)); // stock fully back

        var db = sp.GetRequiredService<AppDbContext>();
        var shift = await db.CashierShifts.Include(s => s.Totals).FirstAsync(s => s.Id == shiftId);
        Assert.Equal(0m, shift.Totals.First(t => t.PaymentMethodId == cashPm).TotalAmount);
        Assert.Equal(0m, shift.ExpectedCash); // OpeningFloat 0 + cash sales 0 after refund
        Assert.True(await db.JournalEntries.AnyAsync(j => j.SourceType == "PosRefund" && j.SourceId == refund.Id));
    }

    [Fact]
    public async Task Partial_refund_tracks_remaining_and_allows_second_refund()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 3, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();

        var r1 = await refunds.GetRefundableAsync(sale.Id);
        var lineId = r1!.Lines[0].PosSaleLineId;
        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("one back", [new PosRefundLineInput(lineId, 1)]),
            "cashier1", "Cashier One", "supervisor");

        var r2 = await refunds.GetRefundableAsync(sale.Id);
        Assert.Equal(2, r2!.Lines[0].RemainingQty);
        Assert.Equal("PartiallyRefunded", r2.RefundStatus);
        Assert.Equal(98, await stock.GetOnHandAsync(variantId, wh)); // 97 + 1

        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("rest back", [new PosRefundLineInput(lineId, 2)]),
            "cashier1", "Cashier One", "supervisor");
        var r3 = await refunds.GetRefundableAsync(sale.Id);
        Assert.Equal(0, r3!.Lines[0].RemainingQty);
        Assert.Equal("Refunded", r3.RefundStatus);
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
    }

    [Fact]
    public async Task Over_refund_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, _, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 2, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();
        var lineId = (await refunds.GetRefundableAsync(sale.Id))!.Lines[0].PosSaleLineId;

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("too much", [new PosRefundLineInput(lineId, 5)]),
                "cashier1", "Cashier One", "supervisor"));
    }

    [Fact]
    public async Task Refund_on_closed_shift_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, wh, variantId, cashPm, _) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cashPm, 2, 2000m);
        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();
        await shiftSvc.CloseAsync(shiftId, new CloseShiftRequest(0m, null));

        var refunds = sp.GetRequiredService<IPosRefundService>();
        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        var before = await stock.GetOnHandAsync(variantId, wh);
        // GetRefundableAsync still resolves the sale line id even when shift is closed:
        var db = sp.GetRequiredService<AppDbContext>();
        var lineId = await db.PosSaleLines.Where(l => l.PosSaleId == sale.Id).Select(l => l.Id).FirstAsync();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("late", [new PosRefundLineInput(lineId, 1)]),
                "cashier1", "Cashier One", "supervisor"));
        Assert.Equal(before, await stock.GetOnHandAsync(variantId, wh)); // unchanged
    }

    [Fact]
    public async Task Card_refund_does_not_touch_cash_drawer()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (shiftId, _, variantId, _, cardPm) = await SeedAsync(sp, 100);
        var sale = await SellAsync(sp, shiftId, variantId, cardPm, 2, 2000m);
        var refunds = sp.GetRequiredService<IPosRefundService>();
        var lineId = (await refunds.GetRefundableAsync(sale.Id))!.Lines[0].PosSaleLineId;

        await refunds.RefundAsync(sale.Id, new CreatePosRefundRequest("card back", [new PosRefundLineInput(lineId, 2)]),
            "cashier1", "Cashier One", "supervisor");

        var db = sp.GetRequiredService<AppDbContext>();
        var shift = await db.CashierShifts.Include(s => s.Totals).FirstAsync(s => s.Id == shiftId);
        Assert.Equal(0m, shift.CashSalesTotal);      // never had cash
        Assert.Equal(0m, shift.ExpectedCash);        // OpeningFloat 0, untouched
        Assert.Equal(0m, shift.Totals.First(t => t.PaymentMethodId == cardPm).TotalAmount); // card total reversed
    }
}
```

> **Verify before running (grep first):** `ICashierShiftService.OpenAsync`/`CloseAsync` signatures and `OpenShiftRequest`/`CloseShiftRequest` shapes; `PaymentMethod` ctor and `PaymentType` values; the `ICashierShiftService` namespace (`ErpOne.Application.CashierShifts`). Adjust the seed helpers if any differ — the assertions stay the same.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PosRefundServiceTests"`
Expected: FAIL — `IPosRefundService` not registered.

- [ ] **Step 3: Implement the service**

Create `src/ErpOne.Infrastructure/Services/Cashier/PosRefundService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.PosRefunds;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PosRefundService(
    AppDbContext db,
    IValidator<CreatePosRefundRequest> validator,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IPosRefundService
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public async Task<RefundableSaleDto?> GetRefundableAsync(int posSaleId, CancellationToken ct = default)
    {
        var sale = await db.PosSales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == posSaleId, ct);
        if (sale is null) return null;
        var shiftOpen = await db.CashierShifts.Where(s => s.Id == sale.CashierShiftId)
            .Select(s => s.Status).FirstOrDefaultAsync(ct) == CashierShiftStatus.Open;
        var refunded = await RefundedQtyByLineAsync(posSaleId, ct);

        var lines = new List<RefundableLineDto>();
        foreach (var l in sale.Lines.OrderBy(l => l.Id))
        {
            var already = refunded.TryGetValue(l.Id, out var q) ? q : 0;
            lines.Add(new RefundableLineDto(l.Id, l.ProductVariantId, l.VariantSku, l.ProductName,
                l.Quantity, already, l.Quantity - already, l.UnitPrice, l.DiscountPercent));
        }
        var remaining = lines.Sum(x => x.RemainingQty);
        var totalSold = lines.Sum(x => x.SoldQty);
        var status = remaining == totalSold ? "Completed" : remaining == 0 ? "Refunded" : "PartiallyRefunded";
        return new RefundableSaleDto(sale.Id, sale.SaleNumber, sale.CashierShiftId, shiftOpen, status, sale.GrandTotal, lines);
    }

    public async Task<PosRefundDto> RefundAsync(int posSaleId, CreatePosRefundRequest request,
        string cashierUserId, string cashierName, string authorizedBy, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sale = await db.PosSales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == posSaleId, ct)
            ?? throw Fail("Sale tidak ditemukan.");
        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == sale.CashierShiftId, ct)
            ?? throw Fail("Shift tidak ditemukan.");
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup, tidak bisa refund.");

        var refunded = await RefundedQtyByLineAsync(posSaleId, ct);
        var now = DateTime.Now;
        var number = await docNumbers.NextAsync(DocumentTypes.PosRefund, now, ct);
        var refund = new PosRefund(number, sale.Id, sale.CashierShiftId, now,
            sale.PaymentMethodId, sale.IsCashPayment, request.Reason, authorizedBy, cashierUserId, cashierName);

        decimal refundSubtotal = 0m, cogsTotal = 0m;
        foreach (var input in request.Lines)
        {
            var saleLine = sale.Lines.FirstOrDefault(l => l.Id == input.PosSaleLineId)
                ?? throw Fail($"Baris {input.PosSaleLineId} bukan bagian dari sale ini.");
            var already = refunded.TryGetValue(saleLine.Id, out var q) ? q : 0;
            var remaining = saleLine.Quantity - already;
            if (input.Quantity > remaining)
                throw Fail($"Qty refund {saleLine.VariantSku} melebihi sisa ({remaining}).");

            refund.AddLine(saleLine.Id, saleLine.ProductVariantId, saleLine.VariantSku, saleLine.ProductName,
                input.Quantity, saleLine.UnitPrice, saleLine.DiscountPercent, saleLine.UnitCost);

            db.StockMovements.Add(new StockMovement(saleLine.ProductVariantId, sale.WarehouseId, MovementType.In,
                input.Quantity, saleLine.UnitCost, now, refType: "PosRefund", refId: null, note: refund.RefundNumber));
            await db.UpsertStockAsync(saleLine.ProductVariantId, sale.WarehouseId, input.Quantity, ct);

            refundSubtotal += Round(Round(input.Quantity * saleLine.UnitPrice) * (100m - saleLine.DiscountPercent) / 100m);
            cogsTotal += Round(input.Quantity * saleLine.UnitCost);
        }

        var allocTxnDiscount = sale.Subtotal == 0m ? 0m : Round(sale.TransactionDiscount * refundSubtotal / sale.Subtotal);
        var baseAmount = refundSubtotal - allocTxnDiscount;
        var taxTotal = Round(baseAmount * sale.TaxRateSnapshot / 100m);
        var grandTotal = baseAmount + taxTotal;
        refund.SetTotals(refundSubtotal, allocTxnDiscount, taxTotal, grandTotal, cogsTotal);

        shift.RecordRefund(sale.PaymentMethodId, sale.IsCashPayment, grandTotal);

        db.PosRefunds.Add(refund);
        await db.SaveChangesAsync(ct);

        await journalPoster.PostPosRefundAsync(refund, ct);

        await tx.CommitAsync(ct);
        return (await GetByIdAsync(refund.Id, ct))!;
    }

    public async Task<IReadOnlyList<PosRefundDto>> GetBySaleAsync(int posSaleId, CancellationToken ct = default)
    {
        var ids = await db.PosRefunds.AsNoTracking().Where(r => r.PosSaleId == posSaleId)
            .OrderByDescending(r => r.Id).Select(r => r.Id).ToListAsync(ct);
        var list = new List<PosRefundDto>();
        foreach (var id in ids) list.Add((await GetByIdAsync(id, ct))!);
        return list;
    }

    public async Task<PagedResult<PosRefundListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, int? shiftId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.PosRefunds.AsNoTracking();
        if (shiftId is { } sid) query = query.Where(r => r.CashierShiftId == sid);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(r => r.RefundNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.RefundNumber, r.RefundDate, r.PosSaleId, r.PaymentMethodId, r.GrandTotal, r.CashierName
            }).ToListAsync(ct);

        var saleNos = await db.PosSales.AsNoTracking().Where(s => rows.Select(x => x.PosSaleId).Contains(s.Id))
            .Select(s => new { s.Id, s.SaleNumber }).ToListAsync(ct);
        var pms = await db.PaymentMethods.AsNoTracking().Where(m => rows.Select(x => x.PaymentMethodId).Contains(m.Id))
            .Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var items = rows.Select(r => new PosRefundListItemDto(
            r.Id, r.RefundNumber, r.RefundDate,
            saleNos.FirstOrDefault(s => s.Id == r.PosSaleId)?.SaleNumber ?? "—",
            pms.FirstOrDefault(p => p.Id == r.PaymentMethodId)?.Name ?? "—",
            r.GrandTotal, r.CashierName)).ToList();
        return new PagedResult<PosRefundListItemDto>(items, total, page, pageSize);
    }

    private async Task<Dictionary<int, int>> RefundedQtyByLineAsync(int posSaleId, CancellationToken ct) =>
        await (from rl in db.PosRefundLines.AsNoTracking()
               join r in db.PosRefunds.AsNoTracking() on rl.PosRefundId equals r.Id
               where r.PosSaleId == posSaleId
               group rl by rl.PosSaleLineId into g
               select new { LineId = g.Key, Qty = g.Sum(x => x.Quantity) })
              .ToDictionaryAsync(x => x.LineId, x => x.Qty, ct);

    private async Task<PosRefundDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var r = await db.PosRefunds.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var saleNo = await db.PosSales.Where(s => s.Id == r.PosSaleId).Select(s => s.SaleNumber).FirstOrDefaultAsync(ct) ?? "—";
        var pmName = await db.PaymentMethods.Where(m => m.Id == r.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(ct) ?? "—";
        var lines = r.Lines.OrderBy(l => l.Id).Select(l => new PosRefundLineDto(
            l.Id, l.PosSaleLineId, l.ProductVariantId, l.VariantSku, l.ProductName,
            l.Quantity, l.UnitPrice, l.DiscountPercent, l.LineTotal)).ToList();
        return new PosRefundDto(r.Id, r.RefundNumber, r.PosSaleId, saleNo, r.RefundDate,
            r.PaymentMethodId, pmName, r.IsCashPayment,
            r.Subtotal, r.TransactionDiscount, r.TaxTotal, r.GrandTotal,
            r.Reason, r.AuthorizedBy, r.CashierName, lines);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PosRefund", message)]);
}
```

- [ ] **Step 4: Register the service in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add the using and registration (near the POS registration ~line 107):

```csharp
using ErpOne.Application.PosRefunds;
```

```csharp
        services.AddScoped<IPosSaleService, PosSaleService>();
        services.AddScoped<IPosRefundService, PosRefundService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PosRefundServiceTests"`
Expected: PASS — all 5 tests green.

- [ ] **Step 6: Checkpoint** — pause for user review & manual commit.

---

### Task 7: Menu action + approval-chain seed

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs`

**Interfaces:**
- Consumes: `ActVoid` constant; `ApprovalDocumentType.PosSaleVoid`.
- Produces: policy `cashier.pos.void` (auto-seeded from `AppMenus.AllPermissions`); default `PosSaleVoid` chain.

- [ ] **Step 1: Add the void action to the POS resource**

In `AppMenus.cs`, change `PosActions`:

```csharp
    private static AppAction[] PosActions => [ActIndex, ActCreate, ActVoid];
```

- [ ] **Step 2: Seed the default approval chain**

In `BootstrapSeeder.cs`, after the Stock Opname chain block, add:

```csharp
        // Seed rantai approval default untuk POS Void (idempotent).
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PosSaleVoid))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PosSaleVoid, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Checkpoint** — pause for user review & manual commit.

---

### Task 8: Web — refund panel + void authorization + history on PosSaleDetail

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Cashier/Pos/PosSaleDetail.razor`

**Interfaces:**
- Consumes: `IPosRefundService` (GetRefundableAsync/RefundAsync/GetBySaleAsync); `RefundableSaleDto`, `RefundableLineDto`, `PosRefundDto`, `CreatePosRefundRequest`, `PosRefundLineInput`; `UserManager<ApplicationUser>`, `RoleManager<ApplicationRole>`, `IApprovalChainService`, `AuthenticationState`; `AppMenus.Perm`/`ClaimType`; `SwalService`.

- [ ] **Step 1: Add injects + usings**

At the top of `PosSaleDetail.razor`, after the existing `@inject` block, add:

```razor
@using Microsoft.AspNetCore.Identity
@using ErpOne.Application.Approvals
@using ErpOne.Application.PosRefunds
@inject IPosRefundService Refunds
@inject IApprovalChainService ApprovalChains
@inject UserManager<ApplicationUser> UserManager
@inject RoleManager<ApplicationRole> RoleManager
@inject SwalService Swal
```

- [ ] **Step 2: Add the status badge + Refund/Void button to `pd-head`**

Replace the `pd-head` actions block:

```razor
        <div class="pd-head">
            <h1>@_sale.SaleNumber</h1>
            <span class="badge @(_sale.IsCashPayment ? "b-done" : "b-dark")">@_sale.PaymentMethodName</span>
            @if (_refundable is not null && _refundable.RefundStatus != "Completed")
            {
                <span class="badge @(_refundable.RefundStatus == "Refunded" ? "b-danger" : "b-warn")">@_refundable.RefundStatus</span>
            }
            <div class="actions">
                <button class="btn btn-primary" @onclick="ReprintAsync"><i class="bi bi-printer"></i> Reprint</button>
                @if (_refundable is { ShiftOpen: true } && _refundable.RefundStatus != "Refunded")
                {
                    <AuthorizeView Policy="cashier.pos.void"><Authorized>
                        <button class="btn btn-danger" @onclick="OpenRefund" disabled="@_busy"><i class="bi bi-x-octagon"></i> Refund / Void</button>
                    </Authorized></AuthorizeView>
                }
            </div>
        </div>
```

- [ ] **Step 3: Add the refund panel + auth overlay (place right after `pd-head`, inside the `else` block)**

```razor
        @if (_showRefund && _refundable is not null)
        {
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-arrow-counterclockwise"></i></span><h2>Refund / Void</h2></div>
                <div class="card-b">
                    @if (_refundError is not null) { <div class="pf-alert err"><i class="bi bi-exclamation-octagon"></i> @_refundError</div> }
                    <div class="table-responsive">
                        <table class="table align-middle">
                            <thead class="table-light"><tr><th>Product</th><th class="text-end">Sold</th><th class="text-end">Refunded</th><th class="text-end">Remaining</th><th class="text-end" style="width:140px">Refund qty</th></tr></thead>
                            <tbody>
                                @for (int i = 0; i < _rows.Count; i++)
                                {
                                    var idx = i;
                                    <tr>
                                        <td><span class="pn">@_rows[idx].ProductName</span> <span class="sku">@_rows[idx].Sku</span></td>
                                        <td class="text-end mono">@_rows[idx].SoldQty</td>
                                        <td class="text-end mono">@_rows[idx].AlreadyRefundedQty</td>
                                        <td class="text-end mono">@_rows[idx].RemainingQty</td>
                                        <td><input class="ctl mono text-end" type="number" min="0" max="@_rows[idx].RemainingQty" step="1" @bind="_rows[idx].RefundQty" disabled="@(_rows[idx].RemainingQty == 0)" /></td>
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                    <div class="grid mt-2">
                        <div class="f c12"><label class="fl">Reason <span class="req">*</span></label><input class="ctl" @bind="_reason" placeholder="Why is this refunded?" /></div>
                    </div>
                    <div class="row-end" style="margin-top:12px">
                        <button class="btn btn-outline-secondary btn-sm" @onclick="RefundAll">Refund all remaining</button>
                        <button class="btn btn-ghost btn-sm" @onclick="@(() => _showRefund = false)">Cancel</button>
                        <button class="btn btn-danger btn-sm" @onclick="OpenAuth" disabled="@_busy">Continue</button>
                    </div>
                </div>
            </section>
        }

        @if (_showAuth)
        {
            <div class="auth-overlay" @onclick="@(() => _showAuth = false)">
                <div class="auth-modal" @onclick:stopPropagation="true">
                    <h3><i class="bi bi-shield-lock"></i> Refund authorization</h3>
                    <p>Enter the credentials of an authorized user to approve this refund.</p>
                    @if (_authError is not null) { <div class="pf-alert err">@_authError</div> }
                    <label class="fl">Username</label>
                    <input class="ctl" @bind="_authUser" autocomplete="off" />
                    <label class="fl" style="margin-top:10px">Password</label>
                    <input class="ctl" type="password" @bind="_authPass" autocomplete="off" />
                    <div class="row-end" style="margin-top:14px">
                        <button class="btn btn-ghost btn-sm" @onclick="@(() => _showAuth = false)" disabled="@_busy">Cancel</button>
                        <button class="btn btn-danger btn-sm" @onclick="ConfirmRefundAsync" disabled="@_busy">
                            @if (_busy) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
                            Authorize &amp; refund
                        </button>
                    </div>
                </div>
            </div>
        }
```

- [ ] **Step 4: Add the refund history card (place after the Summary card, inside `else`)**

```razor
        @if (_history.Count > 0)
        {
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-clock-history"></i></span><h2>Refunds</h2></div>
                <div class="card-b">
                    <table class="items">
                        <thead><tr><th>Refund</th><th>Date</th><th>By</th><th>Authorized</th><th class="r">Amount</th></tr></thead>
                        <tbody>
                            @foreach (var h in _history)
                            {
                                <tr>
                                    <td class="mono">@h.RefundNumber</td>
                                    <td>@h.RefundDate.ToString("dd MMM yyyy HH:mm")</td>
                                    <td>@h.CashierName</td>
                                    <td>@(h.AuthorizedBy ?? "—")</td>
                                    <td class="r mono total">@h.GrandTotal.ToString("N2")</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
            </section>
        }
```

- [ ] **Step 5: Replace the `@code` block with refund logic**

```razor
@code {
    [Parameter] public int Id { get; set; }
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private sealed class RefundRow { public int PosSaleLineId; public string Sku = ""; public string ProductName = ""; public int SoldQty; public int AlreadyRefundedQty; public int RemainingQty; public int RefundQty; }

    private PosSaleDto? _sale;
    private CompanySettingDto? _company;
    private RefundableSaleDto? _refundable;
    private IReadOnlyList<PosRefundDto> _history = [];
    private readonly List<RefundRow> _rows = [];
    private bool _loading = true, _busy, _showRefund, _showAuth;
    private string _reason = "";
    private string _authUser = "", _authPass = "";
    private string? _refundError, _authError;
    private System.Security.Claims.ClaimsPrincipal _user = default!;

    protected override async Task OnInitializedAsync()
    {
        _user = (await AuthStateTask).User;
        _company = await CompanyService.GetAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _sale = await Pos.GetByIdAsync(Id);
        if (_sale is not null)
        {
            _refundable = await Refunds.GetRefundableAsync(Id);
            _history = await Refunds.GetBySaleAsync(Id);
        }
        _loading = false;
    }

    private void OpenRefund()
    {
        _refundError = null; _reason = ""; _rows.Clear();
        foreach (var l in _refundable!.Lines)
            _rows.Add(new RefundRow { PosSaleLineId = l.PosSaleLineId, Sku = l.Sku, ProductName = l.ProductName,
                SoldQty = l.SoldQty, AlreadyRefundedQty = l.AlreadyRefundedQty, RemainingQty = l.RemainingQty, RefundQty = 0 });
        _showRefund = true;
    }

    private void RefundAll() { foreach (var r in _rows) r.RefundQty = r.RemainingQty; }

    private void OpenAuth()
    {
        _refundError = null;
        if (string.IsNullOrWhiteSpace(_reason)) { _refundError = "Reason is required."; return; }
        if (_rows.All(r => r.RefundQty <= 0)) { _refundError = "Enter at least one refund quantity."; return; }
        if (_rows.Any(r => r.RefundQty < 0 || r.RefundQty > r.RemainingQty)) { _refundError = "Refund qty exceeds remaining."; return; }
        _authError = null; _authUser = ""; _authPass = ""; _showAuth = true;
    }

    private async Task ConfirmRefundAsync()
    {
        _authError = null; _busy = true;
        try
        {
            var authName = await VerifyAuthorizerAsync(_authUser, _authPass);
            if (authName is null) { _authError = "Invalid credentials or user not authorized to refund."; return; }
            var lines = _rows.Where(r => r.RefundQty > 0).Select(r => new PosRefundLineInput(r.PosSaleLineId, r.RefundQty)).ToList();
            await Refunds.RefundAsync(Id, new CreatePosRefundRequest(_reason, lines),
                _user.Identity?.Name ?? "", _user.Identity?.Name ?? "", authName);
            _showAuth = false; _showRefund = false;
            await LoadAsync();
            await Swal.ToastAsync("success", "Refund posted");
        }
        catch (Exception ex) { _authError = ex.Message; }
        finally { _busy = false; }
    }

    private async Task<string?> VerifyAuthorizerAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        var user = await UserManager.FindByNameAsync(username.Trim());
        if (user is null || !user.IsActive) return null;
        if (!await UserManager.CheckPasswordAsync(user, password)) return null;

        var userRoles = await UserManager.GetRolesAsync(user);
        var voidRoles = (await ApprovalChains.GetByDocumentTypeAsync(ApprovalDocumentType.PosSaleVoid))
            .Select(s => s.RoleName).ToHashSet();
        var authorized = voidRoles.Count > 0
            ? userRoles.Any(voidRoles.Contains)
            : await UserHasVoidPermissionAsync(userRoles);
        return authorized ? (user.UserName ?? username) : null;
    }

    private async Task<bool> UserHasVoidPermissionAsync(IEnumerable<string> userRoles)
    {
        var perm = AppMenus.Perm("cashier.pos", "void");
        foreach (var roleName in userRoles)
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var claims = await RoleManager.GetClaimsAsync(role);
            if (claims.Any(c => c.Type == AppMenus.ClaimType && c.Value == perm)) return true;
        }
        return false;
    }

    private async Task ReprintAsync() => await JS.InvokeVoidAsync("appPrint");
}
```

> The page must have access to `ApplicationUser`/`ApplicationRole`/`AppMenus`. These are used by finance detail pages without extra usings (global usings / `_Imports.razor`); if the build complains, add the same usings `ArReceiptDetail.razor` relies on (grep its top + `_Imports.razor`).

- [ ] **Step 6: Build the full solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Run the full integration test suite**

Run: `dotnet test tests/ErpOne.IntegrationTests`
Expected: All PASS (~189 integration = prior 184 + 5 new; NumberSequence assert now 15).

- [ ] **Step 8: Manual smoke test (verify end-to-end)**

Run the app, apply the migration (`dotnet ef database update ...`), sign out/in (new `cashier.pos.void` permission):
1. Open a shift, make a POS sale, open its detail at `/cashier/sales/{id}`.
2. Click **Refund / Void** → enter a refund qty (partial) + reason → **Continue** → supervisor credentials → confirm.
3. Verify: stock returned, sale shows "Partially refunded" + a refund in history, shift/cashier-shift report totals dropped, cash drawer expected-cash reduced for a cash sale.
4. Refund the remainder → status "Refunded", button hidden. Close the shift and confirm a fully/partly refunded sale can no longer be refunded.

- [ ] **Step 9: Checkpoint** — pause for user review & manual commit.

---

## Self-Review Notes

- **Spec coverage:** Domain refund aggregate + shift reversal (Task 1); enums/constants (Task 2); EF + migration + NumberSequence Id=15 + test bump (Task 3); Application DTOs/interface/validators (Task 4); GL `PostPosRefundAsync` (Task 5); service with proportional allocation, live remaining-qty guard, same-open-shift guard, original-method reversal + DI + 5 tests (Task 6); menu `cashier.pos.void` + `PosSaleVoid` seed (Task 7); PosSaleDetail refund panel + void-auth + history (Task 8). Non-goals from the spec are respected (no cross-shift, no refund receipt, no MA change, no exchange).
- **Test count:** baseline 318 (134 unit + 184 integration). +5 integration → ~323 total; NumberSequence assert 14→15.
- **Type consistency:** `RefundAsync(posSaleId, CreatePosRefundRequest, cashierUserId, cashierName, authorizedBy)` matches interface, service, tests, and UI call; `PosRefundLineInput(PosSaleLineId, Quantity)`; `refund.SetTotals(subtotal, txnDiscount, taxTotal, grandTotal, cogsTotal)`; `StockMovement(..., "PosRefund", refId:null, note: RefundNumber)`; GL SourceType `"PosRefund"` matches the test's JournalEntries filter; `RefundStatus` values `Completed/PartiallyRefunded/Refunded` used identically in service + UI.
- **Assumptions to verify during execution (grep first):** `ICashierShiftService.OpenAsync/CloseAsync` + `OpenShiftRequest/CloseShiftRequest`; `PaymentMethod` ctor + `PaymentType`; `PosSaleLines` DbSet presence; PosSale/PosSaleLine EF precision conventions; that `_Imports.razor` exposes `ApplicationUser`/`ApplicationRole`/`AppMenus` to Razor pages; the `dotnet ef` startup project.
```
