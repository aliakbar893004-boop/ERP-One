# Tahap B1 — Purchase Order + Engine Approval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tambah modul Purchase Order (Draft → approval rantai-role → Confirmed) + engine approval reusable ke `MyApp`, sebagai bagian Tahap B program Transaksi.

**Architecture:** Clean Architecture mengikuti pola existing. Engine approval dibuat **document-agnostic** (`Approvals/`, hanya tahu `(DocumentType, DocumentId)`) agar dipakai ulang untuk Sales Order di Tahap C. PO mengorkestrasi engine di dalam satu transaksi DB. Tidak ada perubahan stok/HPP di B1 (itu Tahap B2).

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core 10 (SQL Server; test pakai SQLite in-memory), FluentValidation, Bootstrap 5 + Bootstrap Icons, xUnit.

## Global Constraints

- `TreatWarningsAsErrors=true` — kode harus bebas warning (Directory.Build.props).
- `Nullable=enable`, `ImplicitUsings=enable`.
- Entitas: properti `private set`, mutasi lewat constructor/`Update()`/method; invariant dilempar `ArgumentException` (validasi nilai) / `InvalidOperationException` (transisi status tak valid).
- Enum disimpan sebagai string: `e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();`.
- Decimal uang presisi `(18,2)`; persen `(5,2)`; pembulatan `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- Service melempar `FluentValidation.ValidationException` untuk error validasi/otorisasi approval.
- DTO berupa `record` di namespace Application.
- Engine approval merujuk role **by RoleName** (string), dicek via delegate `Func<string,bool> isInRole` (di web: `state.User.IsInRole`). Domain/Infra TIDAK ber-FK ke tabel Identity.
- "Pembuat tidak boleh approve": bandingkan `actingUserName` dengan `PurchaseOrder.CreatedBy` (keduanya = `ICurrentUser.UserName`).
- Validator FluentValidation auto-registered via `AddValidatorsFromAssemblyContaining<CreateProductValidator>` (scan assembly Application) — **tidak perlu** daftar manual.
- Permission baru otomatis di-grant ke role admin via `BootstrapSeeder` (`AppMenus.AllPermissions`).
- Perintah build: `dotnet build MyApp.slnx`. Test: `dotnet test`. Migration: `dotnet ef ... --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`.
- **CATATAN GIT:** folder ini belum di-inisialisasi git. **Lewati semua langkah commit.** Gate tiap task = build sukses (0 warning) + test hijau.
- **Jangan** menyentuh tabel/logika stok (`StockMovement`, `ProductStock`, `ProductVariant.CostPrice`) di B1.

---

### Task 1: Enum approval + entitas `ApprovalChainStep` (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/ApprovalDocumentType.cs`
- Create: `src/MyApp.Domain/Entities/ApprovalStepStatus.cs`
- Create: `src/MyApp.Domain/Entities/ApprovalChainStep.cs`
- Test: `tests/MyApp.UnitTests/ApprovalChainStepTests.cs`

**Interfaces:**
- Produces:
  - `enum ApprovalDocumentType { PurchaseOrder, SalesOrder }`
  - `enum ApprovalStepStatus { Pending, Approved, Rejected }`
  - `ApprovalChainStep(ApprovalDocumentType documentType, int stepOrder, string roleName)`; getter `Id, DocumentType, StepOrder, RoleName`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/ApprovalChainStepTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class ApprovalChainStepTests
{
    [Fact]
    public void Ctor_sets_fields_and_trims_role()
    {
        var s = new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, "  Manager  ");
        Assert.Equal(ApprovalDocumentType.PurchaseOrder, s.DocumentType);
        Assert.Equal(1, s.StepOrder);
        Assert.Equal("Manager", s.RoleName);
    }

    [Fact]
    public void Ctor_rejects_order_below_one() =>
        Assert.Throws<ArgumentException>(() => new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 0, "Manager"));

    [Fact]
    public void Ctor_requires_role() =>
        Assert.Throws<ArgumentException>(() => new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, "  "));
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalChainStepTests`
Expected: FAIL kompilasi — tipe belum ada.

- [ ] **Step 3: Implementasi enum + entitas**

`src/MyApp.Domain/Entities/ApprovalDocumentType.cs`:
```csharp
namespace MyApp.Domain.Entities;

/// <summary>Jenis dokumen yang melewati engine approval.</summary>
public enum ApprovalDocumentType
{
    PurchaseOrder,
    SalesOrder
}
```

`src/MyApp.Domain/Entities/ApprovalStepStatus.cs`:
```csharp
namespace MyApp.Domain.Entities;

/// <summary>Status satu langkah approval pada sebuah dokumen.</summary>
public enum ApprovalStepStatus
{
    Pending,
    Approved,
    Rejected
}
```

`src/MyApp.Domain/Entities/ApprovalChainStep.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Konfigurasi rantai approval (template) per tipe dokumen. Dikelola di Settings.</summary>
public class ApprovalChainStep : AuditableEntity
{
    public int Id { get; private set; }
    public ApprovalDocumentType DocumentType { get; private set; }
    public int StepOrder { get; private set; }
    public string RoleName { get; private set; } = default!;

    private ApprovalChainStep() { } // EF Core

    public ApprovalChainStep(ApprovalDocumentType documentType, int stepOrder, string roleName)
    {
        if (stepOrder < 1)
            throw new ArgumentException("StepOrder must be >= 1.", nameof(stepOrder));
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("RoleName is required.", nameof(roleName));

        DocumentType = documentType;
        StepOrder = stepOrder;
        RoleName = roleName.Trim();
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalChainStepTests`
Expected: PASS (3 test).

---

### Task 2: Entitas `ApprovalStep` (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/ApprovalStep.cs`
- Test: `tests/MyApp.UnitTests/ApprovalStepTests.cs`

**Interfaces:**
- Consumes: `ApprovalDocumentType`, `ApprovalStepStatus` (Task 1).
- Produces: `ApprovalStep(ApprovalDocumentType documentType, int documentId, int stepOrder, string roleName)` (status awal `Pending`); method `Approve(string actedByUserId, string? actedByName, DateTime at)`, `Reject(string actedByUserId, string? actedByName, string reason, DateTime at)`; getter `Id, DocumentType, DocumentId, StepOrder, RoleName, Status, ActedByUserId, ActedByName, ActedAt, Note`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/ApprovalStepTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class ApprovalStepTests
{
    private static ApprovalStep Make() =>
        new(ApprovalDocumentType.PurchaseOrder, 10, 1, "Manager");

    [Fact]
    public void New_step_is_pending()
    {
        var s = Make();
        Assert.Equal(ApprovalStepStatus.Pending, s.Status);
        Assert.Equal(10, s.DocumentId);
        Assert.Null(s.ActedAt);
    }

    [Fact]
    public void Approve_sets_status_and_actor()
    {
        var s = Make();
        var at = new DateTime(2026, 6, 24, 9, 0, 0, DateTimeKind.Utc);
        s.Approve("u1", "Budi", at);
        Assert.Equal(ApprovalStepStatus.Approved, s.Status);
        Assert.Equal("u1", s.ActedByUserId);
        Assert.Equal("Budi", s.ActedByName);
        Assert.Equal(at, s.ActedAt);
    }

    [Fact]
    public void Reject_sets_status_and_note()
    {
        var s = Make();
        s.Reject("u1", "Budi", "Harga terlalu tinggi", DateTime.UtcNow);
        Assert.Equal(ApprovalStepStatus.Rejected, s.Status);
        Assert.Equal("Harga terlalu tinggi", s.Note);
    }

    [Fact]
    public void Cannot_act_twice()
    {
        var s = Make();
        s.Approve("u1", "Budi", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => s.Approve("u2", "Sari", DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => s.Reject("u2", "Sari", "x", DateTime.UtcNow));
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalStepTests`
Expected: FAIL kompilasi.

- [ ] **Step 3: Implementasi entitas**

`src/MyApp.Domain/Entities/ApprovalStep.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Instans satu langkah approval pada dokumen tertentu (dibuat saat submit, snapshot rantai).</summary>
public class ApprovalStep : AuditableEntity
{
    public int Id { get; private set; }
    public ApprovalDocumentType DocumentType { get; private set; }
    public int DocumentId { get; private set; }
    public int StepOrder { get; private set; }
    public string RoleName { get; private set; } = default!;
    public ApprovalStepStatus Status { get; private set; }
    public string? ActedByUserId { get; private set; }
    public string? ActedByName { get; private set; }
    public DateTime? ActedAt { get; private set; }
    public string? Note { get; private set; }

    private ApprovalStep() { } // EF Core

    public ApprovalStep(ApprovalDocumentType documentType, int documentId, int stepOrder, string roleName)
    {
        if (documentId <= 0) throw new ArgumentException("DocumentId must be > 0.", nameof(documentId));
        if (stepOrder < 1) throw new ArgumentException("StepOrder must be >= 1.", nameof(stepOrder));
        if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("RoleName is required.", nameof(roleName));

        DocumentType = documentType;
        DocumentId = documentId;
        StepOrder = stepOrder;
        RoleName = roleName.Trim();
        Status = ApprovalStepStatus.Pending;
    }

    public void Approve(string actedByUserId, string? actedByName, DateTime at)
    {
        EnsurePending();
        Status = ApprovalStepStatus.Approved;
        ActedByUserId = actedByUserId;
        ActedByName = actedByName;
        ActedAt = at;
    }

    public void Reject(string actedByUserId, string? actedByName, string reason, DateTime at)
    {
        EnsurePending();
        Status = ApprovalStepStatus.Rejected;
        ActedByUserId = actedByUserId;
        ActedByName = actedByName;
        Note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ActedAt = at;
    }

    private void EnsurePending()
    {
        if (Status != ApprovalStepStatus.Pending)
            throw new InvalidOperationException("Approval step has already been acted on.");
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalStepTests`
Expected: PASS (4 test).

---

### Task 3: Enum status + entitas `PurchaseOrderLine` (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/PurchaseOrderStatus.cs`
- Create: `src/MyApp.Domain/Entities/PurchaseOrderLine.cs`
- Test: `tests/MyApp.UnitTests/PurchaseOrderLineTests.cs`

**Interfaces:**
- Produces:
  - `enum PurchaseOrderStatus { Draft, PendingApproval, Confirmed, Rejected, Cancelled }`
  - `PurchaseOrderLine(int productVariantId, int quantity, decimal unitPrice, decimal discountPercent, int? taxId, decimal taxRateSnapshot)`; getter `Id, PurchaseOrderId, ProductVariantId, Quantity, UnitPrice, DiscountPercent, TaxId, TaxRateSnapshot, LineSubtotal, LineDiscount, LineTax, LineTotal`. Amount dihitung otomatis.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/PurchaseOrderLineTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class PurchaseOrderLineTests
{
    [Fact]
    public void Computes_amounts_with_discount_and_tax()
    {
        // 10 x 1000 = 10000; diskon 10% = 1000; setelah diskon 9000; pajak 11% = 990; total 9990
        var l = new PurchaseOrderLine(5, 10, 1000m, 10m, taxId: 1, taxRateSnapshot: 11m);
        Assert.Equal(10000m, l.LineSubtotal);
        Assert.Equal(1000m, l.LineDiscount);
        Assert.Equal(990m, l.LineTax);
        Assert.Equal(9990m, l.LineTotal);
    }

    [Fact]
    public void No_tax_when_taxId_null_even_if_rate_passed()
    {
        var l = new PurchaseOrderLine(5, 2, 500m, 0m, taxId: null, taxRateSnapshot: 11m);
        Assert.Equal(0m, l.TaxRateSnapshot);
        Assert.Equal(0m, l.LineTax);
        Assert.Equal(1000m, l.LineTotal);
    }

    [Fact]
    public void Rejects_non_positive_quantity() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 0, 100m, 0m, null, 0m));

    [Fact]
    public void Rejects_negative_price() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 1, -1m, 0m, null, 0m));

    [Fact]
    public void Rejects_discount_out_of_range() =>
        Assert.Throws<ArgumentException>(() => new PurchaseOrderLine(5, 1, 100m, 150m, null, 0m));
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderLineTests`
Expected: FAIL kompilasi.

- [ ] **Step 3: Implementasi enum + entitas**

`src/MyApp.Domain/Entities/PurchaseOrderStatus.cs`:
```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup Purchase Order (B1; penerimaan barang diatur di B2).</summary>
public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled
}
```

`src/MyApp.Domain/Entities/PurchaseOrderLine.cs`:
```csharp
namespace MyApp.Domain.Entities;

/// <summary>Baris item pada Purchase Order. Amount dihitung di domain.</summary>
public class PurchaseOrderLine
{
    public int Id { get; private set; }
    public int PurchaseOrderId { get; private set; }
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

    private PurchaseOrderLine() { } // EF Core

    public PurchaseOrderLine(int productVariantId, int quantity, decimal unitPrice,
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

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderLineTests`
Expected: PASS (5 test).

---

### Task 4: Entitas `PurchaseOrder` (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/PurchaseOrder.cs`
- Test: `tests/MyApp.UnitTests/PurchaseOrderTests.cs`

**Interfaces:**
- Consumes: `PurchaseOrderLine`, `PurchaseOrderStatus` (Task 3).
- Produces: `PurchaseOrder(string poNumber, int supplierId, int warehouseId, DateTime orderDate, DateTime? expectedDate, string? currency, string? notes)`; methods `UpdateHeader(...)` (param sama tanpa poNumber), `SetLines(IEnumerable<PurchaseOrderLine>)`, `Submit()`, `MarkConfirmed()`, `ReturnToDraft(string reason)`, `Cancel()`; getter `Id, PoNumber, SupplierId, WarehouseId, OrderDate, ExpectedDate, Currency, Notes, Status, RejectionNote, Subtotal, DiscountTotal, TaxTotal, GrandTotal, Lines (IReadOnlyCollection<PurchaseOrderLine>)`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/PurchaseOrderTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class PurchaseOrderTests
{
    private static PurchaseOrder Make() =>
        new("PO-202606-0001", supplierId: 1, warehouseId: 2,
            orderDate: new DateTime(2026, 6, 24), expectedDate: null, currency: "idr", notes: null);

    private static PurchaseOrderLine Line() => new(5, 10, 1000m, 0m, null, 0m);

    [Fact]
    public void New_po_is_draft_and_normalizes_currency()
    {
        var po = Make();
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("IDR", po.Currency);
    }

    [Fact]
    public void SetLines_recomputes_totals()
    {
        var po = Make();
        po.SetLines([Line(), Line()]);
        Assert.Equal(20000m, po.Subtotal);
        Assert.Equal(20000m, po.GrandTotal);
        Assert.Equal(2, po.Lines.Count);
    }

    [Fact]
    public void Submit_requires_lines()
    {
        var po = Make();
        Assert.Throws<InvalidOperationException>(() => po.Submit());
        po.SetLines([Line()]);
        po.Submit();
        Assert.Equal(PurchaseOrderStatus.PendingApproval, po.Status);
    }

    [Fact]
    public void Cannot_edit_lines_unless_draft()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        Assert.Throws<InvalidOperationException>(() => po.SetLines([Line()]));
        Assert.Throws<InvalidOperationException>(() =>
            po.UpdateHeader(1, 2, DateTime.Today, null, "IDR", null));
    }

    [Fact]
    public void Confirm_only_from_pending()
    {
        var po = Make();
        Assert.Throws<InvalidOperationException>(() => po.MarkConfirmed());
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        Assert.Equal(PurchaseOrderStatus.Confirmed, po.Status);
    }

    [Fact]
    public void ReturnToDraft_stores_reason()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.ReturnToDraft("revisi harga");
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("revisi harga", po.RejectionNote);
    }

    [Fact]
    public void Cancel_allowed_from_draft_and_pending_only()
    {
        var po = Make();
        po.SetLines([Line()]);
        po.Submit();
        po.MarkConfirmed();
        Assert.Throws<InvalidOperationException>(() => po.Cancel()); // confirmed tak bisa cancel di B1

        var po2 = Make();
        po2.Cancel();
        Assert.Equal(PurchaseOrderStatus.Cancelled, po2.Status);
    }

    [Fact]
    public void ExpectedDate_must_be_on_or_after_order_date() =>
        Assert.Throws<ArgumentException>(() =>
            new PurchaseOrder("PO-1", 1, 2, new DateTime(2026, 6, 24),
                expectedDate: new DateTime(2026, 6, 1), currency: "IDR", notes: null));
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderTests`
Expected: FAIL kompilasi.

- [ ] **Step 3: Implementasi entitas**

`src/MyApp.Domain/Entities/PurchaseOrder.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pesanan pembelian ke supplier. Baris hanya bisa diubah saat Draft.</summary>
public class PurchaseOrder : AuditableEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    public int Id { get; private set; }
    public string PoNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public int WarehouseId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    private PurchaseOrder() { } // EF Core

    public PurchaseOrder(string poNumber, int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (string.IsNullOrWhiteSpace(poNumber))
            throw new ArgumentException("PoNumber is required.", nameof(poNumber));
        PoNumber = poNumber.Trim();
        SetHeader(supplierId, warehouseId, orderDate, expectedDate, currency, notes);
        Status = PurchaseOrderStatus.Draft;
    }

    public void UpdateHeader(int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        EnsureDraft();
        SetHeader(supplierId, warehouseId, orderDate, expectedDate, currency, notes);
    }

    private void SetHeader(int supplierId, int warehouseId, DateTime orderDate,
        DateTime? expectedDate, string? currency, string? notes)
    {
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (expectedDate is { } ed && ed.Date < orderDate.Date)
            throw new ArgumentException("ExpectedDate cannot be before OrderDate.", nameof(expectedDate));

        SupplierId = supplierId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IEnumerable<PurchaseOrderLine> lines)
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
            throw new InvalidOperationException("Cannot submit a purchase order without lines.");
        Status = PurchaseOrderStatus.PendingApproval;
    }

    public void MarkConfirmed()
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending purchase order can be confirmed.");
        Status = PurchaseOrderStatus.Confirmed;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending purchase order can be returned to draft.");
        Status = PurchaseOrderStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval))
            throw new InvalidOperationException("Only draft or pending purchase orders can be cancelled.");
        Status = PurchaseOrderStatus.Cancelled;
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
        if (Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft purchase order can be modified.");
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderTests`
Expected: PASS (8 test).

---

### Task 5: Application layer Approvals (DTO + interface + validator)

**Files:**
- Create: `src/MyApp.Application/Approvals/ApprovalDtos.cs`
- Create: `src/MyApp.Application/Approvals/IApprovalService.cs`
- Create: `src/MyApp.Application/Approvals/IApprovalChainService.cs`
- Create: `src/MyApp.Application/Approvals/ApprovalChainValidators.cs`
- Test: `tests/MyApp.UnitTests/ApprovalChainValidatorTests.cs`

**Interfaces:**
- Consumes: `ApprovalDocumentType` (Task 1).
- Produces:
  - `ApprovalStepDto(int Id, int StepOrder, string RoleName, string Status, string? ActedByName, DateTime? ActedAt, string? Note)`
  - `ApprovalChainStepDto(int Id, int StepOrder, string RoleName)`
  - `ApprovalChainStepInput(int StepOrder, string RoleName)`
  - `IApprovalService` (lihat signatur di bawah)
  - `IApprovalChainService` (lihat signatur di bawah)
  - `ApprovalChainStepInputValidator : AbstractValidator<ApprovalChainStepInput>`

- [ ] **Step 1: Tulis test validator yang gagal**

`tests/MyApp.UnitTests/ApprovalChainValidatorTests.cs`:
```csharp
using FluentValidation.TestHelper;
using MyApp.Application.Approvals;
using Xunit;

namespace MyApp.UnitTests;

public class ApprovalChainValidatorTests
{
    private readonly ApprovalChainStepInputValidator _v = new();

    [Fact]
    public void Valid_passes() =>
        _v.TestValidate(new ApprovalChainStepInput(1, "Manager")).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Blank_role_fails() =>
        _v.TestValidate(new ApprovalChainStepInput(1, "  ")).ShouldHaveValidationErrorFor(x => x.RoleName);
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalChainValidatorTests`
Expected: FAIL kompilasi.

- [ ] **Step 3a: Buat DTOs**

`src/MyApp.Application/Approvals/ApprovalDtos.cs`:
```csharp
namespace MyApp.Application.Approvals;

public record ApprovalStepDto(
    int Id, int StepOrder, string RoleName, string Status,
    string? ActedByName, DateTime? ActedAt, string? Note);

public record ApprovalChainStepDto(int Id, int StepOrder, string RoleName);

public record ApprovalChainStepInput(int StepOrder, string RoleName);
```

- [ ] **Step 3b: Buat interface engine approval**

`src/MyApp.Application/Approvals/IApprovalService.cs`:
```csharp
using MyApp.Domain.Entities;

namespace MyApp.Application.Approvals;

/// <summary>Engine approval document-agnostic. Tidak menyentuh status dokumen.</summary>
public interface IApprovalService
{
    /// <summary>Buat ApprovalStep dari rantai config. Return true bila rantai kosong (langsung fully approved).</summary>
    Task<bool> SubmitAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);

    /// <summary>Approve step Pending terkecil. Return true bila tidak ada lagi step Pending (fully approved).</summary>
    Task<bool> ApproveAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, CancellationToken ct = default);

    /// <summary>Reject step Pending terkecil dengan alasan.</summary>
    Task RejectAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, string reason, CancellationToken ct = default);

    /// <summary>Hapus semua ApprovalStep dokumen (dipakai saat reset/reject→draft).</summary>
    Task ResetAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalStepDto>> GetStepsAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);
}
```

- [ ] **Step 3c: Buat interface config rantai**

`src/MyApp.Application/Approvals/IApprovalChainService.cs`:
```csharp
using MyApp.Domain.Entities;

namespace MyApp.Application.Approvals;

public interface IApprovalChainService
{
    Task<IReadOnlyList<ApprovalChainStepDto>> GetByDocumentTypeAsync(
        ApprovalDocumentType docType, CancellationToken ct = default);

    /// <summary>Ganti seluruh rantai tipe dokumen secara atomik. StepOrder diberi ulang 1..n sesuai urutan list.</summary>
    Task ReplaceChainAsync(ApprovalDocumentType docType,
        IReadOnlyList<ApprovalChainStepInput> steps, CancellationToken ct = default);
}
```

- [ ] **Step 3d: Buat validator**

`src/MyApp.Application/Approvals/ApprovalChainValidators.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.Approvals;

public class ApprovalChainStepInputValidator : AbstractValidator<ApprovalChainStepInput>
{
    public ApprovalChainStepInputValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(256);
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter ApprovalChainValidatorTests`
Expected: PASS (2 test).

---

### Task 6: Application layer PurchaseOrders (DTO + interface + validator)

**Files:**
- Create: `src/MyApp.Application/PurchaseOrders/PurchaseOrderDtos.cs`
- Create: `src/MyApp.Application/PurchaseOrders/IPurchaseOrderService.cs`
- Create: `src/MyApp.Application/PurchaseOrders/PurchaseOrderValidators.cs`
- Test: `tests/MyApp.UnitTests/PurchaseOrderValidatorTests.cs`

**Interfaces:**
- Consumes: `MyApp.Application.Common.PagedResult<T>`, `ApprovalStepDto` (Task 5), `PurchaseOrderStatus` (Task 3).
- Produces: DTOs + `IPurchaseOrderService` + validator (lihat di bawah).

- [ ] **Step 1: Tulis test validator yang gagal**

`tests/MyApp.UnitTests/PurchaseOrderValidatorTests.cs`:
```csharp
using FluentValidation.TestHelper;
using MyApp.Application.PurchaseOrders;
using Xunit;

namespace MyApp.UnitTests;

public class PurchaseOrderValidatorTests
{
    private readonly CreatePurchaseOrderValidator _v = new();

    private static CreatePurchaseOrderRequest Valid() =>
        new(SupplierId: 1, WarehouseId: 2, OrderDate: new DateTime(2026, 6, 24),
            ExpectedDate: null, Notes: null,
            Lines: [new PurchaseOrderLineRequest(5, 10, 1000m, 0m, null)]);

    [Fact]
    public void Valid_passes() => _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Requires_supplier() =>
        _v.TestValidate(Valid() with { SupplierId = 0 }).ShouldHaveValidationErrorFor(x => x.SupplierId);

    [Fact]
    public void Requires_at_least_one_line() =>
        _v.TestValidate(Valid() with { Lines = [] }).ShouldHaveValidationErrorFor(x => x.Lines);

    [Fact]
    public void Line_quantity_must_be_positive() =>
        _v.TestValidate(Valid() with { Lines = [new PurchaseOrderLineRequest(5, 0, 1000m, 0m, null)] })
          .ShouldHaveValidationErrorFor("Lines[0].Quantity");

    [Fact]
    public void Expected_before_order_fails() =>
        _v.TestValidate(Valid() with { ExpectedDate = new DateTime(2026, 6, 1) })
          .ShouldHaveValidationErrorFor(x => x.ExpectedDate);
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderValidatorTests`
Expected: FAIL kompilasi.

- [ ] **Step 3a: Buat DTOs**

`src/MyApp.Application/PurchaseOrders/PurchaseOrderDtos.cs`:
```csharp
using MyApp.Application.Approvals;

namespace MyApp.Application.PurchaseOrders;

public record PurchaseOrderLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId, decimal TaxRateSnapshot,
    decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal);

public record PurchaseOrderDto(
    int Id, string PoNumber, int SupplierId, string SupplierName, int WarehouseId, string WarehouseName,
    DateTime OrderDate, DateTime? ExpectedDate, string Currency, string? Notes,
    string Status, string? RejectionNote,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal,
    DateTime CreatedAt, string? CreatedBy,
    IReadOnlyList<PurchaseOrderLineDto> Lines);

public record PurchaseOrderListItemDto(
    int Id, string PoNumber, string SupplierName, DateTime OrderDate,
    string Currency, decimal GrandTotal, string Status);

public record PurchaseOrderVariantOptionDto(int VariantId, string Sku, string ProductName, decimal CostPrice);

public record PurchaseOrderLineRequest(
    int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent, int? TaxId);

public record CreatePurchaseOrderRequest(
    int SupplierId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public record UpdatePurchaseOrderRequest(
    int SupplierId, int WarehouseId, DateTime OrderDate, DateTime? ExpectedDate, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);
```

- [ ] **Step 3b: Buat interface service**

`src/MyApp.Application/PurchaseOrders/IPurchaseOrderService.cs`:
```csharp
using MyApp.Application.Approvals;
using MyApp.Application.Common;
using MyApp.Domain.Entities;

namespace MyApp.Application.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, PurchaseOrderStatus? status = null, CancellationToken ct = default);
    Task<PurchaseOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default);

    Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task CancelAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3c: Buat validators**

`src/MyApp.Application/PurchaseOrders/PurchaseOrderValidators.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.PurchaseOrders;

public class PurchaseOrderLineRequestValidator : AbstractValidator<PurchaseOrderLineRequest>
{
    public PurchaseOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}

public class UpdatePurchaseOrderValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter PurchaseOrderValidatorTests`
Expected: PASS (5 test).

---

### Task 7: Mapping persistence (AppDbContext)

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Consumes: `PurchaseOrder`, `PurchaseOrderLine`, `ApprovalChainStep`, `ApprovalStep` (Task 1–4).
- Produces: `db.PurchaseOrders`, `db.PurchaseOrderLines`, `db.ApprovalChainSteps`, `db.ApprovalSteps`.

- [ ] **Step 1: Tambah DbSet**

Di `AppDbContext.cs`, setelah baris `public DbSet<Customer> Customers => Set<Customer>();` (baris 28), tambahkan:
```csharp
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<ApprovalChainStep> ApprovalChainSteps => Set<ApprovalChainStep>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
```

- [ ] **Step 2: Tambah konfigurasi fluent**

Di `OnModelCreating`, tepat sebelum blok `modelBuilder.Entity<ProductAttribute>(e =>` (baris ~240), sisipkan:
```csharp
        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.PoNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(PurchaseOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PurchaseOrderLine>(e =>
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

        modelBuilder.Entity<ApprovalChainStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(x => x.RoleName).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.DocumentType, x.StepOrder }).IsUnique();
        });

        modelBuilder.Entity<ApprovalStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(x => x.RoleName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ActedByUserId).HasMaxLength(450);
            e.Property(x => x.ActedByName).HasMaxLength(256);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.DocumentType, x.DocumentId, x.StepOrder });
        });
```

- [ ] **Step 3: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

---

### Task 8: `ApprovalChainService` + DI + integration test

**Files:**
- Create: `src/MyApp.Infrastructure/Services/ApprovalChainService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/ApprovalChainServiceTests.cs`

**Interfaces:**
- Consumes: `IApprovalChainService`, `ApprovalChainStepInput`, `ApprovalChainStepDto` (Task 5); `db.ApprovalChainSteps` (Task 7).
- Produces: `ApprovalChainService : IApprovalChainService`.

- [ ] **Step 1: Tulis integration test yang gagal**

`tests/MyApp.IntegrationTests/ApprovalChainServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Approvals;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.IntegrationTests;

public class ApprovalChainServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ApprovalChainServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Replace_then_get_returns_ordered_chain()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApprovalChainService>();

        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(1, "Supervisor"), new ApprovalChainStepInput(2, "Manager")]);

        var chain = await svc.GetByDocumentTypeAsync(ApprovalDocumentType.SalesOrder);
        Assert.Equal(2, chain.Count);
        Assert.Equal("Supervisor", chain[0].RoleName);
        Assert.Equal(1, chain[0].StepOrder);
        Assert.Equal("Manager", chain[1].RoleName);
        Assert.Equal(2, chain[1].StepOrder);
    }

    [Fact]
    public async Task Replace_overwrites_previous_and_renumbers()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApprovalChainService>();

        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(9, "A"), new ApprovalChainStepInput(3, "B")]);
        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(1, "OnlyOne")]);

        var chain = await svc.GetByDocumentTypeAsync(ApprovalDocumentType.SalesOrder);
        Assert.Single(chain);
        Assert.Equal("OnlyOne", chain[0].RoleName);
        Assert.Equal(1, chain[0].StepOrder);
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests --filter ApprovalChainServiceTests`
Expected: FAIL — `IApprovalChainService` belum terdaftar.

- [ ] **Step 3a: Implementasi service**

`src/MyApp.Infrastructure/Services/ApprovalChainService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Approvals;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class ApprovalChainService(AppDbContext db) : IApprovalChainService
{
    public async Task<IReadOnlyList<ApprovalChainStepDto>> GetByDocumentTypeAsync(
        ApprovalDocumentType docType, CancellationToken ct = default) =>
        await db.ApprovalChainSteps.AsNoTracking()
            .Where(x => x.DocumentType == docType)
            .OrderBy(x => x.StepOrder)
            .Select(x => new ApprovalChainStepDto(x.Id, x.StepOrder, x.RoleName))
            .ToListAsync(ct);

    public async Task ReplaceChainAsync(ApprovalDocumentType docType,
        IReadOnlyList<ApprovalChainStepInput> steps, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.ApprovalChainSteps.Where(x => x.DocumentType == docType).ToListAsync(ct);
        db.ApprovalChainSteps.RemoveRange(existing);
        await db.SaveChangesAsync(ct);

        var order = 1;
        foreach (var s in steps)
            db.ApprovalChainSteps.Add(new ApprovalChainStep(docType, order++, s.RoleName));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
```

- [ ] **Step 3b: Daftarkan di DI**

Di `src/MyApp.Infrastructure/DependencyInjection.cs`: tambahkan using `using MyApp.Application.Approvals;` (di blok using), dan setelah baris `services.AddScoped<ILogService, LogService>();` tambahkan:
```csharp
        services.AddScoped<IApprovalChainService, ApprovalChainService>();
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests --filter ApprovalChainServiceTests`
Expected: PASS (2 test).

---

### Task 9: `ApprovalService` (engine) + DI + integration test

**Files:**
- Create: `src/MyApp.Infrastructure/Services/ApprovalService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/ApprovalServiceTests.cs`

**Interfaces:**
- Consumes: `IApprovalService` (Task 5); `db.ApprovalChainSteps`, `db.ApprovalSteps` (Task 7).
- Produces: `ApprovalService : IApprovalService`.

- [ ] **Step 1: Tulis integration test yang gagal**

`tests/MyApp.IntegrationTests/ApprovalServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using MyApp.Application.Approvals;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.IntegrationTests;

public class ApprovalServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ApprovalServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const ApprovalDocumentType Po = ApprovalDocumentType.PurchaseOrder;
    private static readonly Func<string, bool> InAnyRole = _ => true;

    private async Task SeedChainAsync(IServiceProvider sp, params string[] roles)
    {
        var chain = sp.GetRequiredService<IApprovalChainService>();
        var inputs = roles.Select((r, i) => new ApprovalChainStepInput(i + 1, r)).ToList();
        await chain.ReplaceChainAsync(Po, inputs);
    }

    [Fact]
    public async Task Empty_chain_submits_as_fully_approved()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApprovalService>();
        await svc.ResetAsync(Po, 1001);

        var fully = await svc.SubmitAsync(Po, 1001);
        Assert.True(fully);
        Assert.Empty(await svc.GetStepsAsync(Po, 1001));
    }

    [Fact]
    public async Task Two_level_chain_requires_both_approvals()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Supervisor", "Manager");
        await svc.ResetAsync(Po, 1002);

        var fullyOnSubmit = await svc.SubmitAsync(Po, 1002);
        Assert.False(fullyOnSubmit);

        var afterFirst = await svc.ApproveAsync(Po, 1002, "sari", InAnyRole, creatorUserName: "budi");
        Assert.False(afterFirst);

        var afterSecond = await svc.ApproveAsync(Po, 1002, "andi", InAnyRole, creatorUserName: "budi");
        Assert.True(afterSecond);
    }

    [Fact]
    public async Task Creator_cannot_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1003);
        await svc.SubmitAsync(Po, 1003);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ApproveAsync(Po, 1003, "budi", InAnyRole, creatorUserName: "budi"));
    }

    [Fact]
    public async Task User_without_role_cannot_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1004);
        await svc.SubmitAsync(Po, 1004);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ApproveAsync(Po, 1004, "sari", role => role == "Director", creatorUserName: "budi"));
    }

    [Fact]
    public async Task Reject_marks_current_step_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1005);
        await svc.SubmitAsync(Po, 1005);

        await svc.RejectAsync(Po, 1005, "sari", InAnyRole, creatorUserName: "budi", reason: "mahal");
        var steps = await svc.GetStepsAsync(Po, 1005);
        Assert.Equal("Rejected", steps[0].Status);
        Assert.Equal("mahal", steps[0].Note);
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests --filter ApprovalServiceTests`
Expected: FAIL — `IApprovalService` belum terdaftar.

- [ ] **Step 3a: Implementasi engine**

`src/MyApp.Infrastructure/Services/ApprovalService.cs`:
```csharp
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Approvals;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class ApprovalService(AppDbContext db) : IApprovalService
{
    public async Task<bool> SubmitAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default)
    {
        var chain = await db.ApprovalChainSteps.AsNoTracking()
            .Where(c => c.DocumentType == docType)
            .OrderBy(c => c.StepOrder)
            .ToListAsync(ct);

        foreach (var c in chain)
            db.ApprovalSteps.Add(new ApprovalStep(docType, docId, c.StepOrder, c.RoleName));

        await db.SaveChangesAsync(ct);
        return chain.Count == 0; // rantai kosong → langsung fully approved
    }

    public async Task<bool> ApproveAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, CancellationToken ct = default)
    {
        var step = await CurrentPendingAsync(docType, docId, ct)
            ?? throw Fail("There is no pending approval step for this document.");
        EnsureCanAct(step, actingUserName, isInRole, creatorUserName);

        step.Approve(actingUserName, actingUserName, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);

        var hasPending = await db.ApprovalSteps.AnyAsync(
            s => s.DocumentType == docType && s.DocumentId == docId && s.Status == ApprovalStepStatus.Pending, ct);
        return !hasPending;
    }

    public async Task RejectAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, string reason, CancellationToken ct = default)
    {
        var step = await CurrentPendingAsync(docType, docId, ct)
            ?? throw Fail("There is no pending approval step for this document.");
        EnsureCanAct(step, actingUserName, isInRole, creatorUserName);

        step.Reject(actingUserName, actingUserName, reason, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default)
    {
        var steps = await db.ApprovalSteps
            .Where(s => s.DocumentType == docType && s.DocumentId == docId).ToListAsync(ct);
        if (steps.Count == 0) return;
        db.ApprovalSteps.RemoveRange(steps);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApprovalStepDto>> GetStepsAsync(
        ApprovalDocumentType docType, int docId, CancellationToken ct = default) =>
        await db.ApprovalSteps.AsNoTracking()
            .Where(s => s.DocumentType == docType && s.DocumentId == docId)
            .OrderBy(s => s.StepOrder)
            .Select(s => new ApprovalStepDto(
                s.Id, s.StepOrder, s.RoleName, s.Status.ToString(), s.ActedByName, s.ActedAt, s.Note))
            .ToListAsync(ct);

    private Task<ApprovalStep?> CurrentPendingAsync(ApprovalDocumentType docType, int docId, CancellationToken ct) =>
        db.ApprovalSteps
            .Where(s => s.DocumentType == docType && s.DocumentId == docId && s.Status == ApprovalStepStatus.Pending)
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync(ct);

    private static void EnsureCanAct(ApprovalStep step, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName)
    {
        if (!string.IsNullOrEmpty(creatorUserName) &&
            string.Equals(creatorUserName, actingUserName, StringComparison.OrdinalIgnoreCase))
            throw Fail("You cannot approve or reject a document you created.");
        if (!isInRole(step.RoleName))
            throw Fail($"You do not hold the required role '{step.RoleName}' for this step.");
    }

    private static ValidationException Fail(string message) =>
        new([new ValidationFailure("Approval", message)]);
}
```

- [ ] **Step 3b: Daftarkan di DI**

Di `src/MyApp.Infrastructure/DependencyInjection.cs`, setelah baris `services.AddScoped<IApprovalChainService, ApprovalChainService>();` tambahkan:
```csharp
        services.AddScoped<IApprovalService, ApprovalService>();
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests --filter ApprovalServiceTests`
Expected: PASS (5 test).

---

### Task 10: `PurchaseOrderService` + DI + integration test

**Files:**
- Create: `src/MyApp.Infrastructure/Services/PurchaseOrderService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/PurchaseOrderServiceTests.cs`

**Interfaces:**
- Consumes: `IPurchaseOrderService`, DTO PO (Task 6); `IApprovalService` (Task 5/9); `db.PurchaseOrders`, `db.Suppliers`, `db.Warehouses`, `db.ProductVariants`, `db.Products`, `db.Taxes`.
- Produces: `PurchaseOrderService : IPurchaseOrderService`.

**Catatan helper test:** test ini menyemai Supplier, Warehouse, Product+Variant langsung lewat `AppDbContext` agar FK valid, lalu memanggil service. Engine approval di-set rantai kosong (default) agar submit→Confirmed bisa diuji tanpa role; skenario approve/reject memakai rantai berisi.

- [ ] **Step 1: Tulis integration test yang gagal**

`tests/MyApp.IntegrationTests/PurchaseOrderServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Approvals;
using MyApp.Application.PurchaseOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class PurchaseOrderServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PurchaseOrderServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Menyemai master minimal; mengembalikan (supplierId, warehouseId, variantId).
    private static async Task<(int sup, int wh, int variant)> SeedMastersAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var sup = new Supplier("SUP-PO", "PT PO", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse("WH-PO", "Gudang PO", null, true);
        var product = new Product("PRD-PO", "Produk PO", null, null, null, null, null);
        db.Suppliers.Add(sup);
        db.Warehouses.Add(wh);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant(product.Id, "SKU-PO", null, 1000m, null, 800m, null, null, true);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return (sup.Id, wh.Id, variant.Id);
    }

    private static CreatePurchaseOrderRequest New(int sup, int wh, int variant) =>
        new(sup, wh, new DateTime(2026, 6, 24), null, "test",
            [new PurchaseOrderLineRequest(variant, 10, 1000m, 0m, null)]);

    [Fact]
    public async Task Create_generates_number_and_totals()
    {
        using var scope = _factory.Services.CreateScope();
        var (sup, wh, variant) = await SeedMastersAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        Assert.StartsWith("PO-202606-", po.PoNumber);
        Assert.Equal(10000m, po.GrandTotal);
        Assert.Equal("Draft", po.Status);
        Assert.Single(po.Lines);
    }

    [Fact]
    public async Task Submit_with_empty_chain_confirms_immediately()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>().ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, []);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);

        var fetched = await svc.GetByIdAsync(po.Id);
        Assert.Equal("Confirmed", fetched!.Status);
    }

    [Fact]
    public async Task Submit_approve_chain_confirms()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);
        Assert.Equal("PendingApproval", (await svc.GetByIdAsync(po.Id))!.Status);

        // CreatedBy null pada konteks test (NullCurrentUser) → acting "approver" bukan creator
        await svc.ApproveAsync(po.Id, "approver", _ => true);
        Assert.Equal("Confirmed", (await svc.GetByIdAsync(po.Id))!.Status);
    }

    [Fact]
    public async Task Reject_returns_to_draft_with_note()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        await sp.GetRequiredService<IApprovalChainService>()
            .ReplaceChainAsync(ApprovalDocumentType.PurchaseOrder, [new ApprovalChainStepInput(1, "Manager")]);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var po = await svc.CreateAsync(New(sup, wh, variant));
        await svc.SubmitAsync(po.Id);
        await svc.RejectAsync(po.Id, "approver", _ => true, "harga ketinggian");

        var fetched = await svc.GetByIdAsync(po.Id);
        Assert.Equal("Draft", fetched!.Status);
        Assert.Equal("harga ketinggian", fetched.RejectionNote);
        Assert.Empty(await svc.GetApprovalStepsAsync(po.Id));
    }

    [Fact]
    public async Task Po_numbers_are_unique_within_month()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (sup, wh, variant) = await SeedMastersAsync(sp);
        var svc = sp.GetRequiredService<IPurchaseOrderService>();

        var a = await svc.CreateAsync(New(sup, wh, variant));
        var b = await svc.CreateAsync(New(sup, wh, variant));
        Assert.NotEqual(a.PoNumber, b.PoNumber);
    }
}
```

> **Catatan untuk implementer:** verifikasi signatur ctor `Supplier`, `Warehouse`, `Product`, `ProductVariant` di Domain sebelum menjalankan — sesuaikan argumen di `SeedMastersAsync` bila berbeda. Jalankan `dotnet build` lebih dulu untuk menangkap ketidakcocokan.

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests --filter PurchaseOrderServiceTests`
Expected: FAIL — `IPurchaseOrderService` belum terdaftar.

- [ ] **Step 3a: Implementasi service**

`src/MyApp.Infrastructure/Services/PurchaseOrderService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Approvals;
using MyApp.Application.Common;
using MyApp.Application.PurchaseOrders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class PurchaseOrderService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreatePurchaseOrderRequest> createValidator,
    IValidator<UpdatePurchaseOrderRequest> updateValidator) : IPurchaseOrderService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.PurchaseOrder;

    public async Task<PagedResult<PurchaseOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, PurchaseOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.PurchaseOrders.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.PoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PurchaseOrderListItemDto(
                p.Id, p.PoNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Currency, p.GrandTotal, p.Status.ToString()))
            .ToListAsync(ct);

        return new PagedResult<PurchaseOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<PurchaseOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = po.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = po.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            return new PurchaseOrderLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, l.TaxRateSnapshot,
                l.LineSubtotal, l.LineDiscount, l.LineTax, l.LineTotal);
        }).ToList();

        return new PurchaseOrderDto(po.Id, po.PoNumber, po.SupplierId, supplierName, po.WarehouseId, warehouseName,
            po.OrderDate, po.ExpectedDate, po.Currency, po.Notes, po.Status.ToString(), po.RejectionNote,
            po.Subtotal, po.DiscountTotal, po.TaxTotal, po.GrandTotal, po.CreatedAt, po.CreatedBy, lines);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<PurchaseOrderVariantOptionDto>> SearchVariantsAsync(string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, ProductName = p.Name, v.CostPrice };
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Sku.Contains(term) || x.ProductName.Contains(term));

        return await q.OrderBy(x => x.ProductName).Take(50)
            .Select(x => new PurchaseOrderVariantOptionDto(x.Id, x.Sku, x.ProductName, x.CostPrice))
            .ToListAsync(ct);
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await db.Suppliers.Where(s => s.Id == request.SupplierId)
            .Select(s => s.DefaultCurrency).FirstOrDefaultAsync(ct) ?? "IDR";
        var poNumber = await GenerateNumberAsync(request.OrderDate, ct);

        var po = new PurchaseOrder(poNumber, request.SupplierId, request.WarehouseId,
            request.OrderDate, request.ExpectedDate, currency, request.Notes);
        po.SetLines(await BuildLinesAsync(request.Lines, ct));

        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(po.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;

        var oldLines = await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(ct);
        db.PurchaseOrderLines.RemoveRange(oldLines);

        po.UpdateHeader(request.SupplierId, request.WarehouseId, request.OrderDate, request.ExpectedDate, po.Currency, request.Notes);
        po.SetLines(await BuildLinesAsync(request.Lines, ct));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po is null) return false;
        if (po.Status != PurchaseOrderStatus.Draft)
            throw Fail("Only a draft purchase order can be deleted.");
        db.PurchaseOrders.Remove(po);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        po.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, po.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, po.Id, ct);
        if (fullyApproved) po.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        var fullyApproved = await approval.ApproveAsync(DocType, po.Id, actingUserName, isInRole, po.CreatedBy, ct);
        if (fullyApproved) po.MarkConfirmed();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        await approval.RejectAsync(DocType, po.Id, actingUserName, isInRole, po.CreatedBy, reason, ct);
        po.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, po.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Purchase order not found.");

        po.Cancel();
        await approval.ResetAsync(DocType, po.Id, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<string> GenerateNumberAsync(DateTime orderDate, CancellationToken ct)
    {
        var prefix = $"PO-{orderDate:yyyyMM}-";
        var last = await db.PurchaseOrders.AsNoTracking()
            .Where(p => p.PoNumber.StartsWith(prefix))
            .OrderByDescending(p => p.PoNumber)
            .Select(p => p.PoNumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private async Task<List<PurchaseOrderLine>> BuildLinesAsync(
        IReadOnlyList<PurchaseOrderLineRequest> requests, CancellationToken ct)
    {
        var taxIds = requests.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        var rates = taxIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.Taxes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Rate, ct);

        var lines = new List<PurchaseOrderLine>();
        foreach (var l in requests)
        {
            var rate = l.TaxId.HasValue && rates.TryGetValue(l.TaxId.Value, out var r) ? r : 0m;
            lines.Add(new PurchaseOrderLine(l.ProductVariantId, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, rate));
        }
        return lines;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("PurchaseOrder", message)]);
}
```

- [ ] **Step 3b: Daftarkan di DI**

Di `src/MyApp.Infrastructure/DependencyInjection.cs`: tambahkan `using MyApp.Application.PurchaseOrders;`, dan setelah baris `services.AddScoped<IApprovalService, ApprovalService>();` tambahkan:
```csharp
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests --filter PurchaseOrderServiceTests`
Expected: PASS (5 test).

- [ ] **Step 5: Jalankan seluruh test**

Run: `dotnet test`
Expected: semua test PASS.

---

### Task 11: EF migration (tabel PO, PO lines, approval)

**Files:**
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/<timestamp>_AddPurchaseOrderAndApproval.cs` (digenerate)
- Modify: `src/MyApp.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (digenerate)

**Interfaces:**
- Consumes: mapping dari Task 7.

- [ ] **Step 1: Generate migration**

Run:
```bash
dotnet ef migrations add AddPurchaseOrderAndApproval --project src/MyApp.Infrastructure --startup-project src/MyApp.Web --output-dir Persistence/Migrations
```
Expected: file migration baru + snapshot terupdate, build succeeded.

- [ ] **Step 2: Verifikasi isi migration**

Buka file `<timestamp>_AddPurchaseOrderAndApproval.cs`. Pastikan `Up()` membuat tabel `PurchaseOrders`, `PurchaseOrderLines`, `ApprovalChainSteps`, `ApprovalSteps` dengan:
- index unik `PoNumber`; FK `SupplierId`/`WarehouseId` `OnDelete: Restrict`.
- `PurchaseOrderLines` cascade dari PO; FK `ProductVariantId` Restrict, `TaxId` SetNull.
- kolom decimal `(18,2)` untuk total & harga; `(5,2)` untuk persen.
- enum sebagai `nvarchar`; index unik `(DocumentType, StepOrder)` pada chain config; index `(DocumentType, DocumentId, StepOrder)` pada instance.
- **Tidak ada** perubahan ke tabel stok/produk/varian.

- [ ] **Step 3: Terapkan ke database (bila DB lokal tersedia)**

Run:
```bash
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```
Expected: "Done." tanpa error. (Bila DB lokal tak tersedia, lewati; test pakai SQLite `EnsureCreated`.)

- [ ] **Step 4: Jalankan seluruh test**

Run: `dotnet test`
Expected: semua test PASS.

---

### Task 12: Permission (AppMenus) + seed rantai default (BootstrapSeeder)

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Infrastructure/BootstrapSeeder.cs`

**Interfaces:**
- Produces: permission `transactions.purchase-orders.{index,create,edit,delete,approve}`, `settings.approval-chains.{index,create,edit,delete}`; rantai approval default tersemai untuk `PurchaseOrder`.

- [ ] **Step 1: Tambah action `approve` + array PO di AppMenus**

Di `src/MyApp.Web/Authorization/AppMenus.cs`, setelah baris `public static readonly AppAction ActDelete = ...` (baris 14), tambahkan:
```csharp
    public static readonly AppAction ActApprove = new("approve", "Approve", "bi-check2-circle");
```
Lalu setelah baris `private static AppAction[] ViewCreate => [ActIndex, ActCreate];` (baris 21), tambahkan:
```csharp
    private static AppAction[] PurchaseOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove];
```

> Catatan: `AllActions` (baris 16-17, dipakai untuk hal lain) sengaja tidak diubah; `approve` hanya relevan untuk PO.

- [ ] **Step 2: Naikkan PO ke CRUD+approve**

Di grup `"Transaksi"`, ganti baris (baris 50):
```csharp
            new("transactions.purchase-orders", "Purchase Order", "bi-cart-plus-fill",     ViewOnly),
```
menjadi:
```csharp
            new("transactions.purchase-orders", "Purchase Order", "bi-cart-plus-fill",     PurchaseOrderActions),
```

- [ ] **Step 3: Tambah resource Approval Chains di grup Settings**

Di grup `"Settings"`, setelah baris `new("settings.roles", ...)` (baris 56), tambahkan:
```csharp
            new("settings.approval-chains", "Approval Chain", "bi-diagram-3-fill", CRUD),
```

- [ ] **Step 4: Seed rantai default di BootstrapSeeder**

Di `src/MyApp.Web/Infrastructure/BootstrapSeeder.cs`:
1. Tambah using di atas:
```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;
```
2. Sebelum baris `// Buat user admin jika belum ada` (baris ~44), sisipkan:
```csharp
        // Seed rantai approval default untuk Purchase Order (idempotent).
        // Default memakai role admin agar role pasti ada; admin sebaiknya mengkonfigurasi
        // rantai sebenarnya (role approver non-admin) di Settings → Approval Chain.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PurchaseOrder))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 5: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning. (Permission baru otomatis di-grant ke role admin saat startup via `BootstrapSeeder`. Tiap permission menjadi policy otomatis lewat permission policy provider yang ada — sama seperti halaman Tahap A.)

---

### Task 13: Web — halaman PO Index (ganti placeholder)

**Files:**
- Delete: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrderPlaceholder.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoIndex.razor`

**Interfaces:**
- Consumes: `IPurchaseOrderService` (Task 6/10); `SwalService`, `Pager` (existing); permission `transactions.purchase-orders.*` (Task 12).

- [ ] **Step 1: Hapus placeholder**

Hapus file `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrderPlaceholder.razor` (route `/transactions/purchase-orders` akan dipakai PoIndex).

- [ ] **Step 2: Buat halaman Index**

`src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoIndex.razor`:
```razor
@page "/transactions/purchase-orders"
@attribute [Authorize(Policy = "transactions.purchase-orders.index")]
@rendermode InteractiveServer
@using MyApp.Application.Common
@using MyApp.Application.PurchaseOrders
@using MyApp.Domain.Entities
@inject IPurchaseOrderService PoService
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>Purchase Orders</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0 fw-semibold">Purchase Orders</h1>
    <AuthorizeView Policy="transactions.purchase-orders.create">
        <Authorized>
            <a class="btn btn-primary btn-sm" href="/transactions/purchase-orders/new">
                <i class="bi bi-plus-lg me-1"></i>Buat PO
            </a>
        </Authorized>
    </AuthorizeView>
</div>

<div class="search-card mb-4 d-flex gap-2 flex-wrap">
    <input class="form-control" style="max-width:320px" placeholder="Cari nomor PO..."
           @bind="_search" @bind:event="oninput" @onkeyup="ReloadAsync" />
    <select class="form-select" style="max-width:200px" @bind="_status" @bind:after="ReloadAsync">
        <option value="">Semua status</option>
        @foreach (var s in Enum.GetValues<PurchaseOrderStatus>())
        {
            <option value="@s">@s</option>
        }
    </select>
</div>

@if (_page is null)
{
    <div class="text-center py-5 text-muted">
        <div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...
    </div>
}
else if (_page.Total == 0)
{
    <div class="empty-state">
        <div class="empty-icon">&#128203;</div>
        <p class="empty-text">Belum ada Purchase Order. <a href="/transactions/purchase-orders/new">Buat yang pertama.</a></p>
    </div>
}
else
{
    <div class="data-card">
        <div class="data-card-header">
            <span class="text-muted small">
                Menampilkan @((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total) dari @_page.Total
            </span>
        </div>
        <div class="table-responsive">
            <table class="table table-hover align-middle mb-0">
                <thead class="table-head">
                    <tr>
                        <th class="ps-3">No. PO</th>
                        <th>Supplier</th>
                        <th>Tanggal</th>
                        <th class="text-end">Total</th>
                        <th style="width:140px">Status</th>
                        <th class="text-end pe-3" style="width:80px"></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in _page.Items)
                    {
                        <tr style="cursor:pointer" @onclick="() => Open(item.Id)">
                            <td class="ps-3"><span class="badge bg-light text-dark border">@item.PoNumber</span></td>
                            <td class="fw-medium">@item.SupplierName</td>
                            <td class="text-muted small">@item.OrderDate.ToString("dd MMM yyyy")</td>
                            <td class="text-end">@item.Currency @item.GrandTotal.ToString("N2")</td>
                            <td><span class="badge @StatusClass(item.Status)">@item.Status</span></td>
                            <td class="text-end pe-3"><i class="bi bi-chevron-right text-muted"></i></td>
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
    private PagedResult<PurchaseOrderListItemDto>? _page;
    private int _currentPage = 1;
    private string? _search;
    private string _status = "";

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        PurchaseOrderStatus? status = Enum.TryParse<PurchaseOrderStatus>(_status, out var st) ? st : null;
        _page = await PoService.GetPagedAsync(_currentPage, PageSize, _search, status);
    }

    private async Task ReloadAsync()
    {
        _currentPage = 1;
        await LoadAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _currentPage = page;
        await LoadAsync();
    }

    private void Open(int id) => Nav.NavigateTo($"/transactions/purchase-orders/{id}");

    private static string StatusClass(string status) => status switch
    {
        "Draft" => "bg-secondary",
        "PendingApproval" => "bg-warning text-dark",
        "Confirmed" => "bg-success",
        "Rejected" => "bg-danger",
        "Cancelled" => "bg-dark",
        _ => "bg-light text-dark"
    };
}
```

- [ ] **Step 3: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning. (Entri NavMenu Purchase Order sudah ada sejak Tahap A — tetap menunjuk `/transactions/purchase-orders`.)

---

### Task 14: Web — halaman PO Form (header + editor baris)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoForm.razor`

**Interfaces:**
- Consumes: `IPurchaseOrderService`, `ISupplierService`, `IWarehouseService`, `ITaxService` (existing); DTO PO (Task 6); permission `transactions.purchase-orders.{create,edit}`.

- [ ] **Step 1: Buat halaman Form**

`src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoForm.razor`:
```razor
@page "/transactions/purchase-orders/new"
@page "/transactions/purchase-orders/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@using MyApp.Application.PurchaseOrders
@using MyApp.Application.Suppliers
@using MyApp.Application.Warehouses
@using MyApp.Application.Taxes
@using MyApp.Web.Authorization
@inject IPurchaseOrderService PoService
@inject ISupplierService SupplierService
@inject IWarehouseService WarehouseService
@inject ITaxService TaxService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/transactions/purchase-orders"><i class="bi bi-arrow-left me-1"></i>Purchase Orders</a>
    <h4 class="uf-title">@Title</h4>
</div>

@if (_loading)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else if (_notFound)
{
    <div class="alert alert-warning">Purchase order tidak ditemukan atau bukan Draft.</div>
}
else
{
    @if (_error is not null)
    {
        <div class="alert alert-danger d-flex align-items-center gap-2 mb-3 py-2"><span>&#9888;</span> @_error</div>
    }

    <div class="fs-card mb-4">
        <div class="fs-card-title">Informasi PO</div>
        <div class="row g-3">
            <div class="col-12 col-md-4">
                <label class="form-label lbl-required">Supplier</label>
                <select class="form-select" @bind="_supplierId">
                    <option value="0">— pilih supplier —</option>
                    @foreach (var s in _suppliers)
                    {
                        <option value="@s.Id">@s.Code — @s.Name</option>
                    }
                </select>
            </div>
            <div class="col-12 col-md-4">
                <label class="form-label lbl-required">Gudang Tujuan</label>
                <select class="form-select" @bind="_warehouseId">
                    <option value="0">— pilih gudang —</option>
                    @foreach (var w in _warehouses)
                    {
                        <option value="@w.Id">@w.Code — @w.Name</option>
                    }
                </select>
            </div>
            <div class="col-6 col-md-2">
                <label class="form-label lbl-required">Tanggal</label>
                <input type="date" class="form-control" @bind="_orderDate" />
            </div>
            <div class="col-6 col-md-2">
                <label class="form-label">Perkiraan Tiba</label>
                <input type="date" class="form-control" @bind="_expectedDate" />
            </div>
            <div class="col-12">
                <label class="form-label">Catatan</label>
                <textarea class="form-control" rows="2" maxlength="500" @bind="_notes"></textarea>
            </div>
        </div>
    </div>

    <div class="fs-card mb-4">
        <div class="d-flex justify-content-between align-items-center mb-2">
            <div class="fs-card-title mb-0">Item</div>
            <button class="btn btn-sm btn-outline-primary" @onclick="AddRow"><i class="bi bi-plus-lg me-1"></i>Tambah Baris</button>
        </div>
        <div class="table-responsive">
            <table class="table align-middle">
                <thead class="table-head">
                    <tr>
                        <th style="min-width:220px">Produk (Varian)</th>
                        <th style="width:90px">Qty</th>
                        <th style="width:130px">Harga</th>
                        <th style="width:90px">Disk %</th>
                        <th style="width:150px">Pajak</th>
                        <th class="text-end" style="width:130px">Subtotal</th>
                        <th style="width:40px"></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var row in _rows)
                    {
                        <tr>
                            <td>
                                <select class="form-select form-select-sm" value="@row.VariantId"
                                        @onchange="e => OnVariantChanged(row, e.Value?.ToString())">
                                    <option value="0">— pilih —</option>
                                    @foreach (var v in _variants)
                                    {
                                        <option value="@v.VariantId">@v.Sku — @v.ProductName</option>
                                    }
                                </select>
                            </td>
                            <td><input type="number" min="1" class="form-control form-control-sm" @bind="row.Quantity" /></td>
                            <td><input type="number" min="0" step="0.01" class="form-control form-control-sm" @bind="row.UnitPrice" /></td>
                            <td><input type="number" min="0" max="100" step="0.01" class="form-control form-control-sm" @bind="row.DiscountPercent" /></td>
                            <td>
                                <select class="form-select form-select-sm" @bind="row.TaxId">
                                    <option value="">tanpa pajak</option>
                                    @foreach (var t in _taxes)
                                    {
                                        <option value="@t.Id">@t.Name (@t.Rate%)</option>
                                    }
                                </select>
                            </td>
                            <td class="text-end">@LineTotal(row).ToString("N2")</td>
                            <td><button class="btn btn-sm btn-outline-danger" @onclick="() => _rows.Remove(row)"><i class="bi bi-x-lg"></i></button></td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
        <div class="text-end pe-2 fw-semibold">Grand Total: @GrandTotal().ToString("N2")</div>
    </div>

    <div class="d-flex gap-2 justify-content-end pt-1">
        <button class="btn btn-primary btn-sm px-3" @onclick="SaveAsync" disabled="@_saving">
            @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
            else { <i class="bi bi-floppy2-fill me-1"></i> }
            Simpan Draft
        </button>
        <a class="btn btn-outline-secondary btn-sm" href="/transactions/purchase-orders"><i class="bi bi-x-lg me-1"></i>Batal</a>
    </div>
}

@code {
    [Parameter] public int? Id { get; set; }
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private sealed class Row
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public int? TaxId { get; set; }
    }

    private IReadOnlyList<SupplierDto> _suppliers = [];
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private IReadOnlyList<TaxDto> _taxes = [];
    private IReadOnlyList<PurchaseOrderVariantOptionDto> _variants = [];
    private readonly List<Row> _rows = [];

    private int _supplierId, _warehouseId;
    private DateTime _orderDate = DateTime.Today;
    private DateTime? _expectedDate;
    private string? _notes;
    private bool _loading = true, _saving, _notFound;
    private string? _error;

    private string Title => Id is null ? "Buat Purchase Order" : "Edit Purchase Order";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null ? AppMenus.Perm("transactions.purchase-orders", "create")
                              : AppMenus.Perm("transactions.purchase-orders", "edit");
        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded)
        {
            Nav.NavigateTo("/transactions/purchase-orders");
            return;
        }

        _suppliers = await SupplierService.GetAllAsync();
        _warehouses = await WarehouseService.GetAllAsync();
        _taxes = await TaxService.GetAllAsync();
        _variants = await PoService.SearchVariantsAsync(null);

        if (Id is int id)
        {
            var po = await PoService.GetByIdAsync(id);
            if (po is null || po.Status != "Draft") { _notFound = true; }
            else
            {
                _supplierId = po.SupplierId; _warehouseId = po.WarehouseId;
                _orderDate = po.OrderDate; _expectedDate = po.ExpectedDate; _notes = po.Notes;
                foreach (var l in po.Lines)
                    _rows.Add(new Row { VariantId = l.ProductVariantId, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, TaxId = l.TaxId });
            }
        }
        else
        {
            AddRow();
        }
        _loading = false;
    }

    private void AddRow() => _rows.Add(new Row());

    private void OnVariantChanged(Row row, string? value)
    {
        row.VariantId = int.TryParse(value, out var id) ? id : 0;
        var v = _variants.FirstOrDefault(x => x.VariantId == row.VariantId);
        if (v is not null && row.UnitPrice == 0) row.UnitPrice = v.CostPrice; // saran harga = HPP
    }

    private decimal LineTotal(Row r)
    {
        var sub = Math.Round(r.Quantity * r.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var disc = Math.Round(sub * r.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var rate = r.TaxId is { } tid ? _taxes.FirstOrDefault(t => t.Id == tid)?.Rate ?? 0m : 0m;
        var tax = Math.Round((sub - disc) * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return sub - disc + tax;
    }

    private decimal GrandTotal() => _rows.Sum(LineTotal);

    private async Task SaveAsync()
    {
        _error = null;
        var lines = _rows.Where(r => r.VariantId > 0)
            .Select(r => new PurchaseOrderLineRequest(r.VariantId, r.Quantity, r.UnitPrice, r.DiscountPercent, r.TaxId))
            .ToList();
        if (lines.Count == 0) { _error = "Minimal satu baris item dengan produk dipilih."; return; }

        _saving = true;
        try
        {
            if (Id is int id)
                await PoService.UpdateAsync(id, new UpdatePurchaseOrderRequest(_supplierId, _warehouseId, _orderDate, _expectedDate, _notes, lines));
            else
                await PoService.CreateAsync(new CreatePurchaseOrderRequest(_supplierId, _warehouseId, _orderDate, _expectedDate, _notes, lines));
            Nav.NavigateTo("/transactions/purchase-orders");
        }
        catch (ValidationException ex)
        {
            _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage));
        }
        finally { _saving = false; }
    }
}
```

> **Catatan implementer:** sesuaikan nama field DTO `SupplierDto`/`WarehouseDto`/`TaxDto` (`Code`, `Name`, `Rate`, `Id`) bila berbeda. `TaxDto.Rate` dipakai untuk preview total. Bila `row.TaxId` `<select>` binding ke `int?` bermasalah, gunakan pola `@onchange` seperti pada variant.

- [ ] **Step 2: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

---

### Task 15: Web — halaman PO Detail (timeline + aksi)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoDetail.razor`

**Interfaces:**
- Consumes: `IPurchaseOrderService` (Task 6/10); `SwalService`; permission `transactions.purchase-orders.{edit,approve}`.

- [ ] **Step 1: Buat halaman Detail**

`src/MyApp.Web/Components/Pages/Transactions/PurchaseOrders/PoDetail.razor`:
```razor
@page "/transactions/purchase-orders/{Id:int}"
@attribute [Authorize(Policy = "transactions.purchase-orders.index")]
@rendermode InteractiveServer
@using FluentValidation
@using MyApp.Application.Approvals
@using MyApp.Application.PurchaseOrders
@inject IPurchaseOrderService PoService
@inject IAuthorizationService Auth
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>@(_po?.PoNumber ?? "Purchase Order")</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/transactions/purchase-orders"><i class="bi bi-arrow-left me-1"></i>Purchase Orders</a>
    <h4 class="uf-title">@(_po?.PoNumber ?? "")</h4>
</div>

@if (_loading)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else if (_po is null)
{
    <div class="alert alert-warning">Purchase order tidak ditemukan.</div>
}
else
{
    @if (_error is not null)
    {
        <div class="alert alert-danger d-flex align-items-center gap-2 mb-3 py-2"><span>&#9888;</span> @_error</div>
    }

    <div class="d-flex flex-wrap gap-2 mb-3">
        <span class="badge @StatusClass(_po.Status) fs-6">@_po.Status</span>
        @if (_po.Status == "Draft")
        {
            <a class="btn btn-sm btn-outline-primary" href="@($"/transactions/purchase-orders/{_po.Id}/edit")"><i class="bi bi-pencil me-1"></i>Edit</a>
            <button class="btn btn-sm btn-primary" @onclick="SubmitAsync" disabled="@_busy"><i class="bi bi-send me-1"></i>Submit</button>
            <button class="btn btn-sm btn-outline-dark" @onclick="CancelAsync" disabled="@_busy">Batalkan</button>
        }
        else if (_po.Status == "PendingApproval")
        {
            @if (_canApprove)
            {
                <button class="btn btn-sm btn-success" @onclick="ApproveAsync" disabled="@_busy"><i class="bi bi-check2-circle me-1"></i>Approve</button>
                <button class="btn btn-sm btn-danger" @onclick="() => _showReject = true" disabled="@_busy"><i class="bi bi-x-circle me-1"></i>Reject</button>
            }
            <button class="btn btn-sm btn-outline-dark" @onclick="CancelAsync" disabled="@_busy">Batalkan</button>
        }
    </div>

    @if (_showReject)
    {
        <div class="fs-card mb-4 border-danger">
            <div class="fs-card-title text-danger">Alasan Penolakan</div>
            <textarea class="form-control mb-2" rows="2" maxlength="500" @bind="_rejectReason" placeholder="Tuliskan alasan reject..."></textarea>
            <div class="d-flex gap-2 justify-content-end">
                <button class="btn btn-sm btn-danger" @onclick="RejectAsync" disabled="@_busy">Konfirmasi Reject</button>
                <button class="btn btn-sm btn-outline-secondary" @onclick="() => { _showReject = false; _rejectReason = string.Empty; }">Batal</button>
            </div>
        </div>
    }

    <div class="row g-4">
        <div class="col-12 col-lg-8">
            <div class="fs-card mb-4">
                <div class="fs-card-title">Informasi</div>
                <dl class="row mb-0 small">
                    <dt class="col-4 text-muted">Supplier</dt><dd class="col-8">@_po.SupplierName</dd>
                    <dt class="col-4 text-muted">Gudang Tujuan</dt><dd class="col-8">@_po.WarehouseName</dd>
                    <dt class="col-4 text-muted">Tanggal</dt><dd class="col-8">@_po.OrderDate.ToString("dd MMM yyyy")</dd>
                    <dt class="col-4 text-muted">Perkiraan Tiba</dt><dd class="col-8">@(_po.ExpectedDate?.ToString("dd MMM yyyy") ?? "—")</dd>
                    <dt class="col-4 text-muted">Catatan</dt><dd class="col-8">@(_po.Notes ?? "—")</dd>
                    @if (!string.IsNullOrEmpty(_po.RejectionNote))
                    {
                        <dt class="col-4 text-danger">Alasan Reject</dt><dd class="col-8 text-danger">@_po.RejectionNote</dd>
                    }
                </dl>
            </div>

            <div class="fs-card mb-4">
                <div class="fs-card-title">Item</div>
                <div class="table-responsive">
                    <table class="table align-middle mb-0">
                        <thead class="table-head">
                            <tr><th>Produk</th><th class="text-end">Qty</th><th class="text-end">Harga</th><th class="text-end">Disk</th><th class="text-end">Pajak</th><th class="text-end">Total</th></tr>
                        </thead>
                        <tbody>
                            @foreach (var l in _po.Lines)
                            {
                                <tr>
                                    <td>@l.VariantSku <span class="text-muted small">@l.ProductName</span></td>
                                    <td class="text-end">@l.Quantity</td>
                                    <td class="text-end">@l.UnitPrice.ToString("N2")</td>
                                    <td class="text-end">@l.LineDiscount.ToString("N2")</td>
                                    <td class="text-end">@l.LineTax.ToString("N2")</td>
                                    <td class="text-end fw-medium">@l.LineTotal.ToString("N2")</td>
                                </tr>
                            }
                        </tbody>
                        <tfoot>
                            <tr><td colspan="5" class="text-end fw-semibold">Grand Total (@_po.Currency)</td><td class="text-end fw-bold">@_po.GrandTotal.ToString("N2")</td></tr>
                        </tfoot>
                    </table>
                </div>
            </div>
        </div>

        <div class="col-12 col-lg-4">
            <div class="fs-card">
                <div class="fs-card-title">Approval</div>
                @if (_steps.Count == 0)
                {
                    <p class="text-muted small mb-0">Belum ada langkah approval.</p>
                }
                else
                {
                    <ul class="list-unstyled mb-0">
                        @foreach (var s in _steps)
                        {
                            <li class="d-flex gap-2 mb-3">
                                <i class="bi @StepIcon(s.Status) fs-5 @StepColor(s.Status)"></i>
                                <div>
                                    <div class="fw-medium">Level @s.StepOrder — @s.RoleName</div>
                                    <div class="small text-muted">
                                        @s.Status
                                        @if (s.ActedByName is not null) { <text> · @s.ActedByName</text> }
                                        @if (s.ActedAt is not null) { <text> · @s.ActedAt.Value.ToString("dd MMM HH:mm")</text> }
                                    </div>
                                    @if (!string.IsNullOrEmpty(s.Note)) { <div class="small text-danger">@s.Note</div> }
                                </div>
                            </li>
                        }
                    </ul>
                }
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public int Id { get; set; }
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private PurchaseOrderDto? _po;
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
        _po = await PoService.GetByIdAsync(Id);
        _steps = _po is null ? [] : await PoService.GetApprovalStepsAsync(Id);
        _canApprove = await EvaluateCanApproveAsync();
        _loading = false;
    }

    private async Task<bool> EvaluateCanApproveAsync()
    {
        if (_po is null || _po.Status != "PendingApproval") return false;
        if (!(await Auth.AuthorizeAsync(_user, "transactions.purchase-orders.approve")).Succeeded) return false;
        // bukan pembuat
        if (string.Equals(_po.CreatedBy, _user.Identity?.Name, StringComparison.OrdinalIgnoreCase)) return false;
        // memegang role pada step Pending berjalan
        var current = _steps.FirstOrDefault(s => s.Status == "Pending");
        return current is not null && _user.IsInRole(current.RoleName);
    }

    private async Task SubmitAsync() => await RunAsync(() => PoService.SubmitAsync(Id), "PO disubmit");
    private async Task CancelAsync()
    {
        if (!await Swal.ConfirmAsync("Batalkan PO?", "PO akan ditandai Cancelled.")) return;
        await RunAsync(() => PoService.CancelAsync(Id), "PO dibatalkan");
    }
    private async Task ApproveAsync() =>
        await RunAsync(() => PoService.ApproveAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole), "PO di-approve");

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(_rejectReason)) { _error = "Alasan reject wajib diisi."; return; }
        var reason = _rejectReason;
        _showReject = false;
        _rejectReason = string.Empty;
        await RunAsync(() => PoService.RejectAsync(Id, _user.Identity?.Name ?? "", _user.IsInRole, reason), "PO ditolak, kembali ke Draft");
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
        "Draft" => "bg-secondary", "PendingApproval" => "bg-warning text-dark",
        "Confirmed" => "bg-success", "Rejected" => "bg-danger", "Cancelled" => "bg-dark", _ => "bg-light text-dark"
    };
    private static string StepIcon(string s) => s switch
    {
        "Approved" => "bi-check-circle-fill", "Rejected" => "bi-x-circle-fill", _ => "bi-clock-fill"
    };
    private static string StepColor(string s) => s switch
    {
        "Approved" => "text-success", "Rejected" => "text-danger", _ => "text-warning"
    };
}
```

> **Catatan implementer:** `SwalService` menyediakan `ConfirmAsync(title, text, confirmText)`, `ToastAsync(icon, title)`, `AlertAsync(title, text)` — **tidak** ada prompt input, sehingga alasan reject diambil lewat panel inline (`_showReject`/`_rejectReason`) di atas. `ConfirmAsync` default `confirmText="Yes, delete"`; untuk Cancel PO boleh override teksnya bila ingin.

- [ ] **Step 2: Verifikasi build (Detail)**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

---

### Task 16: Web — Settings Approval Chain (index + form) + nav

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainsIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainForm.razor`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IApprovalChainService` (Task 5/8); `RoleManager<ApplicationRole>` (existing Identity); permission `settings.approval-chains.*` (Task 12).

- [ ] **Step 1: Buat halaman Index**

`src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainsIndex.razor`:
```razor
@page "/settings/approval-chains"
@attribute [Authorize(Policy = "settings.approval-chains.index")]
@rendermode InteractiveServer
@using MyApp.Application.Approvals
@using MyApp.Domain.Entities
@inject IApprovalChainService ChainService

<PageTitle>Approval Chains</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0 fw-semibold">Approval Chains</h1>
</div>
<p class="text-muted">Atur urutan role yang harus menyetujui tiap jenis dokumen. Rantai kosong = dokumen langsung disetujui saat submit.</p>

<div class="data-card">
    <div class="table-responsive">
        <table class="table table-hover align-middle mb-0">
            <thead class="table-head">
                <tr><th class="ps-3">Dokumen</th><th>Rantai (urutan role)</th><th class="text-end pe-3" style="width:100px"></th></tr>
            </thead>
            <tbody>
                @foreach (var dt in Enum.GetValues<ApprovalDocumentType>())
                {
                    var chain = _chains.GetValueOrDefault(dt, []);
                    <tr>
                        <td class="ps-3 fw-medium">@dt</td>
                        <td>
                            @if (chain.Count == 0) { <span class="text-muted">— kosong (auto-approve) —</span> }
                            else { @string.Join(" → ", chain.Select(c => c.RoleName)) }
                        </td>
                        <td class="text-end pe-3">
                            <AuthorizeView Policy="settings.approval-chains.edit">
                                <Authorized>
                                    <a class="btn btn-sm btn-outline-primary" href="@($"/settings/approval-chains/{dt}")"><i class="bi bi-pencil"></i></a>
                                </Authorized>
                            </AuthorizeView>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
</div>

@code {
    private readonly Dictionary<ApprovalDocumentType, IReadOnlyList<ApprovalChainStepDto>> _chains = [];

    protected override async Task OnInitializedAsync()
    {
        foreach (var dt in Enum.GetValues<ApprovalDocumentType>())
            _chains[dt] = await ChainService.GetByDocumentTypeAsync(dt);
    }
}
```

- [ ] **Step 2: Buat halaman Form**

`src/MyApp.Web/Components/Pages/Settings/ApprovalChains/ApprovalChainForm.razor`:
```razor
@page "/settings/approval-chains/{DocType}"
@attribute [Authorize(Policy = "settings.approval-chains.edit")]
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Identity
@using MyApp.Application.Approvals
@using MyApp.Domain.Entities
@using MyApp.Infrastructure.Identity
@inject IApprovalChainService ChainService
@inject RoleManager<ApplicationRole> RoleManager
@inject NavigationManager Nav

<PageTitle>Approval Chain — @DocType</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/settings/approval-chains"><i class="bi bi-arrow-left me-1"></i>Approval Chains</a>
    <h4 class="uf-title">Rantai Approval — @DocType</h4>
</div>

@if (!_valid)
{
    <div class="alert alert-warning">Tipe dokumen tidak dikenal.</div>
}
else
{
    @if (_saved) { <div class="alert alert-success py-2">Tersimpan.</div> }

    <div class="fs-card mb-4" style="max-width:640px">
        <div class="d-flex justify-content-between align-items-center mb-2">
            <div class="fs-card-title mb-0">Langkah (urut dari atas)</div>
            <button class="btn btn-sm btn-outline-primary" @onclick="AddStep"><i class="bi bi-plus-lg me-1"></i>Tambah</button>
        </div>
        @if (_steps.Count == 0)
        {
            <p class="text-muted small">Tidak ada langkah — dokumen akan langsung disetujui saat submit.</p>
        }
        else
        {
            @for (var i = 0; i < _steps.Count; i++)
            {
                var idx = i;
                <div class="d-flex gap-2 align-items-center mb-2">
                    <span class="badge bg-secondary">@(idx + 1)</span>
                    <select class="form-select form-select-sm" @bind="_steps[idx]">
                        <option value="">— pilih role —</option>
                        @foreach (var r in _roles)
                        {
                            <option value="@r">@r</option>
                        }
                    </select>
                    <button class="btn btn-sm btn-outline-secondary" @onclick="() => MoveUp(idx)" disabled="@(idx == 0)"><i class="bi bi-arrow-up"></i></button>
                    <button class="btn btn-sm btn-outline-danger" @onclick="() => _steps.RemoveAt(idx)"><i class="bi bi-x-lg"></i></button>
                </div>
            }
        }
        <div class="d-flex gap-2 justify-content-end pt-2">
            <button class="btn btn-primary btn-sm px-3" @onclick="SaveAsync" disabled="@_saving">
                @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> }
                Simpan
            </button>
        </div>
    </div>
}

@code {
    [Parameter] public string DocType { get; set; } = "";

    private ApprovalDocumentType _dt;
    private bool _valid, _saving, _saved;
    private List<string> _roles = [];
    private readonly List<string> _steps = [];

    protected override async Task OnInitializedAsync()
    {
        _valid = Enum.TryParse(DocType, out _dt);
        if (!_valid) return;

        _roles = RoleManager.Roles.Select(r => r.Name!).OrderBy(n => n).ToList();
        var chain = await ChainService.GetByDocumentTypeAsync(_dt);
        foreach (var c in chain) _steps.Add(c.RoleName);
    }

    private void AddStep() => _steps.Add("");
    private void MoveUp(int idx)
    {
        if (idx <= 0) return;
        (_steps[idx - 1], _steps[idx]) = (_steps[idx], _steps[idx - 1]);
    }

    private async Task SaveAsync()
    {
        _saving = true; _saved = false;
        var inputs = _steps.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select((role, i) => new ApprovalChainStepInput(i + 1, role)).ToList();
        await ChainService.ReplaceChainAsync(_dt, inputs);
        _saved = true; _saving = false;
    }
}
```

- [ ] **Step 3: Tambah entri NavMenu (grup Settings)**

Di `src/MyApp.Web/Components/Layout/NavMenu.razor`, di dalam grup Settings, setelah blok `AuthorizeView Policy="settings.roles.index"` (sekitar baris 211-218), tambahkan:
```razor
                <AuthorizeView Policy="settings.approval-chains.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="settings/approval-chains" title="Approval Chain">
                                <i class="bi bi-diagram-3-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Approval Chain</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
```

- [ ] **Step 4: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

---

### Task 17: Verifikasi penuh + manual

**Files:** (tidak ada perubahan kode)

- [ ] **Step 1: Jalankan seluruh test**

Run: `dotnet test`
Expected: semua test PASS (unit + integration).

- [ ] **Step 2: Build cek warning**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

- [ ] **Step 3: Verifikasi manual (run app)**

Run: `dotnet run --project src/MyApp.Web`, login sebagai admin, lalu periksa:
- Sidebar **Transaksi → Purchase Order** membuka daftar PO (bukan placeholder lagi).
- Buat PO: pilih supplier, gudang, tambah ≥1 baris (varian, qty, harga, diskon, pajak), total terhitung, Simpan Draft → muncul di daftar status **Draft**.
- Buka detail PO Draft → **Submit**. Karena rantai default = role admin dan admin adalah pembuat, tombol Approve **tidak** muncul untuk admin (segregation of duties) → status **PendingApproval**.
- Uji rantai sebenarnya: di **Settings → Approval Chain**, set rantai PurchaseOrder ke sebuah role non-admin; buat user/role approver; login sebagai approver untuk **Approve**/**Reject**. Approve seluruh level → **Confirmed**; Reject → kembali **Draft** dengan alasan tampil.
- **Cancel** PO Draft/PendingApproval → **Cancelled**.
- Set rantai PurchaseOrder **kosong** di Settings → submit PO baru → langsung **Confirmed**.

- [ ] **Step 4: Konfirmasi tidak ada perubahan stok**

Periksa: tidak ada `StockMovement`/`ProductStock` baru akibat alur PO (B1 tidak menyentuh stok). Penerimaan barang & HPP adalah Tahap B2.

---

## Self-Review (terhadap spec)

**Spec coverage:**
- Engine approval (§Arsitektur 1): `ApprovalChainStep`/`ApprovalStep` (Task 1–2), `IApprovalChainService`/`IApprovalService` + DTO/validator (Task 5), impl + DI (Task 8–9), seed default (Task 12).
- PO Domain (§2): `PurchaseOrderLine` + status (Task 3), `PurchaseOrder` (Task 4).
- Application PO (§3): DTO/interface/validator (Task 6).
- Infrastructure (§3): mapping (Task 7), `PurchaseOrderService` orkestrasi transaksi + nomor PO (Task 10), migration (Task 11).
- Web (§4): PO Index ganti placeholder (Task 13), Form (Task 14), Detail+timeline+aksi (Task 15), Settings chain (Task 16); AppMenus+nav (Task 12, 13, 16).
- Testing (§5): unit Task 1–6, integration Task 8–10, run penuh Task 11/17.

**Keputusan kunci tercermin:** rantai role tetap; reject→Draft + RejectionNote + reset; pembuat tak boleh approve (banding `actingUserName` vs `PO.CreatedBy`); nomor PO auto `PO-YYYYMM-####`; baris qty+harga+diskon%+pajak; lifecycle Draft/PendingApproval/Confirmed/Rejected/Cancelled; rantai kosong→auto-confirm.

**Konsistensi tipe:** signatur `IApprovalService`/`IPurchaseOrderService` cocok antara interface (Task 5/6), impl (Task 9/10), dan pemanggil web (Task 15). `ApprovalStepDto.Status` = string (`enum.ToString()`), dipakai konsisten di Detail page. Enum disimpan string di semua entitas.

**Catatan diketahui (bukan placeholder):** signatur ctor master (`Supplier`/`Warehouse`/`Product`/`ProductVariant`) di seed test Task 10 dan nama field DTO master di Task 14 perlu dicocokkan implementer dengan kode aktual (sudah diberi catatan eksplisit). Pajak diperlakukan exclusive (sesuai spec). Tidak ada perubahan stok di B1.
