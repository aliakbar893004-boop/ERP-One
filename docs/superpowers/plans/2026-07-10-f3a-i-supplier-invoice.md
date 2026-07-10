# Fase 3a-i — Supplier Invoice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Record accounts-payable liabilities as Supplier Invoices built from one or more posted GRNs (lines auto-derived from PO pricing), giving a real outstanding-payables figure. No payment or approval (those are 3a-ii).

**Architecture:** Mirrors the PurchaseOrder module. Domain `SupplierInvoice`/`SupplierInvoiceLine` (aggregate computes totals like `PurchaseOrder.SetLines`) → EF config inline in `AppDbContext` → `ErpOne.Application/SupplierInvoices` (service + DTOs + validators) → `ErpOne.Infrastructure/Services/SupplierInvoiceService` (mirrors `PurchaseOrderService`) → Blazor `.pi`/`.cf`/`.pf` pages under a Finance group. Numbering via the centralized `IDocumentNumberService`.

**Tech Stack:** .NET / C#, EF Core (SQL Server prod, SQLite in-memory tests), Blazor Server, FluentValidation, xUnit.

## Global Constraints
- UI English; index `.pi`, form `.cf`, detail `.pf` (Atlas).
- Register new entities in `tablePrefixes` (`T_` transaksi) in `AppDbContext` or the model build fails.
- Money fields precision (18,2); `Status` via `HasConversion<string>()`.
- Numbering format for `SupplierInvoice`: `APV-{yyyyMM}-{0000}` (monthly, pad 4) — seeded as `NumberSequence` Id=7.
- Integration tests use `EnsureCreated()` (schema from model; no migration needed for tests). Migration generated for prod.
- Commit after each task's tests pass.

## Reference patterns (read before starting)
- `src/ErpOne.Domain/Entities/PurchaseOrder.cs` (aggregate + `SetLines`/`RecomputeTotals`) and `PurchaseOrderLine.cs` (`Recompute`).
- `src/ErpOne.Infrastructure/Services/PurchaseOrderService.cs` (transaction, `BuildLinesAsync`, `GetByIdAsync` name lookups, dashboard).
- `src/ErpOne.Web/Components/Pages/Transactions/PurchaseOrders/*.razor` (`PoIndex`/`PoForm`/`PoDetail`).

---

## Task 1: Domain entities + EF config + numbering + migration

**Files:**
- Create: `src/ErpOne.Domain/Entities/SupplierInvoiceStatus.cs`
- Create: `src/ErpOne.Domain/Entities/SupplierInvoiceLine.cs`
- Create: `src/ErpOne.Domain/Entities/SupplierInvoice.cs`
- Modify: `src/ErpOne.Application/Numbering/DocumentTypes.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Create: migration `AddSupplierInvoice`

**Interfaces:**
- Produces:
  - `enum SupplierInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }`
  - `SupplierInvoiceLine` ctor `(int goodsReceiptId, int goodsReceiptLineId, int productVariantId, int quantity, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)`; props `Id, SupplierInvoiceId, GoodsReceiptId, GoodsReceiptLineId, ProductVariantId, Quantity, UnitPrice, DiscountPercent, TaxRateSnapshot, LineSubtotal, LineDiscount, LineTax, LineTotal`.
  - `SupplierInvoice` ctor `(string invoiceNumber, int supplierId, string currency, DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)`; methods `SetLines(IEnumerable<SupplierInvoiceLine>)`, `UpdateHeader(DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)`, `Cancel()`; props `Id, InvoiceNumber, SupplierInvoiceNo, SupplierId, Currency, InvoiceDate, DueDate, Notes, Status, Subtotal, DiscountTotal, TaxTotal, GrandTotal, PaidAmount, Outstanding, Lines`.
  - `DocumentTypes.SupplierInvoice = "SupplierInvoice"`.
  - `AppDbContext.SupplierInvoices`, `AppDbContext.SupplierInvoiceLines`.

- [ ] **Step 1: Create the status enum**

`src/ErpOne.Domain/Entities/SupplierInvoiceStatus.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Supplier Invoice (AP). Tanpa approval — langsung Open.</summary>
public enum SupplierInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }
```

- [ ] **Step 2: Create the line entity**

`src/ErpOne.Domain/Entities/SupplierInvoiceLine.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Baris invoice supplier, diturunkan dari GRN line (received qty) × pricing PO line.</summary>
public class SupplierInvoiceLine
{
    public int Id { get; private set; }
    public int SupplierInvoiceId { get; private set; }
    public int GoodsReceiptId { get; private set; }
    public int GoodsReceiptLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private SupplierInvoiceLine() { } // EF Core

    public SupplierInvoiceLine(int goodsReceiptId, int goodsReceiptLineId, int productVariantId,
        int quantity, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (goodsReceiptId <= 0) throw new ArgumentException("GoodsReceiptId is required.", nameof(goodsReceiptId));
        if (goodsReceiptLineId <= 0) throw new ArgumentException("GoodsReceiptLineId is required.", nameof(goodsReceiptLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));

        GoodsReceiptId = goodsReceiptId;
        GoodsReceiptLineId = goodsReceiptLineId;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        TaxRateSnapshot = taxRateSnapshot;
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

- [ ] **Step 3: Create the aggregate entity**

`src/ErpOne.Domain/Entities/SupplierInvoice.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Tagihan hutang dari 1+ GRN. Baris immutable; header bisa diubah saat Open & belum dibayar.</summary>
public class SupplierInvoice : AuditableEntity
{
    private readonly List<SupplierInvoiceLine> _lines = [];

    public int Id { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public string? SupplierInvoiceNo { get; private set; }
    public int SupplierId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public string? Notes { get; private set; }
    public SupplierInvoiceStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal PaidAmount { get; private set; }

    public decimal Outstanding => GrandTotal - PaidAmount;
    public IReadOnlyCollection<SupplierInvoiceLine> Lines => _lines;

    private SupplierInvoice() { } // EF Core

    public SupplierInvoice(string invoiceNumber, int supplierId, string currency,
        DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("InvoiceNumber is required.", nameof(invoiceNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        InvoiceNumber = invoiceNumber.Trim();
        SupplierId = supplierId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        SetHeader(invoiceDate, dueDate, supplierInvoiceNo, notes);
        Status = SupplierInvoiceStatus.Open;
    }

    public void SetLines(IEnumerable<SupplierInvoiceLine> lines)
    {
        _lines.Clear();
        foreach (var l in lines) _lines.Add(l);
        RecomputeTotals();
    }

    public void UpdateHeader(DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (Status != SupplierInvoiceStatus.Open || PaidAmount != 0)
            throw new InvalidOperationException("Only an open, unpaid invoice can be edited.");
        SetHeader(invoiceDate, dueDate, supplierInvoiceNo, notes);
    }

    public void Cancel()
    {
        if (Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Invoice is already cancelled.");
        if (PaidAmount != 0)
            throw new InvalidOperationException("A paid or partially-paid invoice cannot be cancelled.");
        Status = SupplierInvoiceStatus.Cancelled;
    }

    private void SetHeader(DateTime invoiceDate, DateTime dueDate, string? supplierInvoiceNo, string? notes)
    {
        if (dueDate.Date < invoiceDate.Date)
            throw new ArgumentException("DueDate cannot be before InvoiceDate.", nameof(dueDate));
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        SupplierInvoiceNo = string.IsNullOrWhiteSpace(supplierInvoiceNo) ? null : supplierInvoiceNo.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.LineDiscount);
        TaxTotal = _lines.Sum(l => l.LineTax);
        GrandTotal = _lines.Sum(l => l.LineTotal);
    }
}
```

- [ ] **Step 4: Add the numbering document type**

In `src/ErpOne.Application/Numbering/DocumentTypes.cs`, add:
```csharp
    public const string SupplierInvoice = "SupplierInvoice";
```

- [ ] **Step 5: Register DbSets, config, table prefixes, and seed the number sequence**

In `AppDbContext.cs` add DbSets (near the transaction DbSets, after `GoodsReceiptLines`):
```csharp
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
```

Add configs inside `OnModelCreating` (after the `GoodsReceiptLine` config block):
```csharp
        modelBuilder.Entity<SupplierInvoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.Property(x => x.SupplierInvoiceNo).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Ignore(x => x.Outstanding);

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SupplierInvoice.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SupplierInvoiceLine>(e =>
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
            e.HasOne<GoodsReceipt>().WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
```

Add to `tablePrefixes` (Transaksi section):
```csharp
            [nameof(SupplierInvoice)] = "T_",
            [nameof(SupplierInvoiceLine)] = "T_",
```

Seed the number sequence — extend the existing `NumberSequence` `HasData(...)` list (in the `NumberSequence` config block) with a 7th row:
```csharp
                new { Id = 7, Code = "SupplierInvoice", Prefix = "APV", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```
> Add it as the last item inside the same `e.HasData( ... )` call for `NumberSequence` (reuse the existing `seedAt` variable).

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Generate migration**

Run:
```bash
dotnet ef migrations add AddSupplierInvoice --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web
```
Inspect: `Up()` creates `T_SupplierInvoices` + `T_SupplierInvoiceLines` (FKs, unique index on InvoiceNumber) and `InsertData` adds NumberSequence row Id=7.

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Domain/Entities/SupplierInvoice*.cs src/ErpOne.Application/Numbering/DocumentTypes.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/
git commit -m "feat: add SupplierInvoice entities, config, numbering, and migration"
```

---

## Task 2: SupplierInvoiceService + DTOs + validators + DI + tests

**Files:**
- Create: `src/ErpOne.Application/SupplierInvoices/SupplierInvoiceDtos.cs`
- Create: `src/ErpOne.Application/SupplierInvoices/ISupplierInvoiceService.cs`
- Create: `src/ErpOne.Application/SupplierInvoices/SupplierInvoiceValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/SupplierInvoiceService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/SupplierInvoiceServiceTests.cs`

**Interfaces:**
- Consumes: `SupplierInvoice`, `SupplierInvoiceLine`, `SupplierInvoiceStatus`, `DocumentTypes.SupplierInvoice`, `IDocumentNumberService`.
- Produces:
  - `record SupplierInvoiceListItemDto(int Id, string InvoiceNumber, string SupplierName, DateTime InvoiceDate, DateTime DueDate, string Currency, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, string Status)`
  - `record SupplierInvoiceLineDto(int Id, int GoodsReceiptId, string GrnNumber, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal)`
  - `record SupplierInvoiceDto(int Id, string InvoiceNumber, string? SupplierInvoiceNo, int SupplierId, string SupplierName, string Currency, DateTime InvoiceDate, DateTime DueDate, string? Notes, string Status, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierInvoiceLineDto> Lines)`
  - `record UninvoicedGrnDto(int GoodsReceiptId, string GrnNumber, DateTime ReceiptDate, string PoNumber, decimal GrandTotal, IReadOnlyList<SupplierInvoiceLineDto> Lines)`
  - `record SupplierInvoiceDashboardDto(int Total, int Open, int PartiallyPaid, int Paid, decimal TotalOutstanding)`
  - `record CreateSupplierInvoiceRequest(int SupplierId, DateTime InvoiceDate, DateTime? DueDate, string? SupplierInvoiceNo, string? Notes, IReadOnlyList<int> GrnIds)`
  - `record UpdateSupplierInvoiceHeaderRequest(DateTime InvoiceDate, DateTime DueDate, string? SupplierInvoiceNo, string? Notes)`
  - `ISupplierInvoiceService` with `GetPagedAsync`, `GetByIdAsync`, `GetDashboardAsync`, `GetUninvoicedGrnsAsync`, `CreateAsync`, `UpdateHeaderAsync`, `CancelAsync`.

- [ ] **Step 1: Write DTOs and interface**

`src/ErpOne.Application/SupplierInvoices/SupplierInvoiceDtos.cs`:
```csharp
namespace ErpOne.Application.SupplierInvoices;

public record SupplierInvoiceListItemDto(int Id, string InvoiceNumber, string SupplierName, DateTime InvoiceDate, DateTime DueDate, string Currency, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, string Status);

public record SupplierInvoiceLineDto(int Id, int GoodsReceiptId, string GrnNumber, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal);

public record SupplierInvoiceDto(int Id, string InvoiceNumber, string? SupplierInvoiceNo, int SupplierId, string SupplierName, string Currency, DateTime InvoiceDate, DateTime DueDate, string? Notes, string Status, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierInvoiceLineDto> Lines);

public record UninvoicedGrnDto(int GoodsReceiptId, string GrnNumber, DateTime ReceiptDate, string PoNumber, decimal GrandTotal, IReadOnlyList<SupplierInvoiceLineDto> Lines);

public record SupplierInvoiceDashboardDto(int Total, int Open, int PartiallyPaid, int Paid, decimal TotalOutstanding);

public record CreateSupplierInvoiceRequest(int SupplierId, DateTime InvoiceDate, DateTime? DueDate, string? SupplierInvoiceNo, string? Notes, IReadOnlyList<int> GrnIds);

public record UpdateSupplierInvoiceHeaderRequest(DateTime InvoiceDate, DateTime DueDate, string? SupplierInvoiceNo, string? Notes);
```

`src/ErpOne.Application/SupplierInvoices/ISupplierInvoiceService.cs`:
```csharp
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.SupplierInvoices;

public interface ISupplierInvoiceService
{
    Task<PagedResult<SupplierInvoiceListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, SupplierInvoiceStatus? status = null, CancellationToken ct = default);
    Task<SupplierInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UninvoicedGrnDto>> GetUninvoicedGrnsAsync(int supplierId, CancellationToken ct = default);
    Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<bool> UpdateHeaderAsync(int id, UpdateSupplierInvoiceHeaderRequest request, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
}
```

`src/ErpOne.Application/SupplierInvoices/SupplierInvoiceValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.SupplierInvoices;

public class CreateSupplierInvoiceValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{
    public CreateSupplierInvoiceValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.GrnIds).NotEmpty().WithMessage("Select at least one goods receipt.");
        RuleFor(x => x.SupplierInvoiceNo).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate)
            .GreaterThanOrEqualTo(x => x.InvoiceDate)
            .When(x => x.DueDate.HasValue)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}

public class UpdateSupplierInvoiceHeaderValidator : AbstractValidator<UpdateSupplierInvoiceHeaderRequest>
{
    public UpdateSupplierInvoiceHeaderValidator()
    {
        RuleFor(x => x.SupplierInvoiceNo).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/ErpOne.IntegrationTests/SupplierInvoiceServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SupplierInvoiceServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierInvoiceServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Creates supplier + product + confirmed PO + posted GRN; returns (supplierId, grnId, grnGrandTotal).
    private static async Task<(int supplierId, int grnId)> SeedPostedGrnAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(
            supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new PurchaseOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        // Confirm the PO (empty approval chain in tests → auto-confirmed on submit).
        await po.SubmitAsync(created.Id);

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateAsync(new CreateGoodsReceiptRequest(
            created.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, variant.Id, 10, 1000m)]));
        await grnSvc.PostAsync(grn.Id);

        return (supplier.Id, grn.Id);
    }

    [Fact]
    public async Task Create_from_one_grn_computes_totals_and_number()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, "SUP-INV-1", null, [grnId]));

        Assert.StartsWith("APV-202607-", inv.InvoiceNumber);
        Assert.Equal("Open", inv.Status);
        Assert.Equal(10000m, inv.GrandTotal);      // 10 × 1000, no tax
        Assert.Equal(10000m, inv.Outstanding);
        Assert.Single(inv.Lines);
    }

    [Fact]
    public async Task Invoiced_grn_is_excluded_then_freed_on_cancel()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        Assert.Contains(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, null, null, [grnId]));
        Assert.DoesNotContain(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);

        await svc.CancelAsync(inv.Id);
        Assert.Contains(await svc.GetUninvoicedGrnsAsync(supplierId), g => g.GoodsReceiptId == grnId);
    }

    [Fact]
    public async Task Create_with_empty_grn_list_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, _) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new CreateSupplierInvoiceRequest(supplierId, new DateTime(2026, 7, 3), null, null, null, [])));
    }

    [Fact]
    public async Task DueDate_defaults_from_supplier_payment_term()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, grnId) = await SeedPostedGrnAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var inv = await svc.CreateAsync(new CreateSupplierInvoiceRequest(
            supplierId, new DateTime(2026, 7, 3), null, null, null, [grnId]));
        Assert.Equal(new DateTime(2026, 7, 3).AddDays(30), inv.DueDate);   // PaymentTermDays = 30
    }
}
```
> Note: this test relies on `IGoodsReceiptService.CreateAsync`/`PostAsync` and `CreateGoodsReceiptRequest`/`GoodsReceiptLineRequest` signatures. Before writing, open `src/ErpOne.Application/GoodsReceipts/GoodsReceiptDtos.cs` and `IGoodsReceiptService.cs` and adjust the seed helper's request shapes to match the real signatures (parameter order/names may differ). Same for `CreatePurchaseOrderRequest`/`PurchaseOrderLineRequest`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter SupplierInvoiceServiceTests`
Expected: FAIL — `ISupplierInvoiceService` not registered.

- [ ] **Step 4: Implement the service**

`src/ErpOne.Infrastructure/Services/SupplierInvoiceService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SupplierInvoiceService(
    AppDbContext db,
    IValidator<CreateSupplierInvoiceRequest> createValidator,
    IValidator<UpdateSupplierInvoiceHeaderRequest> updateValidator,
    IDocumentNumberService docNumbers) : ISupplierInvoiceService
{
    public async Task<PagedResult<SupplierInvoiceListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SupplierInvoiceStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.SupplierInvoices.AsNoTracking();
        if (status is { } st) query = query.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.InvoiceNumber.Contains(search) || (i.SupplierInvoiceNo != null && i.SupplierInvoiceNo.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new SupplierInvoiceListItemDto(
                i.Id, i.InvoiceNumber,
                db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                i.InvoiceDate, i.DueDate, i.Currency, i.GrandTotal, i.PaidAmount, i.GrandTotal - i.PaidAmount, i.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<SupplierInvoiceListItemDto>(items, total, page, pageSize);
    }

    public async Task<SupplierInvoiceDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.SupplierInvoices.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Outstanding = g.Sum(x => x.GrandTotal - x.PaidAmount) })
            .ToListAsync(ct);

        int CountOf(SupplierInvoiceStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var outstanding = rows.Where(r => r.Status != SupplierInvoiceStatus.Cancelled).Sum(r => r.Outstanding);

        return new SupplierInvoiceDashboardDto(
            rows.Sum(r => r.Count),
            CountOf(SupplierInvoiceStatus.Open),
            CountOf(SupplierInvoiceStatus.PartiallyPaid),
            CountOf(SupplierInvoiceStatus.Paid),
            outstanding);
    }

    public async Task<SupplierInvoiceDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.SupplierInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == inv.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var lines = await BuildLineDtosAsync(inv.Lines, ct);

        return new SupplierInvoiceDto(inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNo, inv.SupplierId, supplierName,
            inv.Currency, inv.InvoiceDate, inv.DueDate, inv.Notes, inv.Status.ToString(),
            inv.Subtotal, inv.DiscountTotal, inv.TaxTotal, inv.GrandTotal, inv.PaidAmount, inv.Outstanding,
            inv.CreatedAt, inv.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<UninvoicedGrnDto>> GetUninvoicedGrnsAsync(int supplierId, CancellationToken ct = default)
    {
        // GRN ids already referenced by a non-cancelled invoice.
        var invoicedGrnIds = await (
            from l in db.SupplierInvoiceLines.AsNoTracking()
            join inv in db.SupplierInvoices.AsNoTracking() on l.SupplierInvoiceId equals inv.Id
            where inv.Status != SupplierInvoiceStatus.Cancelled
            select l.GoodsReceiptId).Distinct().ToListAsync(ct);

        // Posted GRNs whose PO belongs to this supplier, not yet invoiced.
        var grns = await (
            from g in db.GoodsReceipts.AsNoTracking()
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            where g.Status == GoodsReceiptStatus.Posted
                  && po.SupplierId == supplierId
                  && !invoicedGrnIds.Contains(g.Id)
            select new { g.Id, g.GrnNumber, g.ReceiptDate, po.PoNumber }).ToListAsync(ct);

        var result = new List<UninvoicedGrnDto>();
        foreach (var g in grns)
        {
            var grnLines = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.GoodsReceiptId == g.Id).ToListAsync(ct);
            var invLines = await BuildDerivedLinesAsync(g.Id, g.GrnNumber, grnLines, ct);
            result.Add(new UninvoicedGrnDto(g.Id, g.GrnNumber, g.ReceiptDate, g.PoNumber,
                invLines.Sum(l => l.LineTotal), invLines));
        }
        return result;
    }

    public async Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grnIds = request.GrnIds.Distinct().ToList();

        // Validate GRNs: exist, posted, belong to supplier, not already invoiced.
        var grns = await db.GoodsReceipts.AsNoTracking().Where(g => grnIds.Contains(g.Id)).ToListAsync(ct);
        if (grns.Count != grnIds.Count) throw Fail("One or more goods receipts were not found.");
        if (grns.Any(g => g.Status != GoodsReceiptStatus.Posted)) throw Fail("Only posted goods receipts can be invoiced.");

        var poIds = grns.Select(g => g.PurchaseOrderId).Distinct().ToList();
        var pos = await db.PurchaseOrders.AsNoTracking().Where(p => poIds.Contains(p.Id)).ToListAsync(ct);
        if (pos.Any(p => p.SupplierId != request.SupplierId))
            throw Fail("All goods receipts must belong to the selected supplier.");
        if (pos.Select(p => p.Currency).Distinct().Count() > 1)
            throw Fail("All goods receipts must share the same currency.");

        var alreadyInvoiced = await (
            from l in db.SupplierInvoiceLines
            join inv in db.SupplierInvoices on l.SupplierInvoiceId equals inv.Id
            where inv.Status != SupplierInvoiceStatus.Cancelled && grnIds.Contains(l.GoodsReceiptId)
            select l.GoodsReceiptId).AnyAsync(ct);
        if (alreadyInvoiced) throw Fail("One or more goods receipts have already been invoiced.");

        var supplier = await db.Suppliers.Where(s => s.Id == request.SupplierId)
            .Select(s => new { s.DefaultCurrency, s.PaymentTermDays }).FirstOrDefaultAsync(ct)
            ?? throw Fail("Supplier not found.");
        var currency = pos.FirstOrDefault()?.Currency ?? supplier.DefaultCurrency ?? "IDR";
        var dueDate = request.DueDate ?? request.InvoiceDate.AddDays(supplier.PaymentTermDays);

        // Build lines from all selected GRNs.
        var lines = new List<SupplierInvoiceLine>();
        foreach (var g in grns)
        {
            var grnLines = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.GoodsReceiptId == g.Id).ToListAsync(ct);
            var poLineIds = grnLines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
            var poLines = await db.PurchaseOrderLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);
            foreach (var gl in grnLines)
            {
                var pol = poLines[gl.PurchaseOrderLineId];
                lines.Add(new SupplierInvoiceLine(g.Id, gl.Id, gl.ProductVariantId,
                    gl.QuantityReceived, pol.UnitPrice, pol.DiscountPercent, pol.TaxRateSnapshot));
            }
        }

        var number = await docNumbers.NextAsync(DocumentTypes.SupplierInvoice, request.InvoiceDate, ct);
        var invoice = new SupplierInvoice(number, request.SupplierId, currency,
            request.InvoiceDate, dueDate, request.SupplierInvoiceNo, request.Notes);
        invoice.SetLines(lines);

        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(invoice.Id, ct))!;
    }

    public async Task<bool> UpdateHeaderAsync(int id, UpdateSupplierInvoiceHeaderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return false;
        inv.UpdateHeader(request.InvoiceDate, request.DueDate, request.SupplierInvoiceNo, request.Notes);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw Fail("Invoice not found.");
        inv.Cancel();
        await db.SaveChangesAsync(ct);
    }

    // Builds derived (not-yet-persisted) invoice line DTOs for a GRN preview.
    private async Task<IReadOnlyList<SupplierInvoiceLineDto>> BuildDerivedLinesAsync(
        int grnId, string grnNumber, List<GoodsReceiptLine> grnLines, CancellationToken ct)
    {
        var poLineIds = grnLines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
        var poLines = await db.PurchaseOrderLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var variantIds = grnLines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        var result = new List<SupplierInvoiceLineDto>();
        foreach (var gl in grnLines)
        {
            var pol = poLines[gl.PurchaseOrderLineId];
            var subtotal = Round(gl.QuantityReceived * pol.UnitPrice);
            var discount = Round(subtotal * pol.DiscountPercent / 100m);
            var tax = Round((subtotal - discount) * pol.TaxRateSnapshot / 100m);
            var totalLine = subtotal - discount + tax;
            result.Add(new SupplierInvoiceLineDto(0, grnId, grnNumber, gl.ProductVariantId,
                skus.GetValueOrDefault(gl.ProductVariantId, "—"), names.GetValueOrDefault(gl.ProductVariantId, "—"),
                gl.QuantityReceived, pol.UnitPrice, pol.DiscountPercent, pol.TaxRateSnapshot,
                subtotal, discount, tax, totalLine));
        }
        return result;
    }

    // Builds DTOs for persisted invoice lines.
    private async Task<IReadOnlyList<SupplierInvoiceLineDto>> BuildLineDtosAsync(IReadOnlyCollection<SupplierInvoiceLine> lines, CancellationToken ct)
    {
        var grnIds = lines.Select(l => l.GoodsReceiptId).Distinct().ToList();
        var grnNumbers = await db.GoodsReceipts.AsNoTracking().Where(g => grnIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.GrnNumber, ct);
        var variantIds = lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var (skus, names) = await LoadVariantNamesAsync(variantIds, ct);

        return lines.OrderBy(l => l.Id).Select(l => new SupplierInvoiceLineDto(
            l.Id, l.GoodsReceiptId, grnNumbers.GetValueOrDefault(l.GoodsReceiptId, "—"), l.ProductVariantId,
            skus.GetValueOrDefault(l.ProductVariantId, "—"), names.GetValueOrDefault(l.ProductVariantId, "—"),
            l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRateSnapshot,
            l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal)).ToList();
    }

    private async Task<(Dictionary<int, string> skus, Dictionary<int, string> names)> LoadVariantNamesAsync(List<int> variantIds, CancellationToken ct)
    {
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var skus = variants.ToDictionary(v => v.Id, v => v.Sku);
        var names = variants.ToDictionary(v => v.Id, v => products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—");
        return (skus, names);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SupplierInvoice", message)]);
}
```

- [ ] **Step 5: Register in DI**

In `src/ErpOne.Infrastructure/DependencyInjection.cs`, add using `using ErpOne.Application.SupplierInvoices;` and, after the cash-bank registration:
```csharp
        services.AddScoped<ISupplierInvoiceService, SupplierInvoiceService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter SupplierInvoiceServiceTests`
Expected: PASS (4 tests). If GRN/PO request shapes differ, fix the seed helper (Step 2 note) — not the service.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/SupplierInvoices/ src/ErpOne.Infrastructure/Services/SupplierInvoiceService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/SupplierInvoiceServiceTests.cs
git commit -m "feat: add SupplierInvoiceService (build from GRN, uninvoiced query, cancel) with tests"
```

---

## Task 3: Web pages (index/form/detail) + menu + _Imports

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceForm.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceDetail.razor`
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Modify: `src/ErpOne.Web/Components/_Imports.razor`

**Interfaces:**
- Consumes: `ISupplierInvoiceService` (Task 2), `ISupplierService.GetAllAsync`/`GetActiveAsync` (existing) for the supplier dropdown.

- [ ] **Step 1: Register resource + _Imports**

In `AppMenus.cs`, add to the `Finance` group (after `finance.cash-bank`):
```csharp
            new("finance.ap-invoices", "Supplier Invoices", "bi-receipt", CRUD),
```

In `src/ErpOne.Web/Components/_Imports.razor`, add:
```razor
@using ErpOne.Application.SupplierInvoices
```

- [ ] **Step 2: Create the index page (`.pi`)**

`src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceIndex.razor`:
```razor
@page "/finance/ap-invoices"
@attribute [Authorize(Policy = "finance.ap-invoices.index")]
@rendermode InteractiveServer
@inject ISupplierInvoiceService InvoiceService
@inject NavigationManager Nav

<PageTitle>Supplier Invoices</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Finance</span><span class="sep">·</span><span class="here">Supplier Invoices</span></nav>
            <h1>Supplier Invoices</h1>
            <p>Accounts payable — bills received from suppliers.</p>
        </div>
        <AuthorizeView Policy="finance.ap-invoices.create">
            <Authorized>
                <div class="pi-actions">
                    <a class="btn btn-primary" href="/finance/ap-invoices/new"><i class="bi bi-plus-lg"></i> New invoice</a>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_dash is not null)
    {
        <div class="cr-kpis mb-3">
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.Total</div><div class="l">Total invoices</div></div></div>
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.Open</div><div class="l">Open</div></div></div>
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.PartiallyPaid</div><div class="l">Partially paid</div></div></div>
            <div class="cr-kpi accent"><div class="tx"><div class="v">Rp @_dash.TotalOutstanding.ToString("N0")</div><div class="l">Outstanding</div></div></div>
        </div>
    }

    <div class="toolbar">
        <div class="search">
            <i class="bi bi-search"></i>
            <input placeholder="Search invoice no…" @bind="_search" @bind:event="oninput" @onkeyup="OnSearchKeyUp" />
        </div>
    </div>

    @if (_page is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_page.Total == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-receipt"></i></div><p>No supplier invoices yet.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:150px">Invoice</th>
                            <th>Supplier</th>
                            <th style="width:110px">Date</th>
                            <th style="width:110px">Due</th>
                            <th class="text-end" style="width:140px">Grand total</th>
                            <th class="text-end" style="width:140px">Outstanding</th>
                            <th style="width:120px" class="text-center">Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var i in _page.Items)
                        {
                            <tr style="cursor:pointer" @onclick="@(() => Nav.NavigateTo($"/finance/ap-invoices/{i.Id}"))">
                                <td class="code mono">@i.InvoiceNumber</td>
                                <td class="nm">@i.SupplierName</td>
                                <td class="code">@i.InvoiceDate.ToString("d MMM yyyy")</td>
                                <td class="code">@i.DueDate.ToString("d MMM yyyy")</td>
                                <td class="text-end mono">@i.GrandTotal.ToString("N2")</td>
                                <td class="text-end mono">@i.Outstanding.ToString("N2")</td>
                                <td class="text-center"><span class="badge @StatusClass(i.Status)">@i.Status</span></td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            @if (_page.TotalPages > 1)
            {
                <div class="card-foot"><Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" /></div>
            }
        </div>
    }
</div>

@code {
    private const int PageSize = 15;
    private PagedResult<SupplierInvoiceListItemDto>? _page;
    private SupplierInvoiceDashboardDto? _dash;
    private int _currentPage = 1;
    private string? _search;

    protected override async Task OnInitializedAsync()
    {
        _dash = await InvoiceService.GetDashboardAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() => _page = await InvoiceService.GetPagedAsync(_currentPage, PageSize, _search);

    private async Task OnSearchKeyUp(KeyboardEventArgs e) { _currentPage = 1; await LoadAsync(); }
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }

    private static string StatusClass(string s) => s switch
    {
        "Open" => "bg-primary-subtle text-primary-emphasis",
        "PartiallyPaid" => "bg-warning-subtle text-warning-emphasis",
        "Paid" => "bg-success-subtle text-success-emphasis",
        "Cancelled" => "bg-secondary-subtle text-secondary-emphasis",
        _ => "bg-light text-muted"
    };
}
```
> The `.cr-kpis`/`.cr-kpi` classes are component-scoped elsewhere. Add a tiny `ApInvoiceIndex.razor.css` copying the KPI tile styles from `Home.razor.css` (`.cr-kpis`, `.cr-kpi`, `.cr-kpi .tx .v`, `.cr-kpi .tx .l`, `.cr-kpi.accent`) so the KPI row renders styled. Copy those rules verbatim from `src/ErpOne.Web/Components/Pages/Home.razor.css`.

- [ ] **Step 3: Create the form page (`.cf`)**

`src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceForm.razor`:
```razor
@page "/finance/ap-invoices/new"
@page "/finance/ap-invoices/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@inject ISupplierInvoiceService InvoiceService
@inject ISupplierService SupplierService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs">
            <a href="/finance/ap-invoices">Finance</a><i class="bi bi-chevron-right"></i>
            <a href="/finance/ap-invoices">Supplier Invoices</a><i class="bi bi-chevron-right"></i>
            <span class="here">@(Id is null ? "New" : "Edit")</span>
        </div>
        <h1>@Title</h1>
    </div>

    @if (_loading)
    {
        <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
    }
    else
    {
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h">
                    <span class="hd-ic"><i class="bi bi-receipt"></i></span>
                    <div class="hd-tx"><h2>Invoice information</h2><p>Bill received against posted goods receipts.</p></div>
                </div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c6">
                            <label class="fl">Supplier <span class="req">*</span></label>
                            <select class="ctl" @bind="_supplierId" @bind:after="OnSupplierChangedAsync" disabled="@(Id is not null)">
                                <option value="0">— Select supplier —</option>
                                @foreach (var s in _suppliers)
                                {
                                    <option value="@s.Id">@s.Code — @s.Name</option>
                                }
                            </select>
                        </div>
                        <div class="f c3">
                            <label class="fl">Invoice date <span class="req">*</span></label>
                            <input type="date" class="ctl" @bind="_invoiceDate" />
                        </div>
                        <div class="f c3">
                            <label class="fl">Due date</label>
                            <input type="date" class="ctl" @bind="_dueDate" />
                        </div>
                        <div class="f c6">
                            <label class="fl">Supplier invoice no.</label>
                            <input class="ctl" maxlength="60" @bind="_supplierInvoiceNo" placeholder="Supplier's physical invoice number" />
                        </div>
                        <div class="f c6">
                            <label class="fl">Notes</label>
                            <input class="ctl" maxlength="500" @bind="_notes" placeholder="Optional" />
                        </div>
                    </div>
                </div>
            </section>

            @if (Id is null)
            {
                <section class="card">
                    <div class="card-h">
                        <span class="hd-ic"><i class="bi bi-box-seam"></i></span>
                        <div class="hd-tx"><h2>Goods receipts to bill</h2><p>Select one or more uninvoiced, posted receipts.</p></div>
                    </div>
                    <div class="card-b">
                        @if (_supplierId == 0)
                        {
                            <div class="text-muted small">Select a supplier to list uninvoiced receipts.</div>
                        }
                        else if (_grns.Count == 0)
                        {
                            <div class="text-muted small">No uninvoiced posted receipts for this supplier.</div>
                        }
                        else
                        {
                            <div class="table-responsive">
                                <table class="table align-middle">
                                    <thead class="table-light">
                                        <tr><th style="width:44px"></th><th>GRN</th><th>PO</th><th style="width:120px">Received</th><th class="text-end" style="width:160px">Amount</th></tr>
                                    </thead>
                                    <tbody>
                                        @foreach (var g in _grns)
                                        {
                                            <tr>
                                                <td><input type="checkbox" checked="@_selected.Contains(g.GoodsReceiptId)" @onchange="@(e => ToggleGrn(g.GoodsReceiptId, (bool)e.Value!))" /></td>
                                                <td class="mono">@g.GrnNumber</td>
                                                <td class="mono">@g.PoNumber</td>
                                                <td>@g.ReceiptDate.ToString("d MMM yyyy")</td>
                                                <td class="text-end mono">@g.GrandTotal.ToString("N2")</td>
                                            </tr>
                                        }
                                    </tbody>
                                    <tfoot>
                                        <tr><td colspan="4" class="text-end fw-semibold">Selected total</td><td class="text-end mono fw-semibold">@SelectedTotal().ToString("N2")</td></tr>
                                    </tfoot>
                                </table>
                            </div>
                        }
                    </div>
                </section>
            }

            <div class="pf-footer">
                <div class="in">
                    <span class="note"><span class="req">*</span> required fields</span>
                    <a class="btn btn-ghost" href="/finance/ap-invoices"><i class="bi bi-x-lg"></i> Cancel</a>
                    <button class="btn btn-primary" @onclick="SaveAsync" disabled="@(_saving || (Id is null && _selected.Count == 0))">
                        @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
                        else { <i class="bi bi-check2"></i> }
                        Save invoice
                    </button>
                </div>
            </div>
        </div>
    }
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private int _supplierId;
    private DateTime _invoiceDate = DateTime.Today;
    private DateTime? _dueDate;
    private string? _supplierInvoiceNo, _notes;
    private IReadOnlyList<SupplierDto> _suppliers = [];
    private List<UninvoicedGrnDto> _grns = [];
    private readonly HashSet<int> _selected = [];
    private bool _loading = true, _saving;
    private string? _error;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private string Title => Id is null ? "New Supplier Invoice" : "Edit Supplier Invoice";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null ? AppMenus.Perm("finance.ap-invoices", "create") : AppMenus.Perm("finance.ap-invoices", "edit");
        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded) { Nav.NavigateTo("/finance/ap-invoices"); return; }

        _suppliers = await SupplierService.GetAllAsync();

        if (Id is int id)
        {
            var inv = await InvoiceService.GetByIdAsync(id);
            if (inv is not null)
            {
                _supplierId = inv.SupplierId; _invoiceDate = inv.InvoiceDate; _dueDate = inv.DueDate;
                _supplierInvoiceNo = inv.SupplierInvoiceNo; _notes = inv.Notes;
            }
        }
        _loading = false;
    }

    private async Task OnSupplierChangedAsync()
    {
        _selected.Clear();
        _grns = _supplierId == 0 ? [] : (await InvoiceService.GetUninvoicedGrnsAsync(_supplierId)).ToList();
    }

    private void ToggleGrn(int grnId, bool on) { if (on) _selected.Add(grnId); else _selected.Remove(grnId); }

    private decimal SelectedTotal() => _grns.Where(g => _selected.Contains(g.GoodsReceiptId)).Sum(g => g.GrandTotal);

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            if (Id is int id)
                await InvoiceService.UpdateHeaderAsync(id, new UpdateSupplierInvoiceHeaderRequest(_invoiceDate, _dueDate ?? _invoiceDate, _supplierInvoiceNo, _notes));
            else
                await InvoiceService.CreateAsync(new CreateSupplierInvoiceRequest(_supplierId, _invoiceDate, _dueDate, _supplierInvoiceNo, _notes, _selected.ToList()));
            Nav.NavigateTo("/finance/ap-invoices");
        }
        catch (ValidationException ex) { _error = ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed."; }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```
> Confirm `SupplierDto` shape (`Id`, `Code`, `Name`) in `src/ErpOne.Application/Suppliers/SupplierDtos.cs`; adjust the dropdown fields if names differ.

- [ ] **Step 4: Create the detail page (`.pf`)**

`src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ApInvoiceDetail.razor`:
```razor
@page "/finance/ap-invoices/{Id:int}"
@attribute [Authorize(Policy = "finance.ap-invoices.index")]
@rendermode InteractiveServer
@inject ISupplierInvoiceService InvoiceService
@inject IAuthorizationService Auth
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>@(_inv?.InvoiceNumber ?? "Invoice")</PageTitle>

@if (_inv is null)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else
{
    <div class="pf">
        <div class="pf-top">
            <div class="crumbs">
                <a href="/finance/ap-invoices">Supplier Invoices</a><i class="bi bi-chevron-right"></i>
                <span class="here">@_inv.InvoiceNumber</span>
            </div>
            <div class="d-flex justify-content-between align-items-center">
                <h1>@_inv.InvoiceNumber <span class="badge ms-2 @StatusClass(_inv.Status)">@_inv.Status</span></h1>
                <div class="d-flex gap-2">
                    <AuthorizeView Policy="finance.ap-invoices.edit">
                        <Authorized>
                            @if (_inv.Status == "Open" && _inv.PaidAmount == 0)
                            {
                                <a class="btn btn-outline-primary btn-sm" href="@($"/finance/ap-invoices/{_inv.Id}/edit")"><i class="bi bi-pencil"></i> Edit header</a>
                            }
                        </Authorized>
                    </AuthorizeView>
                    <AuthorizeView Policy="finance.ap-invoices.delete">
                        <Authorized>
                            @if (_inv.Status != "Cancelled" && _inv.PaidAmount == 0)
                            {
                                <button class="btn btn-outline-danger btn-sm" @onclick="CancelAsync"><i class="bi bi-x-circle"></i> Cancel invoice</button>
                            }
                        </Authorized>
                    </AuthorizeView>
                </div>
            </div>
        </div>

        <div class="info-grid">
            <div><span class="k">Supplier</span><span class="v">@_inv.SupplierName</span></div>
            <div><span class="k">Supplier invoice no.</span><span class="v">@(_inv.SupplierInvoiceNo ?? "—")</span></div>
            <div><span class="k">Invoice date</span><span class="v">@_inv.InvoiceDate.ToString("d MMM yyyy")</span></div>
            <div><span class="k">Due date</span><span class="v">@_inv.DueDate.ToString("d MMM yyyy")</span></div>
            <div><span class="k">Currency</span><span class="v">@_inv.Currency</span></div>
            <div><span class="k">Outstanding</span><span class="v">@_inv.Outstanding.ToString("N2")</span></div>
        </div>

        <div class="card mt-3">
            <div class="table-responsive">
                <table class="items">
                    <thead>
                        <tr><th>GRN</th><th>SKU</th><th>Product</th><th class="text-end">Qty</th><th class="text-end">Unit price</th><th class="text-end">Disc %</th><th class="text-end">Tax %</th><th class="text-end">Line total</th></tr>
                    </thead>
                    <tbody>
                        @foreach (var l in _inv.Lines)
                        {
                            <tr>
                                <td class="mono">@l.GrnNumber</td>
                                <td class="mono">@l.Sku</td>
                                <td>@l.ProductName</td>
                                <td class="text-end">@l.Quantity</td>
                                <td class="text-end mono">@l.UnitPrice.ToString("N2")</td>
                                <td class="text-end">@l.DiscountPercent.ToString("0.##")</td>
                                <td class="text-end">@l.TaxRateSnapshot.ToString("0.##")</td>
                                <td class="text-end mono">@l.LineTotal.ToString("N2")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>

        <div class="pf-summary mt-3">
            <div><span>Subtotal</span><span class="mono">@_inv.Subtotal.ToString("N2")</span></div>
            <div><span>Discount</span><span class="mono">@_inv.DiscountTotal.ToString("N2")</span></div>
            <div><span>Tax</span><span class="mono">@_inv.TaxTotal.ToString("N2")</span></div>
            <div class="grand"><span>Grand total</span><span class="mono">@_inv.GrandTotal.ToString("N2")</span></div>
            <div><span>Paid</span><span class="mono">@_inv.PaidAmount.ToString("N2")</span></div>
            <div class="grand"><span>Outstanding</span><span class="mono">@_inv.Outstanding.ToString("N2")</span></div>
        </div>
    </div>
}

@code {
    [Parameter] public int Id { get; set; }
    private SupplierInvoiceDto? _inv;

    protected override async Task OnInitializedAsync() => _inv = await InvoiceService.GetByIdAsync(Id);

    private async Task CancelAsync()
    {
        if (!await Swal.ConfirmAsync("Cancel invoice?", "This releases its goods receipts to be invoiced again.")) return;
        try { await InvoiceService.CancelAsync(Id); await Swal.ToastAsync("success", "Invoice cancelled"); _inv = await InvoiceService.GetByIdAsync(Id); }
        catch (Exception ex) { await Swal.ToastAsync("error", ex.Message); }
    }

    private static string StatusClass(string s) => s switch
    {
        "Open" => "bg-primary-subtle text-primary-emphasis",
        "PartiallyPaid" => "bg-warning-subtle text-warning-emphasis",
        "Paid" => "bg-success-subtle text-success-emphasis",
        "Cancelled" => "bg-secondary-subtle text-secondary-emphasis",
        _ => "bg-light text-muted"
    };
}
```
> The `.pf`, `.info-grid`, `.items`, `.pf-summary` classes: verify they exist/are global by checking an existing detail page (`PoDetail.razor` + its `.razor.css`). If they are component-scoped there, copy the needed rules into an `ApInvoiceDetail.razor.css`. Match whatever `PoDetail` actually uses.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Finance/ApInvoices/ src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/_Imports.razor
git commit -m "feat: add Supplier Invoice pages (index/form/detail) and menu"
```

---

## Final verification
- [ ] `dotnet build && dotnet test` → all pass.
- [ ] Apply migration: `dotnet ef database update --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`.
- [ ] Smoke test: create a Confirmed PO → post a GRN → `/finance/ap-invoices/new` → pick supplier → select the GRN → save → invoice Open with outstanding; the GRN no longer appears for a second invoice; Cancel frees it.

## Self-review notes
- Entity mirrors `PurchaseOrder` (SetLines/RecomputeTotals); line mirrors `PurchaseOrderLine.Recompute`.
- `Outstanding` is a computed property; `e.Ignore(x => x.Outstanding)` keeps it out of the schema. List/dashboard compute outstanding in SQL as `GrandTotal - PaidAmount`.
- Double-invoice guard: uninvoiced query + create-time re-check (both exclude Cancelled invoices).
- PaidAmount stays 0 in 3a-i; PartiallyPaid/Paid transitions arrive in 3a-ii (payments).
- Test seed helper depends on real PO/GRN request signatures — reconcile before running (Step 2 note).
