# D1 — Sesi Kasir (Cashier Shift) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bangun subsistem Sesi Kasir — kasir buka shift dgn saldo awal kas, akumulasi total penjualan per metode selama shift, lalu tutup shift dgn hitung kas fisik vs sistem (selisih) — sebagai fondasi Tahap D (Kasir/POS) sebelum layar POS (D2).

**Architecture:** Clean Architecture (Domain → Application → Infrastructure → Web/Blazor Server), mengikuti pola B/C. Agregat `CashierShift` menyimpan akumulator lewat method domain `RecordSale(...)` yang nanti dipanggil D2, sehingga D1 bisa dibangun & diuji penuh tanpa entitas `PosSale`. Rekonsiliasi menghitung `ExpectedCash = OpeningFloat + CashSalesTotal` dan `CashVariance = CountedCash − ExpectedCash`.

**Tech Stack:** .NET 10, C#, EF Core (SQL Server), Blazor Server + Bootstrap 5 + Bootstrap Icons, FluentValidation, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-02-d1-cashier-shift-design.md`

## Global Constraints

- **No VCS:** proyek ini TIDAK memakai git (lihat ledger C1/C2). Ganti tiap langkah "Commit" dgn verifikasi build+test. Catat progres di `docs/superpowers/plans/d1-progress.md` (pola sama seperti `c2-progress.md`).
- **Baseline saat mulai:** solution build **0 warnings** (`TreatWarningsAsErrors=true` di `Directory.Build.props`), full suite **111 unit + 72 integ = 183 pass**. Karena `TreatWarningsAsErrors`, gunakan `dotnet build -p:NuGetAudit=false` HANYA bila advisory NU1903 masih menghalangi restore; jika `Microsoft.OpenApi 2.7.5` sudah ter-pin (C2), `dotnet build` normal harus 0 warnings.
- **Enum sebagai string:** `.HasConversion<string>().HasMaxLength(20)`.
- **Uang/cost:** `decimal` dipetakan `.HasPrecision(18, 2)`. Pembulatan `Math.Round(v, 2, MidpointRounding.AwayFromZero)` (tidak dibutuhkan di D1 karena tak ada perkalian, tapi patuhi bila menambah perhitungan).
- **Service melempar** `FluentValidation.ValidationException` untuk error validasi/aturan (helper `Fail(...)` pola `DeliveryOrderService`).
- **Otorisasi:** resource+action di `AppMenus.cs`; policy `@attribute [Authorize(Policy="cashier.shifts.<action>")]` / `<AuthorizeView Policy=...>`. Admin auto-grant via `AllPermissions`/`BootstrapSeeder`.
- **Nomor shift:** `SHIFT-YYYYMMDD-####` (harian), pola `GenerateNumberAsync` DO.
- **Current user:** halaman Blazor menyuplai `userId` (claim `ClaimTypes.NameIdentifier`) DAN `userName` (`Identity?.Name`) ke service (pola `SoDetail` yang menyuplai `_user.Identity?.Name`). `ICurrentUser` hanya punya `UserName`, jangan diandalkan untuk id.
- **Aturan 1-shift-terbuka/user:** divalidasi di service + pengaman DB filtered unique index `WHERE [Status] = 'Open'` pada `CashierUserId`.

---

### Task 1: Domain — `CashierShiftStatus`, `CashierShiftTotal`, `CashierShift`

**Files:**
- Create: `src/MyApp.Domain/Entities/CashierShiftStatus.cs`
- Create: `src/MyApp.Domain/Entities/CashierShiftTotal.cs`
- Create: `src/MyApp.Domain/Entities/CashierShift.cs`
- Test: `tests/MyApp.UnitTests/CashierShiftTests.cs`

**Interfaces:**
- Produces:
  - `enum CashierShiftStatus { Open, Closed }`
  - `CashierShiftTotal(int paymentMethodId)` ctor; props `Id`, `CashierShiftId`, `PaymentMethodId`, `TotalAmount`, `TransactionCount`; method `Add(decimal amount)`.
  - `CashierShift(string shiftNumber, int warehouseId, string cashierUserId, string cashierName, decimal openingFloat, DateTime openedAt)`; props `Id, ShiftNumber, WarehouseId, CashierUserId, CashierName, Status, OpenedAt, OpeningFloat, CashSalesTotal, ClosedAt, CountedCash, CashVariance, ClosingNote`; readonly nav `Totals`; computed `ExpectedCash, TotalSalesAmount, TransactionCount`; methods `RecordSale(int paymentMethodId, bool isCash, decimal amount)`, `Close(decimal countedCash, string? note, DateTime closedAt)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/MyApp.UnitTests/CashierShiftTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class CashierShiftTests
{
    private static CashierShift Open(decimal float_ = 100_000m) =>
        new("SHIFT-20260702-0001", warehouseId: 3, cashierUserId: "u-1",
            cashierName: "Rani", openingFloat: float_, openedAt: new DateTime(2026, 7, 2, 8, 0, 0));

    [Fact]
    public void New_shift_is_open_with_zero_sales()
    {
        var s = Open();
        Assert.Equal(CashierShiftStatus.Open, s.Status);
        Assert.Equal(100_000m, s.OpeningFloat);
        Assert.Equal(0m, s.CashSalesTotal);
        Assert.Equal(100_000m, s.ExpectedCash);
        Assert.Equal(0m, s.TotalSalesAmount);
        Assert.Equal(0, s.TransactionCount);
    }

    [Fact]
    public void Ctor_rejects_invalid_args()
    {
        Assert.Throws<ArgumentException>(() => new CashierShift("", 3, "u-1", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 0, "u-1", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "", "Rani", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "u-1", "", 0m, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new CashierShift("S", 3, "u-1", "Rani", -1m, DateTime.Now));
    }

    [Fact]
    public void RecordSale_accumulates_per_method_and_cash_only_into_cash_total()
    {
        var s = Open();
        s.RecordSale(paymentMethodId: 1, isCash: true, amount: 50_000m);
        s.RecordSale(paymentMethodId: 1, isCash: true, amount: 30_000m);
        s.RecordSale(paymentMethodId: 2, isCash: false, amount: 70_000m); // kartu

        Assert.Equal(80_000m, s.CashSalesTotal);               // hanya tunai
        Assert.Equal(180_000m, s.TotalSalesAmount);            // semua metode
        Assert.Equal(3, s.TransactionCount);
        Assert.Equal(180_000m, s.ExpectedCash - s.OpeningFloat + s.OpeningFloat - s.OpeningFloat + s.OpeningFloat is var _ ? s.OpeningFloat + s.CashSalesTotal : 0m); // ExpectedCash = float + cash
        Assert.Equal(100_000m + 80_000m, s.ExpectedCash);

        var cashTotal = Assert.Single(s.Totals, t => t.PaymentMethodId == 1);
        Assert.Equal(80_000m, cashTotal.TotalAmount);
        Assert.Equal(2, cashTotal.TransactionCount);
        var cardTotal = Assert.Single(s.Totals, t => t.PaymentMethodId == 2);
        Assert.Equal(70_000m, cardTotal.TotalAmount);
        Assert.Equal(1, cardTotal.TransactionCount);
    }

    [Fact]
    public void RecordSale_rejects_bad_args_and_when_closed()
    {
        var s = Open();
        Assert.Throws<ArgumentException>(() => s.RecordSale(0, true, 10m));
        Assert.Throws<ArgumentException>(() => s.RecordSale(1, true, 0m));
        Assert.Throws<ArgumentException>(() => s.RecordSale(1, true, -5m));
        s.Close(countedCash: 100_000m, note: null, closedAt: DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => s.RecordSale(1, true, 10m));
    }

    [Fact]
    public void Close_computes_variance_short_over_and_exact()
    {
        var s = Open(100_000m);
        s.RecordSale(1, true, 50_000m);           // ExpectedCash = 150_000
        s.Close(countedCash: 148_000m, note: "  kurang 2rb  ", closedAt: new DateTime(2026, 7, 2, 16, 0, 0));

        Assert.Equal(CashierShiftStatus.Closed, s.Status);
        Assert.Equal(148_000m, s.CountedCash);
        Assert.Equal(-2_000m, s.CashVariance);    // kurang
        Assert.Equal("kurang 2rb", s.ClosingNote); // di-trim
        Assert.Equal(new DateTime(2026, 7, 2, 16, 0, 0), s.ClosedAt);
    }

    [Fact]
    public void Close_rejects_negative_and_double_close()
    {
        var s = Open();
        Assert.Throws<ArgumentException>(() => s.Close(-1m, null, DateTime.Now));
        s.Close(100_000m, null, DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => s.Close(100_000m, null, DateTime.Now));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyApp.UnitTests --filter FullyQualifiedName~CashierShiftTests`
Expected: FAIL — `CashierShift`/`CashierShiftStatus`/`CashierShiftTotal` tidak ada (compile error).

- [ ] **Step 3: Create `CashierShiftStatus`**

`src/MyApp.Domain/Entities/CashierShiftStatus.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Siklus hidup sesi kasir: Open → Closed.</summary>
public enum CashierShiftStatus
{
    Open,
    Closed
}
```

- [ ] **Step 4: Create `CashierShiftTotal`**

`src/MyApp.Domain/Entities/CashierShiftTotal.cs`:

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Akumulasi total penjualan per metode pembayaran dalam satu shift.</summary>
public class CashierShiftTotal
{
    public int Id { get; private set; }
    public int CashierShiftId { get; private set; }
    public int PaymentMethodId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public int TransactionCount { get; private set; }

    private CashierShiftTotal() { } // EF Core

    public CashierShiftTotal(int paymentMethodId)
    {
        if (paymentMethodId <= 0)
            throw new ArgumentException("PaymentMethodId must be > 0.", nameof(paymentMethodId));
        PaymentMethodId = paymentMethodId;
    }

    public void Add(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        TotalAmount += amount;
        TransactionCount += 1;
    }
}
```

- [ ] **Step 5: Create `CashierShift`**

`src/MyApp.Domain/Entities/CashierShift.cs`:

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Sesi kasir: dibuka dgn saldo awal kas, mengakumulasi penjualan per metode, ditutup dgn rekonsiliasi kas.</summary>
public class CashierShift : AuditableEntity
{
    private readonly List<CashierShiftTotal> _totals = [];

    public int Id { get; private set; }
    public string ShiftNumber { get; private set; } = default!;
    public int WarehouseId { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;
    public CashierShiftStatus Status { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public decimal CashSalesTotal { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public decimal? CountedCash { get; private set; }
    public decimal? CashVariance { get; private set; }
    public string? ClosingNote { get; private set; }

    public IReadOnlyCollection<CashierShiftTotal> Totals => _totals;

    public decimal ExpectedCash => OpeningFloat + CashSalesTotal;
    public decimal TotalSalesAmount => _totals.Sum(t => t.TotalAmount);
    public int TransactionCount => _totals.Sum(t => t.TransactionCount);

    private CashierShift() { } // EF Core

    public CashierShift(string shiftNumber, int warehouseId, string cashierUserId,
        string cashierName, decimal openingFloat, DateTime openedAt)
    {
        if (string.IsNullOrWhiteSpace(shiftNumber))
            throw new ArgumentException("ShiftNumber is required.", nameof(shiftNumber));
        if (warehouseId <= 0)
            throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(cashierUserId))
            throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName))
            throw new ArgumentException("CashierName is required.", nameof(cashierName));
        if (openingFloat < 0)
            throw new ArgumentException("OpeningFloat must be >= 0.", nameof(openingFloat));

        ShiftNumber = shiftNumber.Trim();
        WarehouseId = warehouseId;
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
        OpeningFloat = openingFloat;
        OpenedAt = openedAt;
        CashSalesTotal = 0m;
        Status = CashierShiftStatus.Open;
    }

    /// <summary>Catat satu penjualan yang selesai (dipanggil D2 di dalam transaksi penyelesaian sale).
    /// <paramref name="amount"/> = total tagihan sale (bukan uang diterima).</summary>
    public void RecordSale(int paymentMethodId, bool isCash, decimal amount)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Cannot record a sale on a closed shift.");
        if (paymentMethodId <= 0)
            throw new ArgumentException("PaymentMethodId must be > 0.", nameof(paymentMethodId));
        if (amount <= 0)
            throw new ArgumentException("Amount must be > 0.", nameof(amount));

        var total = _totals.FirstOrDefault(t => t.PaymentMethodId == paymentMethodId);
        if (total is null)
        {
            total = new CashierShiftTotal(paymentMethodId);
            _totals.Add(total);
        }
        total.Add(amount);

        if (isCash) CashSalesTotal += amount;
    }

    /// <summary>Tutup shift; hitung selisih kas fisik vs sistem.</summary>
    public void Close(decimal countedCash, string? note, DateTime closedAt)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Shift is already closed.");
        if (countedCash < 0)
            throw new ArgumentException("CountedCash must be >= 0.", nameof(countedCash));

        CountedCash = countedCash;
        CashVariance = countedCash - ExpectedCash;
        ClosedAt = closedAt;
        ClosingNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = CashierShiftStatus.Closed;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MyApp.UnitTests --filter FullyQualifiedName~CashierShiftTests`
Expected: PASS (6 tests).

> Catatan: assertion `ExpectedCash` yang berbelit di test `RecordSale_accumulates...` sengaja disederhanakan ke baris `Assert.Equal(100_000m + 80_000m, s.ExpectedCash);` — biarkan baris eksperimen di atasnya apa adanya (lolos secara aljabar) ATAU hapus baris `is var _`-nya bila reviewer menilai membingungkan; keduanya lolos.

- [ ] **Step 7: Verify build + record progress (no commit)**

Run: `dotnet build` — Expected: 0 warnings.
Buat/append `docs/superpowers/plans/d1-progress.md` menandai Task 1 selesai (jumlah test lulus).

---

### Task 2: Application — DTOs, `ICashierShiftService`, validators

**Files:**
- Create: `src/MyApp.Application/CashierShifts/CashierShiftDtos.cs`
- Create: `src/MyApp.Application/CashierShifts/ICashierShiftService.cs`
- Create: `src/MyApp.Application/CashierShifts/CashierShiftValidators.cs`
- Test: `tests/MyApp.UnitTests/CashierShiftValidatorTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` (`MyApp.Application.Common`), `CashierShiftStatus` (Task 1).
- Produces: DTO records + `ICashierShiftService` (signatur di bawah) + `OpenShiftRequest`/`CloseShiftRequest` + validators `OpenShiftRequestValidator`/`CloseShiftRequestValidator`.

- [ ] **Step 1: Create DTOs**

`src/MyApp.Application/CashierShifts/CashierShiftDtos.cs`:

```csharp
namespace MyApp.Application.CashierShifts;

public record ShiftMethodTotalDto(int PaymentMethodId, string MethodName, decimal TotalAmount, int TransactionCount);

public record CashierShiftListItemDto(
    int Id, string ShiftNumber, int WarehouseId, string WarehouseName, string CashierName,
    DateTime OpenedAt, DateTime? ClosedAt, decimal TotalSalesAmount, string Status);

public record CashierShiftDto(
    int Id, string ShiftNumber, int WarehouseId, string WarehouseName,
    string CashierUserId, string CashierName, string Status,
    DateTime OpenedAt, decimal OpeningFloat, decimal CashSalesTotal, decimal ExpectedCash,
    DateTime? ClosedAt, decimal? CountedCash, decimal? CashVariance, string? ClosingNote,
    decimal TotalSalesAmount, int TransactionCount,
    IReadOnlyList<ShiftMethodTotalDto> MethodTotals);

public record OpenShiftRequest(int WarehouseId, decimal OpeningFloat);

public record CloseShiftRequest(decimal CountedCash, string? ClosingNote);
```

- [ ] **Step 2: Create `ICashierShiftService`**

`src/MyApp.Application/CashierShifts/ICashierShiftService.cs`:

```csharp
using MyApp.Application.Common;
using MyApp.Domain.Entities;

namespace MyApp.Application.CashierShifts;

public interface ICashierShiftService
{
    Task<CashierShiftDto?> GetCurrentAsync(string userId, CancellationToken ct = default);
    Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest request, CancellationToken ct = default);
    Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest request, CancellationToken ct = default);
    Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create validators**

`src/MyApp.Application/CashierShifts/CashierShiftValidators.cs`:

```csharp
using FluentValidation;

namespace MyApp.Application.CashierShifts;

public class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OpeningFloat).GreaterThanOrEqualTo(0);
    }
}

public class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator()
    {
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ClosingNote).MaximumLength(500);
    }
}
```

- [ ] **Step 4: Write validator tests**

`tests/MyApp.UnitTests/CashierShiftValidatorTests.cs`:

```csharp
using MyApp.Application.CashierShifts;
using Xunit;

namespace MyApp.UnitTests;

public class CashierShiftValidatorTests
{
    [Fact]
    public void Open_requires_warehouse_and_nonnegative_float()
    {
        var v = new OpenShiftRequestValidator();
        Assert.False(v.Validate(new OpenShiftRequest(0, 100m)).IsValid);
        Assert.False(v.Validate(new OpenShiftRequest(3, -1m)).IsValid);
        Assert.True(v.Validate(new OpenShiftRequest(3, 0m)).IsValid);
    }

    [Fact]
    public void Close_requires_nonnegative_cash_and_note_length()
    {
        var v = new CloseShiftRequestValidator();
        Assert.False(v.Validate(new CloseShiftRequest(-1m, null)).IsValid);
        Assert.False(v.Validate(new CloseShiftRequest(0m, new string('x', 501))).IsValid);
        Assert.True(v.Validate(new CloseShiftRequest(100m, "ok")).IsValid);
    }
}
```

- [ ] **Step 5: Run validator tests + build**

Run: `dotnet test tests/MyApp.UnitTests --filter FullyQualifiedName~CashierShiftValidatorTests`
Expected: PASS (2 tests).
Run: `dotnet build` — Expected: 0 warnings. Append progres Task 2.

---

### Task 3: Infrastructure — DbContext mapping, DbSets, DI, migration

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` (DbSets dekat baris 33–36; blok mapping baru setelah blok `DeliveryOrderLine` ~baris 352)
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs:60` (setelah registrasi `IDeliveryOrderService`)
- Create (via EF): migration `AddCashierShift`

**Interfaces:**
- Consumes: `CashierShift`, `CashierShiftTotal`, `CashierShiftStatus` (Task 1); `PaymentMethod`, `Warehouse` (existing).
- Produces: `AppDbContext.CashierShifts`, `AppDbContext.CashierShiftTotals`; tabel DB + filtered unique index.

- [ ] **Step 1: Add DbSets**

Di `AppDbContext.cs`, setelah baris `public DbSet<DeliveryOrderLine> DeliveryOrderLines => Set<DeliveryOrderLine>();` (baris 34) tambahkan:

```csharp
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashierShiftTotal> CashierShiftTotals => Set<CashierShiftTotal>();
```

- [ ] **Step 2: Add entity mapping**

Di `OnModelCreating`, setelah blok `modelBuilder.Entity<DeliveryOrderLine>(...)` (berakhir ~baris 352) tambahkan:

```csharp
        modelBuilder.Entity<CashierShift>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShiftNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.ShiftNumber).IsUnique();
            e.Property(x => x.CashierUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CashierName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.OpeningFloat).HasPrecision(18, 2);
            e.Property(x => x.CashSalesTotal).HasPrecision(18, 2);
            e.Property(x => x.CountedCash).HasPrecision(18, 2);
            e.Property(x => x.CashVariance).HasPrecision(18, 2);
            e.Property(x => x.ClosingNote).HasMaxLength(500);

            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            // Pengaman DB: hanya satu shift Open per user.
            e.HasIndex(x => x.CashierUserId).IsUnique().HasFilter("[Status] = 'Open'");

            e.HasMany(x => x.Totals)
                .WithOne()
                .HasForeignKey(t => t.CashierShiftId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(CashierShift.Totals))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CashierShiftTotal>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        });
```

> `MyApp.Domain.Entities` sudah di-`using` di `AppDbContext.cs` (entitas lain sekelasnya). Jika belum, tambahkan.

- [ ] **Step 3: Register the service in DI**

Di `DependencyInjection.cs`, tambahkan `using MyApp.Application.CashierShifts;` bersama using Application lainnya, dan setelah baris 60 (`AddScoped<IDeliveryOrderService, DeliveryOrderService>();`) tambahkan:

```csharp
        services.AddScoped<ICashierShiftService, CashierShiftService>();
```

> Ini akan gagal compile sampai `CashierShiftService` ada (Task 4). Itu OK — Task 3 diverifikasi lewat build SETELAH Task 4 membuat service, ATAU sementara tunda baris DI ini ke Task 4. **Keputusan:** pindahkan baris `AddScoped` ini ke Task 4 Step (registrasi), dan di Task 3 hanya tambahkan `using`-nya. (Lihat Task 4 Step 5.)

- [ ] **Step 4: Add the migration**

Run: `dotnet ef migrations add AddCashierShift -p src/MyApp.Infrastructure -s src/MyApp.Web`
Verifikasi file migration: membuat tabel `CashierShifts` (kolom sesuai di atas) + `CashierShiftTotals`, unique index `IX_CashierShifts_ShiftNumber`, dan **filtered unique index** pada `CashierUserId` dgn `filter: "[Status] = 'Open'"`; FK `Warehouse` Restrict, `Totals` Cascade, `PaymentMethod` Restrict. `Down()` membuang kedua tabel.

- [ ] **Step 5: Apply to the dev database**

Run: `dotnet ef database update -p src/MyApp.Infrastructure -s src/MyApp.Web`
Expected: sukses, dua tabel dibuat.

- [ ] **Step 6: Build**

Run: `dotnet build` — Expected: 0 warnings (dgn catatan Step 3: baris `AddScoped` ada di Task 4). Append progres Task 3.

---

### Task 4: Infrastructure — `CashierShiftService` + integration tests

**Files:**
- Create: `src/MyApp.Infrastructure/Services/CashierShiftService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs` (tambah `AddScoped<ICashierShiftService, CashierShiftService>();`)
- Test: `tests/MyApp.IntegrationTests/CashierShiftServiceTests.cs`

**Interfaces:**
- Consumes: `ICashierShiftService` + DTOs (Task 2); `AppDbContext` (Task 3); `IValidator<OpenShiftRequest>`, `IValidator<CloseShiftRequest>`.
- Produces: `CashierShiftService` (implementasi penuh).

- [ ] **Step 1: Write failing integration tests**

`tests/MyApp.IntegrationTests/CashierShiftServiceTests.cs`:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.CashierShifts;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class CashierShiftServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CashierShiftServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> SeedWarehouseAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();
        return wh.Id;
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Open_generates_daily_number_and_get_current_returns_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 100_000m));
        Assert.StartsWith("SHIFT-", opened.ShiftNumber);
        Assert.Equal("Open", opened.Status);
        Assert.Equal(100_000m, opened.OpeningFloat);
        Assert.Equal(100_000m, opened.ExpectedCash);

        var current = await svc.GetCurrentAsync(user);
        Assert.NotNull(current);
        Assert.Equal(opened.Id, current!.Id);
    }

    [Fact]
    public async Task Open_rejects_second_open_shift_for_same_user()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 0m));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 0m)));
    }

    [Fact]
    public async Task Open_rejects_inactive_or_missing_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ICashierShiftService>();
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.OpenAsync(NewUser(), "Rani", new OpenShiftRequest(999999, 0m)));
    }

    [Fact]
    public async Task Close_computes_variance_and_only_owner_can_close()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();
        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 100_000m));

        // pemilik lain tidak boleh menutup
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CloseAsync(opened.Id, NewUser(), new CloseShiftRequest(100_000m, null)));

        var ok = await svc.CloseAsync(opened.Id, user, new CloseShiftRequest(97_000m, "kurang"));
        Assert.True(ok);
        var reloaded = await svc.GetByIdAsync(opened.Id);
        Assert.Equal("Closed", reloaded!.Status);
        Assert.Equal(97_000m, reloaded.CountedCash);
        Assert.Equal(-3_000m, reloaded.CashVariance);

        // tak bisa tutup dua kali
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CloseAsync(opened.Id, user, new CloseShiftRequest(97_000m, null)));
    }

    [Fact]
    public async Task RecordSale_totals_surface_in_get_by_id()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        // butuh PaymentMethod utk MethodTotals join nama
        var pmCash = new PaymentMethod("CASH" + Guid.NewGuid().ToString("N")[..4], "Tunai", PaymentType.Tunai, true);
        db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();

        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 0m));

        // simulasikan D2: panggil domain RecordSale langsung lalu simpan
        var shift = await db.CashierShifts.FindAsync(opened.Id);
        shift!.RecordSale(pmCash.Id, isCash: true, amount: 25_000m);
        shift.RecordSale(pmCash.Id, isCash: true, amount: 15_000m);
        await db.SaveChangesAsync();

        var dto = await svc.GetByIdAsync(opened.Id);
        Assert.Equal(40_000m, dto!.CashSalesTotal);
        Assert.Equal(40_000m, dto.TotalSalesAmount);
        Assert.Equal(2, dto.TransactionCount);
        var mt = Assert.Single(dto.MethodTotals);
        Assert.Equal("Tunai", mt.MethodName);
        Assert.Equal(40_000m, mt.TotalAmount);
        Assert.Equal(2, mt.TransactionCount);
    }
}
```

> `PaymentType` = `{ Tunai, Transfer, Kartu, QRIS }` (terverifikasi) — gunakan `PaymentType.Tunai` untuk metode tunai. `CustomWebApplicationFactory` + `InitializeDatabase()` adalah harness existing (lihat `DeliveryOrderServiceTests`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyApp.IntegrationTests --filter FullyQualifiedName~CashierShiftServiceTests`
Expected: FAIL — `CashierShiftService` belum ada / DI belum terdaftar.

- [ ] **Step 3: Implement `CashierShiftService`**

`src/MyApp.Infrastructure/Services/CashierShiftService.cs`:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.CashierShifts;
using MyApp.Application.Common;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class CashierShiftService(
    AppDbContext db,
    IValidator<OpenShiftRequest> openValidator,
    IValidator<CloseShiftRequest> closeValidator) : ICashierShiftService
{
    public async Task<CashierShiftDto?> GetCurrentAsync(string userId, CancellationToken ct = default)
    {
        var id = await db.CashierShifts.AsNoTracking()
            .Where(s => s.CashierUserId == userId && s.Status == CashierShiftStatus.Open)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        return id is null ? null : await GetByIdAsync(id.Value, ct);
    }

    public async Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest request, CancellationToken ct = default)
    {
        await openValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.CashierShifts.AnyAsync(s => s.CashierUserId == userId && s.Status == CashierShiftStatus.Open, ct))
            throw Fail("Anda masih punya shift terbuka. Tutup dulu sebelum membuka yang baru.");

        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId, ct);
        if (warehouse is null || !warehouse.IsActive)
            throw Fail("Gudang tidak ditemukan atau tidak aktif.");

        var now = DateTime.Now;
        var shift = new CashierShift(await GenerateNumberAsync(now, ct),
            request.WarehouseId, userId, userName, request.OpeningFloat, now);

        db.CashierShifts.Add(shift);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(shift.Id, ct))!;
    }

    public async Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest request, CancellationToken ct = default)
    {
        await closeValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
        if (shift is null) return false;
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup.");
        if (shift.CashierUserId != userId) throw Fail("Anda hanya bisa menutup shift milik sendiri.");

        shift.Close(request.CountedCash, request.ClosingNote, DateTime.Now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var shift = await db.CashierShifts.AsNoTracking().Include(s => s.Totals)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shift is null) return null;

        var warehouseName = await db.Warehouses.Where(w => w.Id == shift.WarehouseId)
            .Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var methodIds = shift.Totals.Select(t => t.PaymentMethodId).Distinct().ToList();
        var methods = await db.PaymentMethods.AsNoTracking()
            .Where(m => methodIds.Contains(m.Id)).Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var methodTotals = shift.Totals
            .OrderBy(t => t.Id)
            .Select(t => new ShiftMethodTotalDto(t.PaymentMethodId,
                methods.FirstOrDefault(m => m.Id == t.PaymentMethodId)?.Name ?? "—",
                t.TotalAmount, t.TransactionCount))
            .ToList();

        return new CashierShiftDto(shift.Id, shift.ShiftNumber, shift.WarehouseId, warehouseName,
            shift.CashierUserId, shift.CashierName, shift.Status.ToString(),
            shift.OpenedAt, shift.OpeningFloat, shift.CashSalesTotal, shift.ExpectedCash,
            shift.ClosedAt, shift.CountedCash, shift.CashVariance, shift.ClosingNote,
            shift.TotalSalesAmount, shift.TransactionCount, methodTotals);
    }

    public async Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.CashierShifts.AsNoTracking().AsQueryable();
        if (status is { } st) query = query.Where(s => s.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.ShiftNumber.Contains(search) || s.CashierName.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id, s.ShiftNumber, s.WarehouseId, s.CashierName, s.OpenedAt, s.ClosedAt, s.Status,
                TotalSales = db.CashierShiftTotals.Where(t => t.CashierShiftId == s.Id).Sum(t => (decimal?)t.TotalAmount) ?? 0m
            })
            .ToListAsync(ct);

        var whIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => whIds.Contains(w.Id)).Select(w => new { w.Id, w.Name }).ToListAsync(ct);

        var items = rows.Select(r => new CashierShiftListItemDto(
            r.Id, r.ShiftNumber, r.WarehouseId,
            warehouses.FirstOrDefault(w => w.Id == r.WarehouseId)?.Name ?? "—",
            r.CashierName, r.OpenedAt, r.ClosedAt, r.TotalSales, r.Status.ToString())).ToList();

        return new PagedResult<CashierShiftListItemDto>(items, total, page, pageSize);
    }

    private async Task<string> GenerateNumberAsync(DateTime openedAt, CancellationToken ct)
    {
        var prefix = $"SHIFT-{openedAt:yyyyMMdd}-";
        var last = await db.CashierShifts.AsNoTracking()
            .Where(s => s.ShiftNumber.StartsWith(prefix))
            .OrderByDescending(s => s.ShiftNumber)
            .Select(s => s.ShiftNumber).FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("CashierShift", message)]);
}
```

- [ ] **Step 4: Register in DI**

Di `DependencyInjection.cs`, setelah `services.AddScoped<IDeliveryOrderService, DeliveryOrderService>();` (baris 60) tambahkan:

```csharp
        services.AddScoped<ICashierShiftService, CashierShiftService>();
```

Dan pastikan `using MyApp.Application.CashierShifts;` ada di header.

- [ ] **Step 5: Run tests + build**

Run: `dotnet test tests/MyApp.IntegrationTests --filter FullyQualifiedName~CashierShiftServiceTests`
Expected: PASS (5 tests).
Run: `dotnet build` — Expected: 0 warnings. Append progres Task 4.

---

### Task 5: Web — menu resource, `cashier.any` policy, NavMenu group

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Program.cs` (blok policy `*.any` ~baris 105–110)
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor` (setelah blok grup Transaksi, sebelum grup Settings ~baris 195)

**Interfaces:**
- Produces: resource `cashier.shifts` + policy `cashier.any` + entri nav grup "Kasir".

- [ ] **Step 1: Add the resource + action set**

Di `AppMenus.cs`, setelah baris `private static AppAction[] SalesOrderActions => ...;` (baris 28) tambahkan:

```csharp
    private static AppAction[] CashierShiftActions => [ActIndex, ActCreate, ActClose];
```

Lalu tambahkan grup baru di `Groups` setelah grup `"Transaksi"` (setelah baris 61) dan sebelum grup `"Settings"`:

```csharp
        new("Kasir",
        [
            new("cashier.shifts", "Sesi Kasir", "bi-cash-stack", CashierShiftActions),
        ]),
```

> `ActCreate` (buka shift) & `ActClose` (tutup shift) sudah ada. Permission `cashier.shifts.index/create/close` otomatis masuk `AllPermissions` → admin auto-grant.

- [ ] **Step 2: Register `cashier.any` group policy**

Di `Program.cs`, setelah blok `options.AddPolicy("transactions.any", ...)` (berakhir ~baris 110, sebelum `});` penutup) tambahkan:

```csharp
    // Tampilkan grup Kasir jika memiliki setidaknya satu izin cashier.*.index
    options.AddPolicy("cashier.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("cashier."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));
```

- [ ] **Step 3: Add the NavMenu group**

Di `NavMenu.razor`, setelah blok `<AuthorizeView Policy="transactions.any" ...>...</AuthorizeView>` (berakhir ~baris 195) dan sebelum blok `settings.any`, tambahkan:

```razor
        <AuthorizeView Policy="cashier.any" Context="cashierCtx">
            <Authorized>
                <div class="nav-section">
                    <span class="nav-section-label">Kasir</span>
                </div>
                <AuthorizeView Policy="cashier.shifts.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="cashier/shifts" title="Sesi Kasir">
                                <i class="bi bi-cash-stack nav-icon" aria-hidden="true"></i> <span class="nav-label">Sesi Kasir</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
            </Authorized>
        </AuthorizeView>
```

- [ ] **Step 4: Build**

Run: `dotnet build` — Expected: 0 warnings. Append progres Task 5.
(Verifikasi manual saat run app: admin melihat grup "Kasir" di sidebar.)

---

### Task 6: Web — `ShiftIndex.razor` (riwayat + banner + buka shift)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftIndex.razor.css` (copy dari sibling)

**Interfaces:**
- Consumes: `ICashierShiftService`, DTO (Task 2/4); `Pager`, `SwalService` (existing).
- Produces: halaman `/cashier/shifts`.

- [ ] **Step 1: Copy sibling scoped CSS**

Copy `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoIndex.razor.css` → `.../Cashier/Shifts/ShiftIndex.razor.css` (samakan family pi/kpi/toolbar/card + badge `b-ok`/`b-off`/`b-done`). Tak ada perubahan isi kecuali bila ada selector yang tak dipakai.

- [ ] **Step 2: Create the page**

`ShiftIndex.razor` — route `/cashier/shifts`, policy `cashier.shifts.index`, `@rendermode InteractiveServer`. Struktur mirror `DoIndex.razor` (KPI + toolbar + tabel + `Pager`), dgn deviasi berikut:

- **Banner shift terbuka:** di atas tabel, panggil `GetCurrentAsync(userId)`; bila ada → kartu ringkas (No. Shift, gudang, dibuka, Expected Cash) + link `href="/cashier/shifts/{id}"`. Bila tidak → tombol **Buka Shift** (`<AuthorizeView Policy="cashier.shifts.create">`) yang menampilkan form inline: dropdown gudang aktif (`IWarehouseService` — gunakan yang sudah dipakai modul lain untuk daftar gudang aktif; bila tak ada, query via service baru tidak dibuat — pakai `IWarehouseService.GetActiveAsync`/setara yang ada) + input `OpeningFloat` (mono, numeric) + tombol Simpan → `OpenAsync(userId, userName, new OpenShiftRequest(whId, float))` lalu reload.
- **Tabel riwayat:** kolom No. Shift, Gudang, Kasir, Buka (`OpenedAt` dd MMM yyyy HH:mm), Tutup (`ClosedAt?` atau "—"), Total Penjualan (`TotalSalesAmount` N2), Status badge; baris klik → detail. `GetPagedAsync(page, 15, search, statusFilter)`.
- **Filter status:** dropdown Semua/Open/Closed.

`@code` (lengkap):

```csharp
@code {
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private PagedResult<CashierShiftListItemDto>? _page;
    private CashierShiftDto? _current;
    private IReadOnlyList<WarehouseDto> _warehouses = [];   // gudang aktif untuk form buka
    private int _pageNo = 1, _openWhId;
    private decimal _openFloat;
    private string? _search;
    private CashierShiftStatus? _statusFilter;
    private bool _loading = true, _busy, _showOpen;
    private string _userId = "", _userName = "";
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        _userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        _userName = user.Identity?.Name ?? "";
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _current = string.IsNullOrEmpty(_userId) ? null : await ShiftService.GetCurrentAsync(_userId);
        _page = await ShiftService.GetPagedAsync(_pageNo, 15, _search, _statusFilter);
        // muat gudang aktif untuk form buka (IWarehouseService.GetAllAsync ada; filter aktif)
        _warehouses = (await WarehouseService.GetAllAsync()).Where(w => w.IsActive).ToList();
        _loading = false;
    }

    private async Task OpenShiftAsync()
    {
        _error = null; _busy = true;
        try
        {
            if (_openWhId <= 0) { _error = "Pilih gudang."; return; }
            await ShiftService.OpenAsync(_userId, _userName, new OpenShiftRequest(_openWhId, _openFloat));
            _showOpen = false; _openFloat = 0m; _openWhId = 0;
            await LoadAsync();
            await Swal.ToastAsync("success", "Shift dibuka");
        }
        catch (FluentValidation.ValidationException ex) { _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)); }
        finally { _busy = false; }
    }

    private async Task GoToPage(int p) { _pageNo = p; await LoadAsync(); }
    private async Task ApplyFilter() { _pageNo = 1; await LoadAsync(); }

    private static string StatusClass(string s) => s switch
    {
        "Open" => "b-ok",
        "Closed" => "b-done",
        _ => "b-off"
    };
}
```

> **Injects & usings:** `@using MyApp.Application.CashierShifts`, `@using MyApp.Application.Common`, `@using MyApp.Application.Warehouses`, `@inject ICashierShiftService ShiftService`, `@inject IWarehouseService WarehouseService`, `@inject SwalService Swal`. `WarehouseDto` punya `Id`/`Name`/`IsActive` (terverifikasi: `IWarehouseService.GetAllAsync()` mengembalikan `IReadOnlyList<WarehouseDto>`). Markup tabel/KPI/`Pager`/toolbar mengikuti `DoIndex.razor` (baca file itu sebagai template; salin struktur, ganti kolom & handler sesuai di atas).

- [ ] **Step 3: Build**

Run: `dotnet build` — Expected: 0 warnings. Append progres Task 6.

---

### Task 7: Web — `ShiftDetail.razor` (detail + total per metode + tutup shift)

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftDetail.razor`
- Create: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftDetail.razor.css` (copy dari sibling)

**Interfaces:**
- Consumes: `ICashierShiftService`, DTO (Task 2/4); `SwalService`.
- Produces: halaman `/cashier/shifts/{Id:int}`.

- [ ] **Step 1: Copy sibling scoped CSS**

Copy `src/MyApp.Web/Components/Pages/Transactions/DeliveryOrders/DoDetail.razor.css` → `.../Cashier/Shifts/ShiftDetail.razor.css`.

- [ ] **Step 2: Create the page**

`ShiftDetail.razor` — route `/cashier/shifts/{Id:int}`, policy `cashier.shifts.index`, `@rendermode InteractiveServer`. Struktur mirror `DoDetail.razor`/`SoDetail.razor`:

- **Header:** H1 `@_shift.ShiftNumber` + badge status (StatusClass sama seperti Task 6). Action bar: bila `Status=="Open"` **dan** `_shift.CashierUserId == _userId` → tombol **Tutup Shift** (`<AuthorizeView Policy="cashier.shifts.close">`) membuka form tutup.
- **Kartu Info:** dl — Gudang, Kasir, Dibuka (`OpenedAt`), Saldo Awal (`OpeningFloat` N2), Status; bila Closed: Ditutup (`ClosedAt`), Catatan.
- **Kartu Rekonsiliasi:** rows — Penjualan Tunai (`CashSalesTotal`), Saldo Awal (`OpeningFloat`), **Expected Cash** (`ExpectedCash`); bila Closed: Kas Terhitung (`CountedCash`), **Selisih** (`CashVariance`, warna merah bila negatif) + Catatan.
- **Tabel Total per Metode:** kolom Metode (`MethodName`), Transaksi (`TransactionCount`), Total (`TotalAmount` N2); footer Grand Total (`TotalSalesAmount`).
- **Form Tutup (inline card, tampil saat `_showClose`):** input kas fisik terhitung (mono numeric) + textarea catatan (≤500) + tombol Konfirmasi → `CloseAsync(Id, _userId, new CloseShiftRequest(counted, note))` → reload; tampilkan selisih via reload.

`@code` (lengkap):

```csharp
@code {
    [Parameter] public int Id { get; set; }
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private CashierShiftDto? _shift;
    private bool _loading = true, _busy, _showClose;
    private decimal _countedCash;
    private string? _closingNote;
    private string _userId = "";
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        _userId = (await AuthStateTask).User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _shift = await ShiftService.GetByIdAsync(Id);
        _loading = false;
    }

    private async Task CloseShiftAsync()
    {
        _error = null; _busy = true;
        try
        {
            if (!await Swal.ConfirmAsync("Tutup shift ini?", "Setelah ditutup tidak bisa diubah.")) { _busy = false; return; }
            await ShiftService.CloseAsync(Id, _userId, new CloseShiftRequest(_countedCash, _closingNote));
            _showClose = false;
            await LoadAsync();
            await Swal.ToastAsync("success", "Shift ditutup");
        }
        catch (FluentValidation.ValidationException ex) { _error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)); }
        finally { _busy = false; }
    }

    private bool CanClose => _shift is { Status: "Open" } && _shift.CashierUserId == _userId;

    private static string StatusClass(string s) => s switch
    {
        "Open" => "b-ok",
        "Closed" => "b-done",
        _ => "b-off"
    };
}
```

> Sertakan `@using MyApp.Application.CashierShifts`, `@inject ICashierShiftService ShiftService`, `@inject SwalService Swal`, `@inject NavigationManager Nav`. Markup mengikuti `DoDetail.razor` (baca sebagai template; ganti dl/kartu/tabel sesuai di atas).

- [ ] **Step 3: Build**

Run: `dotnet build` — Expected: 0 warnings. Append progres Task 7.

---

### Task 8: Full verification

**Files:** none (verifikasi saja)

- [ ] **Step 1: Clean build**

Run: `dotnet build` — Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: semua hijau — baseline 183 + Task 1 (6) + Task 2 (2) + Task 4 (5) = **111+8=119 unit? cek**: 111 unit + 6 + 2 = **119 unit**, 72 integ + 5 = **77 integ**, total **196**. Konfirmasi angka aktual & catat di ledger.

- [ ] **Step 3: Confirm invariants by re-reading the service**

Re-read `CashierShiftService`:
- `OpenAsync` menolak shift-terbuka kedua per user (query + pengaman filtered unique index) dan gudang tidak aktif; transaksi-wrapped.
- `CloseAsync` hanya untuk shift Open milik user tsb; `Close` menghitung `CashVariance = CountedCash − ExpectedCash`.
- `RecordSale` (domain) tidak dipanggil oleh service apa pun di D1 — hanya method domain, siap dipakai D2.

- [ ] **Step 4: Manual UI walkthrough (hand to user)**

Serahkan ke user (tak bisa diotomasi):
1. Login admin → sidebar grup **Kasir** → **Sesi Kasir** → **Buka Shift** (pilih gudang, saldo awal) → banner shift terbuka muncul.
2. Buka shift kedua saat masih ada yg terbuka → ditolak dgn pesan jelas.
3. Buka detail shift → **Tutup Shift** (masukkan kas terhitung) → Selisih tampil benar (kurang/lebih/pas), status jadi Closed, read-only.
4. Coba tutup shift milik user lain → ditolak.

## Self-Review (oleh penulis plan)

- **Spec coverage:** §1 Domain (`CashierShiftStatus`/`CashierShiftTotal`/`CashierShift`) → Task 1; §2 Application (DTO/interface/validator) → Task 2; §3 Infrastructure (DbContext/mapping/DI/migration + service) → Task 3–4; §4 Web (Index/Detail + menu/policy/nav) → Task 5–7; §5 Testing → tertanam per task + Task 8. ✓
- **Type consistency:** `OpenShiftRequest(int WarehouseId, decimal OpeningFloat)`, `CloseShiftRequest(decimal CountedCash, string? ClosingNote)`, `RecordSale(int, bool, decimal)`, `Close(decimal, string?, DateTime)`, `GenerateNumberAsync(DateTime, ct)` dipakai konsisten di Task 4/6/7. DTO `CashierShiftDto`/`ShiftMethodTotalDto` field cocok dgn proyeksi `GetByIdAsync`. ✓
- **Placeholder scan:** Domain/Application/Infrastructure = kode nyata lengkap. Dua halaman Razor (Task 6/7) mengikuti konvensi proyek (C2): salin scoped CSS sibling + `@code` lengkap diberikan + struktur markup dirujuk ke file sibling konkret dgn kolom/route/policy/handler eksplisit. Satu titik yang HARUS disesuaikan saat implementasi: nama API daftar-gudang-aktif (baca `SoForm`/`GrnForm`) — ditandai eksplisit, bukan TBD tersembunyi. ✓
- **No-git adaptation:** langkah commit diganti build+test + ledger `d1-progress.md`. ✓
- **Asumsi terverifikasi (bukan lagi TBD):** `PaymentType` = `{ Tunai, Transfer, Kartu, QRIS }` → pakai `PaymentType.Tunai`. `IWarehouseService.GetAllAsync()` ada & mengembalikan `IReadOnlyList<WarehouseDto>` (punya `IsActive`) → form buka pakai itu, filter `.Where(w => w.IsActive)`. Sisa penyesuaian markup Razor merujuk sibling `DoIndex`/`DoDetail` sesuai konvensi C2.
