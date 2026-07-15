# Fase 4c — AR/AP Aging Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dua laporan aging berdiri sendiri — Piutang (AR, `/reports/ar-aging`) & Hutang (AP, `/reports/ap-aging`) — dihitung point-in-time per tanggal "as of", dikelompokkan per party dengan subtotal + grand total, plus export Excel/PDF.

**Architecture:** Satu service bersama `IAgingReportService` (metode AR & AP simetris) di layer Application, implementasi di Infrastructure yang menghitung outstanding point-in-time = `GrandTotal − Σ alokasi ber-tanggal ≤ asOf` lalu mem-bucket-kan (`Not Due | 1–30 | 31–60 | 61–90 | 90+`) berdasar hari lewat `DueDate`. Dua halaman Blazor memanggil `Get…Async` (tampilan) dan `Build…ReportAsync` (export). Mengikuti pola `InventoryValuationReportService` + `InventoryValuationIndex.razor`.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core (`AppDbContext`), xUnit integration tests (SQLite `EnsureCreated` via `CustomWebApplicationFactory`). Solution: `ErpOne.slnx`. Export lewat `IReportExporter` (ClosedXML + QuestPDF) yang sudah ada.

## Global Constraints

- Solution file `ErpOne.slnx` (bukan `.sln`). Build/test: `dotnet test ErpOne.slnx`.
- Service = query read-only; **TIDAK ada entity/migration baru**.
- Reuse `ReportDocument`/`IReportExporter` — jangan bikin exporter baru.
- `asOf` parameter eksplisit; service TIDAK membaca `DateTime.Today` (deterministik untuk test). Halaman mengirim `DateTime.Today`.
- Filter tanggal pakai pola `toExclusive = asOf.Date.AddDays(1); … < toExclusive` (sama seperti `InventoryValuationReportService`).
- Outstanding point-in-time = `GrandTotal − Σ Amount alokasi` di mana parent receipt/payment `Status == Posted` **dan** tanggalnya `< toExclusive`. Faktur kandidat: `InvoiceDate < toExclusive` dan `Status != Cancelled` (Paid tetap dipertimbangkan — point-in-time bisa masih outstanding). Simpan hanya `outstanding > 0`.
- Service file `namespace ErpOne.Infrastructure.Services;` (walau di folder `Services/Reports`), sama seperti report service lain.
- Commit MANUAL oleh user — langkah "Commit" di plan hanya penanda batas task; JANGAN jalankan `git commit`/`merge`/`push`. Boleh `git add`. Git identity repo-local `aliakbar893004-boop`.

---

## File Structure

- Create `src/ErpOne.Application/Reports/AgingDtos.cs` — DTO bersama (`AgingSide`, `AgingBucketSet`, `AgingInvoiceDto`, `AgingPartyDto`, `AgingResultDto`).
- Create `src/ErpOne.Application/Reports/IAgingReportService.cs` — interface AR + AP.
- Create `src/ErpOne.Infrastructure/Services/Reports/AgingReportService.cs` — implementasi.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `IAgingReportService` (setelah baris 85).
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — 2 resource di grup Reports (setelah baris 91).
- Create `src/ErpOne.Web/Components/Pages/Reports/Aging/ArAgingIndex.razor` → `/reports/ar-aging`.
- Create `src/ErpOne.Web/Components/Pages/Reports/Aging/ApAgingIndex.razor` → `/reports/ap-aging`.
- Create `tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs`.

---

## Task 1: DTOs + service interface

**Files:**
- Create: `src/ErpOne.Application/Reports/AgingDtos.cs`
- Create: `src/ErpOne.Application/Reports/IAgingReportService.cs`

**Interfaces:**
- Produces: `AgingSide`, `AgingBucketSet`, `AgingInvoiceDto`, `AgingPartyDto`, `AgingResultDto`, dan `IAgingReportService` dengan 4 metode (`GetArAgingAsync`, `GetApAgingAsync`, `BuildArAgingReportAsync`, `BuildApAgingReportAsync`). Dipakai identik di Task 3 (impl), Task 2/4 (test), Task 6/7 (page).
- Consumes: `ReportDocument` dari `ErpOne.Application.Reports`.

- [ ] **Step 1: Buat file DTO**

Create `src/ErpOne.Application/Reports/AgingDtos.cs`:

```csharp
namespace ErpOne.Application.Reports;

public enum AgingSide { Receivable, Payable }

// Outstanding satu faktur seluruhnya jatuh ke TEPAT satu bucket (sisanya 0); Total = Outstanding.
public record AgingBucketSet(
    decimal NotDue, decimal D1_30, decimal D31_60, decimal D61_90, decimal D90Plus, decimal Total);

public record AgingInvoiceDto(
    int InvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate,
    int DaysPastDue, decimal GrandTotal, decimal Outstanding, AgingBucketSet Buckets);

public record AgingPartyDto(
    int PartyId, string PartyCode, string PartyName,
    IReadOnlyList<AgingInvoiceDto> Invoices, AgingBucketSet Subtotals);

public record AgingResultDto(
    DateTime AsOf, AgingSide Side, IReadOnlyList<AgingPartyDto> Parties,
    AgingBucketSet GrandTotals, int InvoiceCount, int PartyCount);
```

- [ ] **Step 2: Buat interface**

Create `src/ErpOne.Application/Reports/IAgingReportService.cs`:

```csharp
namespace ErpOne.Application.Reports;

public interface IAgingReportService
{
    Task<AgingResultDto> GetArAgingAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<AgingResultDto> GetApAgingAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
    Task<ReportDocument> BuildArAgingReportAsync(DateTime asOf, int? customerId, CancellationToken ct = default);
    Task<ReportDocument> BuildApAgingReportAsync(DateTime asOf, int? supplierId, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build untuk memastikan kompilasi**

Run: `dotnet build src/ErpOne.Application/ErpOne.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Application/Reports/AgingDtos.cs src/ErpOne.Application/Reports/IAgingReportService.cs
```

---

## Task 2: Failing test — AR buckets + point-in-time outstanding

**Files:**
- Create: `tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs`

**Interfaces:**
- Consumes: `IAgingReportService.GetArAgingAsync`, `ISalesOrderService.CreateAsync/SubmitAsync`, `ICustomerInvoiceService.CreateAsync`, `ICustomerReceiptService.CreateAsync`, `ICashBankAccountService.CreateAsync`. Pola seed dari `CustomerReceiptServiceTests`.
- Produces: helper `SeedCustomerAsync`, `SeedArInvoiceAsync`, `AddReceiptAsync` + test `Ar_point_in_time_buckets_exclude_post_asof_receipts`.

- [ ] **Step 1: Tulis test yang gagal**

Create `tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.CustomerReceipts;
using ErpOne.Application.Reports;
using ErpOne.Application.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AgingReportServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AgingReportServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Id() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // customer + warehouse + product/variant + IDR cash account; returns ids reused across invoices.
    private static async Task<(int customerId, int accountId, int wh, int variant)> SeedCustomerAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id();
        var customer = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 100_000_000m, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Customers.Add(customer); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();
        var acc = await sp.GetRequiredService<ICashBankAccountService>()
            .CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));
        return (customer.Id, acc.Id, wh.Id, variant.Id);
    }

    // One SO (auto-confirmed) → one Open customer invoice with explicit dueDate; GrandTotal = qty*price.
    private static async Task<(int invoiceId, decimal grand)> SeedArInvoiceAsync(
        IServiceProvider sp, int customerId, int wh, int variant, DateTime invoiceDate, DateTime dueDate, int qty, decimal price)
    {
        var soSvc = sp.GetRequiredService<ISalesOrderService>();
        var so = await soSvc.CreateAsync(new CreateSalesOrderRequest(customerId, wh, invoiceDate, null, null,
            [new SalesOrderLineRequest(variant, qty, price, 0m, null)]));
        await soSvc.SubmitAsync(so.Id); // empty approval chain → auto-confirms
        var inv = await sp.GetRequiredService<ICustomerInvoiceService>()
            .CreateAsync(new CreateCustomerInvoiceRequest(customerId, invoiceDate, dueDate, null, null, [so.Id]));
        return (inv.Id, inv.GrandTotal);
    }

    private static async Task AddReceiptAsync(IServiceProvider sp, int customerId, int accountId, int invoiceId, DateTime date, decimal amount) =>
        await sp.GetRequiredService<ICustomerReceiptService>().CreateAsync(
            new CreateCustomerReceiptRequest(customerId, accountId, date, null, [new ReceiptAllocationInput(invoiceId, amount)]));

    [Fact]
    public async Task Ar_point_in_time_buckets_exclude_post_asof_receipts()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (customerId, accountId, wh, variant) = await SeedCustomerAsync(sp);

        // A: due 15 days ago → bucket 1–30. Two receipts: 4000 before asOf (counted), 6000 after (ignored).
        var (invA, _) = await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-40), asOf.AddDays(-15), 10, 1000m); // grand 10000
        await AddReceiptAsync(sp, customerId, accountId, invA, asOf.AddDays(-5), 4000m);
        await AddReceiptAsync(sp, customerId, accountId, invA, asOf.AddDays(5), 6000m);

        // B: due 10 days in the future → Not Due, no receipts. grand 10000.
        await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-30), asOf.AddDays(10), 10, 1000m);

        // C: due 100 days ago → 90+, fully paid BEFORE asOf → excluded (outstanding 0 point-in-time).
        var (invC, _) = await SeedArInvoiceAsync(sp, customerId, wh, variant, asOf.AddDays(-110), asOf.AddDays(-100), 10, 1000m);
        await AddReceiptAsync(sp, customerId, accountId, invC, asOf.AddDays(-90), 10000m);

        var r = await sp.GetRequiredService<IAgingReportService>().GetArAgingAsync(asOf, customerId);

        Assert.Equal(AgingSide.Receivable, r.Side);
        Assert.Equal(2, r.InvoiceCount);                 // A (6000) + B (10000); C excluded
        Assert.Single(r.Parties);                        // one customer
        Assert.Equal(10_000m, r.GrandTotals.NotDue);     // B
        Assert.Equal(6_000m, r.GrandTotals.D1_30);       // A after the pre-asOf receipt only
        Assert.Equal(0m, r.GrandTotals.D90Plus);         // C gone
        Assert.Equal(16_000m, r.GrandTotals.Total);
    }
}
```

- [ ] **Step 2: Jalankan test — pastikan gagal**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AgingReportServiceTests"`
Expected: FAIL — `IAgingReportService` belum terdaftar / tak ada implementasi (resolve atau compile error).

---

## Task 3: AgingReportService implementation + DI registration

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Reports/AgingReportService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs` (setelah baris 85, `IGrossProfitReportService`)

**Interfaces:**
- Consumes: `AppDbContext` (`CustomerInvoices`, `CustomerReceiptAllocations`, `CustomerReceipts`, `Customers`, `SupplierInvoices`, `SupplierPaymentAllocations`, `SupplierPayments`, `Suppliers`), enum `CustomerInvoiceStatus`/`SupplierInvoiceStatus`/`CustomerReceiptStatus`/`SupplierPaymentStatus`, `ReportDocument`/`ReportColumn`/`ReportRow`/`ReportAlign`.
- Produces: `AgingReportService : IAgingReportService` yang memenuhi test Task 2 & Task 4.

- [ ] **Step 1: Tulis implementasi**

Create `src/ErpOne.Infrastructure/Services/Reports/AgingReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class AgingReportService(AppDbContext db) : IAgingReportService
{
    public async Task<AgingResultDto> GetArAgingAsync(DateTime asOf, int? customerId, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        var invoices = await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate < toExclusive && i.Status != CustomerInvoiceStatus.Cancelled)
            .Where(i => customerId == null || i.CustomerId == customerId)
            .Select(i => new InvoiceRow(i.Id, i.InvoiceNumber, i.CustomerId, i.InvoiceDate, i.DueDate, i.GrandTotal))
            .ToListAsync(ct);

        var paid = await (
            from a in db.CustomerReceiptAllocations.AsNoTracking()
            join r in db.CustomerReceipts.AsNoTracking() on a.CustomerReceiptId equals r.Id
            where r.Status == CustomerReceiptStatus.Posted && r.ReceiptDate < toExclusive
            group a.Amount by a.CustomerInvoiceId into g
            select new { InvoiceId = g.Key, Paid = g.Sum() })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        var partyIds = invoices.Select(i => i.PartyId).Distinct().ToList();
        var parties = await db.Customers.AsNoTracking()
            .Where(c => partyIds.Contains(c.Id))
            .Select(c => new PartyInfo(c.Id, c.Code, c.Name))
            .ToListAsync(ct);

        return Build(asOf.Date, AgingSide.Receivable, invoices, paid, parties);
    }

    public async Task<AgingResultDto> GetApAgingAsync(DateTime asOf, int? supplierId, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        var invoices = await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate < toExclusive && i.Status != SupplierInvoiceStatus.Cancelled)
            .Where(i => supplierId == null || i.SupplierId == supplierId)
            .Select(i => new InvoiceRow(i.Id, i.InvoiceNumber, i.SupplierId, i.InvoiceDate, i.DueDate, i.GrandTotal))
            .ToListAsync(ct);

        var paid = await (
            from a in db.SupplierPaymentAllocations.AsNoTracking()
            join p in db.SupplierPayments.AsNoTracking() on a.SupplierPaymentId equals p.Id
            where p.Status == SupplierPaymentStatus.Posted && p.PaymentDate < toExclusive
            group a.Amount by a.SupplierInvoiceId into g
            select new { InvoiceId = g.Key, Paid = g.Sum() })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        var partyIds = invoices.Select(i => i.PartyId).Distinct().ToList();
        var parties = await db.Suppliers.AsNoTracking()
            .Where(s => partyIds.Contains(s.Id))
            .Select(s => new PartyInfo(s.Id, s.Code, s.Name))
            .ToListAsync(ct);

        return Build(asOf.Date, AgingSide.Payable, invoices, paid, parties);
    }

    public async Task<ReportDocument> BuildArAgingReportAsync(DateTime asOf, int? customerId, CancellationToken ct = default)
    {
        var result = await GetArAgingAsync(asOf, customerId, ct);
        var filter = customerId is null ? "All customers" : $"Customer: {result.Parties.FirstOrDefault()?.PartyName ?? $"#{customerId}"}";
        return ToReportDocument(result, "AR Aging", filter);
    }

    public async Task<ReportDocument> BuildApAgingReportAsync(DateTime asOf, int? supplierId, CancellationToken ct = default)
    {
        var result = await GetApAgingAsync(asOf, supplierId, ct);
        var filter = supplierId is null ? "All suppliers" : $"Supplier: {result.Parties.FirstOrDefault()?.PartyName ?? $"#{supplierId}"}";
        return ToReportDocument(result, "AP Aging", filter);
    }

    private static AgingResultDto Build(DateTime day, AgingSide side,
        List<InvoiceRow> invoices, Dictionary<int, decimal> paidByInvoice, List<PartyInfo> parties)
    {
        var partyById = parties.ToDictionary(p => p.Id);

        var aged = new List<(int PartyId, AgingInvoiceDto Dto)>();
        foreach (var inv in invoices)
        {
            var paid = paidByInvoice.TryGetValue(inv.Id, out var p) ? p : 0m;
            var outstanding = inv.GrandTotal - paid;
            if (outstanding <= 0m) continue;
            var days = (day - inv.DueDate.Date).Days;
            aged.Add((inv.PartyId, new AgingInvoiceDto(
                inv.Id, inv.Number, inv.InvoiceDate, inv.DueDate, days, inv.GrandTotal, outstanding, BucketOf(days, outstanding))));
        }

        var partyDtos = aged
            .GroupBy(x => x.PartyId)
            .Select(g =>
            {
                var info = partyById.TryGetValue(g.Key, out var pi) ? pi : new PartyInfo(g.Key, "?", "(unknown)");
                var invs = g.Select(x => x.Dto).OrderBy(d => d.DueDate).ToList();
                return new AgingPartyDto(info.Id, info.Code, info.Name, invs, Sum(invs.Select(d => d.Buckets)));
            })
            .OrderBy(p => p.PartyName)
            .ToList();

        return new AgingResultDto(day, side, partyDtos, Sum(partyDtos.Select(p => p.Subtotals)), aged.Count, partyDtos.Count);
    }

    private static AgingBucketSet BucketOf(int daysPastDue, decimal amount) => daysPastDue switch
    {
        <= 0  => new(amount, 0, 0, 0, 0, amount),
        <= 30 => new(0, amount, 0, 0, 0, amount),
        <= 60 => new(0, 0, amount, 0, 0, amount),
        <= 90 => new(0, 0, 0, amount, 0, amount),
        _     => new(0, 0, 0, 0, amount, amount),
    };

    private static AgingBucketSet Sum(IEnumerable<AgingBucketSet> sets) =>
        sets.Aggregate(new AgingBucketSet(0, 0, 0, 0, 0, 0),
            (a, b) => new(a.NotDue + b.NotDue, a.D1_30 + b.D1_30, a.D31_60 + b.D31_60,
                a.D61_90 + b.D61_90, a.D90Plus + b.D90Plus, a.Total + b.Total));

    private static ReportDocument ToReportDocument(AgingResultDto r, string title, string filterSummary)
    {
        var rows = new List<ReportRow>();
        foreach (var p in r.Parties)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"▸ {p.PartyCode} — {p.PartyName}", "", "", "", "", "", "", "", "", "", ""] });
            foreach (var i in p.Invoices)
                rows.Add(new ReportRow { Cells = [p.PartyName, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.DaysPastDue,
                    i.Buckets.NotDue, i.Buckets.D1_30, i.Buckets.D31_60, i.Buckets.D61_90, i.Buckets.D90Plus, i.Outstanding] });
            var s = p.Subtotals;
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"{p.PartyName} subtotal", "", "", "", "",
                s.NotDue, s.D1_30, s.D31_60, s.D61_90, s.D90Plus, s.Total] });
        }

        var g = r.GrandTotals;
        return new ReportDocument
        {
            Title = title,
            Subtitle = $"As of {r.AsOf:d MMM yyyy}",
            FilterSummary = filterSummary,
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Party"),
                new ReportColumn("Invoice #"),
                new ReportColumn("Invoice Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Due Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Days", ReportAlign.Right, "N0"),
                new ReportColumn("Not Due", ReportAlign.Right, "N0"),
                new ReportColumn("1-30", ReportAlign.Right, "N0"),
                new ReportColumn("31-60", ReportAlign.Right, "N0"),
                new ReportColumn("61-90", ReportAlign.Right, "N0"),
                new ReportColumn("90+", ReportAlign.Right, "N0"),
                new ReportColumn("Outstanding", ReportAlign.Right, "N0"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Grand total", "", "", "", "",
                g.NotDue, g.D1_30, g.D31_60, g.D61_90, g.D90Plus, g.Total] },
        };
    }

    private sealed record InvoiceRow(int Id, string Number, int PartyId, DateTime InvoiceDate, DateTime DueDate, decimal GrandTotal);
    private sealed record PartyInfo(int Id, string Code, string Name);
}
```

- [ ] **Step 2: Daftarkan di DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — tambah tepat setelah baris `services.AddScoped<IGrossProfitReportService, GrossProfitReportService>();` (baris 85):

```csharp
        services.AddScoped<IAgingReportService, AgingReportService>();
```

(`using ErpOne.Application.Reports;` sudah ada karena report service lain terdaftar di file ini.)

- [ ] **Step 3: Jalankan test Task 2 — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AgingReportServiceTests"`
Expected: PASS (`Ar_point_in_time_buckets_exclude_post_asof_receipts`).

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Infrastructure/Services/Reports/AgingReportService.cs src/ErpOne.Infrastructure/DependencyInjection.cs
```

---

## Task 4: Tests — AP mirror + customer filter

**Files:**
- Modify: `tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs`

**Interfaces:**
- Consumes: `IAgingReportService.GetApAgingAsync`, `IPurchaseOrderService`, `IGoodsReceiptService`, `ISupplierInvoiceService`, `ICashBankAccountService`; seed `SupplierPayment` langsung via `AppDbContext` (`Submit()` + `MarkPosted()`). Pola PO→GRN→invoice dari `SupplierInvoiceServiceTests`.
- Produces: helper `SeedApInvoiceAsync`, test `Ap_point_in_time_buckets` + `Ar_customer_filter_narrows_results`.

- [ ] **Step 1: Tambah usings + helper AP**

Tambahkan usings di atas `AgingReportServiceTests.cs` (setelah using yang ada):

```csharp
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SupplierInvoices;
```

Tambahkan helper di dalam kelas (supplier + PO→GRN→invoice; return supplier & account & invoice):

```csharp
    // supplier + warehouse + product/variant + IDR account + one Open supplier invoice (via PO→GRN); grand = qty*price.
    private static async Task<(int supplierId, int accountId, int invoiceId, decimal grand)> SeedApInvoiceAsync(
        IServiceProvider sp, DateTime invoiceDate, DateTime dueDate, int qty, decimal price)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Id();
        var supplier = new Supplier($"SP{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", null, null, null, true);
        var wh = new Warehouse($"WH{id}", $"GD {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Suppliers.Add(supplier); db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = product.AddVariant($"SK{id}", null, 1000m, null, 800m, null, null, true);
        await db.SaveChangesAsync();

        var po = sp.GetRequiredService<IPurchaseOrderService>();
        var created = await po.CreateAsync(new CreatePurchaseOrderRequest(supplier.Id, wh.Id, invoiceDate, null, null,
            [new PurchaseOrderLineRequest(variant.Id, qty, price, 0m, null)]));
        await po.SubmitAsync(created.Id); // empty chain → auto-confirms

        var grnSvc = sp.GetRequiredService<IGoodsReceiptService>();
        var grn = await grnSvc.CreateDraftAsync(new CreateGoodsReceiptRequest(created.Id, invoiceDate, null,
            [new GoodsReceiptLineRequest(created.Lines[0].Id, qty, price)]));
        await grnSvc.PostAsync(grn.Id);

        var inv = await sp.GetRequiredService<ISupplierInvoiceService>()
            .CreateAsync(new CreateSupplierInvoiceRequest(supplier.Id, invoiceDate, dueDate, $"SUP-{id}", null, [grn.Id]));

        var acc = await sp.GetRequiredService<ICashBankAccountService>()
            .CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 0m, null, null, null, true));

        return (supplier.Id, acc.Id, inv.Id, inv.GrandTotal);
    }

    // Seed a Posted SupplierPayment directly (bypass approval) allocated to one invoice, dated `date`.
    private static async Task AddPaymentAsync(IServiceProvider sp, int supplierId, int accountId, int invoiceId, DateTime date, decimal amount)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var pay = new SupplierPayment($"APP-{Id()}", supplierId, accountId, "IDR", date, null);
        pay.SetAllocations([new SupplierPaymentAllocation(invoiceId, amount)]);
        pay.Submit();     // Draft → PendingApproval
        pay.MarkPosted(); // PendingApproval → Posted
        db.SupplierPayments.Add(pay);
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 2: Tulis test AP + filter (gagal dulu bila helper baru)**

Tambahkan dua test:

```csharp
    [Fact]
    public async Task Ap_point_in_time_buckets()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Invoice 1: due 45 days ago → 31–60. Partial payment 3000 before asOf → outstanding 7000.
        var (sup1, acc1, inv1, _) = await SeedApInvoiceAsync(sp, asOf.AddDays(-75), asOf.AddDays(-45), 10, 1000m); // grand 10000
        await AddPaymentAsync(sp, sup1, acc1, inv1, asOf.AddDays(-10), 3000m);

        // Invoice 2 (different supplier): due 5 days in future → Not Due, grand 5000.
        await SeedApInvoiceAsync(sp, asOf.AddDays(-20), asOf.AddDays(5), 5, 1000m);

        var r = await sp.GetRequiredService<IAgingReportService>().GetApAgingAsync(asOf, null);

        Assert.Equal(AgingSide.Payable, r.Side);
        Assert.Equal(2, r.PartyCount);
        Assert.Equal(7_000m, r.GrandTotals.D31_60);   // inv1 after pre-asOf payment
        Assert.Equal(5_000m, r.GrandTotals.NotDue);   // inv2
        Assert.Equal(12_000m, r.GrandTotals.Total);
    }

    [Fact]
    public async Task Ar_customer_filter_narrows_results()
    {
        var asOf = new DateTime(2026, 7, 15);
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var (custA, _, whA, varA) = await SeedCustomerAsync(sp);
        await SeedArInvoiceAsync(sp, custA, whA, varA, asOf.AddDays(-40), asOf.AddDays(-15), 10, 1000m); // grand 10000
        var (custB, _, whB, varB) = await SeedCustomerAsync(sp);
        await SeedArInvoiceAsync(sp, custB, whB, varB, asOf.AddDays(-40), asOf.AddDays(-15), 7, 1000m);  // grand 7000

        var onlyA = await sp.GetRequiredService<IAgingReportService>().GetArAgingAsync(asOf, custA);

        Assert.Single(onlyA.Parties);
        Assert.Equal(custA, onlyA.Parties[0].PartyId);
        Assert.Equal(10_000m, onlyA.GrandTotals.Total);
    }
```

- [ ] **Step 3: Jalankan semua test aging — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~AgingReportServiceTests"`
Expected: PASS semua (3 test). Bila `SeedApInvoiceAsync` gagal karena beda ctor/param, samakan dengan `SupplierInvoiceServiceTests.SeedPostedGrnAsync` (sudah dijadikan acuan) lalu ulangi.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add tests/ErpOne.IntegrationTests/AgingReportServiceTests.cs
```

---

## Task 5: Menu resources `reports.ar-aging` & `reports.ap-aging`

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (grup Reports, setelah baris 91 `reports.gross-profit`)

**Interfaces:**
- Consumes: `ReportActions` (= `[ActIndex, ActExport]`).
- Produces: permission `reports.ar-aging.index`/`.export` & `reports.ap-aging.index`/`.export` (auto ke `AllPermissions`, di-seed admin oleh `BootstrapSeeder`). Route default `/reports/ar-aging` & `/reports/ap-aging` via konvensi key→href.

- [ ] **Step 1: Tambah dua resource**

Modify grup Reports di `AppMenus.cs` sehingga menjadi:

```csharp
        new("Reports",
        [
            new("reports.stock-ledger", "Stock Ledger", "bi-journal-text", ReportActions),
            new("reports.inventory-valuation", "Inventory Valuation", "bi-cash-stack", ReportActions),
            new("reports.sales", "Sales Report", "bi-graph-up-arrow", ReportActions),
            new("reports.purchases", "Purchase Report", "bi-cart-check", ReportActions),
            new("reports.gross-profit", "Gross Profit", "bi-coin", ReportActions),
            new("reports.ar-aging", "AR Aging", "bi-hourglass-split", ReportActions),
            new("reports.ap-aging", "AP Aging", "bi-hourglass-bottom", ReportActions),
        ]),
```

- [ ] **Step 2: Build Web project**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 6: AR Aging page

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Reports/Aging/ArAgingIndex.razor`

**Interfaces:**
- Consumes: `IAgingReportService.GetArAgingAsync`/`BuildArAgingReportAsync`, `AgingResultDto`, `ICustomerService.GetAllAsync()`→`CustomerDto` (ns `ErpOne.Application.Customers`), `IReportExporter`, `IJSRuntime` (`saveAsFile`).
- Produces: halaman route `/reports/ar-aging`.

- [ ] **Step 1: Tulis halaman**

Create `src/ErpOne.Web/Components/Pages/Reports/Aging/ArAgingIndex.razor`:

```razor
@page "/reports/ar-aging"
@attribute [Authorize(Policy = "reports.ar-aging.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using ErpOne.Application.Customers
@using Microsoft.JSInterop
@inject IAgingReportService Aging
@inject ICustomerService CustomerService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>AR Aging</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">AR Aging</span>
            </nav>
            <h1>AR Aging</h1>
            <p>Outstanding customer receivables by age, point-in-time as of a chosen date.</p>
        </div>
        <AuthorizeView Policy="reports.ar-aging.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_result is not null)
    {
        <div class="kpis">
            <div class="kpi accent">
                <div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div>
                <div class="kpi-tx"><div class="v">Rp @_result.GrandTotals.Total.ToString("N0")</div><div class="l">Total outstanding</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-amb"><i class="bi bi-hourglass-split"></i></div>
                <div class="kpi-tx"><div class="v">Rp @Overdue.ToString("N0")</div><div class="l">Overdue</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-receipt-cutoff"></i></div>
                <div class="kpi-tx"><div class="v">@_result.InvoiceCount.ToString("N0")</div><div class="l">Invoices</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-people"></i></div>
                <div class="kpi-tx"><div class="v">@_result.PartyCount.ToString("N0")</div><div class="l">Customers</div></div>
            </div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_asOf" @bind:after="ReloadAsync" />
        <select @bind="_customerId" @bind:after="ReloadAsync">
            <option value="0">All customers</option>
            @foreach (var c in _customers) { <option value="@c.Id">@c.Name</option> }
        </select>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.InvoiceCount == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cash-stack"></i></div><p>No outstanding receivables for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>Customer</th><th style="width:150px">Invoice #</th>
                            <th style="width:110px">Inv Date</th><th style="width:110px">Due Date</th>
                            <th class="r" style="width:70px">Days</th>
                            <th class="r" style="width:120px">Not Due</th><th class="r" style="width:120px">1–30</th>
                            <th class="r" style="width:120px">31–60</th><th class="r" style="width:120px">61–90</th>
                            <th class="r" style="width:120px">90+</th><th class="r" style="width:130px">Outstanding</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var p in _result.Parties)
                        {
                            <tr class="fw-bold table-light"><td colspan="11">@p.PartyCode — @p.PartyName</td></tr>
                            @foreach (var i in p.Invoices)
                            {
                                <tr>
                                    <td></td>
                                    <td class="code mono">@i.InvoiceNumber</td>
                                    <td class="mono">@i.InvoiceDate.ToString("yyyy-MM-dd")</td>
                                    <td class="mono">@i.DueDate.ToString("yyyy-MM-dd")</td>
                                    <td class="r mono">@i.DaysPastDue</td>
                                    <td class="r mono">@F(i.Buckets.NotDue)</td>
                                    <td class="r mono">@F(i.Buckets.D1_30)</td>
                                    <td class="r mono">@F(i.Buckets.D31_60)</td>
                                    <td class="r mono">@F(i.Buckets.D61_90)</td>
                                    <td class="r mono">@F(i.Buckets.D90Plus)</td>
                                    <td class="r mono">@i.Outstanding.ToString("N0")</td>
                                </tr>
                            }
                            <tr class="fw-bold">
                                <td colspan="5">@p.PartyName subtotal</td>
                                <td class="r mono">@F(p.Subtotals.NotDue)</td>
                                <td class="r mono">@F(p.Subtotals.D1_30)</td>
                                <td class="r mono">@F(p.Subtotals.D31_60)</td>
                                <td class="r mono">@F(p.Subtotals.D61_90)</td>
                                <td class="r mono">@F(p.Subtotals.D90Plus)</td>
                                <td class="r mono">@p.Subtotals.Total.ToString("N0")</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold">
                            <td colspan="5">Grand total</td>
                            <td class="r mono">@F(_result.GrandTotals.NotDue)</td>
                            <td class="r mono">@F(_result.GrandTotals.D1_30)</td>
                            <td class="r mono">@F(_result.GrandTotals.D31_60)</td>
                            <td class="r mono">@F(_result.GrandTotals.D61_90)</td>
                            <td class="r mono">@F(_result.GrandTotals.D90Plus)</td>
                            <td class="r mono">@_result.GrandTotals.Total.ToString("N0")</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private AgingResultDto? _result;
    private IReadOnlyList<CustomerDto> _customers = [];
    private DateTime _asOf = DateTime.Today;
    private int _customerId;

    private decimal Overdue => _result is null ? 0 : _result.GrandTotals.Total - _result.GrandTotals.NotDue;
    private static string F(decimal v) => v == 0 ? "–" : v.ToString("N0");

    protected override async Task OnInitializedAsync()
    {
        _customers = await CustomerService.GetAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() => _result = await Aging.GetArAgingAsync(_asOf, _customerId == 0 ? null : _customerId);
    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Aging.BuildArAgingReportAsync(_asOf, _customerId == 0 ? null : _customerId);
        await DownloadAsync(Exporter.ToExcel(doc), "ar-aging.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Aging.BuildArAgingReportAsync(_asOf, _customerId == 0 ? null : _customerId);
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "ar-aging.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 2: Build Web project**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded (tidak ada error Razor / referensi properti).

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Reports/Aging/ArAgingIndex.razor
```

---

## Task 7: AP Aging page + full suite + manual verify

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Reports/Aging/ApAgingIndex.razor`

**Interfaces:**
- Consumes: `IAgingReportService.GetApAgingAsync`/`BuildApAgingReportAsync`, `AgingResultDto`, `ISupplierService.GetAllAsync()`→`SupplierDto` (ns `ErpOne.Application.Suppliers`), `IReportExporter`, `IJSRuntime`.
- Produces: halaman route `/reports/ap-aging`.

- [ ] **Step 1: Tulis halaman**

Create `src/ErpOne.Web/Components/Pages/Reports/Aging/ApAgingIndex.razor`:

```razor
@page "/reports/ap-aging"
@attribute [Authorize(Policy = "reports.ap-aging.index")]
@rendermode InteractiveServer
@using ErpOne.Application.Reports
@using ErpOne.Application.Suppliers
@using Microsoft.JSInterop
@inject IAgingReportService Aging
@inject ISupplierService SupplierService
@inject IReportExporter Exporter
@inject IJSRuntime JS

<PageTitle>AP Aging</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Reports</span><span class="sep">·</span><span class="here">AP Aging</span>
            </nav>
            <h1>AP Aging</h1>
            <p>Outstanding supplier payables by age, point-in-time as of a chosen date.</p>
        </div>
        <AuthorizeView Policy="reports.ap-aging.export">
            <Authorized>
                <div class="pi-actions">
                    <button class="btn btn-outline-secondary" @onclick="ExportExcel"><i class="bi bi-file-earmark-excel"></i> Excel</button>
                    <button class="btn btn-outline-secondary" @onclick="ExportPdf"><i class="bi bi-file-earmark-pdf"></i> PDF</button>
                </div>
            </Authorized>
        </AuthorizeView>
    </div>

    @if (_result is not null)
    {
        <div class="kpis">
            <div class="kpi accent">
                <div class="ic ic-grn"><i class="bi bi-cash-stack"></i></div>
                <div class="kpi-tx"><div class="v">Rp @_result.GrandTotals.Total.ToString("N0")</div><div class="l">Total outstanding</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-amb"><i class="bi bi-hourglass-bottom"></i></div>
                <div class="kpi-tx"><div class="v">Rp @Overdue.ToString("N0")</div><div class="l">Overdue</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-receipt"></i></div>
                <div class="kpi-tx"><div class="v">@_result.InvoiceCount.ToString("N0")</div><div class="l">Invoices</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-blu"><i class="bi bi-truck"></i></div>
                <div class="kpi-tx"><div class="v">@_result.PartyCount.ToString("N0")</div><div class="l">Suppliers</div></div>
            </div>
        </div>
    }

    <div class="toolbar">
        <input type="date" @bind="_asOf" @bind:after="ReloadAsync" />
        <select @bind="_supplierId" @bind:after="ReloadAsync">
            <option value="0">All suppliers</option>
            @foreach (var s in _suppliers) { <option value="@s.Id">@s.Name</option> }
        </select>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.InvoiceCount == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-cash-stack"></i></div><p>No outstanding payables for these filters.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th>Supplier</th><th style="width:150px">Invoice #</th>
                            <th style="width:110px">Inv Date</th><th style="width:110px">Due Date</th>
                            <th class="r" style="width:70px">Days</th>
                            <th class="r" style="width:120px">Not Due</th><th class="r" style="width:120px">1–30</th>
                            <th class="r" style="width:120px">31–60</th><th class="r" style="width:120px">61–90</th>
                            <th class="r" style="width:120px">90+</th><th class="r" style="width:130px">Outstanding</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var p in _result.Parties)
                        {
                            <tr class="fw-bold table-light"><td colspan="11">@p.PartyCode — @p.PartyName</td></tr>
                            @foreach (var i in p.Invoices)
                            {
                                <tr>
                                    <td></td>
                                    <td class="code mono">@i.InvoiceNumber</td>
                                    <td class="mono">@i.InvoiceDate.ToString("yyyy-MM-dd")</td>
                                    <td class="mono">@i.DueDate.ToString("yyyy-MM-dd")</td>
                                    <td class="r mono">@i.DaysPastDue</td>
                                    <td class="r mono">@F(i.Buckets.NotDue)</td>
                                    <td class="r mono">@F(i.Buckets.D1_30)</td>
                                    <td class="r mono">@F(i.Buckets.D31_60)</td>
                                    <td class="r mono">@F(i.Buckets.D61_90)</td>
                                    <td class="r mono">@F(i.Buckets.D90Plus)</td>
                                    <td class="r mono">@i.Outstanding.ToString("N0")</td>
                                </tr>
                            }
                            <tr class="fw-bold">
                                <td colspan="5">@p.PartyName subtotal</td>
                                <td class="r mono">@F(p.Subtotals.NotDue)</td>
                                <td class="r mono">@F(p.Subtotals.D1_30)</td>
                                <td class="r mono">@F(p.Subtotals.D31_60)</td>
                                <td class="r mono">@F(p.Subtotals.D61_90)</td>
                                <td class="r mono">@F(p.Subtotals.D90Plus)</td>
                                <td class="r mono">@p.Subtotals.Total.ToString("N0")</td>
                            </tr>
                        }
                    </tbody>
                    <tfoot>
                        <tr class="fw-bold">
                            <td colspan="5">Grand total</td>
                            <td class="r mono">@F(_result.GrandTotals.NotDue)</td>
                            <td class="r mono">@F(_result.GrandTotals.D1_30)</td>
                            <td class="r mono">@F(_result.GrandTotals.D31_60)</td>
                            <td class="r mono">@F(_result.GrandTotals.D61_90)</td>
                            <td class="r mono">@F(_result.GrandTotals.D90Plus)</td>
                            <td class="r mono">@_result.GrandTotals.Total.ToString("N0")</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private AgingResultDto? _result;
    private IReadOnlyList<SupplierDto> _suppliers = [];
    private DateTime _asOf = DateTime.Today;
    private int _supplierId;

    private decimal Overdue => _result is null ? 0 : _result.GrandTotals.Total - _result.GrandTotals.NotDue;
    private static string F(decimal v) => v == 0 ? "–" : v.ToString("N0");

    protected override async Task OnInitializedAsync()
    {
        _suppliers = await SupplierService.GetAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() => _result = await Aging.GetApAgingAsync(_asOf, _supplierId == 0 ? null : _supplierId);
    private async Task ReloadAsync() => await LoadAsync();

    private async Task ExportExcel()
    {
        var doc = await Aging.BuildApAgingReportAsync(_asOf, _supplierId == 0 ? null : _supplierId);
        await DownloadAsync(Exporter.ToExcel(doc), "ap-aging.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private async Task ExportPdf()
    {
        var doc = await Aging.BuildApAgingReportAsync(_asOf, _supplierId == 0 ? null : _supplierId);
        await DownloadAsync(await Exporter.ToPdfAsync(doc), "ap-aging.pdf", "application/pdf");
    }

    private async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await JS.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes), mime);
}
```

- [ ] **Step 2: Build + jalankan SELURUH test suite**

Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS (jumlah sebelumnya + 3 test aging baru). Tak ada test yang meng-assert jumlah menu resource.

- [ ] **Step 3: Verifikasi manual (skill `run`/`verify`)**

Jalankan app, sign out/in (agar admin dapat permission baru), buka `/reports/ar-aging` dan `/reports/ap-aging`. Cek: KPI, tabel per party dengan subtotal + grand total, ganti tanggal as-of (bucket bergeser), filter party, dan Export Excel/PDF menghasilkan file berisi kolom bucket + Grand total.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Reports/Aging/ApAgingIndex.razor
```

---

## Self-Review (untuk penulis plan)

**Spec coverage:**
- Dua halaman terpisah (AR/AP) → Task 6, 7. ✓
- 5 bucket (Not Due/1–30/31–60/61–90/90+), umur = hari lewat DueDate → Task 3 `BucketOf`. ✓
- True point-in-time (alokasi ≤ asOf, receipt/payment Posted) → Task 3 query `paid`; diuji Task 2 (exclude post-asOf) & Task 4 (partial). ✓
- Faktur kandidat `InvoiceDate < toExclusive`, `Status != Cancelled`, simpan `outstanding > 0` → Task 3 `Build`. ✓
- Grouping per party + subtotal + grand total → Task 3 `Build`. ✓
- Export ReportDocument (kolom, subtotal, grand) → Task 3 `ToReportDocument`; UI export → Task 6/7. ✓
- Permissions view+export + seeding → Task 5 (`ReportActions`). ✓
- KPI Total/Overdue/Invoices/Parties → Task 6/7. ✓
- Simplification void/cancel dibaca saat ini → tercermin di filter `Status != Cancelled` & join `Status == Posted` (Task 3). ✓
- Testing AR + AP + filter → Task 2, 4. ✓

**Placeholder scan:** Tidak ada TBD/TODO. Semua kode service, page, dan test lengkap. Satu-satunya instruksi bersyarat: Task 4 Step 3 "samakan bila beda ctor" — itu instruksi recovery, bukan placeholder; seed AP sudah lengkap & mengacu `SupplierInvoiceServiceTests`.

**Type consistency:** `AgingResultDto`/`AgingBucketSet`/`AgingInvoiceDto`/`AgingPartyDto`/`AgingSide` identik di Task 1/3/6/7. Metode `GetArAgingAsync(DateTime, int?, CancellationToken)`, `GetApAgingAsync`, `BuildArAgingReportAsync`, `BuildApAgingReportAsync` konsisten interface (Task 1) ↔ impl (Task 3) ↔ test (Task 2/4) ↔ page (Task 6/7). `AgingBucketSet` properti: `NotDue, D1_30, D31_60, D61_90, D90Plus, Total` dipakai seragam. Private `InvoiceRow`/`PartyInfo` hanya di service. Verifikasi runtime: nama entity `Customer.Code/Name`, `Supplier.Code/Name`, `CustomerInvoice.CustomerId/InvoiceDate/DueDate/GrandTotal/Status`, `SupplierInvoice.SupplierId/…`, alokasi `CustomerReceiptId`/`CustomerInvoiceId`/`Amount` & `SupplierPaymentId`/`SupplierInvoiceId`/`Amount`, receipt `Status`/`ReceiptDate`, payment `Status`/`PaymentDate` — semua sudah dikonfirmasi dari entity saat penyusunan spec.
```
