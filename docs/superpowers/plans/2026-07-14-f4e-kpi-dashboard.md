# Fase 4e — KPI Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ganti isi Home (`/`) menjadi landing sederhana, dan tambah halaman `/dashboard` berisi KPI operasional (omzet & transaksi hari ini, AR/AP jatuh tempo, PO/SO pending approval, mini aging AR/AP) plus widget produk/stok lama.

**Architecture:** Satu service pengomposisi `IDashboardService.GetAsync(asOf)` di layer Application + implementasi di Infrastructure, yang merakit `OperationalDashboardDto` dari `SalesFactProvider` (hari ini), query agregat `CustomerInvoice`/`SupplierInvoice` (AR/AP outstanding + bucket aging), count `PurchaseOrder`/`SalesOrder` berstatus `PendingApproval`, dan reuse `IProductService.GetDashboardAsync()` untuk bagian stok. Halaman Blazor `Dashboard.razor` memanggilnya sekali. Menu data-driven via `AppMenus.cs`.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core (`AppDbContext`), xUnit integration tests (SQLite `EnsureCreated` via `CustomWebApplicationFactory`). Solution: `ErpOne.slnx`.

## Global Constraints

- Solution file `ErpOne.slnx` (bukan `.sln`). Build/test: `dotnet test ErpOne.slnx`.
- Service = read-only query; TIDAK ada entity/migration baru.
- Outstanding invoice = `GrandTotal - PaidAmount` untuk `Status is Open or PartiallyPaid` (enum AR & AP identik: `Open, PartiallyPaid, Paid, Cancelled`).
- `asOf` adalah parameter; JANGAN baca `DateTime.Today` di dalam service (agar deterministik untuk test). Page yang mengirim `DateTime.Today`.
- Ikuti pola service report yang sudah ada (`Application/Reports` + `Infrastructure/Services`), registrasi DI di `src/ErpOne.Infrastructure/DependencyInjection.cs`.
- Commit dilakukan MANUAL oleh user — langkah "Commit" di plan ini adalah penanda batas task; JANGAN jalankan `git commit`/`merge`/`push`. Cukup `git add` bila perlu dan laporkan siap-commit.
- Git identity repo-local: `aliakbar893004-boop` (bila user minta commit).

---

## File Structure

- Create `src/ErpOne.Application/Dashboard/DashboardDtos.cs` — semua record DTO dashboard.
- Create `src/ErpOne.Application/Dashboard/IDashboardService.cs` — interface.
- Create `src/ErpOne.Infrastructure/Services/DashboardService.cs` — implementasi query.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `IDashboardService`.
- Create `tests/ErpOne.IntegrationTests/DashboardServiceTests.cs` — integration tests.
- Create `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor` (+ `.razor.css`) — halaman dashboard.
- Modify `src/ErpOne.Web/Components/Pages/Home.razor` (+ `Home.razor.css`) — jadikan landing.
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — tambah resource `dashboard`.

---

## Task 1: Dashboard DTOs + service interface

**Files:**
- Create: `src/ErpOne.Application/Dashboard/DashboardDtos.cs`
- Create: `src/ErpOne.Application/Dashboard/IDashboardService.cs`

**Interfaces:**
- Consumes: `ProductDashboardDto` dari `ErpOne.Application.Products`.
- Produces: `OperationalDashboardDto`, `DashboardKpis`, `PendingApprovalsDto`, `PendingDocRow`, `AgingBuckets`, dan `IDashboardService.GetAsync(DateTime asOf, CancellationToken ct = default)`.

- [ ] **Step 1: Buat file DTO**

Create `src/ErpOne.Application/Dashboard/DashboardDtos.cs`:

```csharp
using ErpOne.Application.Products;

namespace ErpOne.Application.Dashboard;

public record OperationalDashboardDto(
    DashboardKpis Kpis,
    PendingApprovalsDto Pending,
    AgingBuckets ArAging,
    AgingBuckets ApAging,
    ProductDashboardDto Stock);

public record DashboardKpis(
    decimal TodayRevenue,
    int TodayTxnCount,
    decimal ArDue,
    decimal ApDue);

public record PendingApprovalsDto(
    int PoPendingCount, IReadOnlyList<PendingDocRow> PoPending,
    int SoPendingCount, IReadOnlyList<PendingDocRow> SoPending);

public record PendingDocRow(int Id, string Number, string Party, decimal Total, DateTime Date);

public record AgingBuckets(
    decimal Current,
    decimal D31_60,
    decimal D61_90,
    decimal D90Plus,
    decimal Total);
```

- [ ] **Step 2: Buat interface**

Create `src/ErpOne.Application/Dashboard/IDashboardService.cs`:

```csharp
namespace ErpOne.Application.Dashboard;

public interface IDashboardService
{
    Task<OperationalDashboardDto> GetAsync(DateTime asOf, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build untuk memastikan kompilasi**

Run: `dotnet build src/ErpOne.Application/ErpOne.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit (penanda batas task — user commit manual)**

```bash
git add src/ErpOne.Application/Dashboard/
```

---

## Task 2: DashboardService — failing test (KPI omzet + transaksi hari ini)

**Files:**
- Create: `tests/ErpOne.IntegrationTests/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `IDashboardService.GetAsync(DateTime asOf, ...)`, `ProductDashboardDto`, seed helper meniru `SalesReportServiceTests` (`IStockService.RecordOpeningAsync`, `ICashierShiftService.OpenAsync`, `IPosSaleService.CreateSaleAsync`).
- Produces: helper `SeedSalesAsync` + test `Today_revenue_and_txn_count_from_pos_sales`.

- [ ] **Step 1: Tulis test yang gagal**

Create `tests/ErpOne.IntegrationTests/DashboardServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.Dashboard;
using ErpOne.Application.PosSales;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class DashboardServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DashboardServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    // Masters + opening stock (100 @ 1000) + open shift; returns ids for POS sales.
    private static async Task<(string user, int wh, int variant, int pmCash, int shift)> SeedSalesAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        var pmCash = new PaymentMethod($"CSH{id}", "Tunai", PaymentType.Tunai, true);
        db.Warehouses.Add(wh); db.Products.Add(product); db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 2000m, null, 0m, null, null, true);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(variant.Id, wh.Id, 100, 1000m);
        var user = NewUser();
        var shift = await sp.GetRequiredService<ICashierShiftService>().OpenAsync(user, "Rani", new OpenShiftRequest(wh.Id, 0m));
        return (user, wh.Id, variant.Id, pmCash.Id, shift.Id);
    }

    [Fact]
    public async Task Today_revenue_and_txn_count_from_pos_sales()
    {
        var today = DateTime.Today;
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, wh, variant, pmCash, shift) = await SeedSalesAsync(sp);

        // Two POS sales today: 2×2000 and 1×2000 → revenue 6000, 2 transactions.
        var pos = sp.GetRequiredService<IPosSaleService>();
        await pos.CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 4000m, [new PosSaleLineRequest(variant, 2, 2000m, 0m)]));
        await pos.CreateSaleAsync(user, "Rani", shift,
            new CreatePosSaleRequest(pmCash, null, 0m, 2000m, [new PosSaleLineRequest(variant, 1, 2000m, 0m)]));

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(today);

        Assert.Equal(6000m, dash.Kpis.TodayRevenue);
        Assert.Equal(2, dash.Kpis.TodayTxnCount);
    }
}
```

- [ ] **Step 2: Jalankan test — pastikan gagal (belum ada implementasi/DI)**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: FAIL — `IDashboardService` belum terdaftar / tidak ada implementasi (compile error atau resolve error).

---

## Task 3: DashboardService implementation + DI registration

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/DashboardService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs` (setelah baris 84, blok report services)

**Interfaces:**
- Consumes: `AppDbContext`, `SalesFactProvider.GetAsync(SalesFilter, ct)` (dari `ErpOne.Application.Reports`), `IProductService.GetDashboardAsync(ct)`, entity `CustomerInvoice`/`SupplierInvoice`/`PurchaseOrder`/`SalesOrder`, enum `PurchaseOrderStatus.PendingApproval`/`SalesOrderStatus.PendingApproval`.
- Produces: `DashboardService : IDashboardService` yang memenuhi test Task 2 dan Task 4.

- [ ] **Step 1: Tulis implementasi**

Create `src/ErpOne.Infrastructure/Services/DashboardService.cs`:

```csharp
using ErpOne.Application.Dashboard;
using ErpOne.Application.Products;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpOne.Infrastructure.Services;

public class DashboardService(
    AppDbContext db,
    SalesFactProvider sales,
    IProductService products) : IDashboardService
{
    private const int PendingListSize = 5;

    public async Task<OperationalDashboardDto> GetAsync(DateTime asOf, CancellationToken ct = default)
    {
        var day = asOf.Date;

        // KPI: omzet & transaksi hari ini (POS + B2B) via shared fact provider.
        var rows = await sales.GetAsync(new SalesFilter(day, day, null, null, null, null, null), ct);
        var todayRevenue = rows.Sum(r => r.Revenue);
        var todayTxnCount = rows.Select(r => r.DocNumber).Distinct().Count();

        // AR / AP: outstanding + aging buckets (satu query per sisi).
        var arAging = await ArAgingAsync(day, ct);
        var apAging = await ApAgingAsync(day, ct);
        var dueCutoff = day.AddDays(7);
        var arDue = await db.CustomerInvoices
            .Where(i => i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
            .Where(i => i.DueDate <= dueCutoff)
            .SumAsync(i => i.GrandTotal - i.PaidAmount, ct);
        var apDue = await db.SupplierInvoices
            .Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
            .Where(i => i.DueDate <= dueCutoff)
            .SumAsync(i => i.GrandTotal - i.PaidAmount, ct);

        // Pending approvals.
        var poPendingCount = await db.PurchaseOrders.CountAsync(p => p.Status == PurchaseOrderStatus.PendingApproval, ct);
        var poPending = await db.PurchaseOrders
            .Where(p => p.Status == PurchaseOrderStatus.PendingApproval)
            .OrderByDescending(p => p.OrderDate).Take(PendingListSize)
            .Select(p => new PendingDocRow(p.Id, p.OrderNumber, p.Supplier!.Name, p.GrandTotal, p.OrderDate))
            .ToListAsync(ct);
        var soPendingCount = await db.SalesOrders.CountAsync(s => s.Status == SalesOrderStatus.PendingApproval, ct);
        var soPending = await db.SalesOrders
            .Where(s => s.Status == SalesOrderStatus.PendingApproval)
            .OrderByDescending(s => s.OrderDate).Take(PendingListSize)
            .Select(s => new PendingDocRow(s.Id, s.OrderNumber, s.Customer!.Name, s.GrandTotal, s.OrderDate))
            .ToListAsync(ct);

        var stock = await products.GetDashboardAsync(ct);

        return new OperationalDashboardDto(
            new DashboardKpis(todayRevenue, todayTxnCount, arDue, apDue),
            new PendingApprovalsDto(poPendingCount, poPending, soPendingCount, soPending),
            arAging, apAging, stock);
    }

    private async Task<AgingBuckets> ArAgingAsync(DateTime day, CancellationToken ct)
    {
        var open = db.CustomerInvoices
            .Where(i => i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
            .Select(i => new { Age = EF.Functions.DateDiffDay(i.DueDate, day), Amount = i.GrandTotal - i.PaidAmount });
        return await BucketAsync(open, ct);
    }

    private async Task<AgingBuckets> ApAgingAsync(DateTime day, CancellationToken ct)
    {
        var open = db.SupplierInvoices
            .Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
            .Select(i => new { Age = EF.Functions.DateDiffDay(i.DueDate, day), Amount = i.GrandTotal - i.PaidAmount });
        return await BucketAsync(open, ct);
    }

    // Age = hari sejak DueDate (positif = overdue). <=30 => Current (termasuk belum jatuh tempo, age negatif).
    private static async Task<AgingBuckets> BucketAsync<T>(IQueryable<T> q, CancellationToken ct)
        where T : IAgingRow
    {
        var current = await q.Where(x => x.Age <= 30).SumAsync(x => x.Amount, ct);
        var d31 = await q.Where(x => x.Age > 30 && x.Age <= 60).SumAsync(x => x.Amount, ct);
        var d61 = await q.Where(x => x.Age > 60 && x.Age <= 90).SumAsync(x => x.Amount, ct);
        var d90 = await q.Where(x => x.Age > 90).SumAsync(x => x.Amount, ct);
        return new AgingBuckets(current, d31, d61, d90, current + d31 + d61 + d90);
    }
}
```

Note: `BucketAsync<T>` di atas memakai antarmuka `IAgingRow`, tapi projeksi anonim tidak mengimplementasikannya. Ganti pendekatan: JANGAN pakai generic — inline bucket dengan projeksi ke `decimal` age/amount. Ganti dua helper + `BucketAsync` menjadi satu helper yang menerima `IQueryable<(int Age, decimal Amount)>` melalui projeksi eksplisit. Implementasi final:

```csharp
    private async Task<AgingBuckets> ArAgingAsync(DateTime day, CancellationToken ct) =>
        await BucketAsync(db.CustomerInvoices
            .Where(i => i.Status == CustomerInvoiceStatus.Open || i.Status == CustomerInvoiceStatus.PartiallyPaid)
            .Select(i => new AgingRow(EF.Functions.DateDiffDay(i.DueDate, day), i.GrandTotal - i.PaidAmount)), ct);

    private async Task<AgingBuckets> ApAgingAsync(DateTime day, CancellationToken ct) =>
        await BucketAsync(db.SupplierInvoices
            .Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid)
            .Select(i => new AgingRow(EF.Functions.DateDiffDay(i.DueDate, day), i.GrandTotal - i.PaidAmount)), ct);

    private static async Task<AgingBuckets> BucketAsync(IQueryable<AgingRow> q, CancellationToken ct)
    {
        var current = await q.Where(x => x.Age <= 30).SumAsync(x => x.Amount, ct);
        var d31 = await q.Where(x => x.Age > 30 && x.Age <= 60).SumAsync(x => x.Amount, ct);
        var d61 = await q.Where(x => x.Age > 60 && x.Age <= 90).SumAsync(x => x.Amount, ct);
        var d90 = await q.Where(x => x.Age > 90).SumAsync(x => x.Amount, ct);
        return new AgingBuckets(current, d31, d61, d90, current + d31 + d61 + d90);
    }

    private record AgingRow(int Age, decimal Amount);
```

Hapus versi generic `BucketAsync<T>`/`IAgingRow` — pakai HANYA versi `AgingRow` di atas.

Catatan: verifikasi nama properti entity sebelum build — `PurchaseOrder.OrderNumber`/`OrderDate`/`GrandTotal`/`Supplier.Name`, `SalesOrder.OrderNumber`/`OrderDate`/`GrandTotal`/`Customer.Name`. Jika berbeda, sesuaikan projeksi (lihat Step 2).

- [ ] **Step 2: Verifikasi nama properti PO/SO yang dipakai projeksi**

Run: `grep -n "OrderNumber\|OrderDate\|GrandTotal\|public.*Supplier\b" src/ErpOne.Domain/Entities/PurchaseOrder.cs; grep -n "OrderNumber\|OrderDate\|GrandTotal\|public.*Customer\b" src/ErpOne.Domain/Entities/SalesOrder.cs`
Expected: nama muncul. Jika ada yang beda (mis. `Number` bukan `OrderNumber`, atau total bernama `Total`/`NetTotal`), sesuaikan projeksi `PendingDocRow` di `DashboardService.cs` agar sesuai. Bila navigasi `Supplier`/`Customer` tidak ada, ganti ke join `db.Suppliers`/`db.Customers` by id.

- [ ] **Step 3: Daftarkan di DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — tambah setelah baris `services.AddScoped<IGrossProfitReportService, GrossProfitReportService>();` (baris 84):

```csharp
        services.AddScoped<IDashboardService, DashboardService>();
```

Pastikan `using ErpOne.Application.Dashboard;` ada di atas file (tambahkan bila belum).

- [ ] **Step 4: Jalankan test Task 2 — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: PASS (`Today_revenue_and_txn_count_from_pos_sales`).

- [ ] **Step 5: Commit (penanda batas task)**

```bash
git add src/ErpOne.Infrastructure/Services/DashboardService.cs src/ErpOne.Infrastructure/DependencyInjection.cs
```

---

## Task 4: Tests — AR/AP outstanding, aging buckets, pending approvals

**Files:**
- Modify: `tests/ErpOne.IntegrationTests/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `ICustomerInvoiceService`/`ISupplierInvoiceService` bila mudah, atau seed entity langsung via `AppDbContext`. Gunakan pola yang sudah ada di `CustomerInvoiceServiceTests` (SO Confirmed → `ICustomerInvoiceService.CreateAsync`).
- Produces: test `Ar_ap_due_and_aging_buckets` + `Pending_po_so_counted`.

- [ ] **Step 1: Verifikasi cara termudah menyeed invoice AR/AP dengan DueDate terkontrol**

Run: `grep -n "ICustomerInvoiceService\|CreateCustomerInvoiceRequest\|DueDate\|term\|PaymentTerm" src/ErpOne.Application/CustomerInvoices/*.cs`
Expected: pahami apakah `CreateAsync` menerima `dueDate` eksplisit atau menurunkannya dari customer term. Jika DueDate = `invoiceDate + customerTermDays`, kendalikan bucket lewat `invoiceDate` (dan `asOf`) — bukan hardcode DueDate.

- [ ] **Step 2: Tulis test aging (gagal dulu)**

Tambahkan ke `DashboardServiceTests.cs`. Seed satu customer + tiga invoice AR dengan DueDate relatif `asOf`: satu belum jatuh tempo/awal (Current, mis. due = asOf), satu overdue ~45 hari (D31_60), satu overdue ~120 hari (D90Plus). Pakai `ICustomerInvoiceService` bila menerima dueDate; kalau tidak, seed `CustomerInvoice` langsung ke `AppDbContext` dengan line minimal lalu `SaveChanges`. Contoh seed langsung (sesuaikan ctor line bila berbeda — cek `CustomerInvoiceLine`):

```csharp
    private static async Task SeedArInvoiceAsync(IServiceProvider sp, int customerId, DateTime invoiceDate, DateTime dueDate, decimal amount)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var num = "ARV-TST-" + Guid.NewGuid().ToString("N")[..8];
        var inv = new CustomerInvoice(num, customerId, "IDR", invoiceDate, dueDate, null, null);
        // Satu line yang menghasilkan GrandTotal == amount. Cek ctor CustomerInvoiceLine:
        // (salesOrderId, salesOrderLineId, productVariantId, quantity, unitPrice, discountPercent, taxRateSnapshot)
        inv.SetLines([new CustomerInvoiceLine(1, 1, 1, 1, amount, 0m, 0m)]);
        db.CustomerInvoices.Add(inv);
        await db.SaveChangesAsync();
    }
```

Test:

```csharp
    [Fact]
    public async Task Ar_ap_due_and_aging_buckets()
    {
        var asOf = new DateTime(2026, 7, 14);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var cust = new Customer("CU-DTB", "PT Dash", null, null, null, null, null, 30, "IDR", 1_000_000m, true);
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        await SeedArInvoiceAsync(sp, cust.Id, asOf.AddDays(-5), asOf, 1000m);           // Current (age 0)
        await SeedArInvoiceAsync(sp, cust.Id, asOf.AddDays(-60), asOf.AddDays(-45), 2000m); // D31_60 (age 45)
        await SeedArInvoiceAsync(sp, cust.Id, asOf.AddDays(-150), asOf.AddDays(-120), 4000m); // D90Plus (age 120)

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(asOf);

        Assert.Equal(1000m, dash.ArAging.Current);
        Assert.Equal(2000m, dash.ArAging.D31_60);
        Assert.Equal(4000m, dash.ArAging.D90Plus);
        Assert.Equal(7000m, dash.ArAging.Total);
        Assert.Equal(1000m, dash.Kpis.ArDue); // hanya due <= asOf+7 (Current jatuh tempo di asOf) — overdue juga termasuk
    }
```

Catatan: `ArDue` = Σ outstanding dgn `DueDate <= asOf+7`. Ketiga invoice di atas semua DueDate ≤ asOf → semua masuk `ArDue` = 7000. Sesuaikan assert `ArDue` menjadi `Assert.Equal(7000m, dash.Kpis.ArDue);` (overdue selalu termasuk). Perbaiki nilai ekspektasi saat menulis agar konsisten dengan definisi.

- [ ] **Step 3: Jalankan — pastikan gagal lalu benahi seed hingga lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~DashboardServiceTests.Ar_ap_due_and_aging_buckets"`
Expected: awalnya bisa gagal karena ctor line/entity berbeda — sesuaikan `SeedArInvoiceAsync` (nama ctor param `CustomerInvoiceLine`, apakah butuh `ProductVariant`/`SalesOrder` FK valid). Jika FK memaksa data referensial, seed via `ICustomerInvoiceService` dari SO Confirmed (pola `CustomerInvoiceServiceTests.SeedConfirmedSoAsync`) dan atur DueDate lewat invoiceDate. Ulangi hingga PASS.

- [ ] **Step 4: Tulis test pending PO/SO**

Tambahkan (pakai service PO/SO agar sampai status `PendingApproval`; cek `IPurchaseOrderService`/`ISalesOrderService.SubmitAsync`. Jika chain kosong auto-confirm — seperti SO di `CustomerInvoiceServiceTests` — maka jangan submit; set status via entity `Submit()` lalu SaveChanges, atau konfigurasi approval-chain agar tetap PendingApproval). Verifikasi dulu:

Run: `grep -rn "PendingApproval\|SubmitAsync\|auto-confirm\|MarkConfirmed" src/ErpOne.Infrastructure/Services/PurchaseOrderService.cs`

Lalu test:

```csharp
    [Fact]
    public async Task Pending_po_so_counted()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        // Seed satu PO + satu SO berstatus PendingApproval (cara tergantung verifikasi Step 4).
        // ... seed ...
        await db.SaveChangesAsync();

        var dash = await sp.GetRequiredService<IDashboardService>().GetAsync(DateTime.Today);

        Assert.Equal(1, dash.Pending.PoPendingCount);
        Assert.Equal(1, dash.Pending.SoPendingCount);
        Assert.Single(dash.Pending.PoPending);
        Assert.Single(dash.Pending.SoPending);
    }
```

Isi bagian seed berdasarkan hasil verifikasi (cara mencapai `PendingApproval` yang paling ringkas — kemungkinan besar: buat entity, panggil `.Submit()`, `db.Add`, `SaveChanges`).

- [ ] **Step 5: Jalankan semua test DashboardService — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: PASS semua (3 test).

- [ ] **Step 6: Commit (penanda batas task)**

```bash
git add tests/ErpOne.IntegrationTests/DashboardServiceTests.cs
```

---

## Task 5: Menu resource `dashboard`

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs:38-41`

**Interfaces:**
- Consumes: pola `AppResource` + `ViewOnly`.
- Produces: resource `dashboard` (permission `dashboard.index`), route otomatis `/dashboard` via `NavMenuBuilder` (key `dashboard` → href `dashboard`).

- [ ] **Step 1: Tambah resource ke grup teratas**

Modify blok grup pertama (`GroupLabel = null`) di `AppMenus.cs` sehingga menjadi:

```csharp
        new(null,
        [
            new("dashboard", "Dashboard", "bi-speedometer2", ViewOnly),
            new("home", "Home", "bi-house-door-fill", ViewOnly),
        ]),
```

Tidak perlu override di `NavMenuBuilder` — key `dashboard` → href `dashboard` (`/dashboard`) sudah benar via konvensi default.

- [ ] **Step 2: Build Web project**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded. (Permission `dashboard.index` otomatis masuk `AppMenus.AllPermissions`; BootstrapSeeder memberikannya ke admin saat startup.)

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 6: Dashboard page

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor`
- Create: `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor.css`

**Interfaces:**
- Consumes: `IDashboardService.GetAsync(DateTime.Today)`, `OperationalDashboardDto`, `ProductStatus` (untuk bagian stok), `NavigationManager`.
- Produces: halaman route `/dashboard`.

- [ ] **Step 1: Tulis halaman**

Create `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor`. Baris KPI + pending + mini-aging baru, lalu bagian stok DIPINDAH dari `Home.razor` (Products by Status, Stock by Category, Low/Out of Stock table) menggunakan `_data.Stock` sebagai sumber (ganti referensi `_data.X` lama menjadi `_data.Stock.X`). Struktur:

```razor
@page "/dashboard"
@rendermode InteractiveServer
@using ErpOne.Application.Dashboard
@using ErpOne.Domain.Entities
@inject IDashboardService DashboardService
@inject NavigationManager Nav

<PageTitle>Dashboard</PageTitle>

<div class="dash">
    <div class="dash-hero">
        <div class="dash-hero-tx">
            <div class="eyebrow">Overview</div>
            <h1>Dashboard</h1>
            <p>Ringkasan operasional hari ini</p>
        </div>
    </div>

    @if (_data is null)
    {
        <div class="text-center py-5 text-muted"><div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...</div>
    }
    else
    {
        <div class="cr-kpis">
            <div class="cr-kpi" style="cursor:pointer" @onclick="@(() => Nav.NavigateTo("/reports/sales"))">
                <div class="ic"><i class="bi bi-cash-coin"></i></div>
                <div class="tx"><div class="v">Rp @_data.Kpis.TodayRevenue.ToString("N0")</div><div class="l">Omzet Hari Ini</div></div>
            </div>
            <div class="cr-kpi" style="cursor:pointer" @onclick="@(() => Nav.NavigateTo("/reports/sales"))">
                <div class="ic"><i class="bi bi-receipt"></i></div>
                <div class="tx"><div class="v">@_data.Kpis.TodayTxnCount.ToString("N0")</div><div class="l">Transaksi Hari Ini</div></div>
            </div>
            <div class="cr-kpi accent" style="cursor:pointer" @onclick="@(() => Nav.NavigateTo("/finance/ar-invoices"))">
                <div class="ic"><i class="bi bi-arrow-down-left-circle"></i></div>
                <div class="tx"><div class="v">Rp @_data.Kpis.ArDue.ToString("N0")</div><div class="l">Piutang Jatuh Tempo</div></div>
            </div>
            <div class="cr-kpi" style="cursor:pointer" @onclick="@(() => Nav.NavigateTo("/finance/ap-invoices"))">
                <div class="ic"><i class="bi bi-arrow-up-right-circle"></i></div>
                <div class="tx"><div class="v">Rp @_data.Kpis.ApDue.ToString("N0")</div><div class="l">Hutang Jatuh Tempo</div></div>
            </div>
        </div>

        <div class="row g-4 mt-1">
            <div class="col-12 col-lg-6">
                @PendingCard("Purchase Order Pending", "bi-cart-plus-fill", _data.Pending.PoPendingCount, _data.Pending.PoPending, "/transactions/purchase-orders")
            </div>
            <div class="col-12 col-lg-6">
                @PendingCard("Sales Order Pending", "bi-bag-check-fill", _data.Pending.SoPendingCount, _data.Pending.SoPending, "/transactions/sales-orders")
            </div>

            <div class="col-12 col-lg-6">
                @AgingCard("Aging Piutang (AR)", "bi-arrow-down-left-circle", _data.ArAging)
            </div>
            <div class="col-12 col-lg-6">
                @AgingCard("Aging Hutang (AP)", "bi-arrow-up-right-circle", _data.ApAging)
            </div>
        </div>

        @* ==== Bagian stok (dipindah dari Home) ==== *@
        <div class="row g-4 mt-1">
            @* Products by Status + mini-alert + Stock by Category + Low/Out table:
               salin dari Home.razor, ganti `_data.` menjadi `_data.Stock.` *@
        </div>
    }
</div>

@code {
    private OperationalDashboardDto? _data;

    protected override async Task OnInitializedAsync() =>
        _data = await DashboardService.GetAsync(DateTime.Today);

    private void GoToProduct(int id) => Nav.NavigateTo($"/master/products/{id}/edit");

    private RenderFragment PendingCard(string title, string icon, int count, IReadOnlyList<PendingDocRow> rows, string href) => __builder =>
    {
        <div class="h-card h-100">
            <div class="h-card-h" style="cursor:pointer" @onclick="@(() => Nav.NavigateTo(href))">
                <i class="bi @icon me-2"></i>@title
                <span class="badge bg-warning text-dark ms-2">@count</span>
            </div>
            <div class="h-card-b p-0">
                @if (rows.Count == 0)
                {
                    <div class="text-muted small p-4 text-center"><i class="bi bi-check2-circle me-1"></i>Tidak ada yang menunggu approval.</div>
                }
                else
                {
                    <table class="table table-hover align-middle mb-0">
                        <tbody>
                            @foreach (var r in rows)
                            {
                                <tr>
                                    <td class="ps-3 fw-medium">@r.Number</td>
                                    <td class="text-muted small">@r.Party</td>
                                    <td class="text-end pe-3">Rp @r.Total.ToString("N0")</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                }
            </div>
        </div>
    };

    private RenderFragment AgingCard(string title, string icon, AgingBuckets a) => __builder =>
    {
        var max = new[] { a.Current, a.D31_60, a.D61_90, a.D90Plus }.DefaultIfEmpty(0m).Max();
        <div class="h-card h-100">
            <div class="h-card-h"><i class="bi @icon me-2"></i>@title
                <span class="text-muted fw-normal ms-2 small">Total Rp @a.Total.ToString("N0")</span></div>
            <div class="h-card-b">
                @AgingBar("0–30", a.Current, max, "grad-green")
                @AgingBar("31–60", a.D31_60, max, "grad-amber")
                @AgingBar("61–90", a.D61_90, max, "grad-amber")
                @AgingBar("90+", a.D90Plus, max, "grad-dark")
            </div>
        </div>
    };

    private RenderFragment AgingBar(string label, decimal value, decimal max, string grad) => __builder =>
    {
        var pct = max == 0 ? 0 : (int)Math.Round((double)(value / max) * 100);
        <div class="mb-3">
            <div class="d-flex justify-content-between small mb-1">
                <span class="fw-medium">@label</span>
                <span class="text-muted">Rp @value.ToString("N0")</span>
            </div>
            <div class="bar"><div class="bar-fill @grad" style="width:@(Math.Max(pct, value > 0 ? 3 : 0))%"></div></div>
        </div>
    };
}
```

Untuk bagian stok, salin markup Products by Status / mini-alert / Stock by Category / Low-Out table dari `Home.razor` (baris ~43–166) ke dalam blok `@* Bagian stok *@`, ganti setiap `_data.TotalProducts`→`_data.Stock.TotalProducts`, `_data.ByStatus`→`_data.Stock.ByStatus`, dst. Bawa juga helper `StatusDot`/`StatusBar`/`StatusBadge` dari `Home.razor` ke `@code` blok ini.

- [ ] **Step 2: Buat CSS**

Create `src/ErpOne.Web/Components/Pages/Dashboard/Dashboard.razor.css` — salin isi `Home.razor.css` apa adanya (berisi `.dash`, `.dash-hero`, `.h-card`, `.bar`, `.mini-alert`, `.thumb`, `grad-*`, `dot-*`). Nanti Task 7 merampingkan `Home.razor.css`.

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded (tidak ada error Razor / referensi properti).

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Dashboard/
```

---

## Task 7: Home jadi landing sederhana

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Home.razor` (ganti total isi)
- Modify: `src/ErpOne.Web/Components/Pages/Home.razor.css` (rampingkan)

**Interfaces:**
- Consumes: `NavigationManager` (opsional untuk tombol). Tidak lagi inject `IProductService`.
- Produces: `/` = halaman sambutan dengan tautan ke Dashboard & modul.

- [ ] **Step 1: Ganti Home.razor jadi landing**

Replace seluruh isi `src/ErpOne.Web/Components/Pages/Home.razor`:

```razor
@page "/"
@rendermode InteractiveServer

<PageTitle>ERP_One</PageTitle>

<div class="landing">
    <div class="landing-hero">
        <div class="eyebrow">Selamat datang</div>
        <h1>ERP_One</h1>
        <p>Pilih modul untuk mulai bekerja.</p>
    </div>

    <div class="landing-grid">
        <a class="landing-card" href="/dashboard"><i class="bi bi-speedometer2"></i><span>Dashboard</span></a>
        <a class="landing-card" href="/master/products"><i class="bi bi-box-seam-fill"></i><span>Produk</span></a>
        <a class="landing-card" href="/transactions"><i class="bi bi-grid-1x2-fill"></i><span>Transaksi</span></a>
        <a class="landing-card" href="/cashier/pos"><i class="bi bi-bag-check-fill"></i><span>Kasir (POS)</span></a>
        <a class="landing-card" href="/finance/ar-invoices"><i class="bi bi-receipt-cutoff"></i><span>Finance</span></a>
        <a class="landing-card" href="/reports/sales"><i class="bi bi-graph-up-arrow"></i><span>Reports</span></a>
    </div>
</div>
```

- [ ] **Step 2: Ganti Home.razor.css jadi gaya landing**

Replace isi `src/ErpOne.Web/Components/Pages/Home.razor.css` dengan gaya minimal untuk `.landing`/`.landing-hero`/`.landing-grid`/`.landing-card` (grid kartu tautan; reuse token warna yang ada). Contoh minimal:

```css
.landing { max-width: 960px; margin: 0 auto; padding: 1rem; }
.landing-hero { margin-bottom: 1.5rem; }
.landing-hero .eyebrow { text-transform: uppercase; letter-spacing: .08em; font-size: .72rem; color: var(--bs-secondary-color); }
.landing-hero h1 { font-weight: 700; }
.landing-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 1rem; }
.landing-card { display: flex; flex-direction: column; align-items: center; gap: .5rem; padding: 1.5rem 1rem;
    border: 1px solid var(--bs-border-color); border-radius: .75rem; text-decoration: none; color: inherit;
    background: var(--bs-body-bg); transition: transform .12s ease, box-shadow .12s ease; }
.landing-card:hover { transform: translateY(-2px); box-shadow: 0 6px 18px rgba(0,0,0,.08); }
.landing-card i { font-size: 1.6rem; }
.landing-card span { font-weight: 600; font-size: .9rem; }
```

- [ ] **Step 3: Build + jalankan seluruh test suite**

Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS (jumlah sebelumnya 149 + test dashboard baru). Tidak ada test yang meng-assert jumlah menu resource (sudah dicek — tidak ada), jadi tidak ada angka yang perlu dinaikkan.

- [ ] **Step 4: Verifikasi manual (skill `run`/`verify`)**

Jalankan app, sign out/in (agar admin dapat permission `dashboard.index`), buka `/` (landing) lalu `/dashboard` — pastikan KPI, pending, aging, dan bagian stok tampil tanpa error.

- [ ] **Step 5: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Home.razor src/ErpOne.Web/Components/Pages/Home.razor.css
```

---

## Self-Review (checklist untuk penulis plan)

**Spec coverage:**
- Landing `/` + `/dashboard` menu terpisah → Task 5, 6, 7. ✓
- KPI omzet/transaksi/AR due/AP due → Task 2/3 + Task 6. ✓
- PO/SO pending → Task 3 + Task 4 + Task 6. ✓
- Mini aging ringan (satu query per sisi, GROUP BY umur) → Task 3 (`BucketAsync`) + Task 6 (`AgingCard`). ✓
- Widget stok lama dipindah → Task 6 Step 1. ✓
- Menu `dashboard` + permission + seeder → Task 5. ✓
- Testing → Task 2 & 4. ✓
- `asOf` parameter (deterministik) → Task 1/3. ✓

**Placeholder scan:** Bagian seed di Task 4 Step 4 sengaja bergantung pada verifikasi Step 1/Step 4 (cara mencapai `PendingApproval`/DueDate berbeda tergantung API service) — ini instruksi verifikasi, bukan placeholder kode inti. Semua kode service (Task 3) & page (Task 6) lengkap.

**Type consistency:** `OperationalDashboardDto`/`DashboardKpis`/`PendingApprovalsDto`/`PendingDocRow`/`AgingBuckets` dipakai identik di Task 1, 3, 6. `GetAsync(DateTime asOf, CancellationToken)` konsisten di interface (Task 1), impl (Task 3), test (Task 2/4), page (Task 6). `AgingRow` privat hanya di service. ⚠ Catatan implementer: nama properti PO/SO (`OrderNumber`/`OrderDate`/`GrandTotal`/nav `Supplier`/`Customer`) WAJIB diverifikasi di Task 3 Step 2 sebelum build — bila beda, sesuaikan projeksi.
