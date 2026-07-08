# Shared Multi-User Cashier Shift + Single-Active-Cashier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the per-user cashier shift into a shift shared by multiple users (one open shift per warehouse), record which cashier rang each sale, and block a user from opening the POS page in more than one tab/device at once.

**Architecture:** Clean Architecture (Domain → Application → Infrastructure → Web/Blazor Server). Shift ownership (`CashierUserId`/`CashierName`) becomes "opened by"; the DB safety-net index moves from per-user to per-warehouse. `PosSale` gains its own cashier stamp. The single-page lock is an in-memory `IPosSessionRegistry` singleton acquired in the POS component's `OnInitializedAsync` and released in `Dispose`.

**Tech Stack:** .NET 10, EF Core (SQL Server prod / SQLite in-memory tests), Blazor Server, FluentValidation, xUnit.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-08-shared-shift-single-active-cashier-design.md` — authoritative.
- **Cakupan shift:** satu shift `Open` **per gudang** (bukan global, bukan per-user).
- **Penutupan:** hanya **pembuka** shift boleh menutup (aturan sudah ada di `CloseAsync` + `ShiftDetail`; jangan dihapus).
- **Kunci halaman:** **blokir** halaman kedua (tanpa takeover). Asumsi **satu instance server web**.
- **Konvensi kode:** enum→string; uang `decimal(18,2)`; `Math.Round(v, 2, MidpointRounding.AwayFromZero)`; service melempar `FluentValidation.ValidationException` via helper `Fail(...)`; DateTime `DateTime.Now`.
- **Bukan repo git:** langkah "Commit" tidak berlaku (proyek ini bukan git repo). Ganti setiap langkah commit dengan verifikasi build/test.
- **Perintah build/test:** solusi `MyApp.slnx`. Test cepat domain: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj`. Test penuh: `dotnet test MyApp.slnx`.

---

## File Structure

- `src/MyApp.Domain/Entities/PosSale.cs` — **modify**: tambah `CashierUserId`/`CashierName` + param ctor.
- `src/MyApp.Application/CashierShifts/ICashierShiftService.cs` — **modify**: hapus `GetCurrentAsync`; tambah `GetOpenShiftsAsync`, `GetOpenShiftByWarehouseAsync`.
- `src/MyApp.Application/PosSales/IPosSaleService.cs` — **modify**: `CreateSaleAsync` tanda tangan baru.
- `src/MyApp.Infrastructure/Services/CashierShiftService.cs` — **modify**: per-gudang guard + query baru.
- `src/MyApp.Infrastructure/Services/PosSaleService.cs` — **modify**: attach by `shiftId`, stempel kasir, baca `sale.CashierName`.
- `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` — **modify**: index per-gudang + mapping 2 kolom `PosSale`.
- `src/MyApp.Infrastructure/Persistence/Migrations/*_SharedShiftPerWarehouse.cs` — **create** (via `dotnet ef`, lalu edit manual).
- `src/MyApp.Web/Services/PosSessionRegistry.cs` — **create**: interface + implementasi singleton in-memory.
- `src/MyApp.Web/Program.cs` — **modify**: daftarkan singleton.
- `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor` — **modify**: resolusi shift (0/1/≥2), header operator, panggil `CreateSaleAsync` baru, kunci sesi.
- `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftIndex.razor` — **modify**: banner daftar shift terbuka + tombol selalu tampil.
- `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftDetail.razor` — **modify**: tombol "Masuk Kasir" untuk shift Open.
- `tests/MyApp.UnitTests/PosSaleTests.cs` — **modify**: helper + test ctor kasir.
- `tests/MyApp.IntegrationTests/CashierShiftServiceTests.cs` — **modify**: per-gudang + query baru.
- `tests/MyApp.IntegrationTests/PosSaleServiceTests.cs` — **modify**: `shiftId`/`userName`, atribusi kasir, multi-user.
- `tests/MyApp.IntegrationTests/PosSessionRegistryTests.cs` — **create**: unit test registry (objek biasa).

---

### Task 1: Domain — `PosSale` records the operating cashier

**Files:**
- Modify: `src/MyApp.Domain/Entities/PosSale.cs`
- Test: `tests/MyApp.UnitTests/PosSaleTests.cs`

**Interfaces:**
- Consumes: nothing (leaf domain change).
- Produces: `PosSale` ctor signature becomes
  `PosSale(string saleNumber, int cashierShiftId, int warehouseId, DateTime saleDate, int paymentMethodId, bool isCashPayment, int? taxId, decimal taxRateSnapshot, string cashierUserId, string cashierName)`.
  New read-only props `string CashierUserId`, `string CashierName`.

> NOTE: After this task the Infrastructure/Web projects will NOT compile (the single `new PosSale(...)` call site changes in Task 3's PosSaleService edit). That is expected — this task is verified against `MyApp.UnitTests` only, which references just Domain + Application. The full solution compiles again at the end of Task 4.

- [ ] **Step 1: Update the unit-test helper and add a failing ctor test**

In `tests/MyApp.UnitTests/PosSaleTests.cs`, change the `Sale(...)` factory to pass the two new args, and add a new test. Replace the existing `Sale` helper (lines 8-11) with:

```csharp
    private static PosSale Sale(bool cash = true, int? taxId = 1, decimal rate = 11m) =>
        new("POS-20260702-0001", cashierShiftId: 5, warehouseId: 3,
            saleDate: new DateTime(2026, 7, 2, 10, 0, 0), paymentMethodId: 1,
            isCashPayment: cash, taxId: taxId, taxRateSnapshot: rate,
            cashierUserId: "u-op1", cashierName: "Budi");
```

Add these tests at the end of the class (before the closing brace):

```csharp
    [Fact]
    public void Ctor_stores_operating_cashier()
    {
        var s = Sale();
        Assert.Equal("u-op1", s.CashierUserId);
        Assert.Equal("Budi", s.CashierName);
    }

    [Fact]
    public void Ctor_rejects_blank_cashier()
    {
        Assert.Throws<ArgumentException>(() => new PosSale(
            "POS-20260702-0002", 5, 3, new DateTime(2026, 7, 2, 10, 0, 0),
            1, true, null, 0m, cashierUserId: "  ", cashierName: "Budi"));
        Assert.Throws<ArgumentException>(() => new PosSale(
            "POS-20260702-0003", 5, 3, new DateTime(2026, 7, 2, 10, 0, 0),
            1, true, null, 0m, cashierUserId: "u-op1", cashierName: ""));
    }
```

- [ ] **Step 2: Run the tests to verify they fail (compile error)**

Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj`
Expected: FAIL — build error, `PosSale` has no 10-arg constructor / no `CashierUserId` member.

- [ ] **Step 3: Add the fields and constructor params to `PosSale`**

In `src/MyApp.Domain/Entities/PosSale.cs`, add the two properties after `CogsTotal` (after line 25):

```csharp
    public decimal CogsTotal { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;
```

Change the constructor (lines 33-50) to accept and validate the two new params:

```csharp
    public PosSale(string saleNumber, int cashierShiftId, int warehouseId, DateTime saleDate,
        int paymentMethodId, bool isCashPayment, int? taxId, decimal taxRateSnapshot,
        string cashierUserId, string cashierName)
    {
        if (string.IsNullOrWhiteSpace(saleNumber)) throw new ArgumentException("SaleNumber is required.", nameof(saleNumber));
        if (cashierShiftId <= 0) throw new ArgumentException("CashierShiftId is required.", nameof(cashierShiftId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (paymentMethodId <= 0) throw new ArgumentException("PaymentMethodId is required.", nameof(paymentMethodId));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));
        if (string.IsNullOrWhiteSpace(cashierUserId)) throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName)) throw new ArgumentException("CashierName is required.", nameof(cashierName));

        SaleNumber = saleNumber.Trim();
        CashierShiftId = cashierShiftId;
        WarehouseId = warehouseId;
        SaleDate = saleDate;
        PaymentMethodId = paymentMethodId;
        IsCashPayment = isCashPayment;
        TaxId = taxId;
        TaxRateSnapshot = taxId is null ? 0m : taxRateSnapshot;
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj`
Expected: PASS (all unit tests, including the 2 new ones).

- [ ] **Step 5: Checkpoint**

No git. Verify `dotnet build src/MyApp.Domain/MyApp.Domain.csproj` succeeds. (Infrastructure/Web still broken — expected until Task 4.)

---

### Task 2: `CashierShiftService` — per-warehouse open rule + open-shift queries

**Files:**
- Modify: `src/MyApp.Application/CashierShifts/ICashierShiftService.cs`
- Modify: `src/MyApp.Infrastructure/Services/CashierShiftService.cs`
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs:376` (the filtered unique index)
- Test: `tests/MyApp.IntegrationTests/CashierShiftServiceTests.cs`

**Interfaces:**
- Consumes: `CashierShiftDto` (existing), `CashierShiftStatus` (existing).
- Produces on `ICashierShiftService`:
  - `Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken ct = default)`
  - `Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken ct = default)`
  - (removed) `GetCurrentAsync`
  - `OpenAsync` now rejects when the **warehouse** already has an open shift.

> NOTE: `PosSaleServiceTests` also calls `GetCurrentAsync` and `CreateSaleAsync` — those files are fixed in Task 3. Until then the full integration suite will not compile. Verify this task by building Infrastructure + running only the `CashierShiftServiceTests` class is NOT possible in isolation (same assembly as PosSaleServiceTests). Therefore: this task's gate is `dotnet build src/MyApp.Infrastructure/MyApp.Infrastructure.csproj` succeeding; the `CashierShiftServiceTests` assertions actually execute at the end of Task 3.

- [ ] **Step 1: Update the service interface**

Replace `src/MyApp.Application/CashierShifts/ICashierShiftService.cs` body (lines 6-14) with:

```csharp
public interface ICashierShiftService
{
    Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken ct = default);
    Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken ct = default);
    Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest request, CancellationToken ct = default);
    Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest request, CancellationToken ct = default);
    Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken ct = default);
}
```

- [ ] **Step 2: Rewrite the query methods and the open guard in the implementation**

In `src/MyApp.Infrastructure/Services/CashierShiftService.cs`, replace the `GetCurrentAsync` method (lines 15-22) with these two methods:

```csharp
    public async Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken ct = default)
    {
        var ids = await db.CashierShifts.AsNoTracking()
            .Where(s => s.Status == CashierShiftStatus.Open)
            .OrderBy(s => s.OpenedAt)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var list = new List<CashierShiftDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await GetByIdAsync(id, ct);
            if (dto is not null) list.Add(dto);
        }
        return list;
    }

    public async Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken ct = default)
    {
        var id = await db.CashierShifts.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.Status == CashierShiftStatus.Open)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        return id is null ? null : await GetByIdAsync(id.Value, ct);
    }
```

In the same file, change the open guard inside `OpenAsync` (line 29-30) from per-user to per-warehouse:

```csharp
        if (await db.CashierShifts.AnyAsync(s => s.WarehouseId == request.WarehouseId && s.Status == CashierShiftStatus.Open, ct))
            throw Fail("Gudang ini sudah punya shift terbuka. Tutup dulu sebelum membuka yang baru.");
```

(Leave `CloseAsync` unchanged — the opener-only rule stays.)

- [ ] **Step 3: Move the filtered unique index to WarehouseId**

In `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`, replace line 376:

```csharp
            // Pengaman DB: hanya satu shift Open per user.
            e.HasIndex(x => x.CashierUserId).IsUnique().HasFilter("[Status] = 'Open'");
```

with (note the explicit name to avoid clashing with the auto FK index on `WarehouseId`):

```csharp
            // Pengaman DB: hanya satu shift Open per gudang.
            e.HasIndex(x => x.WarehouseId).IsUnique()
                .HasFilter("[Status] = 'Open'")
                .HasDatabaseName("UX_CashierShifts_Warehouse_Open");
```

- [ ] **Step 4: Rewrite the affected shift tests**

In `tests/MyApp.IntegrationTests/CashierShiftServiceTests.cs`:

Replace the test at lines 31-49 with (uses the new query instead of `GetCurrentAsync`):

```csharp
    [Fact]
    public async Task Open_generates_daily_number_and_open_query_returns_it()
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

        var current = await svc.GetOpenShiftByWarehouseAsync(wh);
        Assert.NotNull(current);
        Assert.Equal(opened.Id, current!.Id);

        Assert.Contains(await svc.GetOpenShiftsAsync(), s => s.Id == opened.Id);
    }
```

Replace the test at lines 51-63 with (per-warehouse rejection, including a different user; plus a new allow-different-warehouse test):

```csharp
    [Fact]
    public async Task Open_rejects_second_open_shift_for_same_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();

        await svc.OpenAsync(NewUser(), "Rani", new OpenShiftRequest(wh, 0m));
        // user lain pun ditolak di gudang yang sama
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.OpenAsync(NewUser(), "Sari", new OpenShiftRequest(wh, 0m)));
    }

    [Fact]
    public async Task Open_allows_same_user_at_a_different_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh1 = await SeedWarehouseAsync(sp);
        var wh2 = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        var a = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh1, 0m));
        var b = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh2, 0m));
        Assert.NotEqual(a.Id, b.Id);
    }
```

(The `Close_computes_variance_and_only_owner_can_close` and `RecordSale_totals_surface_in_get_by_id` tests are unchanged.)

- [ ] **Step 5: Verify backend compiles**

Run: `dotnet build src/MyApp.Infrastructure/MyApp.Infrastructure.csproj`
Expected: Build succeeded. (Web + integration test assembly still broken — fixed in Task 3.)

---

### Task 3: `PosSaleService` attribution + Web wiring (full solution compiles & tests pass)

**Files:**
- Modify: `src/MyApp.Application/PosSales/IPosSaleService.cs`
- Modify: `src/MyApp.Infrastructure/Services/PosSaleService.cs`
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor`
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftIndex.razor`
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftDetail.razor`
- Test: `tests/MyApp.IntegrationTests/PosSaleServiceTests.cs`

**Interfaces:**
- Consumes: `ICashierShiftService.GetOpenShiftsAsync`, `GetOpenShiftByWarehouseAsync` (Task 2); `PosSale` 10-arg ctor (Task 1).
- Produces: `IPosSaleService.CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken ct = default)`. `PosSaleDto.CashierName` / `PosSaleListItemDto.CashierName` now = the operator who rang the sale.

- [ ] **Step 1: Update `IPosSaleService.CreateSaleAsync` signature**

Replace `src/MyApp.Application/PosSales/IPosSaleService.cs` line 8 with:

```csharp
    Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken ct = default);
```

- [ ] **Step 2: Update the integration tests first (they express the new behavior)**

In `tests/MyApp.IntegrationTests/PosSaleServiceTests.cs`:

Change the `SeedAsync` helper (lines 25-42) to also return the shift id:

```csharp
    // Returns (userId, warehouseId, variantId, cashPaymentMethodId, shiftId), opens a shift, seeds stock.
    private static async Task<(string user, int wh, int variant, int pmCash, int shift)> SeedAsync(IServiceProvider sp, int openingQty = 100)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", $"BC{id}", 100_000m, null, 40_000m, null, null, true); // price 100k, cost 40k
        await db.SaveChangesAsync();
        if (openingQty > 0)
            await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, openingQty, 40_000m);

        var user = NewUser();
        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(user, "Rani", new OpenShiftRequest(wh.Id, 0m));
        return (user, wh.Id, variant.Id, pmCash.Id, shift.Id);
    }
```

Update each test to the new signature/return. Replace lines 44-68 (`CreateSale_reduces_stock...`):

```csharp
    [Fact]
    public async Task CreateSale_reduces_stock_writes_movement_and_snapshots_cogs()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var sale = await svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, TaxId: null, TransactionDiscount: 0m, AmountTendered: 500_000m,
            Lines: [new PosSaleLineRequest(variant, 5, 100_000m, 0m)]));

        Assert.StartsWith("POS-", sale.SaleNumber);
        Assert.Equal(500_000m, sale.GrandTotal);
        Assert.Equal("Rani", sale.CashierName);

        var onHand = await db.ProductStocks.Where(s => s.ProductVariantId == variant && s.WarehouseId == wh).SumAsync(s => s.Quantity);
        Assert.Equal(95, onHand);
        var mv = await db.StockMovements.Where(m => m.RefType == "POS" && m.ProductVariantId == variant).SingleAsync();
        Assert.Equal(-5, mv.Quantity);
        Assert.Equal(40_000m, mv.UnitCost);
        var cost = await db.ProductVariants.Where(v => v.Id == variant).Select(v => v.CostPrice).SingleAsync();
        Assert.Equal(40_000m, cost); // MA tak berubah
    }
```

Replace lines 70-86 (`CreateSale_accumulates_cash_into_shift`):

```csharp
    [Fact]
    public async Task CreateSale_accumulates_cash_into_shift()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var shiftSvc = sp.GetRequiredService<ICashierShiftService>();

        await svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 200_000m, [new PosSaleLineRequest(variant, 2, 100_000m, 0m)]));

        var reloaded = await shiftSvc.GetOpenShiftByWarehouseAsync(wh);
        Assert.Equal(200_000m, reloaded!.CashSalesTotal);
        Assert.Equal(200_000m, reloaded.TotalSalesAmount);
        Assert.Equal(1, reloaded.TransactionCount);
    }
```

Replace lines 88-105 (`CreateSale_rejects_insufficient_stock...`):

```csharp
    [Fact]
    public async Task CreateSale_rejects_insufficient_stock_without_mutation()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedAsync(sp, openingQty: 3);
        var svc = sp.GetRequiredService<IPosSaleService>();
        var db = sp.GetRequiredService<AppDbContext>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateSaleAsync(user, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 500_000m, [new PosSaleLineRequest(variant, 5, 100_000m, 0m)])));

        var onHand = await db.ProductStocks.Where(s => s.ProductVariantId == variant && s.WarehouseId == wh).SumAsync(s => s.Quantity);
        Assert.Equal(3, onHand); // tak berubah
        Assert.False(await db.PosSales.AnyAsync(s => s.WarehouseId == wh));
    }
```

Replace lines 107-117 (`CreateSale_requires_open_shift`) with a "shift must be open" test plus a new multi-user attribution test:

```csharp
    [Fact]
    public async Task CreateSale_rejects_when_shift_not_open()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, _) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateSaleAsync(user, "Rani", 999999, new CreatePosSaleRequest(
            pmCash, null, 0m, 500_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)])));
    }

    [Fact]
    public async Task CreateSale_records_operating_cashier_for_each_user_on_one_shift()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (userA, wh, variant, pmCash, shift) = await SeedAsync(sp);
        var svc = sp.GetRequiredService<IPosSaleService>();

        var s1 = await svc.CreateSaleAsync(userA, "Rani", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 100_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)]));
        // user kedua menjual ke SHIFT yang sama
        var s2 = await svc.CreateSaleAsync(NewUser(), "Sari", shift, new CreatePosSaleRequest(
            pmCash, null, 0m, 100_000m, [new PosSaleLineRequest(variant, 1, 100_000m, 0m)]));

        Assert.Equal("Rani", s1.CashierName);
        Assert.Equal("Sari", s2.CashierName);
        Assert.Equal(s1.CashierShiftId, s2.CashierShiftId);
    }
```

- [ ] **Step 3: Run the integration tests to verify they fail (compile error)**

Run: `dotnet test MyApp.slnx`
Expected: FAIL — build errors (`CreateSaleAsync` arity, `PosSale` ctor in `PosSaleService`, Web call sites). This confirms the tests target the new shape.

- [ ] **Step 4: Update `PosSaleService`**

In `src/MyApp.Infrastructure/Services/PosSaleService.cs`, change `CreateSaleAsync` (lines 42-49) — new signature and load-by-shiftId:

```csharp
    public async Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
            ?? throw Fail("Shift tidak ditemukan.");
        if (shift.Status != CashierShiftStatus.Open) throw Fail("Shift sudah ditutup.");
```

Change the `PosSale` construction (lines 83-84) to stamp the operating cashier:

```csharp
        var sale = new PosSale(await GenerateNumberAsync(now, ct), shift.Id, whId, now,
            request.PaymentMethodId, isCash, request.TaxId, taxRate, userId, userName);
```

Change `GetByIdAsync` — read the cashier from the sale itself. Replace line 117:

```csharp
        var cashierName = await db.CashierShifts.Where(s => s.Id == sale.CashierShiftId).Select(s => s.CashierName).FirstOrDefaultAsync(ct) ?? "—";
```

with:

```csharp
        var cashierName = sale.CashierName;
```

Change `GetPagedAsync` — use `PosSale.CashierName` directly. Replace the projection field on line 143 (add `s.CashierName`) and remove the shift-cashier join (lines 146-148, 155). Concretely, replace lines 141-157 with:

```csharp
        var rows = await query.OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new { s.Id, s.SaleNumber, s.SaleDate, s.CashierName, s.PaymentMethodId, s.GrandTotal })
            .ToListAsync(ct);

        var pmIds = rows.Select(r => r.PaymentMethodId).Distinct().ToList();
        var pms = await db.PaymentMethods.AsNoTracking().Where(m => pmIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name }).ToListAsync(ct);

        var items = rows.Select(r => new PosSaleListItemDto(
            r.Id, r.SaleNumber, r.SaleDate,
            r.CashierName,
            pms.FirstOrDefault(p => p.Id == r.PaymentMethodId)?.Name ?? "—",
            r.GrandTotal)).ToList();
```

- [ ] **Step 5: Update `PosRegister.razor` — shift resolution + operator header + new call**

In `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor`:

Change the header operator line (line 29) from `@_shift.CashierName` to the logged-in operator:

```razor
                    <span>@_shift.WarehouseName · @_userName</span>
```

Add an outlet-picker block. Between the `_shift is null` block (ends line 49) and the `else` (line 50), change the `@if (_shift is null)` to also handle the picker. Replace lines 39-50 with:

```razor
    @if (_pickOutlet)
    {
        <div class="pos-noshift">
            <div class="ns-card">
                <div class="ns-ic"><i class="bi bi-shop"></i></div>
                <h2>Pilih outlet</h2>
                <p>Ada beberapa shift terbuka. Pilih laci/outlet yang Anda layani.</p>
                <div class="outlet-list">
                    @foreach (var s in _openShifts)
                    {
                        <button class="btn btn-line" @onclick="() => SelectOutlet(s)">
                            <i class="bi bi-cash-stack"></i> @s.WarehouseName — <span class="mono">@s.ShiftNumber</span> (dibuka @s.CashierName)
                        </button>
                    }
                </div>
            </div>
        </div>
    }
    else if (_shift is null)
    {
        <div class="pos-noshift">
            <div class="ns-card">
                <div class="ns-ic"><i class="bi bi-lock"></i></div>
                <h2>Belum ada shift terbuka</h2>
                <p>Buka sesi kasir dulu sebelum mulai menjual.</p>
                <a class="btn btn-primary" href="/cashier/shifts"><i class="bi bi-unlock"></i> Buka Shift</a>
            </div>
        </div>
    }
    else
    {
```

Add the fields to `@code` (near line 267, after `private CashierShiftDto? _shift;`):

```csharp
    private CashierShiftDto? _shift;
    private IReadOnlyList<CashierShiftDto> _openShifts = [];
    private bool _pickOutlet;
```

Replace the shift lookup in `OnInitializedAsync` (line 300) from:

```csharp
        _shift = await Shifts.GetCurrentAsync(_userId);
```

to:

```csharp
        _openShifts = await Shifts.GetOpenShiftsAsync();
        if (_openShifts.Count == 1) _shift = _openShifts[0];
        else if (_openShifts.Count > 1) _pickOutlet = true;
```

Add the `SelectOutlet` handler (place it right after `OnInitializedAsync`, before `UpdateClock`):

```csharp
    private void SelectOutlet(CashierShiftDto s) { _shift = s; _pickOutlet = false; }
```

Update `PayAsync` — the create call (line 379) and the refresh (line 381):

```csharp
            _lastSale = await Pos.CreateSaleAsync(_userId, _userName, _shift.Id, req);
            ClearCart();
            _shift = await Shifts.GetOpenShiftByWarehouseAsync(_shift.WarehouseId);
```

- [ ] **Step 6: Update `ShiftIndex.razor` — open-shift banner + always-visible button**

In `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftIndex.razor`:

Replace the header action block (lines 24-33) so "Buka Shift" always shows for authorized users:

```razor
        <AuthorizeView Policy="cashier.shifts.create">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-primary" @onclick="() => _showOpen = !_showOpen"><i class="bi bi-unlock"></i> Buka Shift</button>
                </div>
            </Authorized>
        </AuthorizeView>
```

Replace the open-shift banner block (lines 41-49) to list every open shift. Because the "Buka Shift" button now always shows, the open form must be its **own independent** `@if (_showOpen)` block — NOT chained to the banner with `else if` (otherwise the form never renders while a shift is open in another warehouse). The original code had `@if (_current is not null) { banner } else if (_showOpen) { form }`. Replace that entire `if/else if` (lines 41-75, i.e. up to and including the `_showOpen` form's closing `}`) so it becomes two separate blocks:

```razor
    @if (_openShifts.Count > 0)
    {
        <div class="card" style="margin-bottom:14px">
            @foreach (var s in _openShifts)
            {
                <div class="card-top">
                    <span class="n"><i class="bi bi-unlock-fill text-success"></i> Shift terbuka: <b class="mono">@s.ShiftNumber</b> · @s.WarehouseName · dibuka @s.OpenedAt.ToString("dd MMM yyyy HH:mm")</span>
                    <span style="display:flex;gap:8px">
                        <a class="btn btn-primary btn-sm" href="/cashier/pos"><i class="bi bi-bag-check"></i> Masuk Kasir</a>
                        <a class="btn btn-line btn-sm" href="@($"/cashier/shifts/{s.Id}")"><i class="bi bi-box-arrow-in-right"></i> Buka detail</a>
                    </span>
                </div>
            }
        </div>
    }

    @if (_showOpen)
    {
        <div class="card" style="margin-bottom:14px">
            <div class="card-top"><span class="n"><b>Buka Shift Baru</b></span></div>
            <div style="padding:16px;display:flex;gap:14px;flex-wrap:wrap;align-items:flex-end">
                <div>
                    <label class="fl">Gudang</label>
                    <select class="ctl sel" @bind="_openWhId">
                        <option value="0">— pilih gudang —</option>
                        @foreach (var w in _warehouses)
                        {
                            <option value="@w.Id">@w.Name</option>
                        }
                    </select>
                </div>
                <div>
                    <label class="fl">Saldo Awal Kas (Rp)</label>
                    <input class="ctl mono" type="number" min="0" step="1" @bind="_openFloat" />
                </div>
                <div style="display:flex;gap:8px">
                    <button class="btn btn-primary" @onclick="OpenShiftAsync" disabled="@_busy"><i class="bi bi-check-lg"></i> Buka</button>
                    <button class="btn btn-line" @onclick="() => { _showOpen = false; _error = null; }">Batal</button>
                </div>
            </div>
        </div>
    }
```

Update the `@code` field + load. Replace `private CashierShiftDto? _current;` (line 159) with:

```csharp
    private IReadOnlyList<CashierShiftDto> _openShifts = [];
```

Replace the load line inside `LoadAsync` (line 180):

```csharp
        _openShifts = await Shifts.GetOpenShiftsAsync();
```

- [ ] **Step 7: Update `ShiftDetail.razor` — add "Masuk Kasir" for open shift**

In `src/MyApp.Web/Components/Pages/Cashier/Shifts/ShiftDetail.razor`, inside the `<div class="actions">` (lines 42-51), add a "Masuk Kasir" link shown whenever the shift is Open (any authorized cashier, not just the opener). Replace lines 42-51 with:

```razor
            <div class="actions">
                @if (_shift.Status == "Open")
                {
                    <a class="btn btn-line" href="/cashier/pos"><i class="bi bi-bag-check"></i> Masuk Kasir</a>
                }
                @if (CanClose)
                {
                    <AuthorizeView Policy="cashier.shifts.close">
                        <Authorized>
                            <button class="btn btn-primary" @onclick="() => _showClose = !_showClose" disabled="@_busy"><i class="bi bi-lock"></i> Tutup Shift</button>
                        </Authorized>
                    </AuthorizeView>
                }
            </div>
```

(`CanClose` already restricts the close button to the opener — leave it.)

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test MyApp.slnx`
Expected: PASS — all unit + integration tests, including the rewritten `CashierShiftServiceTests` (Task 2) and `PosSaleServiceTests`.

- [ ] **Step 9: Checkpoint**

No git. Confirm `dotnet build MyApp.slnx` succeeds with no warnings introduced by these files.

---

### Task 4: Single-active-cashier page lock

**Files:**
- Create: `src/MyApp.Web/Services/PosSessionRegistry.cs`
- Modify: `src/MyApp.Web/Program.cs`
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor`
- Test: `tests/MyApp.IntegrationTests/PosSessionRegistryTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MyApp.Web.Services.IPosSessionRegistry` with
  `bool TryAcquire(string userId, string token)`, `void Release(string userId, string token)`, `DateTime? ActiveSince(string userId)`.

- [ ] **Step 1: Write the failing registry test**

Create `tests/MyApp.IntegrationTests/PosSessionRegistryTests.cs`:

```csharp
using MyApp.Web.Services;
using Xunit;

namespace MyApp.IntegrationTests;

public class PosSessionRegistryTests
{
    [Fact]
    public void First_acquire_wins_second_token_is_blocked()
    {
        var reg = new PosSessionRegistry();
        Assert.True(reg.TryAcquire("u1", "tokenA"));
        Assert.False(reg.TryAcquire("u1", "tokenB")); // sesi lain diblokir
        Assert.True(reg.TryAcquire("u1", "tokenA"));  // sesi sama boleh re-acquire (reconnect)
    }

    [Fact]
    public void Release_with_wrong_token_does_not_free_the_slot()
    {
        var reg = new PosSessionRegistry();
        reg.TryAcquire("u1", "tokenA");
        reg.Release("u1", "tokenB");                   // token salah → tidak melepas
        Assert.False(reg.TryAcquire("u1", "tokenB"));
    }

    [Fact]
    public void Release_with_correct_token_frees_the_slot()
    {
        var reg = new PosSessionRegistry();
        reg.TryAcquire("u1", "tokenA");
        reg.Release("u1", "tokenA");
        Assert.True(reg.TryAcquire("u1", "tokenB"));   // slot bebas → user lain/tab lain boleh
    }

    [Fact]
    public void Different_users_are_independent()
    {
        var reg = new PosSessionRegistry();
        Assert.True(reg.TryAcquire("u1", "a"));
        Assert.True(reg.TryAcquire("u2", "b"));
    }

    [Fact]
    public void ActiveSince_reports_holder_and_null_when_free()
    {
        var reg = new PosSessionRegistry();
        Assert.Null(reg.ActiveSince("u1"));
        reg.TryAcquire("u1", "a");
        Assert.NotNull(reg.ActiveSince("u1"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test MyApp.slnx --filter FullyQualifiedName~PosSessionRegistryTests`
Expected: FAIL — build error, `PosSessionRegistry` does not exist.

- [ ] **Step 3: Implement the registry**

Create `src/MyApp.Web/Services/PosSessionRegistry.cs`:

```csharp
using System.Collections.Concurrent;

namespace MyApp.Web.Services;

/// <summary>Menegakkan "satu halaman kasir aktif per user" (in-memory, satu instance server).
/// Kunci diambil saat membuka layar POS dan dilepas saat sirkuit Blazor dibuang.</summary>
public interface IPosSessionRegistry
{
    /// <summary>Klaim slot untuk user. True bila belum ada pemegang lain, atau pemegangnya token yang sama
    /// (re-render/reconnect). False bila sesi lain (token beda) sedang memegang.</summary>
    bool TryAcquire(string userId, string token);

    /// <summary>Lepas slot hanya bila token cocok (dispose sesi lama tak mengusir sesi baru).</summary>
    void Release(string userId, string token);

    /// <summary>Kapan sesi aktif diambil (untuk pesan "dibuka sejak …"), atau null bila bebas.</summary>
    DateTime? ActiveSince(string userId);
}

public sealed class PosSessionRegistry : IPosSessionRegistry
{
    private readonly ConcurrentDictionary<string, (string Token, DateTime Since)> _sessions = new();

    public bool TryAcquire(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return false;
        var now = DateTime.Now;
        var current = _sessions.AddOrUpdate(
            userId,
            _ => (token, now),
            (_, existing) => existing); // pemegang berbeda → jangan timpa
        return current.Token == token;
    }

    public void Release(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return;
        if (_sessions.TryGetValue(userId, out var e) && e.Token == token)
            _sessions.TryRemove(new KeyValuePair<string, (string Token, DateTime Since)>(userId, e));
    }

    public DateTime? ActiveSince(string userId) =>
        _sessions.TryGetValue(userId, out var e) ? e.Since : null;
}
```

- [ ] **Step 4: Run the registry test to verify it passes**

Run: `dotnet test MyApp.slnx --filter FullyQualifiedName~PosSessionRegistryTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Register the singleton**

In `src/MyApp.Web/Program.cs`, after the SwalService registration (line 42), add:

```csharp
builder.Services.AddScoped<SwalService>();

// Kunci "satu halaman kasir aktif per user" (in-memory, satu instance server)
builder.Services.AddSingleton<MyApp.Web.Services.IPosSessionRegistry, MyApp.Web.Services.PosSessionRegistry>();
```

- [ ] **Step 6: Wire the lock into `PosRegister.razor`**

In `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor`:

Add the injection near the other `@inject` lines (after line 16):

```razor
@inject NavigationManager Nav
@inject MyApp.Web.Services.IPosSessionRegistry Sessions
```

Add a lock screen as the FIRST branch inside `<div class="pos" ...>` — insert immediately after the `</header>` (after line 37), before `@if (_pickOutlet)`:

```razor
    @if (_blocked)
    {
        <div class="pos-noshift">
            <div class="ns-card">
                <div class="ns-ic"><i class="bi bi-shield-lock"></i></div>
                <h2>Kasir sedang dibuka di tempat lain</h2>
                <p>Akun Anda sudah membuka layar kasir di tab atau perangkat lain@(_blockedSince is null ? "" : $" (sejak {_blockedSince:HH:mm})"). Satu akun hanya boleh membuka satu layar kasir.</p>
                <div style="display:flex;gap:8px;justify-content:center">
                    <button class="btn btn-primary" @onclick="RetryLockAsync"><i class="bi bi-arrow-clockwise"></i> Coba lagi</button>
                    <a class="btn btn-line" href="/cashier/shifts"><i class="bi bi-box-arrow-left"></i> Keluar</a>
                </div>
            </div>
        </div>
    }
    else if (_pickOutlet)
```

(The existing `@if (_pickOutlet)` becomes `else if (_pickOutlet)`.)

Add fields to `@code` (next to `_pickOutlet`):

```csharp
    private bool _pickOutlet;
    private bool _blocked;
    private DateTime? _blockedSince;
    private string _token = "";
```

Refactor `OnInitializedAsync` to acquire the lock first, then load. Replace the body (lines 295-308) with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        _userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        _userName = user.Identity?.Name ?? "";

        _token = Guid.NewGuid().ToString("N");
        if (!Sessions.TryAcquire(_userId, _token))
        {
            _blocked = true;
            _blockedSince = Sessions.ActiveSince(_userId);
        }
        else
        {
            await LoadSessionAsync();
        }

        UpdateClock();
        _clockTimer = new System.Threading.Timer(_ => InvokeAsync(() => { UpdateClock(); StateHasChanged(); }),
            null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private async Task LoadSessionAsync()
    {
        _openShifts = await Shifts.GetOpenShiftsAsync();
        if (_openShifts.Count == 1) _shift = _openShifts[0];
        else if (_openShifts.Count > 1) _pickOutlet = true;
        _taxes = await Taxes.GetAllAsync();
        _methods = (await PayMethods.GetAllAsync()).Where(m => m.IsActive).ToList();
        _paymentMethodId = _methods.FirstOrDefault(m => m.Type == MyApp.Domain.Entities.PaymentType.Tunai)?.Id ?? _methods.FirstOrDefault()?.Id ?? 0;
        _taxId = _taxes.FirstOrDefault()?.Id;
    }

    private async Task RetryLockAsync()
    {
        if (Sessions.TryAcquire(_userId, _token))
        {
            _blocked = false;
            _blockedSince = null;
            await LoadSessionAsync();
        }
        else
        {
            _blockedSince = Sessions.ActiveSince(_userId);
        }
    }
```

> This moves the open-shift resolution + taxes/methods loading (added in Task 3 Step 5) into `LoadSessionAsync`. Delete the now-duplicated resolution/loading lines that Task 3 placed directly in `OnInitializedAsync` (the `_openShifts = ...`, `_taxes = ...`, `_methods = ...`, `_paymentMethodId = ...`, `_taxId = ...` lines) — they now live only in `LoadSessionAsync`.

Update `Dispose` (line 399) to release the lock:

```csharp
    public void Dispose()
    {
        Sessions.Release(_userId, _token);
        _clockTimer?.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
```

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test MyApp.slnx`
Expected: PASS — all tests.

- [ ] **Step 8: Manual smoke check (single-active lock)**

Run: `dotnet run --project src/MyApp.Web` (or F5). Log in, open `/cashier/pos` in one tab (attaches to an open shift). Open `/cashier/pos` in a second tab of the same browser/session → expect the lock screen. Close the first tab, wait for its circuit to drop (or just click **Coba lagi** after the first tab is closed) → second tab acquires and loads.

- [ ] **Step 9: Checkpoint**

No git. Confirm `dotnet build MyApp.slnx` clean.

---

### Task 5: EF migration `SharedShiftPerWarehouse`

**Files:**
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/*_SharedShiftPerWarehouse.cs` (scaffold, then edit)

**Interfaces:**
- Consumes: model changes from Tasks 1-2 (new `PosSale` columns; new index name `UX_CashierShifts_Warehouse_Open`).
- Produces: a migration that swaps the shift index and adds+backfills the two `PosSale` columns.

> Tests use `EnsureCreated()` (build schema from the model), so they do NOT exercise migrations — this task is for the real SQL Server DB. It must run after Tasks 1-2 so the model reflects all changes.

- [ ] **Step 1: Scaffold the migration**

Run: `dotnet ef migrations add SharedShiftPerWarehouse -p src/MyApp.Infrastructure -s src/MyApp.Web`
Expected: creates `src/MyApp.Infrastructure/Persistence/Migrations/<timestamp>_SharedShiftPerWarehouse.cs`.

- [ ] **Step 2: Verify/replace the `Up` and `Down` bodies**

Open the generated migration. Ensure `Up` (a) drops the old per-user index, (b) adds the two columns as nullable, (c) backfills from the shift opener, (d) makes them NOT NULL, (e) creates the new per-warehouse unique filtered index. Replace `Up`/`Down` with:

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CashierShifts_CashierUserId",
            table: "CashierShifts");

        migrationBuilder.AddColumn<string>(
            name: "CashierUserId",
            table: "PosSales",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CashierName",
            table: "PosSales",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        // Backfill riwayat: kasir = pembuka shift-nya.
        migrationBuilder.Sql(@"
            UPDATE p
            SET p.CashierUserId = s.CashierUserId,
                p.CashierName    = s.CashierName
            FROM PosSales p
            INNER JOIN CashierShifts s ON s.Id = p.CashierShiftId;");

        migrationBuilder.AlterColumn<string>(
            name: "CashierUserId",
            table: "PosSales",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(450)",
            oldMaxLength: 450,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "CashierName",
            table: "PosSales",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_CashierShifts_Warehouse_Open",
            table: "CashierShifts",
            column: "WarehouseId",
            unique: true,
            filter: "[Status] = 'Open'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_CashierShifts_Warehouse_Open",
            table: "CashierShifts");

        migrationBuilder.DropColumn(name: "CashierName", table: "PosSales");
        migrationBuilder.DropColumn(name: "CashierUserId", table: "PosSales");

        migrationBuilder.CreateIndex(
            name: "IX_CashierShifts_CashierUserId",
            table: "CashierShifts",
            column: "CashierUserId",
            unique: true,
            filter: "[Status] = 'Open'");
    }
```

> If the scaffolder used a different name than `IX_CashierShifts_CashierUserId` for the old index, copy the exact name from the previous migration `20260702055641_AddCashierShift.cs` and use it in the `DropIndex`/`Down` `CreateIndex` calls.

- [ ] **Step 3: Verify it builds and the model has no pending changes**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded.

Run: `dotnet ef migrations has-pending-model-changes -p src/MyApp.Infrastructure -s src/MyApp.Web`
Expected: "No changes have been made" (the migration fully captures the model).

- [ ] **Step 4: Apply to the dev database**

> Data caveat: if the current DB already has ≥2 `Open` shifts in one warehouse, the unique index creation fails — close the extras first, then re-run.

Run: `dotnet ef database update -p src/MyApp.Infrastructure -s src/MyApp.Web`
Expected: migration applies cleanly.

- [ ] **Step 5: Checkpoint**

No git. Final: `dotnet test MyApp.slnx` → all green.

---

## Self-Review Notes

- **Spec coverage:** Domain attribution (Task 1), per-warehouse rule + open queries (Task 2), service attribution + Web wiring + opener-only close retained (Task 3), single-active lock (Task 4), migration + backfill + index swap (Task 5). ShiftDetail opener-only close already exists and is preserved.
- **Type consistency:** `CreateSaleAsync(userId, userName, shiftId, request, ct)` used identically in interface, impl, PosRegister, and tests. `GetOpenShiftsAsync`/`GetOpenShiftByWarehouseAsync` names match across interface, impl, PosRegister, ShiftIndex, and tests. Registry method names match test + component + implementation.
- **Compile-ripple honesty:** `GetCurrentAsync` removal and the ctor/signature changes break intermediate builds; Tasks 1-2 are verified against the narrowest buildable unit, and the full suite runs at the end of Task 3 onward. This is called out inline so an executor is not surprised.
