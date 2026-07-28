# Fase 6b-1 — Pricing Foundation (Price List + Guardrail Diskon) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memusatkan harga jual ke Price List (dengan tier qty) di belakang seam `IPricingService`, dan menutup celah harga/diskon yang saat ini dipercaya dari client dengan batas diskon per role yang divalidasi server.

**Architecture:** Seam `IPricingService` di `ErpOne.Application/Pricing` (pola `ICostingService`), implementasi `PricingService` di Infrastructure. Aturan hitung murni diekstrak ke `PriceMath` statik agar bisa diuji tanpa DB. POS (`PosSaleService.CreateSaleAsync`) dan SO (`SalesOrderService.BuildLinesAsync`) me-resolve harga di server lalu memvalidasi penyimpangan harga efektif client terhadap harga engine.

**Tech Stack:** .NET 10, Blazor Server, EF Core (SQL Server; SQLite in-memory untuk test), FluentValidation, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-27-f6b1-pricing-pricelist-design.md`

## Global Constraints

- **Namespace itu flat, bukan mengikuti folder.** Entity di folder `Entities/Master` tetap `namespace ErpOne.Domain.Entities`. Service di folder `Services/Master` tetap `namespace ErpOne.Infrastructure.Services`. Application: `ErpOne.Application.Pricing`, `ErpOne.Application.PriceLists` (bukan `...Master.PriceLists`).
- **Entity bisnis baru WAJIB didaftarkan di `tablePrefixes`** (`AppDbContext.cs:1123`) dengan prefix `M_`. Ada pengaman di `AppDbContext.cs:1202-1207` yang membuat model gagal dibangun bila terlewat.
- **Mapping EF inline di `OnModelCreating`.** Proyek ini tidak memakai `IEntityTypeConfiguration`; tidak ada folder `Configurations`.
- **`HasData` hanya menerima nilai statik** — pakai `new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`, bukan `DateTime.UtcNow`.
- **Uang: `decimal` dengan `HasPrecision(18, 2)`. Persen: `HasPrecision(5, 2)`.** Pembulatan selalu `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- **UI berbahasa Inggris**, desain global `.pi` (index) / `.cf` (form Atlas). Jangan pakai `sh-header`/`fs-card`/`data-card` lama.
- **Permission tidak perlu diseed manual** — `Web/Infrastructure/BootstrapSeeder.cs:44` sudah memberikan `AppMenus.AllPermissions` ke role admin secara idempotent. Cukup daftarkan resource di `AppMenus.cs`.
- **`roleNames` tidak boleh masuk request DTO** (client-controlled). Ia adalah parameter method terpisah, diisi halaman Blazor dari cascading `Task<AuthenticationState>`.
- **Test integrasi memakai SQLite in-memory dengan fixture berbagi** (`CustomWebApplicationFactory`, `EnsureCreated()`). Test harus **order-independent**: jangan mengandalkan data dari test lain, buat data sendiri.
- **Commit dijalankan user secara manual.** Setiap task diakhiri langkah commit yang menuliskan perintah + pesannya; jangan jalankan `git commit`/`merge`/`push` sendiri. Laporkan perintahnya ke user.
- Verifikasi akhir: `dotnet build ErpOne.slnx` harus **0 warning, 0 error**; seluruh test hijau. Baseline sebelum mulai: **166 unit + 225 integration**.

### Signature nyata (terverifikasi saat Task 1 — pakai ini, jangan mengarang)

```csharp
// 8 parameter, bukan 7
new Product(code, name, description, categoryId, brandId, baseUnitId, taxId, ProductStatus.Aktif)

// enum berbahasa Indonesia: Aktif / Nonaktif / Habis / Arsip — TIDAK ada "Active"
ProductStatus.Aktif

// API resmi untuk membuat varian; TIDAK perlu refleksi untuk mengeset ProductId
ProductVariant v = product.AddVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive);

// isDefault ada di posisi ke-5; defaultPriceListId adalah argumen ke-6
new Warehouse(code, name, address, isActive, isDefault, defaultPriceListId)
```

Pola pembuatan varian di test:

```csharp
var product = new Product("X-P1", "Probe", null, null, null, null, null, ProductStatus.Aktif);
var variant = product.AddVariant("X-SKU-1", null, 100_000m, null, 0m, null, null, true);
db.Products.Add(product);
await db.SaveChangesAsync();   // variant.Id terisi setelah ini
```

---

## File Structure

| Berkas | Tanggung jawab |
|---|---|
| `Domain/Entities/Master/PriceList.cs` | Agregat price list + invarian kode/nama; memegang koleksi baris |
| `Domain/Entities/Master/PriceListLine.cs` | Satu tier: (varian, MinQty, harga) + invarian |
| `Domain/Entities/Settings/PricingSetting.cs` | Baris tunggal setelan global (default batas diskon) |
| `Application/Pricing/PriceMath.cs` | **Aturan hitung murni**: pilih tier, penyimpangan, batas efektif. Tanpa DB, tanpa EF |
| `Application/Pricing/IPricingService.cs` | Kontrak seam + record request/result |
| `Infrastructure/Services/Pricing/PricingService.cs` | Query price list + rakit `PriceResult`; delegasi hitung ke `PriceMath` |
| `Application/PriceLists/*` | Kontrak CRUD + DTO + validator price list |
| `Infrastructure/Services/Master/PriceListService.cs` | CRUD price list + keunikan kode + restrict delete |
| `Application/Pricing/IPricingSettingService.cs` | Kontrak setelan global |
| `Infrastructure/Services/Settings/PricingSettingService.cs` | Baca/tulis baris tunggal setelan |
| `Web/Components/Pages/Master/PriceLists/*` | Index `.pi` + Form `.cf` |
| `Web/Components/Pages/Settings/Pricing/PricingSettingIndex.razor` | Halaman setelan |

Pemisahan `PriceMath` dari `PricingService` adalah keputusan struktural utama: aturan inti pricing jadi bisa dibaca dan diuji tanpa menyentuh EF, dan `PricingService` tinggal jadi lapisan query.

---

## Task 1: Domain entities, EF mapping, migration

**Files:**
- Create: `src/ErpOne.Domain/Entities/Master/PriceList.cs`
- Create: `src/ErpOne.Domain/Entities/Master/PriceListLine.cs`
- Create: `src/ErpOne.Domain/Entities/Settings/PricingSetting.cs`
- Modify: `src/ErpOne.Domain/Entities/Master/Customer.cs`
- Modify: `src/ErpOne.Domain/Entities/Master/Warehouse.cs`
- Modify: `src/ErpOne.Infrastructure/Identity/ApplicationRole.cs`
- Modify: `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`
- Test: `tests/ErpOne.UnitTests/PriceListDomainTests.cs`
- Test: `tests/ErpOne.IntegrationTests/PricingSchemaTests.cs`

**Interfaces:**
- Consumes: `AuditableEntity` dari `ErpOne.Domain.Common`.
- Produces: `PriceList` (`Id`, `Code`, `Name`, `Description`, `IsActive`, `Lines`, ctor `(string code, string name, string? description, bool isActive)`, `Update(...)` sama parameternya, `SetLines(IEnumerable<PriceListLine>)`); `PriceListLine` (`Id`, `PriceListId`, `ProductVariantId`, `MinQty`, `UnitPrice`, ctor `(int productVariantId, int minQty, decimal unitPrice)`); `PricingSetting` (`Id`, `DefaultMaxDiscountPercent`, `SetDefaultMaxDiscountPercent(decimal)`); `Customer.PriceListId`; `Warehouse.DefaultPriceListId`; `ApplicationRole.MaxDiscountPercent`.

- [ ] **Step 1: Tulis unit test yang gagal**

Buat `tests/ErpOne.UnitTests/PriceListDomainTests.cs`:

```csharp
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class PriceListDomainTests
{
    [Fact]
    public void Code_is_normalized_to_uppercase_and_trimmed()
    {
        var list = new PriceList(" grosir ", "Grosir", null, true);
        Assert.Equal("GROSIR", list.Code);
    }

    [Fact]
    public void Empty_code_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new PriceList("  ", "Grosir", null, true));
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new PriceList("GROSIR", " ", null, true));
    }

    [Fact]
    public void Line_rejects_min_qty_below_one()
    {
        Assert.Throws<ArgumentException>(() => new PriceListLine(1, 0, 90_000m));
    }

    [Fact]
    public void Line_rejects_negative_price()
    {
        Assert.Throws<ArgumentException>(() => new PriceListLine(1, 1, -1m));
    }

    [Fact]
    public void Line_accepts_valid_tier()
    {
        var line = new PriceListLine(7, 10, 85_000m);
        Assert.Equal(7, line.ProductVariantId);
        Assert.Equal(10, line.MinQty);
        Assert.Equal(85_000m, line.UnitPrice);
    }

    [Fact]
    public void SetLines_replaces_previous_lines()
    {
        var list = new PriceList("GROSIR", "Grosir", null, true);
        list.SetLines([new PriceListLine(1, 1, 90_000m)]);
        list.SetLines([new PriceListLine(2, 1, 80_000m), new PriceListLine(2, 10, 75_000m)]);

        Assert.Equal(2, list.Lines.Count);
        Assert.All(list.Lines, l => Assert.Equal(2, l.ProductVariantId));
    }

    [Fact]
    public void PricingSetting_rejects_percent_outside_zero_hundred()
    {
        var setting = new PricingSetting();
        Assert.Throws<ArgumentException>(() => setting.SetDefaultMaxDiscountPercent(-1m));
        Assert.Throws<ArgumentException>(() => setting.SetDefaultMaxDiscountPercent(100.01m));
    }

    [Fact]
    public void PricingSetting_accepts_boundary_values()
    {
        var setting = new PricingSetting();
        setting.SetDefaultMaxDiscountPercent(0m);
        Assert.Equal(0m, setting.DefaultMaxDiscountPercent);
        setting.SetDefaultMaxDiscountPercent(100m);
        Assert.Equal(100m, setting.DefaultMaxDiscountPercent);
    }
}
```

- [ ] **Step 2: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.UnitTests --filter "FullyQualifiedName~PriceListDomainTests"`
Expected: FAIL — kompilasi gagal, `PriceList`/`PriceListLine`/`PricingSetting` belum ada.

- [ ] **Step 3: Buat `PriceList`**

`src/ErpOne.Domain/Entities/Master/PriceList.cs`:

```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Daftar harga struktural (Retail/Grosir/Reseller). Dimensi waktu adalah urusan promo, bukan di sini.</summary>
public class PriceList : AuditableEntity
{
    private readonly List<PriceListLine> _lines = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<PriceListLine> Lines => _lines;

    private PriceList() { } // EF Core

    public PriceList(string code, string name, string? description, bool isActive)
        => Apply(code, name, description, isActive);

    public void Update(string code, string name, string? description, bool isActive)
        => Apply(code, name, description, isActive);

    public void SetLines(IEnumerable<PriceListLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }

    private void Apply(string code, string name, string? description, bool isActive)
    {
        SetCode(code);
        SetName(name);
        Description = Clean(description);
        IsActive = isActive;
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Buat `PriceListLine`**

`src/ErpOne.Domain/Entities/Master/PriceListLine.cs`:

```csharp
namespace ErpOne.Domain.Entities;

/// <summary>Satu tier harga: berlaku bila qty >= MinQty. Tier = beberapa baris dengan MinQty berbeda.</summary>
public class PriceListLine
{
    public int Id { get; private set; }
    public int PriceListId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int MinQty { get; private set; }
    public decimal UnitPrice { get; private set; }

    private PriceListLine() { } // EF Core

    public PriceListLine(int productVariantId, int minQty, decimal unitPrice)
    {
        if (productVariantId <= 0)
            throw new ArgumentException("ProductVariantId must be > 0.", nameof(productVariantId));
        if (minQty < 1)
            throw new ArgumentException("MinQty must be >= 1.", nameof(minQty));
        if (unitPrice < 0)
            throw new ArgumentException("UnitPrice must be >= 0.", nameof(unitPrice));

        ProductVariantId = productVariantId;
        MinQty = minQty;
        UnitPrice = unitPrice;
    }
}
```

- [ ] **Step 5: Buat `PricingSetting`**

`src/ErpOne.Domain/Entities/Settings/PricingSetting.cs`:

```csharp
using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) setelan pricing company-wide. Pola CostingSetting.</summary>
public class PricingSetting : AuditableEntity
{
    public int Id { get; private set; }

    /// <summary>Batas diskon dipakai bila user tidak punya role dengan MaxDiscountPercent terisi.</summary>
    public decimal DefaultMaxDiscountPercent { get; private set; } = 100m;

    // EF Core; baris tunggal diseed via HasData. Juga dipakai unit test.
    public PricingSetting() { }

    public void SetDefaultMaxDiscountPercent(decimal percent)
    {
        if (percent is < 0m or > 100m)
            throw new ArgumentException("Percent must be 0..100.", nameof(percent));
        DefaultMaxDiscountPercent = percent;
    }
}
```

- [ ] **Step 6: Tambah `PriceListId` ke `Customer`**

Di `src/ErpOne.Domain/Entities/Master/Customer.cs`, tambah property setelah `CreditLimit`:

```csharp
    public int? PriceListId { get; private set; }
```

Tambah parameter `int? priceListId` sebagai **parameter terakhir** pada ctor, `Update(...)`, dan `Apply(...)`, lalu set di `Apply`:

```csharp
        PriceListId = priceListId is > 0 ? priceListId : null;
```

Signature akhir ketiganya (urutan parameter persis seperti ini):

```csharp
    public Customer(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive, int? priceListId = null)

    public void Update(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive, int? priceListId = null)

    private void Apply(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive, int? priceListId)
```

Default `= null` pada ctor & `Update` menjaga pemanggil lama tetap ter-kompilasi.

- [ ] **Step 7: Tambah `DefaultPriceListId` ke `Warehouse`**

Di `src/ErpOne.Domain/Entities/Master/Warehouse.cs`, tambah property:

```csharp
    public int? DefaultPriceListId { get; private set; }
```

Tambah `int? defaultPriceListId = null` sebagai parameter terakhir ctor & `Update`, teruskan ke `Apply`/blok setter yang ada (`SetCode(code); SetName(name); SetAddress(address);` di `Warehouse.cs:19` dan `:25`), dan set:

```csharp
        DefaultPriceListId = defaultPriceListId is > 0 ? defaultPriceListId : null;
```

- [ ] **Step 8: Tambah `MaxDiscountPercent` ke `ApplicationRole`**

Di `src/ErpOne.Infrastructure/Identity/ApplicationRole.cs`, tambah setelah `Description`:

```csharp
    /// <summary>Batas diskon manual untuk role ini. null = tidak diatur (pakai default global).
    /// 0 = tidak boleh memberi diskon sama sekali.</summary>
    public decimal? MaxDiscountPercent { get; set; }
```

- [ ] **Step 9: Tambah DbSet, mapping, dan prefix tabel**

Di `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs`:

(a) Tambah DbSet setelah `CostingSettings` (baris ~72):

```csharp
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListLine> PriceListLines => Set<PriceListLine>();
    public DbSet<PricingSetting> PricingSettings => Set<PricingSetting>();
```

(b) Tambah blok mapping di `OnModelCreating`, tepat setelah blok `CostingSetting` (berakhir ~baris 1048):

```csharp
        modelBuilder.Entity<PriceList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(255);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.PriceListId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(PriceList.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PriceListLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);

            // Satu harga per (price list, varian, tier).
            e.HasIndex(x => new { x.PriceListId, x.ProductVariantId, x.MinQty }).IsUnique();
        });

        modelBuilder.Entity<PricingSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DefaultMaxDiscountPercent).HasPrecision(5, 2);

            // Seed 100 = perilaku sebelum fitur ini (diskon bebas), agar rilis tidak breaking.
            e.HasData(new
            {
                Id = 1,
                DefaultMaxDiscountPercent = 100m,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });
```

(c) Tambah FK opsional pada blok `Customer` dan `Warehouse` yang sudah ada:

```csharp
            // di dalam modelBuilder.Entity<Customer>(e => { ... })
            e.HasOne<PriceList>().WithMany()
                .HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Restrict);

            // di dalam modelBuilder.Entity<Warehouse>(e => { ... })
            e.HasOne<PriceList>().WithMany()
                .HasForeignKey(x => x.DefaultPriceListId).OnDelete(DeleteBehavior.Restrict);
```

(d) Tambah presisi kolom role baru — letakkan bersama mapping Identity, atau tambahkan blok baru dekat blok `PricingSetting`:

```csharp
        modelBuilder.Entity<ApplicationRole>(e =>
        {
            e.Property(x => x.MaxDiscountPercent).HasPrecision(5, 2);
        });
```

(e) **Wajib** — tambah tiga entri di dictionary `tablePrefixes` (bagian `// Master`, ~baris 1149):

```csharp
            [nameof(PriceList)] = "M_",
            [nameof(PriceListLine)] = "M_",
            [nameof(PricingSetting)] = "M_",
```

Tanpa langkah (e) pembangunan model **gagal** oleh pengaman di `AppDbContext.cs:1202-1207`.

- [ ] **Step 10: Jalankan unit test — harus lolos**

Run: `dotnet test tests/ErpOne.UnitTests --filter "FullyQualifiedName~PriceListDomainTests"`
Expected: PASS (9 test).

- [ ] **Step 11: Tulis test integrasi skema**

Buat `tests/ErpOne.IntegrationTests/PricingSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingSchemaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingSchemaTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public void Pricing_tables_use_master_prefix()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("M_PriceLists", db.Model.FindEntityType(typeof(ErpOne.Domain.Entities.PriceList))!.GetTableName());
        Assert.Equal("M_PriceListLines", db.Model.FindEntityType(typeof(ErpOne.Domain.Entities.PriceListLine))!.GetTableName());
        Assert.Equal("M_PricingSettings", db.Model.FindEntityType(typeof(ErpOne.Domain.Entities.PricingSetting))!.GetTableName());
    }

    [Fact]
    public async Task PricingSetting_seed_row_exists_with_hundred_percent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.PricingSettings.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Id);
        Assert.Equal(100m, row.DefaultMaxDiscountPercent);
    }

    [Fact]
    public async Task Duplicate_tier_for_same_variant_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var variantId = await db.ProductVariants.Select(v => v.Id).FirstOrDefaultAsync();
        if (variantId == 0)
        {
            var product = new ErpOne.Domain.Entities.Product("SCHEMA-P1", "Schema Probe", null, null, null, null,
                ErpOne.Domain.Entities.ProductStatus.Active);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            var variant = new ErpOne.Domain.Entities.ProductVariant("SCHEMA-SKU-1", null, 100_000m, null, 0m, null, null, true);
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
        }

        var list = new ErpOne.Domain.Entities.PriceList("SCHEMA-DUP", "Schema Dup", null, true);
        list.SetLines([
            new ErpOne.Domain.Entities.PriceListLine(variantId, 1, 90_000m),
            new ErpOne.Domain.Entities.PriceListLine(variantId, 1, 80_000m),
        ]);
        db.PriceLists.Add(list);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

Catatan: `Product`/`ProductVariant` ctor di test di atas mengikuti signature yang ada. Bila tidak cocok, **jangan** ubah entity — sesuaikan pemanggilan di test dengan signature nyata (lihat `ProductVariant.cs:28`).

- [ ] **Step 12: Jalankan test integrasi**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PricingSchemaTests"`
Expected: PASS (3 test). Bila gagal di prefix tabel, Step 9(e) terlewat.

- [ ] **Step 13: Buat migration**

Run:
```bash
cd "F:/4. My Data/Project/MyApplication"
dotnet ef migrations add AddPricingFoundation \
  --project src/ErpOne.Infrastructure \
  --startup-project src/ErpOne.Web
```
Periksa berkas migration yang dihasilkan: harus membuat `M_PriceLists`, `M_PriceListLines`, `M_PricingSettings`, menambah kolom `PriceListId` di `M_Customers`, `DefaultPriceListId` di `M_Warehouses`, `MaxDiscountPercent` di `AspNetRoles`, plus `InsertData` untuk `M_PricingSettings`.

- [ ] **Step 14: Build + seluruh test**

Run: `dotnet build ErpOne.slnx -v q --nologo` lalu `dotnet test ErpOne.slnx --nologo -v q`
Expected: 0 warning, 0 error; unit 175 lolos, integration 228 lolos.

- [ ] **Step 15: Commit (user menjalankan)**

```bash
git add src/ErpOne.Domain/Entities/Master/PriceList.cs src/ErpOne.Domain/Entities/Master/PriceListLine.cs \
        src/ErpOne.Domain/Entities/Settings/PricingSetting.cs src/ErpOne.Domain/Entities/Master/Customer.cs \
        src/ErpOne.Domain/Entities/Master/Warehouse.cs src/ErpOne.Infrastructure/Identity/ApplicationRole.cs \
        src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations \
        tests/ErpOne.UnitTests/PriceListDomainTests.cs tests/ErpOne.IntegrationTests/PricingSchemaTests.cs
git commit -m "feat(pricing): PriceList domain + EF mapping + migration"
```

---

## Task 2: `PriceMath` — aturan hitung murni

**Files:**
- Create: `src/ErpOne.Application/Pricing/PriceMath.cs`
- Test: `tests/ErpOne.UnitTests/PriceMathTests.cs`

**Interfaces:**
- Consumes: tidak ada (murni).
- Produces: `PriceMath.PickTier(IEnumerable<(int MinQty, decimal UnitPrice)> tiers, int quantity) → (int MinQty, decimal UnitPrice)?`; `PriceMath.DeviationPercent(decimal resolvedPrice, decimal unitPrice, decimal discountPercent) → decimal`; `PriceMath.EffectiveMaxDiscountPercent(IEnumerable<decimal?> roleLimits, decimal globalDefault) → decimal`.

- [ ] **Step 1: Tulis unit test yang gagal**

Buat `tests/ErpOne.UnitTests/PriceMathTests.cs`:

```csharp
using ErpOne.Application.Pricing;
using Xunit;

namespace ErpOne.UnitTests;

public class PriceMathTests
{
    private static readonly (int MinQty, decimal UnitPrice)[] Tiers =
    [
        (1, 90_000m),
        (10, 85_000m),
        (50, 78_000m),
    ];

    [Theory]
    [InlineData(1, 1, 90_000)]
    [InlineData(9, 1, 90_000)]
    [InlineData(10, 10, 85_000)]
    [InlineData(49, 10, 85_000)]
    [InlineData(50, 50, 78_000)]
    [InlineData(600, 50, 78_000)]
    public void PickTier_takes_largest_min_qty_not_exceeding_quantity(int qty, int expectedMinQty, decimal expectedPrice)
    {
        var tier = PriceMath.PickTier(Tiers, qty);

        Assert.NotNull(tier);
        Assert.Equal(expectedMinQty, tier!.Value.MinQty);
        Assert.Equal(expectedPrice, tier.Value.UnitPrice);
    }

    [Fact]
    public void PickTier_returns_null_when_no_tiers()
    {
        Assert.Null(PriceMath.PickTier([], 10));
    }

    [Fact]
    public void PickTier_returns_null_when_quantity_below_smallest_tier()
    {
        Assert.Null(PriceMath.PickTier([(5, 90_000m), (10, 85_000m)], 4));
    }

    [Fact]
    public void PickTier_is_independent_of_input_order()
    {
        var shuffled = new[] { (50, 78_000m), (1, 90_000m), (10, 85_000m) };
        var tier = PriceMath.PickTier(shuffled, 12);

        Assert.Equal(10, tier!.Value.MinQty);
    }

    [Fact]
    public void Deviation_from_discount_percent_only()
    {
        // harga engine 100.000, client kirim harga sama + diskon 10% -> menyimpang 10%
        Assert.Equal(10m, PriceMath.DeviationPercent(100_000m, 100_000m, 10m));
    }

    [Fact]
    public void Deviation_from_price_override_only()
    {
        // harga engine 100.000, client kirim 90.000 tanpa diskon -> menyimpang 10%
        Assert.Equal(10m, PriceMath.DeviationPercent(100_000m, 90_000m, 0m));
    }

    [Fact]
    public void Deviation_combines_price_override_and_discount()
    {
        // 90.000 * 0,9 = 81.000 dari 100.000 -> menyimpang 19%
        Assert.Equal(19m, PriceMath.DeviationPercent(100_000m, 90_000m, 10m));
    }

    [Fact]
    public void Deviation_is_negative_when_price_is_above_engine_price()
    {
        Assert.Equal(-20m, PriceMath.DeviationPercent(100_000m, 120_000m, 0m));
    }

    [Fact]
    public void Deviation_is_zero_when_resolved_price_is_zero()
    {
        // harga master belum diatur -> jangan bagi nol, anggap lolos
        Assert.Equal(0m, PriceMath.DeviationPercent(0m, 50_000m, 90m));
    }

    [Fact]
    public void Deviation_is_hundred_when_line_is_fully_discounted()
    {
        Assert.Equal(100m, PriceMath.DeviationPercent(100_000m, 100_000m, 100m));
    }

    [Fact]
    public void Effective_max_takes_largest_role_limit()
    {
        Assert.Equal(30m, PriceMath.EffectiveMaxDiscountPercent([5m, 30m, 15m], 100m));
    }

    [Fact]
    public void Effective_max_ignores_null_role_limits()
    {
        Assert.Equal(15m, PriceMath.EffectiveMaxDiscountPercent([null, 15m, null], 100m));
    }

    [Fact]
    public void Effective_max_falls_back_to_global_default_when_all_null()
    {
        Assert.Equal(7m, PriceMath.EffectiveMaxDiscountPercent([null, null], 7m));
    }

    [Fact]
    public void Effective_max_falls_back_to_global_default_when_no_roles()
    {
        Assert.Equal(42m, PriceMath.EffectiveMaxDiscountPercent([], 42m));
    }

    [Fact]
    public void Effective_max_of_zero_is_honoured_not_treated_as_unset()
    {
        // role dengan batas 0 = tidak boleh diskon; tidak boleh jatuh ke default global
        Assert.Equal(0m, PriceMath.EffectiveMaxDiscountPercent([0m], 100m));
    }
}
```

- [ ] **Step 2: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.UnitTests --filter "FullyQualifiedName~PriceMathTests"`
Expected: FAIL — `PriceMath` belum ada.

- [ ] **Step 3: Implementasi `PriceMath`**

`src/ErpOne.Application/Pricing/PriceMath.cs`:

```csharp
namespace ErpOne.Application.Pricing;

/// <summary>Aturan hitung pricing yang murni (tanpa DB) agar dapat diuji sebagai unit
/// dan dibaca tanpa membaca query EF.</summary>
public static class PriceMath
{
    /// <summary>Tier yang berlaku: MinQty terbesar yang tidak melebihi qty. null bila tak ada yang cocok.</summary>
    public static (int MinQty, decimal UnitPrice)? PickTier(
        IEnumerable<(int MinQty, decimal UnitPrice)> tiers, int quantity)
    {
        (int MinQty, decimal UnitPrice)? best = null;
        foreach (var tier in tiers)
        {
            if (tier.MinQty > quantity) continue;
            if (best is null || tier.MinQty > best.Value.MinQty) best = tier;
        }
        return best;
    }

    /// <summary>Penyimpangan harga efektif client terhadap harga engine, dalam persen.
    /// Positif = lebih murah dari harga engine. resolvedPrice &lt;= 0 menghasilkan 0 (dianggap lolos,
    /// menghindari bagi nol saat harga master belum diatur).</summary>
    public static decimal DeviationPercent(decimal resolvedPrice, decimal unitPrice, decimal discountPercent)
    {
        if (resolvedPrice <= 0m) return 0m;

        var effective = unitPrice * (1m - discountPercent / 100m);
        var deviation = (1m - effective / resolvedPrice) * 100m;
        return Math.Round(deviation, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Batas diskon efektif: MAX dari role yang nilainya terisi (menambah role menambah
    /// wewenang), atau default global bila tidak ada yang terisi. Nilai 0 dihormati, bukan dianggap kosong.</summary>
    public static decimal EffectiveMaxDiscountPercent(
        IEnumerable<decimal?> roleLimits, decimal globalDefault)
    {
        decimal? max = null;
        foreach (var limit in roleLimits)
        {
            if (limit is null) continue;
            if (max is null || limit.Value > max.Value) max = limit.Value;
        }
        return max ?? globalDefault;
    }
}
```

- [ ] **Step 4: Jalankan test — harus lolos**

Run: `dotnet test tests/ErpOne.UnitTests --filter "FullyQualifiedName~PriceMathTests"`
Expected: PASS (20 test, termasuk 6 dari `[Theory]`).

- [ ] **Step 5: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Pricing/PriceMath.cs tests/ErpOne.UnitTests/PriceMathTests.cs
git commit -m "feat(pricing): PriceMath — tier picking, deviation, effective max discount"
```

---

## Task 3: Seam `IPricingService` + implementasi + DI

**Files:**
- Create: `src/ErpOne.Application/Pricing/IPricingService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Pricing/PricingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/PricingServiceTests.cs`

**Interfaces:**
- Consumes: `PriceMath` (Task 2); `PriceList`, `PriceListLine`, `PricingSetting`, `Customer.PriceListId`, `Warehouse.DefaultPriceListId`, `ApplicationRole.MaxDiscountPercent` (Task 1).
- Produces: `PriceSource` enum (`VariantPrice`, `VariantDiscountPrice`, `PriceList`); `PriceRequest(int ProductVariantId, int Quantity, int? CustomerId, int? WarehouseId, DateOnly OnDate)`; `PriceResult(decimal UnitPrice, decimal ListPrice, PriceSource Source, int? PriceListId, string? PriceListName, int? MatchedMinQty)`; `IPricingService.ResolveAsync`, `ResolveManyAsync`, `GetMaxDiscountPercentAsync`.

- [ ] **Step 1: Tulis kontrak**

`src/ErpOne.Application/Pricing/IPricingService.cs`:

```csharp
namespace ErpOne.Application.Pricing;

public enum PriceSource { VariantPrice, VariantDiscountPrice, PriceList }

/// <summary>OnDate belum dipakai di 6b-1; ada sejak awal agar promo terjadwal (6b-2)
/// tidak memaksa perubahan signature di seluruh pemanggil.</summary>
public sealed record PriceRequest(
    int ProductVariantId,
    int Quantity,
    int? CustomerId,
    int? WarehouseId,
    DateOnly OnDate);

public sealed record PriceResult(
    decimal UnitPrice,
    decimal ListPrice,
    PriceSource Source,
    int? PriceListId,
    string? PriceListName,
    int? MatchedMinQty);

public interface IPricingService
{
    Task<PriceResult> ResolveAsync(PriceRequest req, CancellationToken ct = default);

    /// <summary>Batch — dipakai POS search &amp; prefill SO agar tidak N+1.</summary>
    Task<IReadOnlyList<PriceResult>> ResolveManyAsync(
        IReadOnlyList<PriceRequest> reqs, CancellationToken ct = default);

    /// <summary>Batas diskon efektif untuk kumpulan role. Kosong/null → default global.</summary>
    Task<decimal> GetMaxDiscountPercentAsync(
        IEnumerable<string>? roleNames, CancellationToken ct = default);
}
```

- [ ] **Step 2: Tulis test integrasi yang gagal**

Buat `tests/ErpOne.IntegrationTests/PricingServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Pricing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static readonly DateOnly Today = new(2026, 7, 27);

    /// <summary>Buat varian dengan harga master tertentu. Kode unik per pemanggil agar test independen.</summary>
    private static async Task<int> NewVariantAsync(AppDbContext db, string suffix, decimal price, decimal? discountPrice = null)
    {
        var product = new Product($"PRC-P-{suffix}", $"Pricing Probe {suffix}", null, null, null, null, ProductStatus.Active);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant($"PRC-SKU-{suffix}", null, price, discountPrice, 0m, null, null, true);
        typeof(ProductVariant).GetProperty(nameof(ProductVariant.ProductId))!
            .SetValue(variant, product.Id);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private static async Task<int> NewPriceListAsync(AppDbContext db, string code, bool isActive,
        int variantId, params (int MinQty, decimal Price)[] tiers)
    {
        var list = new PriceList(code, code, null, isActive);
        list.SetLines(tiers.Select(t => new PriceListLine(variantId, t.MinQty, t.Price)));
        db.PriceLists.Add(list);
        await db.SaveChangesAsync();
        return list.Id;
    }

    [Fact]
    public async Task Resolves_tier_by_quantity()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "TIER", 100_000m);
        var listId = await NewPriceListAsync(db, "PRC-TIER", true, variantId,
            (1, 90_000m), (10, 85_000m), (50, 78_000m));

        var wh = new Warehouse("PRC-WH-TIER", "Tier WH", null, true, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var nine = await pricing.ResolveAsync(new PriceRequest(variantId, 9, null, wh.Id, Today));
        var ten = await pricing.ResolveAsync(new PriceRequest(variantId, 10, null, wh.Id, Today));
        var sixty = await pricing.ResolveAsync(new PriceRequest(variantId, 60, null, wh.Id, Today));

        Assert.Equal(90_000m, nine.UnitPrice);
        Assert.Equal(1, nine.MatchedMinQty);
        Assert.Equal(85_000m, ten.UnitPrice);
        Assert.Equal(78_000m, sixty.UnitPrice);
        Assert.Equal(PriceSource.PriceList, sixty.Source);
        Assert.Equal(100_000m, sixty.ListPrice); // harga master tetap dilaporkan
    }

    [Fact]
    public async Task Falls_back_to_variant_discount_price_then_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var withDiscount = await NewVariantAsync(db, "FB1", 100_000m, 95_000m);
        var plain = await NewVariantAsync(db, "FB2", 70_000m);

        var a = await pricing.ResolveAsync(new PriceRequest(withDiscount, 1, null, null, Today));
        var b = await pricing.ResolveAsync(new PriceRequest(plain, 1, null, null, Today));

        Assert.Equal(95_000m, a.UnitPrice);
        Assert.Equal(PriceSource.VariantDiscountPrice, a.Source);
        Assert.Equal(70_000m, b.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, b.Source);
    }

    [Fact]
    public async Task Variant_absent_from_price_list_falls_back_to_master_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var listed = await NewVariantAsync(db, "ABS1", 100_000m);
        var unlisted = await NewVariantAsync(db, "ABS2", 60_000m);
        var listId = await NewPriceListAsync(db, "PRC-ABSENT", true, listed, (1, 90_000m));

        var wh = new Warehouse("PRC-WH-ABS", "Absent WH", null, true, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(unlisted, 5, null, wh.Id, Today));

        Assert.Equal(60_000m, result.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, result.Source);
        Assert.Null(result.PriceListId);
    }

    [Fact]
    public async Task Customer_price_list_wins_over_warehouse_default()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "PRIO", 100_000m);
        var retailId = await NewPriceListAsync(db, "PRC-RETAIL", true, variantId, (1, 95_000m));
        var grosirId = await NewPriceListAsync(db, "PRC-GROSIR", true, variantId, (1, 80_000m));

        var wh = new Warehouse("PRC-WH-PRIO", "Prio WH", null, true, retailId);
        db.Warehouses.Add(wh);
        var cust = new Customer("PRC-C-PRIO", "Prio Customer", null, null, null, null, null, 0, "IDR", 0m, true, grosirId);
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(variantId, 1, cust.Id, wh.Id, Today));

        Assert.Equal(80_000m, result.UnitPrice);
        Assert.Equal(grosirId, result.PriceListId);
    }

    [Fact]
    public async Task Inactive_price_list_is_ignored_without_error()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var variantId = await NewVariantAsync(db, "INACT", 100_000m);
        var listId = await NewPriceListAsync(db, "PRC-INACTIVE", false, variantId, (1, 50_000m));

        var wh = new Warehouse("PRC-WH-INACT", "Inactive WH", null, true, listId);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var result = await pricing.ResolveAsync(new PriceRequest(variantId, 1, null, wh.Id, Today));

        Assert.Equal(100_000m, result.UnitPrice);
        Assert.Equal(PriceSource.VariantPrice, result.Source);
    }

    [Fact]
    public async Task ResolveMany_returns_results_in_request_order()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var a = await NewVariantAsync(db, "MANY-A", 10_000m);
        var b = await NewVariantAsync(db, "MANY-B", 20_000m);
        var c = await NewVariantAsync(db, "MANY-C", 30_000m);

        var results = await pricing.ResolveManyAsync(
        [
            new PriceRequest(c, 1, null, null, Today),
            new PriceRequest(a, 1, null, null, Today),
            new PriceRequest(b, 1, null, null, Today),
        ]);

        Assert.Equal([30_000m, 10_000m, 20_000m], results.Select(r => r.UnitPrice));
    }

    [Fact]
    public async Task Max_discount_falls_back_to_global_default_when_no_roles()
    {
        using var scope = _factory.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync(null));
        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync([]));
    }

    [Fact]
    public async Task Max_discount_takes_largest_across_roles()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        db.Roles.Add(new ErpOne.Infrastructure.Identity.ApplicationRole("PRC-Cashier")
        { NormalizedName = "PRC-CASHIER", MaxDiscountPercent = 5m });
        db.Roles.Add(new ErpOne.Infrastructure.Identity.ApplicationRole("PRC-Supervisor")
        { NormalizedName = "PRC-SUPERVISOR", MaxDiscountPercent = 20m });
        db.Roles.Add(new ErpOne.Infrastructure.Identity.ApplicationRole("PRC-Unset")
        { NormalizedName = "PRC-UNSET" });
        await db.SaveChangesAsync();

        Assert.Equal(20m, await pricing.GetMaxDiscountPercentAsync(["PRC-Cashier", "PRC-Supervisor"]));
        Assert.Equal(5m, await pricing.GetMaxDiscountPercentAsync(["PRC-Cashier", "PRC-Unset"]));
        Assert.Equal(100m, await pricing.GetMaxDiscountPercentAsync(["PRC-Unset"]));
    }
}
```

Catatan: bila `ProductVariant` sudah punya cara resmi menetapkan `ProductId` (mis. lewat `Product.AddVariant`), pakai itu dan hapus baris refleksi di helper `NewVariantAsync`. Periksa `Product.cs` dulu; refleksi hanya jalan pintas bila memang tidak ada API-nya.

- [ ] **Step 3: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PricingServiceTests"`
Expected: FAIL — `IPricingService` belum terdaftar di DI.

- [ ] **Step 4: Implementasi `PricingService`**

`src/ErpOne.Infrastructure/Services/Pricing/PricingService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Pricing;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PricingService(AppDbContext db) : IPricingService
{
    public async Task<PriceResult> ResolveAsync(PriceRequest req, CancellationToken ct = default) =>
        (await ResolveManyAsync([req], ct))[0];

    public async Task<IReadOnlyList<PriceResult>> ResolveManyAsync(
        IReadOnlyList<PriceRequest> reqs, CancellationToken ct = default)
    {
        if (reqs.Count == 0) return [];

        var variantIds = reqs.Select(r => r.ProductVariantId).Distinct().ToList();
        var variantRows = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Price, v.DiscountPrice })
            .ToListAsync(ct);
        var variants = variantRows.ToDictionary(v => v.Id, v => (v.Price, v.DiscountPrice));

        var customerIds = reqs.Where(r => r.CustomerId is > 0).Select(r => r.CustomerId!.Value).Distinct().ToList();
        var warehouseIds = reqs.Where(r => r.WarehouseId is > 0).Select(r => r.WarehouseId!.Value).Distinct().ToList();

        var customerLists = customerIds.Count == 0
            ? new Dictionary<int, int?>()
            : (await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PriceListId }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.PriceListId);

        var warehouseLists = warehouseIds.Count == 0
            ? new Dictionary<int, int?>()
            : (await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
                .Select(w => new { w.Id, w.DefaultPriceListId }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.DefaultPriceListId);

        var candidateIds = customerLists.Values.Concat(warehouseLists.Values)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        // Hanya price list AKTIF yang dipertimbangkan; sisanya jatuh ke fallback.
        var activeLists = candidateIds.Count == 0
            ? new Dictionary<int, string>()
            : (await db.PriceLists.AsNoTracking()
                .Where(p => candidateIds.Contains(p.Id) && p.IsActive)
                .Select(p => new { p.Id, p.Name }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Name);

        var activeIds = activeLists.Keys.ToList();
        var tierRows = activeIds.Count == 0
            ? []
            : await db.PriceListLines.AsNoTracking()
                .Where(l => activeIds.Contains(l.PriceListId) && variantIds.Contains(l.ProductVariantId))
                .Select(l => new { l.PriceListId, l.ProductVariantId, l.MinQty, l.UnitPrice })
                .ToListAsync(ct);

        var results = new List<PriceResult>(reqs.Count);
        foreach (var r in reqs)
        {
            var hasVariant = variants.TryGetValue(r.ProductVariantId, out var v);
            var listPrice = hasVariant ? v.Price : 0m;

            var listId = PickPriceListId(r, customerLists, warehouseLists, activeLists);
            if (listId is not null)
            {
                var tiers = tierRows
                    .Where(l => l.PriceListId == listId.Value && l.ProductVariantId == r.ProductVariantId)
                    .Select(l => (l.MinQty, l.UnitPrice));

                if (PriceMath.PickTier(tiers, r.Quantity) is { } tier)
                {
                    results.Add(new PriceResult(tier.UnitPrice, listPrice, PriceSource.PriceList,
                        listId, activeLists[listId.Value], tier.MinQty));
                    continue;
                }
            }

            if (hasVariant && v.DiscountPrice is { } discountPrice)
                results.Add(new PriceResult(discountPrice, listPrice, PriceSource.VariantDiscountPrice, null, null, null));
            else
                results.Add(new PriceResult(listPrice, listPrice, PriceSource.VariantPrice, null, null, null));
        }

        return results;
    }

    /// <summary>Customer menang atas gudang; keduanya harus menunjuk price list yang aktif.</summary>
    private static int? PickPriceListId(
        PriceRequest r,
        Dictionary<int, int?> customerLists,
        Dictionary<int, int?> warehouseLists,
        Dictionary<int, string> activeLists)
    {
        if (r.CustomerId is > 0
            && customerLists.TryGetValue(r.CustomerId.Value, out var fromCustomer)
            && fromCustomer is not null
            && activeLists.ContainsKey(fromCustomer.Value))
            return fromCustomer;

        if (r.WarehouseId is > 0
            && warehouseLists.TryGetValue(r.WarehouseId.Value, out var fromWarehouse)
            && fromWarehouse is not null
            && activeLists.ContainsKey(fromWarehouse.Value))
            return fromWarehouse;

        return null;
    }

    public async Task<decimal> GetMaxDiscountPercentAsync(
        IEnumerable<string>? roleNames, CancellationToken ct = default)
    {
        var globalDefault = await db.PricingSettings.AsNoTracking()
            .Select(x => x.DefaultMaxDiscountPercent).FirstOrDefaultAsync(ct);

        var names = roleNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [];
        if (names.Count == 0) return globalDefault;

        var limits = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null && names.Contains(r.Name))
            .Select(r => r.MaxDiscountPercent)
            .ToListAsync(ct);

        return PriceMath.EffectiveMaxDiscountPercent(limits, globalDefault);
    }
}
```

- [ ] **Step 5: Registrasi DI**

Di `src/ErpOne.Infrastructure/DependencyInjection.cs`, tambah di dekat `AddScoped<ICurrencyService, CurrencyService>()` (baris ~70):

```csharp
        services.AddScoped<IPricingService, PricingService>();
```

Tambah `using ErpOne.Application.Pricing;` bila belum ada.

- [ ] **Step 6: Jalankan test — harus lolos**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PricingServiceTests"`
Expected: PASS (8 test).

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Pricing/IPricingService.cs \
        src/ErpOne.Infrastructure/Services/Pricing/PricingService.cs \
        src/ErpOne.Infrastructure/DependencyInjection.cs \
        tests/ErpOne.IntegrationTests/PricingServiceTests.cs
git commit -m "feat(pricing): IPricingService seam + resolution (price list, tier, fallback)"
```

---

## Task 4: CRUD Price List

**Files:**
- Create: `src/ErpOne.Application/PriceLists/IPriceListService.cs`
- Create: `src/ErpOne.Application/PriceLists/PriceListDtos.cs`
- Create: `src/ErpOne.Application/PriceLists/PriceListValidators.cs`
- Create: `src/ErpOne.Infrastructure/Services/Master/PriceListService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/PriceListServiceTests.cs`

**Interfaces:**
- Consumes: `PriceList`, `PriceListLine` (Task 1); `PagedResult<T>` dari `ErpOne.Application.Common`.
- Produces: `PriceListDto(int Id, string Code, string Name, string? Description, bool IsActive, int LineCount, DateTime CreatedAt, string? CreatedBy)`; `PriceListDetailDto(int Id, string Code, string Name, string? Description, bool IsActive, IReadOnlyList<PriceListLineDto> Lines)`; `PriceListLineDto(int Id, int ProductVariantId, string VariantSku, string ProductName, int MinQty, decimal UnitPrice)`; `PriceListLineRequest(int ProductVariantId, int MinQty, decimal UnitPrice)`; `CreatePriceListRequest`/`UpdatePriceListRequest`; `IPriceListService`.

- [ ] **Step 1: Tulis DTO**

`src/ErpOne.Application/PriceLists/PriceListDtos.cs`:

```csharp
namespace ErpOne.Application.PriceLists;

public record PriceListDto(int Id, string Code, string Name, string? Description, bool IsActive,
    int LineCount, DateTime CreatedAt, string? CreatedBy);

public record PriceListLineDto(int Id, int ProductVariantId, string VariantSku, string ProductName,
    int MinQty, decimal UnitPrice);

public record PriceListDetailDto(int Id, string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineDto> Lines);

public record PriceListLineRequest(int ProductVariantId, int MinQty, decimal UnitPrice);

public record CreatePriceListRequest(string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineRequest> Lines);

public record UpdatePriceListRequest(string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineRequest> Lines);
```

- [ ] **Step 2: Tulis kontrak**

`src/ErpOne.Application/PriceLists/IPriceListService.cs`:

```csharp
using ErpOne.Application.Common;

namespace ErpOne.Application.PriceLists;

public interface IPriceListService
{
    Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PriceListDto>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<PriceListDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<PriceListDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PriceListDetailDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdatePriceListRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3: Tulis validator**

`src/ErpOne.Application/PriceLists/PriceListValidators.cs`:

```csharp
using FluentValidation;

namespace ErpOne.Application.PriceLists;

public class PriceListLineRequestValidator : AbstractValidator<PriceListLineRequest>
{
    public PriceListLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.MinQty).GreaterThanOrEqualTo(1);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class CreatePriceListValidator : AbstractValidator<CreatePriceListRequest>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(255);
        RuleForEach(x => x.Lines).SetValidator(new PriceListLineRequestValidator());
        RuleFor(x => x.Lines).Must(NoDuplicateTiers)
            .WithMessage("Each product variant may appear only once per minimum quantity.");
    }

    internal static bool NoDuplicateTiers(IReadOnlyList<PriceListLineRequest> lines) =>
        lines is null || lines.Count == lines.Select(l => (l.ProductVariantId, l.MinQty)).Distinct().Count();
}

public class UpdatePriceListValidator : AbstractValidator<UpdatePriceListRequest>
{
    public UpdatePriceListValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(255);
        RuleForEach(x => x.Lines).SetValidator(new PriceListLineRequestValidator());
        RuleFor(x => x.Lines).Must(CreatePriceListValidator.NoDuplicateTiers)
            .WithMessage("Each product variant may appear only once per minimum quantity.");
    }
}
```

- [ ] **Step 4: Tulis test integrasi yang gagal**

Buat `tests/ErpOne.IntegrationTests/PriceListServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PriceLists;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PriceListServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PriceListServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> NewVariantAsync(AppDbContext db, string suffix, decimal price)
    {
        var product = new Product($"PL-P-{suffix}", $"PL Probe {suffix}", null, null, null, null, ProductStatus.Active);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant($"PL-SKU-{suffix}", null, price, null, 0m, null, null, true);
        typeof(ProductVariant).GetProperty(nameof(ProductVariant.ProductId))!.SetValue(variant, product.Id);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    [Fact]
    public async Task Create_normalizes_code_and_persists_tiers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "CREATE", 100_000m);

        var created = await svc.CreateAsync(new CreatePriceListRequest(" pl-create ", "Create List", null, true,
        [
            new PriceListLineRequest(variantId, 1, 90_000m),
            new PriceListLineRequest(variantId, 10, 85_000m),
        ]));

        Assert.Equal("PL-CREATE", created.Code);
        Assert.Equal(2, created.Lines.Count);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(2, fetched!.Lines.Count);
        Assert.Contains(fetched.Lines, l => l.MinQty == 10 && l.UnitPrice == 85_000m);
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        await svc.CreateAsync(new CreatePriceListRequest("PL-DUP", "Dup", null, true, []));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreatePriceListRequest("pl-dup", "Dup Again", null, true, [])));
    }

    [Fact]
    public async Task Duplicate_tier_in_request_is_rejected_by_validator()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "DUPTIER", 100_000m);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreatePriceListRequest("PL-DUPTIER", "Dup Tier", null, true,
            [
                new PriceListLineRequest(variantId, 1, 90_000m),
                new PriceListLineRequest(variantId, 1, 80_000m),
            ])));
    }

    [Fact]
    public async Task Update_replaces_lines_wholesale()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "UPD", 100_000m);
        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-UPD", "Upd", null, true,
            [new PriceListLineRequest(variantId, 1, 90_000m), new PriceListLineRequest(variantId, 10, 85_000m)]));

        var ok = await svc.UpdateAsync(created.Id, new UpdatePriceListRequest("PL-UPD", "Upd", null, true,
            [new PriceListLineRequest(variantId, 5, 88_000m)]));

        Assert.True(ok);
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Single(fetched!.Lines);
        Assert.Equal(5, fetched.Lines[0].MinQty);
    }

    [Fact]
    public async Task Delete_is_rejected_while_referenced_by_customer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-REF-C", "Ref Customer", null, true, []));

        db.Customers.Add(new Customer("PL-C-REF", "Ref Customer", null, null, null, null, null, 0, "IDR", 0m, true, created.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Delete_is_rejected_while_referenced_by_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-REF-W", "Ref Warehouse", null, true, []));

        db.Warehouses.Add(new Warehouse("PL-WH-REF", "Ref WH", null, true, created.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Delete_unreferenced_list_removes_it_with_lines()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "DEL", 100_000m);
        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-DEL", "Del", null, true,
            [new PriceListLineRequest(variantId, 1, 90_000m)]));

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
        Assert.False(await db.PriceListLines.AnyAsync(l => l.PriceListId == created.Id));
    }
}
```

- [ ] **Step 5: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PriceListServiceTests"`
Expected: FAIL — `IPriceListService` belum terdaftar.

- [ ] **Step 6: Implementasi `PriceListService`**

`src/ErpOne.Infrastructure/Services/Master/PriceListService.cs`:

```csharp
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.PriceLists;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PriceListService(
    AppDbContext db,
    IValidator<CreatePriceListRequest> createValidator,
    IValidator<UpdatePriceListRequest> updateValidator) : IPriceListService
{
    public async Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default) =>
        await BaseQuery().OrderBy(x => x.Code).ToListAsync(ct);

    public async Task<IReadOnlyList<PriceListDto>> GetActiveAsync(CancellationToken ct = default) =>
        await BaseQuery(activeOnly: true).OrderBy(x => x.Code).ToListAsync(ct);

    public async Task<PagedResult<PriceListDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = BaseQuery();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<PriceListDto>(items, total, page, pageSize);
    }

    public async Task<PriceListDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var list = await db.PriceLists.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (list is null) return null;

        var variantIds = list.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var lines = list.Lines
            .OrderBy(l => l.ProductVariantId).ThenBy(l => l.MinQty)
            .Select(l =>
            {
                var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
                var productName = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
                return new PriceListLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", productName, l.MinQty, l.UnitPrice);
            })
            .ToList();

        return new PriceListDetailDto(list.Id, list.Code, list.Name, list.Description, list.IsActive, lines);
    }

    public async Task<PriceListDetailDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);
        await EnsureVariantsExistAsync(request.Lines, ct);

        var entity = new PriceList(code, request.Name, request.Description, request.IsActive);
        entity.SetLines(request.Lines.Select(l => new PriceListLine(l.ProductVariantId, l.MinQty, l.UnitPrice)));

        db.PriceLists.Add(entity);
        await db.SaveChangesAsync(ct);

        return (await GetByIdAsync(entity.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdatePriceListRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.PriceLists.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return false;

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        await EnsureVariantsExistAsync(request.Lines, ct);

        // Baris diganti seluruhnya — hapus yang lama agar tidak ada sisa (pola SalesOrderService.UpdateAsync).
        var oldLines = await db.PriceListLines.Where(l => l.PriceListId == id).ToListAsync(ct);
        db.PriceListLines.RemoveRange(oldLines);

        entity.Update(code, request.Name, request.Description, request.IsActive);
        entity.SetLines(request.Lines.Select(l => new PriceListLine(l.ProductVariantId, l.MinQty, l.UnitPrice)));

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.PriceLists.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return false;

        if (await db.Customers.AnyAsync(c => c.PriceListId == id, ct))
            throw Fail("This price list is assigned to one or more customers and cannot be deleted.");
        if (await db.Warehouses.AnyAsync(w => w.DefaultPriceListId == id, ct))
            throw Fail("This price list is the default for one or more warehouses and cannot be deleted.");

        db.PriceLists.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<PriceListDto> BaseQuery(bool activeOnly = false)
    {
        var query = db.PriceLists.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);

        return query.Select(x => new PriceListDto(x.Id, x.Code, x.Name, x.Description, x.IsActive,
            x.Lines.Count, x.CreatedAt, x.CreatedBy));
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.PriceLists.AsNoTracking()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists) throw Fail($"Code '{code}' is already in use.");
    }

    private async Task EnsureVariantsExistAsync(IReadOnlyList<PriceListLineRequest> lines, CancellationToken ct)
    {
        if (lines.Count == 0) return;

        var ids = lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var found = await db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct);

        var missing = ids.Except(found).ToList();
        if (missing.Count > 0)
            throw Fail($"Unknown product variant(s): {string.Join(", ", missing)}.");
    }

    private static ValidationException Fail(string message) =>
        new([new ValidationFailure(nameof(CreatePriceListRequest.Code), message)]);
}
```

- [ ] **Step 7: Registrasi DI**

Di `src/ErpOne.Infrastructure/DependencyInjection.cs`, tambah:

```csharp
        services.AddScoped<IPriceListService, PriceListService>();
```

Validator ter-registrasi otomatis oleh `AddValidatorsFromAssemblyContaining<CreateProductValidator>()` (`DependencyInjection.cs:63`) karena berada di assembly Application yang sama — tidak perlu registrasi manual.

- [ ] **Step 8: Jalankan test — harus lolos**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PriceListServiceTests"`
Expected: PASS (7 test).

- [ ] **Step 9: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/PriceLists src/ErpOne.Infrastructure/Services/Master/PriceListService.cs \
        src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/PriceListServiceTests.cs
git commit -m "feat(pricing): PriceList CRUD service + validators"
```

---

## Task 5: Setelan pricing global

**Files:**
- Create: `src/ErpOne.Application/Pricing/IPricingSettingService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Settings/PricingSettingService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Test: `tests/ErpOne.IntegrationTests/PricingSettingServiceTests.cs`

**Interfaces:**
- Consumes: `PricingSetting` (Task 1).
- Produces: `PricingSettingDto(decimal DefaultMaxDiscountPercent)`; `IPricingSettingService.GetAsync()`, `UpdateAsync(decimal defaultMaxDiscountPercent, CancellationToken)`.

- [ ] **Step 1: Tulis kontrak**

`src/ErpOne.Application/Pricing/IPricingSettingService.cs`:

```csharp
namespace ErpOne.Application.Pricing;

public record PricingSettingDto(decimal DefaultMaxDiscountPercent);

public interface IPricingSettingService
{
    Task<PricingSettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(decimal defaultMaxDiscountPercent, CancellationToken ct = default);
}
```

- [ ] **Step 2: Tulis test yang gagal**

Buat `tests/ErpOne.IntegrationTests/PricingSettingServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Pricing;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PricingSettingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingSettingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Update_then_read_roundtrips()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPricingSettingService>();

        await svc.UpdateAsync(12.5m);
        Assert.Equal(12.5m, (await svc.GetAsync()).DefaultMaxDiscountPercent);

        // Kembalikan ke 100 agar test lain (fixture berbagi) tidak terpengaruh.
        await svc.UpdateAsync(100m);
    }

    [Fact]
    public async Task Percent_outside_range_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPricingSettingService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(-1m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(101m));
    }
}
```

- [ ] **Step 3: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PricingSettingServiceTests"`
Expected: FAIL — service belum ada.

- [ ] **Step 4: Implementasi**

`src/ErpOne.Infrastructure/Services/Settings/PricingSettingService.cs`:

```csharp
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Pricing;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PricingSettingService(AppDbContext db) : IPricingSettingService
{
    public async Task<PricingSettingDto> GetAsync(CancellationToken ct = default)
    {
        var percent = await db.PricingSettings.AsNoTracking()
            .Select(x => x.DefaultMaxDiscountPercent).FirstOrDefaultAsync(ct);
        return new PricingSettingDto(percent);
    }

    public async Task UpdateAsync(decimal defaultMaxDiscountPercent, CancellationToken ct = default)
    {
        if (defaultMaxDiscountPercent is < 0m or > 100m)
            throw new ValidationException(
                [new ValidationFailure(nameof(PricingSettingDto.DefaultMaxDiscountPercent),
                    "Percent must be between 0 and 100.")]);

        var row = await db.PricingSettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PricingSetting seed row (Id=1) is missing.");

        row.SetDefaultMaxDiscountPercent(defaultMaxDiscountPercent);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Registrasi DI**

```csharp
        services.AddScoped<IPricingSettingService, PricingSettingService>();
```

- [ ] **Step 6: Jalankan test — harus lolos**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PricingSettingServiceTests"`
Expected: PASS (2 test).

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Pricing/IPricingSettingService.cs \
        src/ErpOne.Infrastructure/Services/Settings/PricingSettingService.cs \
        src/ErpOne.Infrastructure/DependencyInjection.cs \
        tests/ErpOne.IntegrationTests/PricingSettingServiceTests.cs
git commit -m "feat(pricing): global pricing setting (default max discount)"
```

---

## Task 6: Guardrail di Sales Order

**Files:**
- Modify: `src/ErpOne.Application/Transactions/SalesOrders/ISalesOrderService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Transactions/SalesOrderService.cs:12-17` (ctor), `:133` (`CreateAsync`), `:153` (`UpdateAsync`), `:252` (`BuildLinesAsync`)
- Test: `tests/ErpOne.IntegrationTests/SalesOrderPricingGuardrailTests.cs`

**Interfaces:**
- Consumes: `IPricingService` (Task 3), `PriceMath.DeviationPercent` (Task 2).
- Produces: `ISalesOrderService.CreateAsync(CreateSalesOrderRequest request, IReadOnlyList<string>? roleNames = null, CancellationToken ct = default)` dan `UpdateAsync(int id, UpdateSalesOrderRequest request, IReadOnlyList<string>? roleNames = null, CancellationToken ct = default)`.

- [ ] **Step 1: Tulis test yang gagal**

Buat `tests/ErpOne.IntegrationTests/SalesOrderPricingGuardrailTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PriceLists;
using ErpOne.Application.Transactions.SalesOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SalesOrderPricingGuardrailTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SalesOrderPricingGuardrailTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const string TightRole = "SOG-Tight";   // 5%
    private const string LooseRole = "SOG-Loose";   // 40%

    private static async Task<(int variantId, int customerId, int warehouseId)> SeedAsync(
        AppDbContext db, IPriceListService priceLists, string suffix)
    {
        var product = new Product($"SOG-P-{suffix}", $"SOG Probe {suffix}", null, null, null, null, ProductStatus.Active);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant($"SOG-SKU-{suffix}", null, 100_000m, null, 0m, null, null, true);
        typeof(ProductVariant).GetProperty(nameof(ProductVariant.ProductId))!.SetValue(variant, product.Id);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        // Price list customer: harga dasar 90.000
        var list = await priceLists.CreateAsync(new CreatePriceListRequest($"SOG-PL-{suffix}", "SOG List", null, true,
            [new PriceListLineRequest(variant.Id, 1, 90_000m)]));

        var customer = new Customer($"SOG-C-{suffix}", "SOG Customer", null, null, null, null, null, 30, "IDR", 0m, true, list.Id);
        db.Customers.Add(customer);
        var warehouse = new Warehouse($"SOG-WH-{suffix}", "SOG WH", null, true);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        if (!await db.Roles.AnyAsync(r => r.Name == TightRole))
        {
            db.Roles.Add(new ApplicationRole(TightRole) { NormalizedName = TightRole.ToUpperInvariant(), MaxDiscountPercent = 5m });
            db.Roles.Add(new ApplicationRole(LooseRole) { NormalizedName = LooseRole.ToUpperInvariant(), MaxDiscountPercent = 40m });
            await db.SaveChangesAsync();
        }

        return (variant.Id, customer.Id, warehouse.Id);
    }

    private static CreateSalesOrderRequest Request(int customerId, int warehouseId, int variantId,
        decimal unitPrice, decimal discountPercent) =>
        new(customerId, warehouseId, new DateTime(2026, 7, 27), null, null,
            [new SalesOrderLineRequest(variantId, 1, unitPrice, discountPercent, null)]);

    [Fact]
    public async Task Discount_within_role_limit_is_accepted_and_keeps_negotiated_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "OK");

        // Harga engine 90.000; kirim 85.000 = menyimpang 5,56% -> di dalam batas 40%
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 85_000m, 0m), [LooseRole]);

        Assert.Equal(85_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Deviation_above_role_limit_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "REJECT");

        // Harga engine 90.000; kirim 90.000 dengan diskon 25% -> menyimpang 25% > batas 5%
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            so.CreateAsync(Request(customerId, warehouseId, variantId, 90_000m, 25m), [TightRole]));

        Assert.Contains("SOG-SKU-REJECT", ex.Message);
    }

    [Fact]
    public async Task Price_override_alone_can_breach_the_limit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "OVERRIDE");

        // Tanpa diskon %, tapi harga diturunkan dari 90.000 ke 60.000 -> menyimpang 33,33% > 5%
        await Assert.ThrowsAsync<ValidationException>(() =>
            so.CreateAsync(Request(customerId, warehouseId, variantId, 60_000m, 0m), [TightRole]));
    }

    [Fact]
    public async Task Price_above_engine_price_is_always_allowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "ABOVE");

        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 120_000m, 0m), [TightRole]);

        Assert.Equal(120_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task No_roles_falls_back_to_global_default_and_allows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "NOROLE");

        // Default global 100% -> apa pun lolos; ini yang menjaga pemanggil lama tidak rusak.
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 10_000m, 0m));

        Assert.Equal(10_000m, created.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Update_is_guarded_too()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var so = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var (variantId, customerId, warehouseId) = await SeedAsync(db, priceLists, "UPDGUARD");
        var created = await so.CreateAsync(Request(customerId, warehouseId, variantId, 90_000m, 0m), [TightRole]);

        await Assert.ThrowsAsync<ValidationException>(() => so.UpdateAsync(created.Id,
            new UpdateSalesOrderRequest(warehouseId, new DateTime(2026, 7, 27), null, null,
                [new SalesOrderLineRequest(variantId, 1, 90_000m, 50m, null)]),
            [TightRole]));
    }
}
```

Catatan: bila signature `CreateSalesOrderRequest`/`UpdateSalesOrderRequest`/`SalesOrderLineRequest` berbeda dari yang dipakai di atas, samakan dengan definisi nyata di `src/ErpOne.Application/Transactions/SalesOrders/SalesOrderDtos.cs` — jangan mengubah DTO-nya.

- [ ] **Step 2: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~SalesOrderPricingGuardrailTests"`
Expected: FAIL — `CreateAsync` belum menerima `roleNames`.

- [ ] **Step 3: Ubah kontrak `ISalesOrderService`**

Di `src/ErpOne.Application/Transactions/SalesOrders/ISalesOrderService.cs`, ganti dua signature:

```csharp
    Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request,
        IReadOnlyList<string>? roleNames = null, CancellationToken ct = default);

    Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request,
        IReadOnlyList<string>? roleNames = null, CancellationToken ct = default);
```

`roleNames` adalah parameter method, **bukan** bagian DTO: DTO datang dari client dan bisa dipalsukan. Nilai `null` berarti "tidak ada role terisi" → jatuh ke default global (100 setelah seed), sehingga pemanggil lama tidak berubah perilaku.

- [ ] **Step 4: Sisipkan resolusi + validasi di `SalesOrderService`**

(a) Tambah `IPricingService pricing` ke ctor (`SalesOrderService.cs:12-17`):

```csharp
public class SalesOrderService(
    AppDbContext db,
    IApprovalService approval,
    IValidator<CreateSalesOrderRequest> createValidator,
    IValidator<UpdateSalesOrderRequest> updateValidator,
    IDocumentNumberService docNumbers,
    IPricingService pricing) : ISalesOrderService
```

Tambah `using ErpOne.Application.Pricing;`.

(b) Ubah `BuildLinesAsync` (`:252`) agar menerima konteks pricing dan memvalidasi. Ganti seluruh method dengan:

```csharp
    private async Task<List<SalesOrderLine>> BuildLinesAsync(
        IReadOnlyList<SalesOrderLineRequest> requests,
        int customerId, int warehouseId, DateTime orderDate,
        IReadOnlyList<string>? roleNames,
        CancellationToken ct)
    {
        var taxIds = requests.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        var rates = taxIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.Taxes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Rate, ct);

        await EnforceDiscountLimitAsync(requests, customerId, warehouseId, orderDate, roleNames, ct);

        var lines = new List<SalesOrderLine>();
        foreach (var l in requests)
        {
            var rate = l.TaxId.HasValue && rates.TryGetValue(l.TaxId.Value, out var r) ? r : 0m;
            lines.Add(new SalesOrderLine(l.ProductVariantId, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxId, rate));
        }
        return lines;
    }

    /// <summary>Harga dasar dihitung server; penyimpangan harga efektif client terhadapnya dibatasi
    /// batas role. Ini menutup dua celah sekaligus: override UnitPrice dan DiscountPercent.</summary>
    private async Task EnforceDiscountLimitAsync(
        IReadOnlyList<SalesOrderLineRequest> requests,
        int customerId, int warehouseId, DateTime orderDate,
        IReadOnlyList<string>? roleNames,
        CancellationToken ct)
    {
        if (requests.Count == 0) return;

        var maxDiscount = await pricing.GetMaxDiscountPercentAsync(roleNames, ct);
        var onDate = DateOnly.FromDateTime(orderDate);

        var resolved = await pricing.ResolveManyAsync(
            requests.Select(l => new PriceRequest(l.ProductVariantId, l.Quantity, customerId, warehouseId, onDate)).ToList(),
            ct);

        for (var i = 0; i < requests.Count; i++)
        {
            var line = requests[i];
            var deviation = PriceMath.DeviationPercent(resolved[i].UnitPrice, line.UnitPrice, line.DiscountPercent);
            if (deviation <= maxDiscount) continue;

            var sku = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                .Select(v => v.Sku).FirstOrDefaultAsync(ct) ?? line.ProductVariantId.ToString();

            throw Fail($"Discount on {sku} is {deviation:0.##}% below the current price " +
                       $"({resolved[i].UnitPrice:N0}), which exceeds your limit of {maxDiscount:0.##}%.");
        }
    }
```

(c) Ubah `CreateAsync` (`:133`) — signature dan pemanggilan `BuildLinesAsync`:

```csharp
    public async Task<SalesOrderDto> CreateAsync(CreateSalesOrderRequest request,
        IReadOnlyList<string>? roleNames = null, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currency = await db.Customers.Where(c => c.Id == request.CustomerId)
            .Select(c => c.DefaultCurrency).FirstOrDefaultAsync(ct) ?? "IDR";
        var soNumber = await docNumbers.NextAsync(DocumentTypes.SalesOrder, request.OrderDate, ct);

        var so = new SalesOrder(soNumber, request.CustomerId, request.WarehouseId,
            request.OrderDate, request.ExpectedDate, currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, request.CustomerId, request.WarehouseId,
            request.OrderDate, roleNames, ct));

        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await GetByIdAsync(so.Id, ct))!;
    }
```

(d) Ubah `UpdateAsync` (`:153`) — signature dan pemanggilan; `CustomerId` diambil dari entity karena request update tidak memuatnya:

```csharp
    public async Task<bool> UpdateAsync(int id, UpdateSalesOrderRequest request,
        IReadOnlyList<string>? roleNames = null, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var so = await db.SalesOrders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (so is null) return false;

        var oldLines = await db.SalesOrderLines.Where(l => l.SalesOrderId == id).ToListAsync(ct);
        db.SalesOrderLines.RemoveRange(oldLines);

        so.UpdateHeader(so.CustomerId, request.WarehouseId, request.OrderDate, request.ExpectedDate, so.Currency, request.Notes);
        so.SetLines(await BuildLinesAsync(request.Lines, so.CustomerId, request.WarehouseId,
            request.OrderDate, roleNames, ct));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
```

- [ ] **Step 5: Jalankan test guardrail — harus lolos**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~SalesOrderPricingGuardrailTests"`
Expected: PASS (6 test).

- [ ] **Step 6: Jalankan test SO yang sudah ada — tidak boleh regresi**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~SalesOrderServiceTests"`
Expected: PASS, jumlah sama seperti sebelumnya. Test lama memanggil `CreateAsync(request)` tanpa `roleNames` → default global 100% → lolos seperti sebelumnya.

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Transactions/SalesOrders/ISalesOrderService.cs \
        src/ErpOne.Infrastructure/Services/Transactions/SalesOrderService.cs \
        tests/ErpOne.IntegrationTests/SalesOrderPricingGuardrailTests.cs
git commit -m "feat(pricing): server-resolved price + discount guardrail on Sales Order"
```

---

## Task 7: Guardrail + harga server di POS

**Files:**
- Modify: `src/ErpOne.Application/Cashier/PosSales/IPosSaleService.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs:13-18` (ctor), `:20-46` (`SearchProductsAsync`), `:48` (`CreateSaleAsync`)
- Test: `tests/ErpOne.IntegrationTests/PosSalePricingGuardrailTests.cs`

**Interfaces:**
- Consumes: `IPricingService` (Task 3), `PriceMath.DeviationPercent` (Task 2).
- Produces: `IPosSaleService.CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, IReadOnlyList<string>? roleNames = null, CancellationToken ct = default)`.

`roleNames` diletakkan **setelah** `request` agar seluruh pemanggil lama (~6 test file) tetap ter-kompilasi tanpa diubah.

- [ ] **Step 1: Tulis test yang gagal**

Buat `tests/ErpOne.IntegrationTests/PosSalePricingGuardrailTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Cashier.PosSales;
using ErpOne.Application.PriceLists;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosSalePricingGuardrailTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosSalePricingGuardrailTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const string CashierRole = "POSG-Cashier"; // 5%

    /// <summary>Siapkan gudang dengan price list default (harga dasar 90.000), stok, shift terbuka,
    /// dan varian berharga master 100.000.</summary>
    private static async Task<(int variantId, int shiftId, string sku)> SeedAsync(
        IServiceProvider sp, string suffix)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var priceLists = sp.GetRequiredService<IPriceListService>();

        var product = new Product($"POSG-P-{suffix}", $"POSG Probe {suffix}", null, null, null, null, ProductStatus.Active);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var sku = $"POSG-SKU-{suffix}";
        var variant = new ProductVariant(sku, null, 100_000m, null, 0m, null, null, true);
        typeof(ProductVariant).GetProperty(nameof(ProductVariant.ProductId))!.SetValue(variant, product.Id);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest($"POSG-PL-{suffix}", "POSG List", null, true,
            [new PriceListLineRequest(variant.Id, 1, 90_000m)]));

        var warehouse = new Warehouse($"POSG-WH-{suffix}", $"POSG WH {suffix}", null, true, list.Id);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        // Stok awal agar penjualan tidak ditolak karena stok kurang.
        db.StockMovements.Add(new StockMovement(variant.Id, warehouse.Id, MovementType.In, 100, 50_000m,
            new DateTime(2026, 7, 27), refType: "SEED", refId: null, note: "guardrail seed"));
        await db.UpsertStockAsync(variant.Id, warehouse.Id, 100);
        await db.SaveChangesAsync();

        var shifts = sp.GetRequiredService<ICashierShiftService>();
        var shift = await shifts.OpenAsync($"posg-{suffix}", $"POSG User {suffix}", warehouse.Id, 0m);

        if (!await db.Roles.AnyAsync(r => r.Name == CashierRole))
        {
            db.Roles.Add(new ApplicationRole(CashierRole)
            { NormalizedName = CashierRole.ToUpperInvariant(), MaxDiscountPercent = 5m });
            await db.SaveChangesAsync();
        }

        return (variant.Id, shift.Id, sku);
    }

    private static async Task<int> ActivePaymentMethodIdAsync(AppDbContext db) =>
        await db.PaymentMethods.Where(m => m.IsActive).Select(m => m.Id).FirstAsync();

    [Fact]
    public async Task Search_returns_price_list_price_not_master_price()
    {
        using var scope = _factory.Services.CreateScope();
        var (variantId, shiftId, sku) = await SeedAsync(scope.ServiceProvider, "SEARCH");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();

        var warehouseId = await db.CashierShifts.Where(s => s.Id == shiftId).Select(s => s.WarehouseId).FirstAsync();
        var options = await pos.SearchProductsAsync(warehouseId, sku);

        var option = Assert.Single(options);
        Assert.Equal(90_000m, option.UnitPrice);   // dari price list gudang
        Assert.Equal(100_000m, option.Price);      // harga master tetap untuk harga coret
    }

    [Fact]
    public async Task Client_supplied_price_is_ignored_in_favour_of_engine_price()
    {
        using var scope = _factory.Services.CreateScope();
        var (variantId, shiftId, _) = await SeedAsync(scope.ServiceProvider, "FAKE");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();
        var pmId = await ActivePaymentMethodIdAsync(db);

        // Client "mengarang" harga 1 rupiah. Server harus memakai 90.000 dari price list.
        var sale = await pos.CreateSaleAsync("posg-fake", "POSG Fake", shiftId,
            new CreatePosSaleRequest(pmId, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(variantId, 1, 1m, 0m)]));

        Assert.Equal(90_000m, sale.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Discount_above_role_limit_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var (variantId, shiftId, sku) = await SeedAsync(scope.ServiceProvider, "REJECT");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();
        var pmId = await ActivePaymentMethodIdAsync(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => pos.CreateSaleAsync(
            "posg-reject", "POSG Reject", shiftId,
            new CreatePosSaleRequest(pmId, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(variantId, 1, 90_000m, 30m)]),
            [CashierRole]));

        Assert.Contains(sku, ex.Message);
    }

    [Fact]
    public async Task Discount_within_role_limit_is_accepted()
    {
        using var scope = _factory.Services.CreateScope();
        var (variantId, shiftId, _) = await SeedAsync(scope.ServiceProvider, "OK");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pos = scope.ServiceProvider.GetRequiredService<IPosSaleService>();
        var pmId = await ActivePaymentMethodIdAsync(db);

        var sale = await pos.CreateSaleAsync("posg-ok", "POSG Ok", shiftId,
            new CreatePosSaleRequest(pmId, null, 0m, 1_000_000m,
                [new PosSaleLineRequest(variantId, 1, 90_000m, 4m)]),
            [CashierRole]);

        Assert.Equal(4m, sale.Lines[0].DiscountPercent);
        Assert.Equal(90_000m, sale.Lines[0].UnitPrice);
    }
}
```

Catatan: signature `CreatePosSaleRequest`, `PosSaleLineRequest`, `ICashierShiftService.OpenAsync`, dan `StockMovement` ctor harus disamakan dengan definisi nyata (`PosSaleDtos.cs`, `CashierShift*`, `StockMovement.cs`) — pola pemanggilan `UpsertStockAsync` & `StockMovement` bisa dicontek dari `PosSaleServiceTests.cs` yang sudah ada. **Contek test yang ada, jangan mengarang.**

- [ ] **Step 2: Jalankan untuk memastikan gagal**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PosSalePricingGuardrailTests"`
Expected: FAIL — harga masih dari `DiscountPrice ?? Price` dan `roleNames` belum ada.

- [ ] **Step 3: Ubah kontrak `IPosSaleService`**

```csharp
    Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId,
        CreatePosSaleRequest request, IReadOnlyList<string>? roleNames = null,
        CancellationToken ct = default);
```

- [ ] **Step 4: Sisipkan pricing di `PosSaleService`**

(a) Tambah `IPricingService pricing` ke ctor (`:13-18`) dan `using ErpOne.Application.Pricing;`.

(b) Ganti bagian akhir `SearchProductsAsync` (`:41-45`) agar harga berasal dari engine:

```csharp
        var priced = await pricing.ResolveManyAsync(
            rows.Select(r => new PriceRequest(r.Id, 1, null, warehouseId, DateOnly.FromDateTime(DateTime.Now))).ToList(),
            ct);

        return rows.Select((r, i) => new PosProductOptionDto(
            r.Id, r.Sku, r.ProductName, r.Barcode,
            priced[i].UnitPrice,
            stock.FirstOrDefault(s => s.VariantId == r.Id)?.Qty ?? 0,
            r.Price, r.DiscountPercent)).ToList();
```

Qty 1 dipakai karena pencarian belum tahu qty; tier qty tetap berlaku saat baris masuk keranjang dan divalidasi ulang saat submit.

(c) Ubah signature `CreateSaleAsync` (`:48`) menjadi:

```csharp
    public async Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId,
        CreatePosSaleRequest request, IReadOnlyList<string>? roleNames = null,
        CancellationToken ct = default)
```

(d) Setelah `var whId = shift.WarehouseId;` dan `var now = DateTime.Now;` (`:68-69`), sisipkan resolusi + validasi:

```csharp
        // Harga dasar dihitung server — nilai UnitPrice dari client hanya dipakai untuk
        // mengukur penyimpangan, tidak untuk menetapkan harga.
        var maxDiscount = await pricing.GetMaxDiscountPercentAsync(roleNames, ct);
        var resolvedPrices = await pricing.ResolveManyAsync(
            request.Lines.Select(l => new PriceRequest(l.ProductVariantId, l.Quantity, null, whId,
                DateOnly.FromDateTime(now))).ToList(),
            ct);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var deviation = PriceMath.DeviationPercent(resolvedPrices[i].UnitPrice, line.UnitPrice, line.DiscountPercent);
            if (deviation <= maxDiscount) continue;

            var deviantSku = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                .Select(v => v.Sku).FirstOrDefaultAsync(ct) ?? line.ProductVariantId.ToString();

            throw Fail($"Discount on {deviantSku} is {deviation:0.##}% below the current price " +
                       $"({resolvedPrices[i].UnitPrice:N0}), which exceeds your limit of {maxDiscount:0.##}%.");
        }
```

(e) Pada loop pembuatan baris (`:92-104`), pakai harga engine, bukan harga client. Ganti `sale.AddLine(...)` di `:99`:

```csharp
            var resolvedUnitPrice = resolvedPrices[request.Lines.IndexOf(line)].UnitPrice;
            sale.AddLine(v.Id, v.Sku, name, line.Quantity, resolvedUnitPrice, line.DiscountPercent, unitCost);
```

`IndexOf` tidak aman bila ada dua baris identik. Ubah loop `foreach (var line in request.Lines)` (`:92`) menjadi `for` berindeks dan pakai indeksnya:

```csharp
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Varian {line.ProductVariantId} tidak ditemukan.");
            var name = await db.Products.Where(p => p.Id == v.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "—";

            var unitCost = await costing.GetOutboundUnitCostAsync(v.Id, whId, line.Quantity, ct);
            sale.AddLine(v.Id, v.Sku, name, line.Quantity, resolvedPrices[i].UnitPrice, line.DiscountPercent, unitCost);

            db.StockMovements.Add(new StockMovement(v.Id, whId, MovementType.Out,
                -line.Quantity, unitCost, now, refType: "POS", refId: null, note: sale.SaleNumber));
            await db.UpsertStockAsync(v.Id, whId, -line.Quantity, ct);
        }
```

- [ ] **Step 5: Jalankan test guardrail — harus lolos**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PosSalePricingGuardrailTests"`
Expected: PASS (4 test).

- [ ] **Step 6: Jalankan test POS yang sudah ada — cek regresi**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~PosSale"`
Expected: PASS. **Perhatian:** test lama yang mengirim `UnitPrice` bebas sekarang akan menerima harga engine (= harga master, karena gudangnya tanpa price list) — bila ada assertion pada `UnitPrice` yang kebetulan berbeda dari harga master varian, test itu memang harus disesuaikan: harga kini ditentukan server. Sesuaikan **ekspektasi test**, jangan melemahkan implementasi.

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Cashier/PosSales/IPosSaleService.cs \
        src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs \
        tests/ErpOne.IntegrationTests/PosSalePricingGuardrailTests.cs
git commit -m "feat(pricing): POS uses server-resolved price + discount guardrail"
```

---

## Task 8: Halaman Price List + Setelan + permission

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs`
- Create: `src/ErpOne.Web/Components/Pages/Master/PriceLists/PriceListIndex.razor`
- Create: `src/ErpOne.Web/Components/Pages/Master/PriceLists/PriceListForm.razor`
- Create: `src/ErpOne.Web/Components/Pages/Settings/Pricing/PricingSettingIndex.razor`

**Interfaces:**
- Consumes: `IPriceListService`, `IPricingSettingService` (Task 4 & 5).
- Produces: rute `/master/price-lists`, `/master/price-lists/new`, `/master/price-lists/{id:int}`, `/settings/pricing`.

- [ ] **Step 1: Daftarkan resource permission**

Di `src/ErpOne.Web/Authorization/AppMenus.cs`, grup `"Master"` (setelah `master.currencies`, baris ~60):

```csharp
            new("master.price-lists", "Price List", "bi-tags-fill", CRUD),
```

Grup `"Settings"` (setelah `settings.costing`, baris ~120):

```csharp
            new("settings.pricing", "Pricing", "bi-percent", [ActIndex, ActEdit]),
```

Tidak ada perubahan seeder: `BootstrapSeeder.cs:44` sudah memberikan seluruh `AppMenus.AllPermissions` ke role admin.

- [ ] **Step 2: Buat halaman Index**

Buat `PriceListIndex.razor` dengan **menyalin struktur** `src/ErpOne.Web/Components/Pages/Master/Currencies/CurrencyIndex.razor` (desain `.pi` yang sudah benar), lalu ubah:

- `@page "/master/price-lists"`
- `@attribute [Authorize(Policy = "master.price-lists.index")]` (samakan bentuk policy dengan yang dipakai halaman Currency)
- Inject `IPriceListService PriceLists`
- Kolom tabel: Code, Name, Lines (`LineCount`), Active, Created
- KPI chips: total price list, jumlah aktif, jumlah nonaktif
- Aksi baris: Edit → `/master/price-lists/{id}`, Delete → `PriceLists.DeleteAsync(id)` dengan konfirmasi `SwalService` seperti halaman lain
- Tangkap `ValidationException` saat delete dan tampilkan pesannya (price list yang masih dipakai akan ditolak service)

- [ ] **Step 3: Buat halaman Form**

Buat `PriceListForm.razor` dengan menyalin struktur form `.cf` dari `Master/Currencies/CurrencyForm.razor` untuk header, dan **menyalin pola editor baris inline** dari `Transactions/SalesOrders/SoForm.razor` (blok `_rows`, `AddRow`, `RemoveRow`, tabel baris) untuk bagian tier.

- `@page "/master/price-lists/new"` dan `@page "/master/price-lists/{Id:int}"`
- Field header: Code, Name, Description, IsActive
- Tabel baris: dropdown varian (sumber: layanan varian yang sama dengan yang dipakai `SoForm.razor` untuk mengisi `_variants`), input `MinQty` (number, min 1), input `UnitPrice` (number, min 0, step 0.01)
- Tombol "Add tier"; hapus baris per baris
- Simpan: `CreateAsync`/`UpdateAsync`; tangkap `ValidationException` → tampilkan di `_error` seperti `SoForm.razor:342`
- Setelah simpan sukses, navigasi ke `/master/price-lists`

Petunjuk UX untuk tier (tampilkan sebagai teks bantu di halaman): *"MinQty 1 adalah harga dasar. Tambahkan baris dengan MinQty lebih besar untuk harga borongan."*

- [ ] **Step 4: Buat halaman Setelan**

Buat `PricingSettingIndex.razor` dengan menyalin `src/ErpOne.Web/Components/Pages/Settings/Costing/CostingSettingIndex.razor` lalu ubah:

- `@page "/settings/pricing"`
- Inject `IPricingSettingService PricingSettings`
- Satu input number `DefaultMaxDiscountPercent` (0–100, step 0.01) + tombol Save → `UpdateAsync(...)`
- Teks penjelas: *"Applies to users whose roles have no discount limit set. Per-role limits are configured in Settings → Role."*

- [ ] **Step 5: Build**

Run: `dotnet build ErpOne.slnx -v q --nologo`
Expected: 0 warning, 0 error.

- [ ] **Step 6: Verifikasi menu & rute**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter "FullyQualifiedName~NavMenuBuilderTests"`
Expected: PASS. Bila test ini memeriksa jumlah resource, sesuaikan ekspektasinya (+2 resource).

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs \
        src/ErpOne.Web/Components/Pages/Master/PriceLists \
        src/ErpOne.Web/Components/Pages/Settings/Pricing
git commit -m "feat(pricing): Price List pages + pricing settings page + menu"
```

---

## Task 9: Assignment di form Customer, Warehouse, dan Role

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor`
- Modify: `src/ErpOne.Web/Components/Pages/Master/Warehouses/WarehouseForm.razor`
- Modify: `src/ErpOne.Web/Components/Pages/Settings/RoleForm.razor`
- Modify: DTO & service Customer/Warehouse agar `PriceListId`/`DefaultPriceListId` ikut tersimpan
- Modify: service Role agar `MaxDiscountPercent` ikut tersimpan

- [ ] **Step 1: Alirkan `PriceListId` pada Customer**

Tambah `int? PriceListId` ke `CustomerDto`, `CreateCustomerRequest`, `UpdateCustomerRequest` (`src/ErpOne.Application/Master/Customers/CustomerDtos.cs` — periksa nama berkas sebenarnya), teruskan ke ctor/`Update` entity di `CustomerService`, dan sertakan di proyeksi `ToDto`.

Di `CustomerForm.razor`, tambah dropdown:

```razor
<div class="cf-field">
    <label class="cf-label">Price List</label>
    <select class="ctl" @bind="_priceListId">
        <option value="0">— Use warehouse default —</option>
        @foreach (var pl in _priceLists)
        {
            <option value="@pl.Id">@pl.Code — @pl.Name</option>
        }
    </select>
    <div class="cf-help">Overrides the warehouse default for this customer, in every outlet.</div>
</div>
```

Isi `_priceLists` di `OnInitializedAsync` dengan `await PriceLists.GetActiveAsync()`; kirim `null` bila `_priceListId == 0`.

- [ ] **Step 2: Alirkan `DefaultPriceListId` pada Warehouse**

Perlakuan sama untuk `WarehouseDto`/request dan `WarehouseService`. Dropdown di `WarehouseForm.razor`:

```razor
<div class="cf-field">
    <label class="cf-label">Default Price List</label>
    <select class="ctl" @bind="_defaultPriceListId">
        <option value="0">— Use master product price —</option>
        @foreach (var pl in _priceLists)
        {
            <option value="@pl.Id">@pl.Code — @pl.Name</option>
        }
    </select>
    <div class="cf-help">Used by POS for sales in this warehouse.</div>
</div>
```

- [ ] **Step 3: Alirkan `MaxDiscountPercent` pada Role**

Di `RoleForm.razor` tambah field:

```razor
<div class="cf-field">
    <label class="cf-label">Max Discount %</label>
    <input type="number" min="0" max="100" step="0.01" class="ctl" @bind="_maxDiscountPercent" />
    <div class="cf-help">
        Leave empty to use the global default (@_globalDefault.ToString("0.##")%).
        Enter 0 to forbid discounts entirely for this role.
    </div>
</div>
```

`_maxDiscountPercent` bertipe `decimal?`; `_globalDefault` diisi dari `IPricingSettingService.GetAsync()`. Simpan ke `ApplicationRole.MaxDiscountPercent` lewat `RoleManager`/service role yang dipakai halaman itu. Kosong berarti `null`, **bukan** 0 — bedanya bermakna.

- [ ] **Step 4: Build + seluruh test**

Run: `dotnet build ErpOne.slnx -v q --nologo` lalu `dotnet test ErpOne.slnx --nologo -v q`
Expected: 0 warning; seluruh test hijau. Bila `CustomerServiceTests`/`WarehouseServiceTests` gagal kompilasi karena DTO berubah, tambahkan argumen baru pada pemanggilan test (nilainya `null`).

- [ ] **Step 5: Commit (user menjalankan)**

```bash
git add src/ErpOne.Application/Master src/ErpOne.Infrastructure/Services/Master \
        src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor \
        src/ErpOne.Web/Components/Pages/Master/Warehouses/WarehouseForm.razor \
        src/ErpOne.Web/Components/Pages/Settings/RoleForm.razor tests
git commit -m "feat(pricing): assign price list to customer & warehouse, max discount per role"
```

---

## Task 10: POS & SO form memakai harga engine

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Cashier/Pos/PosRegister.razor:284` (auth state), `:423` (tambah ke keranjang), `:450` (submit)
- Modify: `src/ErpOne.Web/Components/Pages/Transactions/SalesOrders/SoForm.razor:315-321` (`OnVariantChanged`), `:340` (`SaveAsync`), tabel baris

- [ ] **Step 1: Kirim role dari POS**

Di `PosRegister.razor`, `OnInitializedAsync` (`:333-337`) sudah membaca `AuthenticationState`. Tambah pembacaan role:

```csharp
    private string[] _roles = [];
    // di OnInitializedAsync, setelah _userName:
    _roles = user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray();
```

Lalu pada submit (`:450`), teruskan role:

```csharp
        var sale = await PosService.CreateSaleAsync(_userId, _userName, _shift!.Id,
            new CreatePosSaleRequest(/* argumen yang sudah ada */),
            _roles);
```

Tangkap `ValidationException` di sekitar pemanggilan dan tampilkan pesannya lewat `SwalService` — pesan guardrail menyebut SKU dan batas, jadi kasir tahu apa yang harus dilakukan.

- [ ] **Step 2: Tampilkan asal harga di POS**

Hasil pencarian sudah mengembalikan `UnitPrice` dari engine (Task 7). Badge harga coret yang ada (`:106-111`) otomatis benar karena membandingkan `r.UnitPrice < r.Price`. Tidak ada perubahan markup yang wajib; opsional tambahkan tooltip nama price list bila `PosProductOptionDto` diperluas — **jangan** perluas DTO hanya untuk itu di fase ini.

- [ ] **Step 3: SO — resolve harga saat varian dipilih**

Di `SoForm.razor`, inject `IPricingService Pricing`, lalu ganti `OnVariantChanged` (`:315-321`):

```csharp
    private async Task OnVariantChanged(Row row, string? value)
    {
        row.VariantId = int.TryParse(value, out var id) ? id : 0;
        await ResolveRowPriceAsync(row);
        await RefreshCreditAsync();
    }

    /// <summary>Ambil harga dasar dari engine. Hanya mengisi bila user belum menetapkan harga sendiri,
    /// agar harga hasil negosiasi tidak tertimpa.</summary>
    private async Task ResolveRowPriceAsync(Row row)
    {
        if (row.VariantId <= 0 || _customerId <= 0) return;

        var result = await Pricing.ResolveAsync(new PriceRequest(
            row.VariantId, Math.Max(1, row.Quantity), _customerId, _warehouseId,
            DateOnly.FromDateTime(_orderDate)));

        row.PriceListName = result.PriceListName;
        if (row.UnitPrice == 0 || row.UnitPrice == row.ResolvedPrice) row.UnitPrice = result.UnitPrice;
        row.ResolvedPrice = result.UnitPrice;
    }
```

Tambah dua property ke kelas `Row` (`:229`):

```csharp
        public decimal ResolvedPrice { get; set; }
        public string? PriceListName { get; set; }
```

`row.UnitPrice == row.ResolvedPrice` adalah pembeda "harga masih otomatis" vs "sudah diubah manual": bila user belum menyentuhnya, harga ikut berubah saat qty melewati tier; bila sudah diubah, harga nego dibiarkan.

- [ ] **Step 4: SO — resolve ulang saat qty berubah**

Ini inti tier qty. Pada input Quantity di tabel baris, tambahkan `@bind:after`:

```razor
<input type="number" min="1" class="ctl ctl-sm mono text-end"
       @bind="row.Quantity" @bind:after="() => OnQuantityChangedAsync(row)" />
```

```csharp
    private async Task OnQuantityChangedAsync(Row row)
    {
        await ResolveRowPriceAsync(row);
        await RefreshCreditAsync();
    }
```

Tanpa langkah ini tier qty tidak pernah aktif — user mengisi qty 50 tetapi harga tetap harga tier 1.

Tambahkan juga pemanggilan ulang saat customer berubah (harga dasar bergantung price list customer) di `OnCustomerChanged` (`:309`):

```csharp
        foreach (var row in _rows) await ResolveRowPriceAsync(row);
```

- [ ] **Step 5: SO — tampilkan nama price list & kirim role**

Tambah kolom kecil di tabel baris yang menampilkan `row.PriceListName` (atau `—`), agar sales tahu harga berasal dari daftar mana.

Pada `SaveAsync` (`:340-354`), baca role dari cascading `AuthenticationState` (tambahkan `[CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;` bila belum ada, pola `PosRegister.razor:284`) dan teruskan:

```csharp
        var roles = (await AuthStateTask).User
            .FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray();

        if (Id is int id)
            await SoService.UpdateAsync(id, new UpdateSalesOrderRequest(_warehouseId, _orderDate, _expectedDate, _notes, lines), roles);
        else
            await SoService.CreateAsync(new CreateSalesOrderRequest(/* argumen yang sudah ada */), roles);
```

Pastikan `ValidationException` dari guardrail ditampilkan di `_error` (blok try/catch yang sudah ada di `SaveAsync`).

- [ ] **Step 6: Build + seluruh test**

Run: `dotnet build ErpOne.slnx -v q --nologo` lalu `dotnet test ErpOne.slnx --nologo -v q`
Expected: 0 warning, 0 error; seluruh test hijau.

- [ ] **Step 7: Commit (user menjalankan)**

```bash
git add src/ErpOne.Web/Components/Pages/Cashier/Pos/PosRegister.razor \
        src/ErpOne.Web/Components/Pages/Transactions/SalesOrders/SoForm.razor
git commit -m "feat(pricing): POS & SO forms resolve price from engine (tier-aware)"
```

---

## Task 11: Regresi akhir & dokumentasi

**Files:**
- Modify: `docs/DEVELOPMENT-PLAN.md`

- [ ] **Step 1: Build bersih + seluruh test**

Run:
```bash
dotnet build ErpOne.slnx -v q --nologo
dotnet test ErpOne.slnx --nologo -v q
```
Expected: 0 warning, 0 error. Unit ≥ 195 lolos, integration ≥ 252 lolos, 0 gagal.

- [ ] **Step 2: Grep sisa jalur harga yang belum lewat engine**

Run:
```bash
rg -n "DiscountPrice \?\? " src/
```
Expected: hanya muncul di `PricingService.cs` (rantai fallback). Kemunculan di `PosSaleService`, `SoForm.razor`, atau tempat lain berarti masih ada jalur yang melewati engine — perbaiki.

- [ ] **Step 3: Verifikasi manual di aplikasi**

Jalankan aplikasi, lalu periksa:
1. `/master/price-lists` — buat price list "GROSIR" dengan tier 1 / 10 / 50 untuk satu SKU.
2. Assign ke satu customer; assign price list lain sebagai default satu gudang.
3. `/settings/pricing` — ubah default max discount ke 5, simpan, muat ulang.
4. Settings → Role — isi Max Discount % pada satu role.
5. SO baru untuk customer tersebut: harga terisi otomatis dari price list; **ubah qty ke 10 dan 50 → harga ikut berubah**.
6. Coba diskon di atas batas → ditolak dengan pesan yang menyebut SKU.
7. POS di gudang ber-price list: harga di hasil pencarian sesuai price list, bukan harga master.

- [ ] **Step 4: Perbarui `docs/DEVELOPMENT-PLAN.md`**

Pada Fase 6, ganti butir *"Price List / Promo / Diskon terpusat"* menjadi tiga sub-butir dengan status:

```markdown
- **Price List / Promo / Diskon terpusat** — dipecah tiga:
  - [x] **6b-1 Fondasi**: `IPricingService`, Price List + tier qty, assignment customer/gudang, guardrail diskon per role (server-side).
  - [ ] **6b-2 Promo per item**: promo terjadwal (%, nominal, fixed price), pemilihan 1 promo terbaik, jejak perhitungan.
  - [ ] **6b-3 Promo transaksi + BOGO**: diskon total dengan alokasi ke baris, Buy-X-Get-Y.
```

- [ ] **Step 5: Commit final (user menjalankan)**

```bash
git add docs/DEVELOPMENT-PLAN.md
git commit -m "docs(pricing): mark 6b-1 done, split remaining pricing work into 6b-2/6b-3"
```

---

## Self-Review

**Cakupan spec → task**

| Bagian spec | Task |
|---|---|
| §1.1 `PriceList`, `PriceListLine`, `PricingSetting` | Task 1 ✓ |
| §1.2 `Customer.PriceListId`, `Warehouse.DefaultPriceListId`, `ApplicationRole.MaxDiscountPercent` | Task 1 ✓ |
| §1.3 mapping inline, `tablePrefixes`, migration | Task 1 Step 9 & 13 ✓ |
| §2 seam `IPricingService` + `ResolveManyAsync` + `OnDate` | Task 3 ✓ |
| §3 algoritma resolusi (tier, prioritas, fallback) | Task 2 (`PickTier`) + Task 3 ✓ |
| §4.1–4.2 rumus penyimpangan & batas efektif | Task 2 ✓ |
| §4.3 penerapan di POS & SO; DO/AR tidak divalidasi | Task 6 & 7 ✓ (DO/AR memang tidak disentuh) |
| §5.1 permission `master.price-lists`, `settings.pricing` | Task 8 Step 1 ✓ |
| §5.2 halaman Index/Form/Setelan | Task 8 ✓ |
| §5.3 perubahan halaman (Role, Customer, Warehouse, POS, SO) | Task 9 & 10 ✓ |
| §6 error handling (fallback, restrict delete, duplikat tier, R=0) | Task 1 (index), 3 (fallback), 4 (delete/duplikat), 2 (R=0) ✓ |
| §7 rencana test (unit murni + integrasi) | Task 1, 2, 3, 4, 5, 6, 7 ✓ |
| §9 kriteria selesai | Task 11 ✓ |

**Konsistensi tipe & nama**

- `PriceMath.PickTier` mengembalikan `(int MinQty, decimal UnitPrice)?` — dipakai konsisten di Task 3 sebagai `if (PriceMath.PickTier(...) is { } tier)`.
- `PriceResult` punya 6 anggota dengan urutan tetap (`UnitPrice`, `ListPrice`, `Source`, `PriceListId`, `PriceListName`, `MatchedMinQty`) — dipakai sama di Task 3, 6, 7, 10.
- `GetMaxDiscountPercentAsync` menerima `IEnumerable<string>?` (nullable) di kontrak dan implementasi — cocok dengan pemanggilan `roleNames` nullable dari Task 6 & 7.
- `roleNames` bertipe `IReadOnlyList<string>?` di `ISalesOrderService`/`IPosSaleService`, dan `string[]` yang dikirim dari Razor memenuhi tipe itu.
- `CreateSaleAsync` memakai nama itu di semua task (bukan `CreateAsync`), sesuai `PosSaleService.cs:48`.

**Catatan risiko yang sengaja dibiarkan**

- Task 7 Step 6 kemungkinan menuntut penyesuaian ekspektasi test POS lama, karena harga sekarang ditentukan server. Ini konsekuensi yang diinginkan, bukan kejutan.
- `CreatePosSaleRequest.TransactionDiscount` tetap tanpa guardrail sampai 6b-3 (tercatat di spec §Di luar scope).
- Signature DTO di test Task 6 & 7 ditulis berdasarkan pola; pelaksana wajib menyamakannya dengan DTO nyata alih-alih mengubah DTO.
