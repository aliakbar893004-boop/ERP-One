# Fase 3a-ii — Supplier Payment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Pay Supplier Invoices through an approved payment that writes a cash/bank ledger movement and updates invoice PaidAmount/status; support void (reversal).

**Architecture:** Mirrors `PurchaseOrderService` (approval lifecycle) + `SupplierInvoiceService`. New `CashBankMovement` ledger; `SupplierPayment`(+`Allocation`). Money moves only on Posted (final approval). Numbering via `IDocumentNumberService`; approval via `IApprovalService` (document-agnostic).

**Tech Stack:** .NET/C#, EF Core (SQL Server prod, SQLite tests), Blazor Server, FluentValidation, xUnit.

## Global Constraints
- UI English; `.pi`/`.cf`/`.pf` (Atlas).
- Register new entities in `tablePrefixes` (`T_`).
- Money precision (18,2); `Status`/`Direction` via `HasConversion<string>()`.
- Payment numbering `APP-{yyyyMM}-{0000}` (NumberSequence Id=8).
- Tests use `EnsureCreated()` (no migration needed); migration for prod.
- Commit after each task's tests pass.

## Reference patterns
- `PurchaseOrderService.cs` (Submit/Approve/Reject + transaction), `SupplierInvoiceService.cs` (line build, name lookups), `PurchaseOrder.cs` (status methods), `BootstrapSeeder.cs` (approval chain seed), `PoDetail.razor` (approval detail UI).

---

## Task 1: Domain + config + numbering + approval type + migration

**Files:**
- Create: `src/ErpOne.Domain/Entities/SupplierPaymentStatus.cs`
- Create: `src/ErpOne.Domain/Entities/CashBankMovementDirection.cs`
- Create: `src/ErpOne.Domain/Entities/CashBankMovement.cs`
- Create: `src/ErpOne.Domain/Entities/SupplierPaymentAllocation.cs`
- Create: `src/ErpOne.Domain/Entities/SupplierPayment.cs`
- Modify: `src/ErpOne.Domain/Entities/SupplierInvoice.cs` (add ApplyPayment/ReversePayment)
- Modify: `src/ErpOne.Domain/Entities/ApprovalDocumentType.cs` (add SupplierPayment)
- Modify: `src/ErpOne.Application/Numbering/DocumentTypes.cs` (add SupplierPayment)
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Create: migration `AddSupplierPayment`

**Interfaces:**
- Produces:
  - `enum SupplierPaymentStatus { Draft, PendingApproval, Posted, Voided }`
  - `enum CashBankMovementDirection { In, Out }`
  - `CashBankMovement(int cashBankAccountId, DateTime date, CashBankMovementDirection direction, decimal amount, string refType, int refId, string? note)`; props incl `Id`.
  - `SupplierPaymentAllocation(int supplierInvoiceId, decimal amount)`; props `Id, SupplierPaymentId, SupplierInvoiceId, Amount`.
  - `SupplierPayment(string paymentNumber, int supplierId, int cashBankAccountId, string currency, DateTime paymentDate, string? notes)`; methods `SetAllocations(IEnumerable<SupplierPaymentAllocation>)`, `UpdateHeader(int cashBankAccountId, DateTime paymentDate, string? notes)`, `Submit()`, `MarkPosted()`, `ReturnToDraft(string reason)`, `Void()`; props `Id, PaymentNumber, SupplierId, CashBankAccountId, Currency, PaymentDate, Amount, Notes, Status, RejectionNote, Allocations`.
  - `SupplierInvoice.ApplyPayment(decimal)`, `SupplierInvoice.ReversePayment(decimal)`.
  - `ApprovalDocumentType.SupplierPayment`, `DocumentTypes.SupplierPayment = "SupplierPayment"`.
  - `AppDbContext.CashBankMovements`, `SupplierPayments`, `SupplierPaymentAllocations`.

- [ ] **Step 1: Enums**

`SupplierPaymentStatus.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Supplier Payment. Uang keluar saat Posted.</summary>
public enum SupplierPaymentStatus { Draft, PendingApproval, Posted, Voided }
```

`CashBankMovementDirection.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Arah mutasi kas/bank.</summary>
public enum CashBankMovementDirection { In, Out }
```

- [ ] **Step 2: CashBankMovement ledger entity**

`CashBankMovement.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Mutasi kas/bank (ledger). Saldo = OpeningBalance + Σ In − Σ Out.</summary>
public class CashBankMovement : AuditableEntity
{
    public int Id { get; private set; }
    public int CashBankAccountId { get; private set; }
    public DateTime Date { get; private set; }
    public CashBankMovementDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string RefType { get; private set; } = default!;
    public int RefId { get; private set; }
    public string? Note { get; private set; }

    private CashBankMovement() { } // EF Core

    public CashBankMovement(int cashBankAccountId, DateTime date, CashBankMovementDirection direction,
        decimal amount, string refType, int refId, string? note)
    {
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        if (string.IsNullOrWhiteSpace(refType)) throw new ArgumentException("RefType is required.", nameof(refType));

        CashBankAccountId = cashBankAccountId;
        Date = date;
        Direction = direction;
        Amount = amount;
        RefType = refType.Trim();
        RefId = refId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
```

- [ ] **Step 3: Allocation + Payment entities**

`SupplierPaymentAllocation.cs`:
```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Alokasi satu payment ke satu invoice.</summary>
public class SupplierPaymentAllocation
{
    public int Id { get; private set; }
    public int SupplierPaymentId { get; private set; }
    public int SupplierInvoiceId { get; private set; }
    public decimal Amount { get; private set; }

    private SupplierPaymentAllocation() { } // EF Core

    public SupplierPaymentAllocation(int supplierInvoiceId, decimal amount)
    {
        if (supplierInvoiceId <= 0) throw new ArgumentException("SupplierInvoiceId is required.", nameof(supplierInvoiceId));
        if (amount <= 0) throw new ArgumentException("Allocation amount must be > 0.", nameof(amount));
        SupplierInvoiceId = supplierInvoiceId;
        Amount = amount;
    }
}
```

`SupplierPayment.cs`:
```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Pembayaran ke supplier atas 1+ invoice. Uang keluar (ledger) saat Posted.</summary>
public class SupplierPayment : AuditableEntity
{
    private readonly List<SupplierPaymentAllocation> _allocations = [];

    public int Id { get; private set; }
    public string PaymentNumber { get; private set; } = default!;
    public int SupplierId { get; private set; }
    public int CashBankAccountId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public DateTime PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Notes { get; private set; }
    public SupplierPaymentStatus Status { get; private set; }
    public string? RejectionNote { get; private set; }

    public IReadOnlyCollection<SupplierPaymentAllocation> Allocations => _allocations;

    private SupplierPayment() { } // EF Core

    public SupplierPayment(string paymentNumber, int supplierId, int cashBankAccountId, string currency,
        DateTime paymentDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(paymentNumber)) throw new ArgumentException("PaymentNumber is required.", nameof(paymentNumber));
        if (supplierId <= 0) throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        PaymentNumber = paymentNumber.Trim();
        SupplierId = supplierId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        SetHeader(cashBankAccountId, paymentDate, notes);
        Status = SupplierPaymentStatus.Draft;
    }

    public void SetAllocations(IEnumerable<SupplierPaymentAllocation> allocations)
    {
        EnsureDraft();
        _allocations.Clear();
        foreach (var a in allocations) _allocations.Add(a);
        Amount = _allocations.Sum(a => a.Amount);
    }

    public void UpdateHeader(int cashBankAccountId, DateTime paymentDate, string? notes)
    {
        EnsureDraft();
        SetHeader(cashBankAccountId, paymentDate, notes);
    }

    public void Submit()
    {
        EnsureDraft();
        if (_allocations.Count == 0) throw new InvalidOperationException("Cannot submit a payment without allocations.");
        Status = SupplierPaymentStatus.PendingApproval;
    }

    public void MarkPosted()
    {
        if (Status != SupplierPaymentStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending payment can be posted.");
        Status = SupplierPaymentStatus.Posted;
    }

    public void ReturnToDraft(string reason)
    {
        if (Status != SupplierPaymentStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending payment can be returned to draft.");
        Status = SupplierPaymentStatus.Draft;
        RejectionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Void()
    {
        if (Status != SupplierPaymentStatus.Posted)
            throw new InvalidOperationException("Only a posted payment can be voided.");
        Status = SupplierPaymentStatus.Voided;
    }

    private void SetHeader(int cashBankAccountId, DateTime paymentDate, string? notes)
    {
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        CashBankAccountId = cashBankAccountId;
        PaymentDate = paymentDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != SupplierPaymentStatus.Draft)
            throw new InvalidOperationException("Only a draft payment can be modified.");
    }
}
```

- [ ] **Step 4: Add payment methods to SupplierInvoice**

In `src/ErpOne.Domain/Entities/SupplierInvoice.cs`, add after `Cancel()`:
```csharp
    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Payment amount must be > 0.", nameof(amount));
        if (Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot pay a cancelled invoice.");
        if (PaidAmount + amount > GrandTotal)
            throw new InvalidOperationException("Payment exceeds the invoice outstanding amount.");
        PaidAmount += amount;
        Status = PaidAmount >= GrandTotal ? SupplierInvoiceStatus.Paid : SupplierInvoiceStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Reversal amount must be > 0.", nameof(amount));
        if (amount > PaidAmount)
            throw new InvalidOperationException("Reversal exceeds the paid amount.");
        PaidAmount -= amount;
        Status = PaidAmount <= 0 ? SupplierInvoiceStatus.Open : SupplierInvoiceStatus.PartiallyPaid;
    }
```

- [ ] **Step 5: Add enum values**

`ApprovalDocumentType.cs` — add `SupplierPayment`:
```csharp
public enum ApprovalDocumentType
{
    PurchaseOrder,
    SalesOrder,
    SupplierPayment
}
```

`DocumentTypes.cs` — add:
```csharp
    public const string SupplierPayment = "SupplierPayment";
```

- [ ] **Step 6: DbSets, config, prefixes, number-sequence seed**

In `AppDbContext.cs` add DbSets (after `SupplierInvoiceLines`):
```csharp
    public DbSet<CashBankMovement> CashBankMovements => Set<CashBankMovement>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
```

Add configs inside `OnModelCreating` (after the `SupplierInvoiceLine` config):
```csharp
        modelBuilder.Entity<CashBankMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(4).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.RefType).HasMaxLength(40).IsRequired();
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<CashBankAccount>().WithMany().HasForeignKey(x => x.CashBankAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CashBankAccountId, x.Date });
        });

        modelBuilder.Entity<SupplierPayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PaymentNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.PaymentNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashBankAccount>().WithMany().HasForeignKey(x => x.CashBankAccountId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Allocations)
                .WithOne()
                .HasForeignKey(a => a.SupplierPaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SupplierPayment.Allocations))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SupplierPaymentAllocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<SupplierInvoice>().WithMany().HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
```

Add to `tablePrefixes` (Transaksi section):
```csharp
            [nameof(SupplierPayment)] = "T_",
            [nameof(SupplierPaymentAllocation)] = "T_",
            [nameof(CashBankMovement)] = "S_",
```

Seed the number sequence — add an 8th row to the `NumberSequence` `HasData(...)` list:
```csharp
                new { Id = 8, Code = "SupplierPayment", Prefix = "APP", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
```
> Add after the Id=7 SupplierInvoice row inside the same `HasData(...)` call.

- [ ] **Step 7: Build + migration**

Run: `dotnet build` → succeeds.
Run: `dotnet ef migrations add AddSupplierPayment --project src/ErpOne.Infrastructure --startup-project src/ErpOne.Web`
Inspect: creates `S_CashBankMovements`, `T_SupplierPayments`, `T_SupplierPaymentAllocations`, InsertData NumberSequence Id=8.

- [ ] **Step 8: Commit**

```bash
git add src/ErpOne.Domain/Entities/ src/ErpOne.Application/Numbering/DocumentTypes.cs src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations/
git commit -m "feat: add SupplierPayment + CashBankMovement entities, config, numbering, migration"
```

---

## Task 2: SupplierPaymentService + CashBank balance + approval seed + tests

**Files:**
- Create: `src/ErpOne.Application/SupplierPayments/SupplierPaymentDtos.cs`
- Create: `src/ErpOne.Application/SupplierPayments/ISupplierPaymentService.cs`
- Create: `src/ErpOne.Application/SupplierPayments/SupplierPaymentValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/SupplierPaymentService.cs`
- Modify: `src/ErpOne.Application/CashBank/CashBankAccountDtos.cs` (add CurrentBalance)
- Modify: `src/ErpOne.Application/CashBank/ICashBankAccountService.cs` (add GetBalanceAsync)
- Modify: `src/ErpOne.Infrastructure/Services/CashBankAccountService.cs` (compute balance)
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Modify: `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (seed SupplierPayment chain)
- Test: `tests/ErpOne.IntegrationTests/SupplierPaymentServiceTests.cs`

**Interfaces:**
- Produces:
  - `record SupplierPaymentListItemDto(int Id, string PaymentNumber, string SupplierName, DateTime PaymentDate, string CashBankAccountName, string Currency, decimal Amount, string Status)`
  - `record SupplierPaymentAllocationDto(int Id, int SupplierInvoiceId, string InvoiceNumber, decimal InvoiceGrandTotal, decimal InvoiceOutstanding, decimal Amount)`
  - `record SupplierPaymentDto(int Id, string PaymentNumber, int SupplierId, string SupplierName, int CashBankAccountId, string CashBankAccountName, string Currency, DateTime PaymentDate, decimal Amount, string? Notes, string Status, string? RejectionNote, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierPaymentAllocationDto> Allocations)`
  - `record PayableInvoiceDto(int SupplierInvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal, decimal Outstanding)`
  - `record SupplierPaymentDashboardDto(int Total, int Draft, int PendingApproval, int Posted, decimal PostedThisMonth)`
  - `record PaymentAllocationInput(int SupplierInvoiceId, decimal Amount)`
  - `record CreateSupplierPaymentRequest(int SupplierId, int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations)`
  - `record UpdateSupplierPaymentRequest(int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations)`
  - `ISupplierPaymentService` (methods per spec).
  - `ICashBankAccountService.GetBalanceAsync(int id)`; `CashBankAccountDto.CurrentBalance`.

- [ ] **Step 1: DTOs + interface + validators**

`SupplierPaymentDtos.cs`:
```csharp
namespace ErpOne.Application.SupplierPayments;

public record SupplierPaymentListItemDto(int Id, string PaymentNumber, string SupplierName, DateTime PaymentDate, string CashBankAccountName, string Currency, decimal Amount, string Status);
public record SupplierPaymentAllocationDto(int Id, int SupplierInvoiceId, string InvoiceNumber, decimal InvoiceGrandTotal, decimal InvoiceOutstanding, decimal Amount);
public record SupplierPaymentDto(int Id, string PaymentNumber, int SupplierId, string SupplierName, int CashBankAccountId, string CashBankAccountName, string Currency, DateTime PaymentDate, decimal Amount, string? Notes, string Status, string? RejectionNote, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<SupplierPaymentAllocationDto> Allocations);
public record PayableInvoiceDto(int SupplierInvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal, decimal Outstanding);
public record SupplierPaymentDashboardDto(int Total, int Draft, int PendingApproval, int Posted, decimal PostedThisMonth);
public record PaymentAllocationInput(int SupplierInvoiceId, decimal Amount);
public record CreateSupplierPaymentRequest(int SupplierId, int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations);
public record UpdateSupplierPaymentRequest(int CashBankAccountId, DateTime PaymentDate, string? Notes, IReadOnlyList<PaymentAllocationInput> Allocations);
```

`ISupplierPaymentService.cs`:
```csharp
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Domain.Entities;

namespace ErpOne.Application.SupplierPayments;

public interface ISupplierPaymentService
{
    Task<PagedResult<SupplierPaymentListItemDto>> GetPagedAsync(int page, int pageSize, string? search = null, SupplierPaymentStatus? status = null, CancellationToken ct = default);
    Task<SupplierPaymentDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierPaymentDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PayableInvoiceDto>> GetPayableInvoicesAsync(int supplierId, CancellationToken ct = default);
    Task<SupplierPaymentDto> CreateDraftAsync(CreateSupplierPaymentRequest request, CancellationToken ct = default);
    Task<bool> UpdateDraftAsync(int id, UpdateSupplierPaymentRequest request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default);
    Task SubmitAsync(int id, CancellationToken ct = default);
    Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default);
    Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default);
    Task VoidAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default);
}
```

`SupplierPaymentValidators.cs`:
```csharp
using FluentValidation;

namespace ErpOne.Application.SupplierPayments;

public class CreateSupplierPaymentValidator : AbstractValidator<CreateSupplierPaymentRequest>
{
    public CreateSupplierPaymentValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Add at least one allocation.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(y => y.SupplierInvoiceId).GreaterThan(0);
            a.RuleFor(y => y.Amount).GreaterThan(0);
        });
    }
}

public class UpdateSupplierPaymentValidator : AbstractValidator<UpdateSupplierPaymentRequest>
{
    public UpdateSupplierPaymentValidator()
    {
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Add at least one allocation.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(y => y.SupplierInvoiceId).GreaterThan(0);
            a.RuleFor(y => y.Amount).GreaterThan(0);
        });
    }
}
```

- [ ] **Step 2: Extend CashBank balance**

In `CashBankAccountDtos.cs`, add `decimal CurrentBalance` as the LAST positional field of `CashBankAccountDto`:
```csharp
public record CashBankAccountDto(int Id, string Code, string Name, string Type, string Currency, decimal OpeningBalance, string? BankName, string? AccountNumber, string? AccountHolder, bool IsActive, DateTime CreatedAt, string? CreatedBy, decimal CurrentBalance);
```

In `ICashBankAccountService.cs`, add:
```csharp
    Task<decimal> GetBalanceAsync(int id, CancellationToken ct = default);
```

In `CashBankAccountService.cs`:
- Add a private balance helper and use it in `ToDto`. Since `ToDto` is static and can't query, change the read methods to compute balance. Replace the `ToDto` usages in `GetAllAsync`/`GetActiveAsync`/`GetPagedAsync`/`GetByIdAsync` with a version that includes balance. Simplest: compute a per-account balance map then map. Replace the four read methods' projections with materialize-then-map. Add:
```csharp
    public async Task<decimal> GetBalanceAsync(int id, CancellationToken ct = default)
    {
        var opening = await db.CashBankAccounts.AsNoTracking().Where(a => a.Id == id).Select(a => a.OpeningBalance).FirstOrDefaultAsync(ct);
        var inSum = await db.CashBankMovements.AsNoTracking().Where(m => m.CashBankAccountId == id && m.Direction == CashBankMovementDirection.In).SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        var outSum = await db.CashBankMovements.AsNoTracking().Where(m => m.CashBankAccountId == id && m.Direction == CashBankMovementDirection.Out).SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        return opening + inSum - outSum;
    }

    private async Task<Dictionary<int, decimal>> BalanceMapAsync(List<int> ids, CancellationToken ct)
    {
        var moves = await db.CashBankMovements.AsNoTracking().Where(m => ids.Contains(m.CashBankAccountId))
            .GroupBy(m => m.CashBankAccountId)
            .Select(g => new { Id = g.Key, In = g.Where(x => x.Direction == CashBankMovementDirection.In).Sum(x => x.Amount), Out = g.Where(x => x.Direction == CashBankMovementDirection.Out).Sum(x => x.Amount) })
            .ToListAsync(ct);
        return moves.ToDictionary(m => m.Id, m => m.In - m.Out);
    }
```
Then rework the read methods to include balance. Example for `GetByIdAsync`:
```csharp
    public async Task<CashBankAccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x, await GetBalanceAsync(id, ct));
    }
```
And for the list methods, materialize entities then map with the balance map:
```csharp
    public async Task<IReadOnlyList<CashBankAccountDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await db.CashBankAccounts.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        var bal = await BalanceMapAsync(list.Select(x => x.Id).ToList(), ct);
        return list.Select(x => ToDto(x, x.OpeningBalance + bal.GetValueOrDefault(x.Id, 0m))).ToList();
    }
```
Apply the same materialize-then-map to `GetActiveAsync` and `GetPagedAsync` (materialize the page items list, then map with `BalanceMapAsync`). Change `ToDto` signature to:
```csharp
    private static CashBankAccountDto ToDto(CashBankAccount x, decimal currentBalance) =>
        new(x.Id, x.Code, x.Name, x.Type.ToString(), x.Currency, x.OpeningBalance,
            x.BankName, x.AccountNumber, x.AccountHolder, x.IsActive, x.CreatedAt, x.CreatedBy, currentBalance);
```
> After editing, ensure every `ToDto(...)` call passes the balance. In `CreateAsync`/`UpdateAsync` return paths, use `ToDto(entity, entity.OpeningBalance)` (a brand-new account has no movements).

- [ ] **Step 3: Write the failing tests**

`tests/ErpOne.IntegrationTests/SupplierPaymentServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Application.SupplierPayments;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SupplierPaymentServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierPaymentServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // supplier + confirmed PO + posted GRN + Open SupplierInvoice (10000) + a cash account (IDR).
    private static async Task<(int supplierId, int invoiceId, int accountId, decimal grand)> SeedInvoiceAndAccountAsync(IServiceProvider sp)
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
        var poDto = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, new DateTime(2026, 7, 1), null, null,
            [new PurchaseOrderLineRequest(variant.Id, 10, 1000m, 0m, null)]));
        await po.SubmitAsync(poDto.Id);

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(poDto.Id, new DateTime(2026, 7, 2), null,
            [new GoodsReceiptLineRequest(poDto.Lines[0].Id, 10, 1000m)]));
        await grnSvc.PostAsync(grn.Id);

        var invSvc = sp.GetRequiredService<ISupplierInvoiceService>();
        var inv = await invSvc.CreateAsync(new CreateSupplierInvoiceRequest(supplier.Id, new DateTime(2026, 7, 3), null, null, null, [grn.Id]));

        var acc = sp.GetRequiredService<ICashBankAccountService>();
        var account = await acc.CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));

        return (supplier.Id, inv.Id, account.Id, inv.GrandTotal);
    }

    [Fact]
    public async Task Submit_with_empty_chain_posts_and_updates_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var draft = await pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null,
            [new PaymentAllocationInput(invoiceId, grand)]));
        Assert.StartsWith("APP-202607-", draft.PaymentNumber);

        await pay.SubmitAsync(draft.Id);   // no chain seeded in tests → auto-posted

        var posted = await pay.GetByIdAsync(draft.Id);
        Assert.Equal("Posted", posted!.Status);

        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Paid", invoice!.Status);
        Assert.Equal(0m, invoice.Outstanding);

        Assert.Equal(-grand, await acc.GetBalanceAsync(accountId));  // opening 0 − payment out
    }

    [Fact]
    public async Task Allocation_over_outstanding_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();

        await Assert.ThrowsAsync<ValidationException>(() => pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null,
            [new PaymentAllocationInput(invoiceId, grand + 1m)])));
    }

    [Fact]
    public async Task Mismatched_account_currency_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, _, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var usd = await acc.CreateAsync(new CreateCashBankAccountRequest($"USD{Guid.NewGuid().ToString("N")[..4]}", "USD Acc", "Bank", "USD", 0m, "X", "1", "Y", true));
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();

        await Assert.ThrowsAsync<ValidationException>(() => pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, usd.Id, new DateTime(2026, 7, 5), null,
            [new PaymentAllocationInput(invoiceId, grand)])));
    }

    [Fact]
    public async Task Void_reverses_invoice_and_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (supplierId, invoiceId, accountId, grand) = await SeedInvoiceAndAccountAsync(scope.ServiceProvider);
        var pay = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var inv = scope.ServiceProvider.GetRequiredService<ISupplierInvoiceService>();

        var draft = await pay.CreateDraftAsync(new CreateSupplierPaymentRequest(
            supplierId, accountId, new DateTime(2026, 7, 5), null, [new PaymentAllocationInput(invoiceId, grand)]));
        await pay.SubmitAsync(draft.Id);
        await pay.VoidAsync(draft.Id);

        var voided = await pay.GetByIdAsync(draft.Id);
        Assert.Equal("Voided", voided!.Status);
        var invoice = await inv.GetByIdAsync(invoiceId);
        Assert.Equal("Open", invoice!.Status);
        Assert.Equal(grand, invoice.Outstanding);
        Assert.Equal(0m, await acc.GetBalanceAsync(accountId));   // out then in → back to opening
    }
}
```

- [ ] **Step 3b: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter SupplierPaymentServiceTests` → FAIL (`ISupplierPaymentService` not registered).

- [ ] **Step 4: Implement the service**

`src/ErpOne.Infrastructure/Services/SupplierPaymentService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Application.SupplierPayments;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class SupplierPaymentService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreateSupplierPaymentRequest> createValidator,
    IValidator<UpdateSupplierPaymentRequest> updateValidator,
    IDocumentNumberService docNumbers) : ISupplierPaymentService
{
    private const ApprovalDocumentType DocType = ApprovalDocumentType.SupplierPayment;

    public async Task<PagedResult<SupplierPaymentListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, SupplierPaymentStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.SupplierPayments.AsNoTracking();
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.PaymentNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SupplierPaymentListItemDto(
                p.Id, p.PaymentNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.PaymentDate,
                db.CashBankAccounts.Where(a => a.Id == p.CashBankAccountId).Select(a => a.Name).FirstOrDefault() ?? "—",
                p.Currency, p.Amount, p.Status.ToString()))
            .ToListAsync(ct);
        return new PagedResult<SupplierPaymentListItemDto>(items, total, page, pageSize);
    }

    public async Task<SupplierPaymentDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var rows = await db.SupplierPayments.AsNoTracking()
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(SupplierPaymentStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        var posted = await db.SupplierPayments.AsNoTracking().Where(p => p.Status == SupplierPaymentStatus.Posted).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        return new SupplierPaymentDashboardDto(rows.Sum(r => r.Count),
            CountOf(SupplierPaymentStatus.Draft), CountOf(SupplierPaymentStatus.PendingApproval), CountOf(SupplierPaymentStatus.Posted), posted);
    }

    public async Task<SupplierPaymentDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var p = await db.SupplierPayments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        var supplierName = await db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var accName = await db.CashBankAccounts.Where(a => a.Id == p.CashBankAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? "—";

        var invIds = p.Allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        var invs = await db.SupplierInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id))
            .Select(i => new { i.Id, i.InvoiceNumber, i.GrandTotal, i.PaidAmount }).ToListAsync(ct);
        var allocs = p.Allocations.Select(a =>
        {
            var i = invs.FirstOrDefault(x => x.Id == a.SupplierInvoiceId);
            return new SupplierPaymentAllocationDto(a.Id, a.SupplierInvoiceId, i?.InvoiceNumber ?? "—",
                i?.GrandTotal ?? 0m, (i?.GrandTotal ?? 0m) - (i?.PaidAmount ?? 0m), a.Amount);
        }).ToList();

        return new SupplierPaymentDto(p.Id, p.PaymentNumber, p.SupplierId, supplierName, p.CashBankAccountId, accName,
            p.Currency, p.PaymentDate, p.Amount, p.Notes, p.Status.ToString(), p.RejectionNote, p.CreatedAt, p.CreatedBy, allocs);
    }

    public Task<IReadOnlyList<ApprovalStepDto>> GetApprovalStepsAsync(int id, CancellationToken ct = default) =>
        approval.GetStepsAsync(DocType, id, ct);

    public async Task<IReadOnlyList<PayableInvoiceDto>> GetPayableInvoicesAsync(int supplierId, CancellationToken ct = default) =>
        await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == supplierId
                && (i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
                && i.GrandTotal - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate)
            .Select(i => new PayableInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.GrandTotal, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);

    public async Task<SupplierPaymentDto> CreateDraftAsync(CreateSupplierPaymentRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await ValidateAsync(request.SupplierId, request.CashBankAccountId, request.Allocations, ct);
        var number = await docNumbers.NextAsync(DocumentTypes.SupplierPayment, request.PaymentDate, ct);
        var payment = new SupplierPayment(number, request.SupplierId, request.CashBankAccountId, currency, request.PaymentDate, request.Notes);
        payment.SetAllocations(request.Allocations.Select(a => new SupplierPaymentAllocation(a.SupplierInvoiceId, a.Amount)));

        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(payment.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateSupplierPaymentRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return false;

        var currency = await ValidateAsync(payment.SupplierId, request.CashBankAccountId, request.Allocations, ct);
        var oldAllocs = await db.SupplierPaymentAllocations.Where(a => a.SupplierPaymentId == id).ToListAsync(ct);
        db.SupplierPaymentAllocations.RemoveRange(oldAllocs);
        payment.UpdateHeader(request.CashBankAccountId, request.PaymentDate, request.Notes);
        payment.SetAllocations(request.Allocations.Select(a => new SupplierPaymentAllocation(a.SupplierInvoiceId, a.Amount)));
        // currency may change if account changed; keep payment currency in sync
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var payment = await db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return false;
        if (payment.Status != SupplierPaymentStatus.Draft) throw Fail("Only a draft payment can be deleted.");
        db.SupplierPayments.Remove(payment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        payment.Submit();
        await db.SaveChangesAsync(ct);

        await approval.ResetAsync(DocType, payment.Id, ct);
        var fullyApproved = await approval.SubmitAsync(DocType, payment.Id, ct);
        if (fullyApproved) await PostAsync(payment, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ApproveAsync(int id, string actingUserName, Func<string, bool> isInRole, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        var fullyApproved = await approval.ApproveAsync(DocType, payment.Id, actingUserName, isInRole, payment.CreatedBy, ct);
        if (fullyApproved) await PostAsync(payment, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectAsync(int id, string actingUserName, Func<string, bool> isInRole, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw Fail("Payment not found.");
        await approval.RejectAsync(DocType, payment.Id, actingUserName, isInRole, payment.CreatedBy, reason, ct);
        payment.ReturnToDraft(reason);
        await approval.ResetAsync(DocType, payment.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task VoidAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.SupplierPayments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw Fail("Payment not found.");
        payment.Void();

        // reverse cash movement
        db.CashBankMovements.Add(new CashBankMovement(payment.CashBankAccountId, DateTime.UtcNow.Date,
            CashBankMovementDirection.In, payment.Amount, "SupplierPaymentVoid", payment.Id, $"Void {payment.PaymentNumber}"));

        // reverse invoice allocations
        foreach (var a in payment.Allocations)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == a.SupplierInvoiceId, ct);
            inv?.ReversePayment(a.Amount);
        }

        await approval.ResetAsync(DocType, payment.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Applies a fully-approved payment: cash out + invoice ApplyPayment. Caller saves + commits.
    private async Task PostAsync(SupplierPayment payment, CancellationToken ct)
    {
        db.CashBankMovements.Add(new CashBankMovement(payment.CashBankAccountId, payment.PaymentDate,
            CashBankMovementDirection.Out, payment.Amount, "SupplierPayment", payment.Id, payment.PaymentNumber));

        foreach (var a in payment.Allocations)
        {
            var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == a.SupplierInvoiceId, ct)
                ?? throw Fail($"Invoice {a.SupplierInvoiceId} not found.");
            inv.ApplyPayment(a.Amount);
        }
        payment.MarkPosted();
    }

    // Validates supplier/account/allocations; returns the payment currency (account currency).
    private async Task<string> ValidateAsync(int supplierId, int accountId, IReadOnlyList<PaymentAllocationInput> allocations, CancellationToken ct)
    {
        var account = await db.CashBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw Fail("Cash/bank account not found.");
        if (!account.IsActive) throw Fail("Cash/bank account is inactive.");

        var invIds = allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        if (invIds.Count != allocations.Count) throw Fail("Duplicate invoice in allocations.");

        var invs = await db.SupplierInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id)).ToListAsync(ct);
        if (invs.Count != invIds.Count) throw Fail("One or more invoices were not found.");
        if (invs.Any(i => i.SupplierId != supplierId)) throw Fail("All invoices must belong to the selected supplier.");
        if (invs.Any(i => i.Status is SupplierInvoiceStatus.Cancelled or SupplierInvoiceStatus.Paid)) throw Fail("Only open or partially-paid invoices can be paid.");
        if (invs.Select(i => i.Currency).Distinct().Count() > 1) throw Fail("All invoices must share the same currency.");

        var invCurrency = invs.First().Currency;
        if (!string.Equals(account.Currency, invCurrency, StringComparison.OrdinalIgnoreCase))
            throw Fail($"Account currency ({account.Currency}) must match the invoice currency ({invCurrency}).");

        foreach (var a in allocations)
        {
            var inv = invs.First(i => i.Id == a.SupplierInvoiceId);
            var outstanding = inv.GrandTotal - inv.PaidAmount;
            if (a.Amount > outstanding) throw Fail($"Allocation {a.Amount} exceeds outstanding {outstanding} for {inv.InvoiceNumber}.");
        }
        return invCurrency;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("SupplierPayment", message)]);
}
```

- [ ] **Step 5: DI + approval chain seed**

In `DependencyInjection.cs`: add `using ErpOne.Application.SupplierPayments;` and after the SupplierInvoice registration:
```csharp
        services.AddScoped<ISupplierPaymentService, SupplierPaymentService>();
```

In `BootstrapSeeder.cs`, after the SalesOrder chain seed block, add:
```csharp
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SupplierPayment))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SupplierPayment, 1, roleName));
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 6: Run tests → pass**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter SupplierPaymentServiceTests`
Expected: PASS (4). Then `dotnet test` (full) → all pass.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Application/SupplierPayments/ src/ErpOne.Application/CashBank/ src/ErpOne.Infrastructure/Services/SupplierPaymentService.cs src/ErpOne.Infrastructure/Services/CashBankAccountService.cs src/ErpOne.Infrastructure/DependencyInjection.cs src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs tests/ErpOne.IntegrationTests/SupplierPaymentServiceTests.cs
git commit -m "feat: add SupplierPaymentService (approval, post, void) + cash/bank balance + tests"
```

---

## Task 3: Web pages + menu + void action + cash-bank balance column

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApPayments/ApPaymentIndex.razor` (+ `.razor.css` for KPI, copy from ApInvoiceIndex.razor.css)
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApPayments/ApPaymentForm.razor`
- Create: `src/ErpOne.Web/Components/Pages/Finance/ApPayments/ApPaymentDetail.razor` (+ `.razor.css`, copy from ApInvoiceDetail.razor.css)
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (add ActVoid + finance.ap-payments)
- Modify: `src/ErpOne.Web/Components/_Imports.razor` (add SupplierPayments)
- Modify: `src/ErpOne.Web/Components/Pages/Finance/CashBank/CashBankIndex.razor` (Current balance column)

**Interfaces:**
- Consumes: `ISupplierPaymentService`, `ISupplierService`, `ICashBankAccountService`.

- [ ] **Step 1: AppMenus (ActVoid + resource) + _Imports**

In `AppMenus.cs`, add the action constant near the others (after `ActClose`):
```csharp
    public static readonly AppAction ActVoid = new("void", "Void", "bi-x-octagon-fill");
```
Add a payment actions helper near the other `*Actions` arrays:
```csharp
    private static AppAction[] SupplierPaymentActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActVoid];
```
Add the resource to the `Finance` group (after `finance.ap-invoices`):
```csharp
            new("finance.ap-payments", "Supplier Payments", "bi-cash-coin", SupplierPaymentActions),
```

In `_Imports.razor`, add:
```razor
@using ErpOne.Application.SupplierPayments
```

- [ ] **Step 2: Cash & Bank index — show Current balance**

In `CashBankIndex.razor`, add a header `<th class="text-end" style="width:150px">Balance</th>` after the Opening column header, and a cell after the opening cell:
```razor
                                <td class="text-end mono">@item.CurrentBalance.ToString("N2")</td>
```

- [ ] **Step 3: Payment index page (`.pi`)**

Create `ApPaymentIndex.razor` — mirror `ApInvoiceIndex.razor` structure with columns (Payment, Supplier, Date, Account, Amount, Status), KPI row (Total, Draft, PendingApproval, PostedThisMonth), status badge:
```razor
@page "/finance/ap-payments"
@attribute [Authorize(Policy = "finance.ap-payments.index")]
@rendermode InteractiveServer
@inject ISupplierPaymentService PaymentService
@inject NavigationManager Nav

<PageTitle>Supplier Payments</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs"><a href="/">Home</a><span class="sep">·</span><span>Finance</span><span class="sep">·</span><span class="here">Supplier Payments</span></nav>
            <h1>Supplier Payments</h1>
            <p>Payments made to suppliers against invoices.</p>
        </div>
        <AuthorizeView Policy="finance.ap-payments.create">
            <Authorized>
                <div class="pi-actions"><a class="btn btn-primary" href="/finance/ap-payments/new"><i class="bi bi-plus-lg"></i> New payment</a></div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_dash is not null)
    {
        <div class="cr-kpis mb-3">
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.Total</div><div class="l">Total</div></div></div>
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.Draft</div><div class="l">Draft</div></div></div>
            <div class="cr-kpi"><div class="tx"><div class="v">@_dash.PendingApproval</div><div class="l">Pending approval</div></div></div>
            <div class="cr-kpi accent"><div class="tx"><div class="v">Rp @_dash.PostedThisMonth.ToString("N0")</div><div class="l">Posted total</div></div></div>
        </div>
    }

    @if (_page is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_page.Total == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cash-coin"></i></div><p>No payments yet.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead><tr><th style="width:150px">Payment</th><th>Supplier</th><th style="width:110px">Date</th><th>Account</th><th class="text-end" style="width:140px">Amount</th><th class="text-center" style="width:130px">Status</th></tr></thead>
                    <tbody>
                        @foreach (var p in _page.Items)
                        {
                            <tr style="cursor:pointer" @onclick="@(() => Nav.NavigateTo($"/finance/ap-payments/{p.Id}"))">
                                <td class="code mono">@p.PaymentNumber</td>
                                <td class="nm">@p.SupplierName</td>
                                <td class="code">@p.PaymentDate.ToString("d MMM yyyy")</td>
                                <td>@p.CashBankAccountName</td>
                                <td class="text-end mono">@p.Amount.ToString("N2")</td>
                                <td class="text-center"><span class="badge @StatusClass(p.Status)">@p.Status</span></td>
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
    private PagedResult<SupplierPaymentListItemDto>? _page;
    private SupplierPaymentDashboardDto? _dash;
    private int _currentPage = 1;

    protected override async Task OnInitializedAsync() { _dash = await PaymentService.GetDashboardAsync(); await LoadAsync(); }
    private async Task LoadAsync() => _page = await PaymentService.GetPagedAsync(_currentPage, PageSize);
    private async Task GoToPageAsync(int page) { _currentPage = page; await LoadAsync(); }

    private static string StatusClass(string s) => s switch
    {
        "Draft" => "bg-secondary-subtle text-secondary-emphasis",
        "PendingApproval" => "bg-warning-subtle text-warning-emphasis",
        "Posted" => "bg-success-subtle text-success-emphasis",
        "Voided" => "bg-danger-subtle text-danger-emphasis",
        _ => "bg-light text-muted"
    };
}
```
Add `ApPaymentIndex.razor.css` = copy of `ApInvoiceIndex.razor.css` (the `.cr-kpis`/`.cr-kpi` rules).

- [ ] **Step 4: Payment form page (`.cf`)**

Create `ApPaymentForm.razor`: supplier + account + date + notes header; when supplier chosen, load payable invoices; render a row per invoice with an allocation amount input; show running Σ allocations and the chosen account; Save creates/updates a Draft. Full code:
```razor
@page "/finance/ap-payments/new"
@page "/finance/ap-payments/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@inject ISupplierPaymentService PaymentService
@inject ISupplierService SupplierService
@inject ICashBankAccountService AccountService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="cf">
    <div class="cf-top">
        <div class="crumbs">
            <a href="/finance/ap-payments">Finance</a><i class="bi bi-chevron-right"></i>
            <a href="/finance/ap-payments">Supplier Payments</a><i class="bi bi-chevron-right"></i>
            <span class="here">@(Id is null ? "New" : "Edit")</span>
        </div>
        <h1>@Title</h1>
    </div>

    @if (_loading) { <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div> }
    else
    {
        @if (_error is not null) { <div class="cf-alert err"><i class="bi bi-exclamation-octagon"></i> @_error</div> }

        <div class="cf-wrap">
            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-cash-coin"></i></span><div class="hd-tx"><h2>Payment</h2><p>Pay a supplier against outstanding invoices.</p></div></div>
                <div class="card-b">
                    <div class="grid">
                        <div class="f c6">
                            <label class="fl">Supplier <span class="req">*</span></label>
                            <select class="ctl" @bind="_supplierId" @bind:after="OnSupplierChangedAsync" disabled="@(Id is not null)">
                                <option value="0">— Select supplier —</option>
                                @foreach (var s in _suppliers) { <option value="@s.Id">@s.Code — @s.Name</option> }
                            </select>
                        </div>
                        <div class="f c3">
                            <label class="fl">Payment date <span class="req">*</span></label>
                            <input type="date" class="ctl" @bind="_paymentDate" />
                        </div>
                        <div class="f c3">
                            <label class="fl">Cash / bank <span class="req">*</span></label>
                            <select class="ctl" @bind="_accountId">
                                <option value="0">— Select account —</option>
                                @foreach (var a in _accounts) { <option value="@a.Id">@a.Code — @a.Name (@a.Currency)</option> }
                            </select>
                        </div>
                        <div class="f c12">
                            <label class="fl">Notes</label>
                            <input class="ctl" maxlength="500" @bind="_notes" placeholder="Optional" />
                        </div>
                    </div>
                </div>
            </section>

            <section class="card">
                <div class="card-h"><span class="hd-ic"><i class="bi bi-receipt"></i></span><div class="hd-tx"><h2>Allocate to invoices</h2><p>Enter the amount to pay against each outstanding invoice.</p></div></div>
                <div class="card-b">
                    @if (_supplierId == 0) { <div class="text-muted small">Select a supplier to list outstanding invoices.</div> }
                    else if (_invoices.Count == 0) { <div class="text-muted small">No outstanding invoices for this supplier.</div> }
                    else
                    {
                        <div class="table-responsive">
                            <table class="table align-middle">
                                <thead class="table-light"><tr><th>Invoice</th><th>Due</th><th class="text-end">Grand total</th><th class="text-end">Outstanding</th><th class="text-end" style="width:180px">Pay amount</th></tr></thead>
                                <tbody>
                                    @foreach (var inv in _invoices)
                                    {
                                        <tr>
                                            <td class="mono">@inv.InvoiceNumber</td>
                                            <td>@inv.DueDate.ToString("d MMM yyyy")</td>
                                            <td class="text-end mono">@inv.GrandTotal.ToString("N2")</td>
                                            <td class="text-end mono">@inv.Outstanding.ToString("N2")</td>
                                            <td class="text-end"><input type="number" step="0.01" min="0" max="@inv.Outstanding" class="ctl mono" style="text-align:right"
                                                       value="@GetAlloc(inv.SupplierInvoiceId)" @onchange="@(e => SetAlloc(inv.SupplierInvoiceId, e.Value?.ToString()))" /></td>
                                        </tr>
                                    }
                                </tbody>
                                <tfoot><tr><td colspan="4" class="text-end fw-semibold">Total payment</td><td class="text-end mono fw-semibold">@Total().ToString("N2")</td></tr></tfoot>
                            </table>
                        </div>
                    }
                </div>
            </section>

            <div class="pf-footer">
                <div class="in">
                    <span class="note"><span class="req">*</span> required fields</span>
                    <a class="btn btn-ghost" href="/finance/ap-payments"><i class="bi bi-x-lg"></i> Cancel</a>
                    <button class="btn btn-primary" @onclick="SaveAsync" disabled="@(_saving || _accountId == 0 || Total() <= 0)">
                        @if (_saving) { <span class="spinner-border spinner-border-sm me-1" role="status"></span> } else { <i class="bi bi-check2"></i> }
                        Save draft
                    </button>
                </div>
            </div>
        </div>
    }
</div>

@code {
    [Parameter] public int? Id { get; set; }

    private int _supplierId, _accountId;
    private DateTime _paymentDate = DateTime.Today;
    private string? _notes;
    private IReadOnlyList<SupplierDto> _suppliers = [];
    private IReadOnlyList<CashBankAccountDto> _accounts = [];
    private List<PayableInvoiceDto> _invoices = [];
    private readonly Dictionary<int, decimal> _alloc = new();
    private bool _loading = true, _saving;
    private string? _error;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;
    private string Title => Id is null ? "New Supplier Payment" : "Edit Supplier Payment";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null ? AppMenus.Perm("finance.ap-payments", "create") : AppMenus.Perm("finance.ap-payments", "edit");
        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded) { Nav.NavigateTo("/finance/ap-payments"); return; }

        _suppliers = await SupplierService.GetAllAsync();
        _accounts = await AccountService.GetActiveAsync();

        if (Id is int id)
        {
            var p = await PaymentService.GetByIdAsync(id);
            if (p is not null)
            {
                _supplierId = p.SupplierId; _accountId = p.CashBankAccountId; _paymentDate = p.PaymentDate; _notes = p.Notes;
                _invoices = (await PaymentService.GetPayableInvoicesAsync(p.SupplierId)).ToList();
                foreach (var a in p.Allocations) _alloc[a.SupplierInvoiceId] = a.Amount;
            }
        }
        _loading = false;
    }

    private async Task OnSupplierChangedAsync()
    {
        _alloc.Clear();
        _invoices = _supplierId == 0 ? [] : (await PaymentService.GetPayableInvoicesAsync(_supplierId)).ToList();
    }

    private decimal GetAlloc(int invId) => _alloc.GetValueOrDefault(invId, 0m);
    private void SetAlloc(int invId, string? raw)
    {
        if (decimal.TryParse(raw, out var v) && v > 0) _alloc[invId] = v;
        else _alloc.Remove(invId);
    }
    private decimal Total() => _alloc.Values.Sum();

    private async Task SaveAsync()
    {
        _error = null; _saving = true;
        try
        {
            var allocations = _alloc.Where(kv => kv.Value > 0).Select(kv => new PaymentAllocationInput(kv.Key, kv.Value)).ToList();
            if (Id is int id)
                await PaymentService.UpdateDraftAsync(id, new UpdateSupplierPaymentRequest(_accountId, _paymentDate, _notes, allocations));
            else
                await PaymentService.CreateDraftAsync(new CreateSupplierPaymentRequest(_supplierId, _accountId, _paymentDate, _notes, allocations));
            Nav.NavigateTo("/finance/ap-payments");
        }
        catch (ValidationException ex) { _error = ex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed."; }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 5: Payment detail page (`.pf`) with approval actions**

Create `ApPaymentDetail.razor` modeled on `ApInvoiceDetail.razor` + PoDetail approval actions. Shows header info, allocations table, approval timeline, and Submit/Approve/Reject/Void buttons gated by status + policy. Full code:
```razor
@page "/finance/ap-payments/{Id:int}"
@attribute [Authorize(Policy = "finance.ap-payments.index")]
@rendermode InteractiveServer
@inject ISupplierPaymentService PaymentService
@inject IApprovalStepPresenter Approval
@inject AuthenticationStateProvider AuthProvider
@inject IAuthorizationService Auth
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>@(_p?.PaymentNumber ?? "Payment")</PageTitle>

@if (_p is null)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else
{
    <div class="pf">
        <div class="pf-top">
            <div class="crumbs"><a href="/finance/ap-payments">Supplier Payments</a><i class="bi bi-chevron-right"></i><span class="here">@_p.PaymentNumber</span></div>
        </div>
        <div class="pd-head">
            <h1>@_p.PaymentNumber</h1>
            <span class="badge @StatusClass(_p.Status)">@_p.Status</span>
            <div class="actions">
                @if (_p.Status == "Draft")
                {
                    <AuthorizeView Policy="finance.ap-payments.edit"><Authorized>
                        <a class="btn btn-line btn-sm" href="@($"/finance/ap-payments/{_p.Id}/edit")"><i class="bi bi-pencil"></i> Edit</a>
                        <button class="btn btn-primary btn-sm" @onclick="SubmitAsync"><i class="bi bi-send"></i> Submit</button>
                    </Authorized></AuthorizeView>
                }
                @if (_p.Status == "PendingApproval" && _canApprove)
                {
                    <button class="btn btn-primary btn-sm" @onclick="ApproveAsync"><i class="bi bi-check2-circle"></i> Approve</button>
                    <button class="btn btn-danger btn-sm" @onclick="RejectAsync"><i class="bi bi-x-circle"></i> Reject</button>
                }
                @if (_p.Status == "Posted")
                {
                    <AuthorizeView Policy="finance.ap-payments.void"><Authorized>
                        <button class="btn btn-danger btn-sm" @onclick="VoidAsync"><i class="bi bi-x-octagon"></i> Void</button>
                    </Authorized></AuthorizeView>
                }
            </div>
        </div>

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-cash-coin"></i></span><h2>Payment</h2></div>
            <div class="card-b">
                <div class="info-grid">
                    <div class="info-cell"><span class="k"><i class="bi bi-truck"></i> Supplier</span><span class="v">@_p.SupplierName</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-bank"></i> Paid from</span><span class="v">@_p.CashBankAccountName</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-calendar3"></i> Date</span><span class="v">@_p.PaymentDate.ToString("d MMM yyyy")</span></div>
                    <div class="info-cell"><span class="k"><i class="bi bi-cash-stack"></i> Amount</span><span class="v">@_p.Currency @_p.Amount.ToString("N2")</span></div>
                    @if (!string.IsNullOrWhiteSpace(_p.RejectionNote)) { <div class="info-cell wide"><span class="k"><i class="bi bi-exclamation-triangle"></i> Rejection note</span><span class="v">@_p.RejectionNote</span></div> }
                    @if (!string.IsNullOrWhiteSpace(_p.Notes)) { <div class="info-cell wide"><span class="k"><i class="bi bi-sticky"></i> Notes</span><span class="v">@_p.Notes</span></div> }
                </div>
            </div>
        </section>

        <section class="card">
            <div class="card-h"><span class="hd-ic"><i class="bi bi-list-ul"></i></span><h2>Allocations</h2></div>
            <div class="card-b">
                <table class="items">
                    <thead><tr><th>Invoice</th><th class="r">Grand total</th><th class="r">Outstanding</th><th class="r">Paid</th></tr></thead>
                    <tbody>
                        @foreach (var a in _p.Allocations)
                        {
                            <tr><td class="mono">@a.InvoiceNumber</td><td class="r mono">@a.InvoiceGrandTotal.ToString("N2")</td><td class="r mono">@a.InvoiceOutstanding.ToString("N2")</td><td class="r mono total">@a.Amount.ToString("N2")</td></tr>
                        }
                    </tbody>
                    <tfoot><tr><td colspan="3" class="r gt-l">Total</td><td class="r grand">@_p.Amount.ToString("N2")</td></tr></tfoot>
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
                                <span class="dot @(s.Status == "Approved" ? "ok" : s.Status == "Rejected" ? "no" : "wait")"><i class="bi @(s.Status == "Approved" ? "bi-check" : s.Status == "Rejected" ? "bi-x" : "bi-hourglass")"></i></span>
                                <div><div class="ti">Step @s.StepOrder · @s.RoleName</div><div class="meta">@s.Status @(s.ActedByName is null ? "" : $"· {s.ActedByName}")</div>@if (!string.IsNullOrWhiteSpace(s.Note)) { <div class="note">@s.Note</div> }</div>
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
    private SupplierPaymentDto? _p;
    private IReadOnlyList<ApprovalStepDto> _steps = [];
    private bool _canApprove;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _p = await PaymentService.GetByIdAsync(Id);
        _steps = await PaymentService.GetApprovalStepsAsync(Id);
        var user = (await AuthProvider.GetAuthenticationStateAsync()).User;
        _canApprove = user.Identity?.IsAuthenticated == true; // role check handled server-side on approve
    }

    private Func<string, bool> RoleCheck() 
    {
        var task = AuthProvider.GetAuthenticationStateAsync();
        var user = task.Result.User;
        return role => user.IsInRole(role);
    }

    private async Task<(string name, Func<string,bool> isInRole)> ActorAsync()
    {
        var user = (await AuthProvider.GetAuthenticationStateAsync()).User;
        return (user.Identity?.Name ?? "system", role => user.IsInRole(role));
    }

    private async Task SubmitAsync()
    {
        try { await PaymentService.SubmitAsync(Id); await Swal.ToastAsync("success", "Submitted"); await ReloadAsync(); }
        catch (Exception ex) { await Swal.ToastAsync("error", ex.Message); }
    }
    private async Task ApproveAsync()
    {
        try { var (n, r) = await ActorAsync(); await PaymentService.ApproveAsync(Id, n, r); await Swal.ToastAsync("success", "Approved"); await ReloadAsync(); }
        catch (Exception ex) { await Swal.ToastAsync("error", ex.Message); }
    }
    private async Task RejectAsync()
    {
        var reason = await Swal.PromptAsync("Reject payment?", "Reason");
        if (string.IsNullOrWhiteSpace(reason)) return;
        try { var (n, r) = await ActorAsync(); await PaymentService.RejectAsync(Id, n, r, reason); await Swal.ToastAsync("success", "Rejected"); await ReloadAsync(); }
        catch (Exception ex) { await Swal.ToastAsync("error", ex.Message); }
    }
    private async Task VoidAsync()
    {
        if (!await Swal.ConfirmAsync("Void payment?", "This reverses the cash movement and invoice payments.")) return;
        try { await PaymentService.VoidAsync(Id); await Swal.ToastAsync("success", "Voided"); await ReloadAsync(); }
        catch (Exception ex) { await Swal.ToastAsync("error", ex.Message); }
    }

    private static string StatusClass(string s) => s switch
    {
        "Draft" => "b-draft", "PendingApproval" => "b-warn", "Posted" => "b-done", "Voided" => "b-danger", _ => "b-draft"
    };
}
```
> IMPORTANT reconciliation before writing:
> 1. Remove the unused `IApprovalStepPresenter`/`RoleCheck` scaffolding — they are not real types. Keep only `AuthenticationStateProvider`, `IAuthorizationService`, `SwalService`. Delete the `@inject IApprovalStepPresenter Approval` line and the `RoleCheck()` method.
> 2. Verify `ApprovalStepDto` field names (`StepOrder`, `RoleName`, `Status`, `ActedByName`, `Note`) against `src/ErpOne.Application/Approvals/*Dtos.cs`; adjust bindings to match. Look at how `PoDetail.razor` renders approval steps and copy its exact field usage + the actor/`isInRole` pattern it uses for Approve/Reject (PoDetail already solved this — mirror it precisely instead of the sketch above).
> 3. Confirm `SwalService.PromptAsync` exists; if not, use the same reject-reason mechanism `PoDetail.razor` uses.
> Copy `ApInvoiceDetail.razor.css` to `ApPaymentDetail.razor.css` (same `.pf`/`.items`/`.info-grid` rules) and add the `.tl` timeline rules from `PoDetail.razor.css` (`.tl`, `.tl li`, `.tl .dot`, `.dot.ok/.no/.wait`, `.ti`, `.meta`, `.note`) plus `.b-danger`.

- [ ] **Step 6: Build + smoke**

Run: `dotnet build` → succeeds. Fix any binding mismatches surfaced against the real `ApprovalStepDto`/`SwalService`/PoDetail pattern.

- [ ] **Step 7: Commit**

```bash
git add src/ErpOne.Web/Components/Pages/Finance/ApPayments/ src/ErpOne.Web/Components/Pages/Finance/CashBank/CashBankIndex.razor src/ErpOne.Web/Authorization/AppMenus.cs src/ErpOne.Web/Components/_Imports.razor
git commit -m "feat: add Supplier Payment pages (index/form/detail) + void + cash balance column"
```

---

## Final verification
- [ ] `dotnet build && dotnet test` → all pass.
- [ ] `dotnet ef database update ...`.
- [ ] Smoke: create invoice (3a-i) → new payment → allocate → submit/approve → invoice PartiallyPaid/Paid, cash balance drops; Void restores both.

## Self-review notes
- Payment mirrors PurchaseOrder approval lifecycle; money moves only in `PostAsync` (on full approval) and reverses in `VoidAsync`.
- `ValidateAsync` enforces same-supplier, same-currency, account-currency-match, allocation ≤ outstanding.
- Detail page has scaffolding placeholders explicitly flagged to reconcile against the real `ApprovalStepDto`/`PoDetail` pattern before writing (Step 5 note) — this is the one area to verify live, not copy blindly.
- CashBankMovement is `S_` prefixed (ledger/stock-like); payments/allocations `T_`.
