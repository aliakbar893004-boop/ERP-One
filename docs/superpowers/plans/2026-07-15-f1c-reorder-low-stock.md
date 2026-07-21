# Fase 1c — Reorder Level & Low-Stock Alert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ambang reorder per SKU (`ReorderLevel` + `ReorderQty` di `ProductVariant`) + alert stok menipis di dashboard (ganti threshold hardcode) & halaman `/inventory/low-stock` (per gudang, difilter), dievaluasi per (SKU × gudang).

**Architecture:** Tambah 2 kolom int di `ProductVariant` (thread lewat entity → DTO → validator → ProductService → form). Service query `ILowStockService.GetLowStockAsync` join `ProductStock`×`ProductVariant`×`Product`×`Warehouse`. Dashboard `GetDashboardAsync` pakai reorder level (bentuk DTO tetap). Halaman `.pi` baru + menu `inventory.low-stock`. Karena entity berubah → **migration EF**.

**Tech Stack:** .NET 10, Blazor Server, EF Core (`AppDbContext`), FluentValidation, xUnit integration tests (SQLite `EnsureCreated` via `CustomWebApplicationFactory`). Solution `ErpOne.slnx`.

## Global Constraints

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`. App HARUS di-stop di Visual Studio dulu (DLL lock MSB3021) sebelum build/test.
- `ReorderLevel`/`ReorderQty` = int, default 0, tolak `< 0`. `ReorderLevel == 0` = tidak dilacak (tak ada alert).
- Low = `ProductStock (varian,gudang)` dgn `variant.ReorderLevel > 0 && Quantity <= ReorderLevel`. OutOfStock = subset `Quantity == 0`. Suggested = `ReorderQty > 0 ? ReorderQty : Math.Max(ReorderLevel - Quantity, 0)`.
- Namespace flat konsisten repo: Application service `ErpOne.Application.LowStock`; impl `ErpOne.Infrastructure.Services`. Product DTO namespace = `ErpOne.Application.Products`.
- Integration test SQLite `EnsureCreated` bangun schema dari MODEL (bukan migration) → kolom baru otomatis ada; migration hanya untuk SQL Server produksi.
- Commit MANUAL oleh user — langkah "Commit" hanya penanda; JANGAN `git commit/merge/push`. Boleh `git add`. Git identity `aliakbar893004-boop`.

---

## File Structure

- Modify `src/ErpOne.Domain/Entities/Master/ProductVariant.cs` — 2 property + validasi + param ctor/Update.
- Modify `src/ErpOne.Domain/Entities/Master/Product.cs` — `AddVariant(..., int reorderLevel = 0, int reorderQty = 0)`.
- Modify `src/ErpOne.Application/Master/Products/ProductDtos.cs` — `VariantInput` + `ProductVariantDto` +2 field.
- Modify `src/ErpOne.Application/Master/Products/CreateProductValidator.cs` — `VariantInputValidator` rule.
- Modify `src/ErpOne.Infrastructure/Services/Master/ProductService.cs` — create/update/import mapping + variant→Dto + `GetDashboardAsync`.
- Create migration `AddVariantReorderLevel` (via `dotnet ef`).
- Create `src/ErpOne.Application/LowStock/LowStockDtos.cs`, `src/ErpOne.Application/LowStock/ILowStockService.cs`.
- Create `src/ErpOne.Infrastructure/Services/Inventory/LowStockService.cs`.
- Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `ILowStockService`.
- Modify `src/ErpOne.Web/Authorization/AppMenus.cs` — resource `inventory.low-stock`.
- Create `src/ErpOne.Web/Components/Pages/Inventory/LowStock/LowStockIndex.razor` → `/inventory/low-stock`.
- Modify `src/ErpOne.Web/Components/Pages/Master/Products/ProductForm.razor` — 2 input per varian.
- Create `tests/ErpOne.IntegrationTests/LowStockServiceTests.cs`.

---

## Task 1: Entity — ProductVariant reorder fields + Product.AddVariant

**Files:**
- Modify: `src/ErpOne.Domain/Entities/Master/ProductVariant.cs`
- Modify: `src/ErpOne.Domain/Entities/Master/Product.cs`

**Interfaces:**
- Produces: `ProductVariant.ReorderLevel`/`ReorderQty` (int), ctor & `Update` menerima `int reorderLevel = 0, int reorderQty = 0`; `Product.AddVariant(..., int reorderLevel = 0, int reorderQty = 0)`.

- [ ] **Step 1: Tambah property + setter validasi di `ProductVariant`**

Di `ProductVariant.cs`, tambah setelah baris `public bool IsActive { get; private set; }`:

```csharp
    public int ReorderLevel { get; private set; }
    public int ReorderQty { get; private set; }
```

Ubah ctor menjadi (tambah 2 param trailing opsional + set):

```csharp
    public ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        SetSku(sku);
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetDiscountPercent(discountPercent);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
        SetReorder(reorderLevel, reorderQty);
    }
```

Ubah `Update` menjadi (tambah 2 param trailing + set):

```csharp
    public void Update(string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetDiscountPercent(discountPercent);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
        SetReorder(reorderLevel, reorderQty);
    }
```

Tambah helper privat (dekat `SetWeight`):

```csharp
    private void SetReorder(int reorderLevel, int reorderQty)
    {
        if (reorderLevel < 0) throw new ArgumentException("ReorderLevel must be >= 0.", nameof(reorderLevel));
        if (reorderQty < 0) throw new ArgumentException("ReorderQty must be >= 0.", nameof(reorderQty));
        ReorderLevel = reorderLevel;
        ReorderQty = reorderQty;
    }
```

- [ ] **Step 2: Teruskan di `Product.AddVariant`**

Di `Product.cs`, ubah `AddVariant`:

```csharp
    public ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null,
        int reorderLevel = 0, int reorderQty = 0)
    {
        var v = new ProductVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive, discountPercent, reorderLevel, reorderQty);
        _variants.Add(v);
        return v;
    }
```

- [ ] **Step 3: Build Domain**

Run: `dotnet build src/ErpOne.Domain/ErpOne.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Domain/Entities/Master/ProductVariant.cs src/ErpOne.Domain/Entities/Master/Product.cs
```

---

## Task 2: DTOs + validator + ProductService threading

**Files:**
- Modify: `src/ErpOne.Application/Master/Products/ProductDtos.cs`
- Modify: `src/ErpOne.Application/Master/Products/CreateProductValidator.cs`
- Modify: `src/ErpOne.Infrastructure/Services/Master/ProductService.cs`

**Interfaces:**
- Consumes: `ProductVariant`/`AddVariant`/`Update` (Task 1).
- Produces: `VariantInput.ReorderLevel`/`ReorderQty`, `ProductVariantDto.ReorderLevel`/`ReorderQty` — dipakai Task 4/8.

- [ ] **Step 1: Tambah field di DTO**

Di `ProductDtos.cs`, ubah `ProductVariantDto` (tambah 2 field sebelum `Attributes`):

```csharp
public record ProductVariantDto(
    int Id, string Sku, string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int Stock, bool IsActive, decimal? DiscountPercent,
    int ReorderLevel, int ReorderQty,
    IReadOnlyList<AttributeValueRefDto> Attributes);
```

Ubah `VariantInput` (tambah 2 param trailing opsional, setelah `DiscountPercent`):

```csharp
public record VariantInput(
    string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int OpeningStock, bool IsActive,
    IReadOnlyList<int> AttributeValueIds, decimal? DiscountPercent = null,
    int ReorderLevel = 0, int ReorderQty = 0);
```

- [ ] **Step 2: Rule validator**

Di `CreateProductValidator.cs`, tambah ke `VariantInputValidator` ctor:

```csharp
        RuleFor(v => v.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(v => v.ReorderQty).GreaterThanOrEqualTo(0);
```

- [ ] **Step 3: Threading di `ProductService`**

Di `ProductService.cs`:

(a) Create mapping (sekitar baris 85) — tambah 2 arg di `AddVariant`:
```csharp
            var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
```

(b) Update — existing variant (sekitar baris 157):
```csharp
                existing.Update(v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
```

(c) Update — new variant (sekitar baris 166):
```csharp
                var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                    v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
```

(d) Import (sekitar baris 373) — pakai default (tak ada reorder di import), biarkan apa adanya (trailing opsional). TIDAK diubah.

(e) Variant→Dto (sekitar baris 487) — tambah 2 arg sebelum atribut:
```csharp
        var variants = p.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantDto(
            v.Id, v.Sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions,
            stockByVariant.TryGetValue(v.Id, out var q) ? q : 0, v.IsActive, v.DiscountPercent,
            v.ReorderLevel, v.ReorderQty,
            v.Attributes.Where(a => values.ContainsKey(a.AttributeValueId))
                .Select(a => { var x = values[a.AttributeValueId]; return new AttributeValueRefDto(a.AttributeValueId, x.AttrName, x.Code, x.Value); })
```
(Sisakan penutup `.ToList())` seperti aslinya — hanya sisipkan baris `v.ReorderLevel, v.ReorderQty,`.)

- [ ] **Step 4: Build Application + Infrastructure**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded (ini juga build Application & Domain).

- [ ] **Step 5: Commit (penanda batas task)**

```bash
git add src/ErpOne.Application/Master/Products/ProductDtos.cs src/ErpOne.Application/Master/Products/CreateProductValidator.cs src/ErpOne.Infrastructure/Services/Master/ProductService.cs
```

---

## Task 3: EF migration `AddVariantReorderLevel`

**Files:**
- Create: `src/ErpOne.Infrastructure/Persistence/Migrations/*_AddVariantReorderLevel.cs` (auto).

**Interfaces:** N/A (schema).

- [ ] **Step 1: Pastikan app di-stop (Visual Studio) & `dotnet ef` tersedia**

Run: `dotnet ef --version`
Expected: versi tampil. Bila error "command not found": `dotnet tool install --global dotnet-ef` (atau `dotnet tool restore` bila ada manifest), lalu ulang.

- [ ] **Step 2: Generate migration**

Run: `dotnet ef migrations add AddVariantReorderLevel -p src/ErpOne.Infrastructure -s src/ErpOne.Web`
Expected: file migration baru + `AppDbContextModelSnapshot.cs` terupdate. Isi `Up` harus menambah 2 kolom int (nullable:false, defaultValue:0) ke tabel `ProductVariants`, `Down` drop keduanya (mirip `20260703035115_AddVariantDiscountPercent.cs`).

**Fallback bila `dotnet ef` tak bisa dipakai:** buat file `src/ErpOne.Infrastructure/Persistence/Migrations/20260715120000_AddVariantReorderLevel.cs` manual:
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    public partial class AddVariantReorderLevel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "ReorderLevel", table: "ProductVariants", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ReorderQty", table: "ProductVariants", type: "int", nullable: false, defaultValue: 0);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReorderLevel", table: "ProductVariants");
            migrationBuilder.DropColumn(name: "ReorderQty", table: "ProductVariants");
        }
    }
}
```
(Fallback manual TIDAK memperbarui `AppDbContextModelSnapshot.cs`; catat ke user bahwa snapshot perlu di-regenerate via `dotnet ef` nanti sebelum migration berikutnya. Test SQLite tetap jalan karena pakai EnsureCreated dari model.)

- [ ] **Step 3: Build untuk memastikan migration kompilasi**

Run: `dotnet build src/ErpOne.Infrastructure/ErpOne.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit (penanda batas task)**

```bash
git add src/ErpOne.Infrastructure/Persistence/Migrations/
```

---

## Task 4: LowStockService (DTO + interface + impl + DI) — TDD

**Files:**
- Create: `src/ErpOne.Application/LowStock/LowStockDtos.cs`
- Create: `src/ErpOne.Application/LowStock/ILowStockService.cs`
- Create: `src/ErpOne.Infrastructure/Services/Inventory/LowStockService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs`
- Create: `tests/ErpOne.IntegrationTests/LowStockServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (`ProductStocks`, `ProductVariants`, `Products`, `Warehouses`), `ProductVariant.ReorderLevel`/`ReorderQty`.
- Produces: `ILowStockService.GetLowStockAsync(int? warehouseId, ...)` → `LowStockSummaryDto`.

- [ ] **Step 1: DTO + interface**

Create `src/ErpOne.Application/LowStock/LowStockDtos.cs`:
```csharp
namespace ErpOne.Application.LowStock;

public record LowStockRowDto(
    int VariantId, string Sku, int ProductId, string ProductName,
    int WarehouseId, string WarehouseName,
    int Quantity, int ReorderLevel, int ReorderQty, int SuggestedOrderQty, bool IsOutOfStock);

public record LowStockSummaryDto(IReadOnlyList<LowStockRowDto> Rows, int LowCount, int OutOfStockCount);
```

Create `src/ErpOne.Application/LowStock/ILowStockService.cs`:
```csharp
namespace ErpOne.Application.LowStock;

public interface ILowStockService
{
    Task<LowStockSummaryDto> GetLowStockAsync(int? warehouseId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Tulis test yang gagal**

Create `tests/ErpOne.IntegrationTests/LowStockServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.LowStock;
using ErpOne.Application.Products;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class LowStockServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public LowStockServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // default warehouse + category; returns (warehouseId, categoryId).
    private static async Task<(int wh, int cat)> SeedBaseAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, true); // isDefault = true (opening stock lands here)
        var cat = new ProductCategory($"CT{id}", $"Kategori {id}", null);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();
        return (wh.Id, cat.Id);
    }

    [Fact]
    public async Task Low_stock_lists_variant_below_reorder_with_suggested_qty()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, cat) = await SeedBaseAsync(sp);

        // Product with two variants via CreateAsync (also proves reorder persists):
        //  V1 opening 8, reorder 10, reorderQty 50 → LOW (suggested 50)
        //  V2 opening 20, reorder 10 → safe (not listed)
        var products = sp.GetRequiredService<IProductService>();
        var created = await products.CreateAsync(new CreateProductRequest(
            $"Prod {Sfx()}", null, cat, null, null, null, ProductStatus.Aktif,
            [
                new VariantInput(null, 2000m, null, 1000m, null, null, 8, true, [], null, 10, 50),
                new VariantInput(null, 2000m, null, 1000m, null, null, 20, true, [], null, 10, 0),
            ]));

        var svc = sp.GetRequiredService<ILowStockService>();
        var result = await svc.GetLowStockAsync(wh);

        // Isolate to this product's variants (DB shared across tests).
        var mine = result.Rows.Where(r => r.ProductId == created).ToList();
        var low = Assert.Single(mine);
        Assert.Equal(8, low.Quantity);
        Assert.Equal(10, low.ReorderLevel);
        Assert.Equal(50, low.SuggestedOrderQty);
        Assert.False(low.IsOutOfStock);
    }

    [Fact]
    public async Task Zero_reorder_not_tracked_and_zero_qty_flagged_out_and_warehouse_filter()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (wh, cat) = await SeedBaseAsync(sp);
        var other = new Warehouse($"WX{Sfx()}", "Lain", null, true, false);
        db.Warehouses.Add(other);
        await db.SaveChangesAsync();

        // Seed directly for full control:
        //  vOut: reorder 5, a ProductStock row qty 0 → LOW + IsOutOfStock, suggested max(5-0,0)=5
        //  vUntracked: reorder 0, qty 1 → NOT listed
        var id = Sfx();
        var product = new Product($"PR{id}", $"Produk {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vOut = product.AddVariant($"OUT{id}", null, 2000m, null, 1000m, null, null, true, null, 5, 0);
        var vUn = product.AddVariant($"UN{id}", null, 2000m, null, 1000m, null, null, true, null, 0, 0);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(vOut.Id, wh, 0));
        db.ProductStocks.Add(new ProductStock(vUn.Id, wh, 1));
        await db.SaveChangesAsync();

        var svc = sp.GetRequiredService<ILowStockService>();
        var result = await svc.GetLowStockAsync(wh);
        var mine = result.Rows.Where(r => r.ProductId == product.Id).ToList();

        var outRow = Assert.Single(mine);            // only vOut; vUn excluded (reorder 0)
        Assert.Equal(vOut.Id, outRow.VariantId);
        Assert.True(outRow.IsOutOfStock);
        Assert.Equal(5, outRow.SuggestedOrderQty);

        // Warehouse filter: nothing in `other` warehouse.
        var none = await svc.GetLowStockAsync(other.Id);
        Assert.DoesNotContain(none.Rows, r => r.ProductId == product.Id);
    }
}
```

- [ ] **Step 3: Jalankan — pastikan gagal (belum ada impl/DI)**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~LowStockServiceTests"`
Expected: FAIL (resolve/compile — `ILowStockService` belum terdaftar/impl).

- [ ] **Step 4: Tulis implementasi**

Create `src/ErpOne.Infrastructure/Services/Inventory/LowStockService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.LowStock;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class LowStockService(AppDbContext db) : ILowStockService
{
    public async Task<LowStockSummaryDto> GetLowStockAsync(int? warehouseId, CancellationToken ct = default)
    {
        var q =
            from ps in db.ProductStocks.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on ps.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on ps.WarehouseId equals w.Id
            where v.ReorderLevel > 0 && ps.Quantity <= v.ReorderLevel
            select new { v.Id, v.Sku, p.ProductId, ProductName = p.Name, WarehouseId = w.Id, WarehouseName = w.Name,
                         ps.Quantity, v.ReorderLevel, v.ReorderQty };

        if (warehouseId is int wid) q = q.Where(x => x.WarehouseId == wid);

        var raw = await q.ToListAsync(ct);

        var rows = raw
            .Select(x => new LowStockRowDto(
                x.Id, x.Sku, x.ProductId, x.ProductName, x.WarehouseId, x.WarehouseName,
                x.Quantity, x.ReorderLevel, x.ReorderQty,
                x.ReorderQty > 0 ? x.ReorderQty : Math.Max(x.ReorderLevel - x.Quantity, 0),
                x.Quantity == 0))
            .OrderByDescending(r => r.IsOutOfStock)
            .ThenBy(r => r.Quantity - r.ReorderLevel)
            .ThenBy(r => r.Sku)
            .ToList();

        return new LowStockSummaryDto(rows, rows.Count, rows.Count(r => r.IsOutOfStock));
    }
}
```

Note: projeksi `p.ProductId` sebenarnya `p.Id` — `Product` PK bernama `Id`. Ganti `p.ProductId` → `p.Id` di select (alias tetap `ProductId = p.Id`). Implementasi final select:
```csharp
            select new { v.Id, v.Sku, ProductId = p.Id, ProductName = p.Name, WarehouseId = w.Id, WarehouseName = w.Name,
                         ps.Quantity, v.ReorderLevel, v.ReorderQty };
```

- [ ] **Step 5: Daftarkan DI**

Modify `src/ErpOne.Infrastructure/DependencyInjection.cs` — tambah dekat service inventory (mis. setelah `services.AddScoped<IStockService, StockService>();`):
```csharp
        services.AddScoped<ILowStockService, LowStockService>();
```
Pastikan `using ErpOne.Application.LowStock;` ada di atas file (tambahkan bila belum).

- [ ] **Step 6: Jalankan test — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~LowStockServiceTests"`
Expected: PASS (2 test).

- [ ] **Step 7: Commit (penanda batas task)**

```bash
git add src/ErpOne.Application/LowStock/ src/ErpOne.Infrastructure/Services/Inventory/LowStockService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/LowStockServiceTests.cs
```

---

## Task 5: Dashboard low-stock via reorder level

**Files:**
- Modify: `src/ErpOne.Infrastructure/Services/Master/ProductService.cs` (`GetDashboardAsync`, hapus `LowStockThreshold`)
- Modify: `tests/ErpOne.IntegrationTests/LowStockServiceTests.cs` (tambah 1 test)

**Interfaces:**
- Consumes: `ProductVariant.ReorderLevel`, `AppDbContext`.
- Produces: `ProductDashboardDto.LowStockCount`/`LowStock` berbasis reorder level (bentuk DTO tetap).

- [ ] **Step 1: Hapus konstanta**

Di `ProductService.cs` hapus baris `private const int LowStockThreshold = 5;`.

- [ ] **Step 2: Ganti perhitungan low-stock di `GetDashboardAsync`**

Ganti dua baris (`outOfStock`/`lowStock` di sekitar 418-419) dan blok `lowStockProductIds` (436-437) memakai reorder level. Setelah `stockByProduct` dihitung, tambahkan:

```csharp
        // Produk "low" = punya >=1 baris (varian,gudang) dgn ReorderLevel>0 && qty<=ReorderLevel.
        var lowProductIds = (await db.ProductStocks.AsNoTracking()
                .Join(db.ProductVariants.AsNoTracking(), s => s.ProductVariantId, v => v.Id,
                    (s, v) => new { v.ProductId, s.Quantity, v.ReorderLevel })
                .Where(x => x.ReorderLevel > 0 && x.Quantity <= x.ReorderLevel)
                .Select(x => x.ProductId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var outOfStock = stockByProduct.Count(x => x.Stock == 0);
        var lowStock = stockByProduct.Count(x => x.Stock > 0 && lowProductIds.Contains(x.ProductId));
```

Dan ubah `lowStockProductIds`:
```csharp
        var lowStockProductIds = stockByProduct
            .Where(x => x.Stock > 0 && lowProductIds.Contains(x.ProductId))
            .OrderBy(x => x.Stock).Take(8).Select(x => x.ProductId).ToList();
```
(Sisanya — `lowStockProducts`, `lowStockItems`, `return` — TIDAK berubah.)

- [ ] **Step 3: Test dashboard low count**

Tambahkan ke `LowStockServiceTests.cs`:
```csharp
    [Fact]
    public async Task Dashboard_low_count_uses_reorder_level()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (wh, cat) = await SeedBaseAsync(sp);

        // Product LOW: variant reorder 10, stock 5. Product SAFE: reorder 10, stock 50.
        var id = Sfx();
        var pLow = new Product($"PL{id}", $"Low {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vLow = pLow.AddVariant($"L{id}", null, 2000m, null, 1000m, null, null, true, null, 10, 0);
        var pSafe = new Product($"PS{id}", $"Safe {id}", null, cat, null, null, null, ProductStatus.Aktif);
        var vSafe = pSafe.AddVariant($"S{id}", null, 2000m, null, 1000m, null, null, true, null, 10, 0);
        db.Products.AddRange(pLow, pSafe);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(vLow.Id, wh, 5));
        db.ProductStocks.Add(new ProductStock(vSafe.Id, wh, 50));
        await db.SaveChangesAsync();

        var dash = await sp.GetRequiredService<IProductService>().GetDashboardAsync();

        Assert.Contains(dash.LowStock, i => i.Id == pLow.Id);
        Assert.DoesNotContain(dash.LowStock, i => i.Id == pSafe.Id);
    }
```
(Tambah `using ErpOne.Application.Products;` sudah ada di file test.)

- [ ] **Step 4: Jalankan test low-stock (3 test) — pastikan lulus**

Run: `dotnet test ErpOne.slnx --filter "FullyQualifiedName~LowStockServiceTests"`
Expected: PASS (3 test).

- [ ] **Step 5: Commit (penanda batas task)**

```bash
git add src/ErpOne.Infrastructure/Services/Master/ProductService.cs tests/ErpOne.IntegrationTests/LowStockServiceTests.cs
```

---

## Task 6: Menu resource `inventory.low-stock`

**Files:**
- Modify: `src/ErpOne.Web/Authorization/AppMenus.cs` (grup Inventory)

**Interfaces:**
- Produces: permission `inventory.low-stock.index` (auto ke `AllPermissions`, di-seed admin). Route `/inventory/low-stock` via konvensi key→href.

- [ ] **Step 1: Tambah resource**

Di `AppMenus.cs`, grup Inventory, tambah setelah `inventory.adjustments`:
```csharp
            new("inventory.low-stock", "Low Stock", "bi-exclamation-triangle", ViewOnly),
```

- [ ] **Step 2: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Authorization/AppMenus.cs
```

---

## Task 7: Low-stock page

**Files:**
- Create: `src/ErpOne.Web/Components/Pages/Inventory/LowStock/LowStockIndex.razor`

**Interfaces:**
- Consumes: `ILowStockService.GetLowStockAsync`, `LowStockSummaryDto`, `IWarehouseService.GetAllAsync()`→`WarehouseDto` (ns `ErpOne.Application.Warehouses`).
- Produces: route `/inventory/low-stock`.

- [ ] **Step 1: Tulis halaman**

Create `src/ErpOne.Web/Components/Pages/Inventory/LowStock/LowStockIndex.razor`:
```razor
@page "/inventory/low-stock"
@attribute [Authorize(Policy = "inventory.low-stock.index")]
@rendermode InteractiveServer
@using ErpOne.Application.LowStock
@using ErpOne.Application.Warehouses
@inject ILowStockService LowStock
@inject IWarehouseService WarehouseService

<PageTitle>Low Stock</PageTitle>

<div class="pi">
    <div class="pi-head">
        <div>
            <nav class="crumbs">
                <a href="/">Home</a><span class="sep">·</span><span>Inventory</span><span class="sep">·</span><span class="here">Low Stock</span>
            </nav>
            <h1>Low Stock</h1>
            <p>Variants at or below their reorder level, per warehouse.</p>
        </div>
    </div>

    @if (_result is not null)
    {
        <div class="kpis">
            <div class="kpi accent">
                <div class="ic ic-amb"><i class="bi bi-exclamation-triangle"></i></div>
                <div class="kpi-tx"><div class="v">@_result.LowCount.ToString("N0")</div><div class="l">Items low</div></div>
            </div>
            <div class="kpi">
                <div class="ic ic-red"><i class="bi bi-x-octagon"></i></div>
                <div class="kpi-tx"><div class="v">@_result.OutOfStockCount.ToString("N0")</div><div class="l">Out of stock</div></div>
            </div>
        </div>
    }

    <div class="toolbar">
        <select @bind="_warehouseId" @bind:after="ReloadAsync">
            <option value="0">All warehouses</option>
            @foreach (var w in _warehouses) { <option value="@w.Id">@w.Name</option> }
        </select>
    </div>

    @if (_result is null)
    {
        <div class="pi-loading"><span class="spinner-border spinner-border-sm me-2" role="status"></span>Loading…</div>
    }
    else if (_result.Rows.Count == 0)
    {
        <div class="empty"><div class="empty-ic"><i class="bi bi-check2-circle"></i></div><p>No variants below reorder level.</p></div>
    }
    else
    {
        <div class="card">
            <div class="table-responsive">
                <table>
                    <thead>
                        <tr>
                            <th style="width:160px">SKU</th><th>Product</th><th>Warehouse</th>
                            <th class="r" style="width:90px">Qty</th>
                            <th class="r" style="width:120px">Reorder Level</th>
                            <th class="r" style="width:140px">Suggested Order</th>
                            <th style="width:90px">Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var r in _result.Rows)
                        {
                            <tr>
                                <td class="code mono">@r.Sku</td>
                                <td class="nm">@r.ProductName</td>
                                <td>@r.WarehouseName</td>
                                <td class="r mono">@r.Quantity.ToString("N0")</td>
                                <td class="r mono">@r.ReorderLevel.ToString("N0")</td>
                                <td class="r mono">@r.SuggestedOrderQty.ToString("N0")</td>
                                <td>
                                    @if (r.IsOutOfStock)
                                    {
                                        <span class="badge bg-danger">Out</span>
                                    }
                                    else
                                    {
                                        <span class="badge bg-warning text-dark">Low</span>
                                    }
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>

@code {
    private LowStockSummaryDto? _result;
    private IReadOnlyList<WarehouseDto> _warehouses = [];
    private int _warehouseId;

    protected override async Task OnInitializedAsync()
    {
        _warehouses = await WarehouseService.GetAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() => _result = await LowStock.GetLowStockAsync(_warehouseId == 0 ? null : _warehouseId);
    private async Task ReloadAsync() => await LoadAsync();
}
```

- [ ] **Step 2: Build Web**

Run: `dotnet build src/ErpOne.Web/ErpOne.Web.csproj`
Expected: Build succeeded. (Bila `.ic-red` tak ada di CSS, badge tetap jalan; ikon KPI mungkin tanpa warna khusus — abaikan, non-blocking.)

- [ ] **Step 3: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Inventory/LowStock/LowStockIndex.razor
```

---

## Task 8: Product form reorder inputs + full suite + verify

**Files:**
- Modify: `src/ErpOne.Web/Components/Pages/Master/Products/ProductForm.razor`

**Interfaces:**
- Consumes: `VariantInput.ReorderLevel`/`ReorderQty`, `ProductVariantDto.ReorderLevel`/`ReorderQty`.

Pola: **tiru persis `DiscountPercent`/`Weight`** yang sudah ada di dua mode (single & multi-variant).

- [ ] **Step 1: State single-mode**

Di blok field single (dekat `private decimal? _weight;`, ~baris 538), tambah:
```csharp
    private int _reorderLevel;
    private int _reorderQty;
```

- [ ] **Step 2: Field di `VariantRow`**

Di `class VariantRow` (~baris 919, dekat `public decimal? Weight { get; set; }`), tambah:
```csharp
        public int ReorderLevel { get; set; }
        public int ReorderQty { get; set; }
```

- [ ] **Step 3: Input UI (single mode)** — dekat input Weight single (~baris 312), tambah setelahnya:
```razor
                                <div class="c6">
                                    <label class="fl">Reorder Level</label>
                                    <input class="ctl mono" type="number" step="1" min="0" placeholder="0"
                                           @bind="_reorderLevel" @bind:event="oninput" />
                                </div>
                                <div class="c6">
                                    <label class="fl">Reorder Qty</label>
                                    <input class="ctl mono" type="number" step="1" min="0" placeholder="0"
                                           @bind="_reorderQty" @bind:event="oninput" />
                                </div>
```

- [ ] **Step 4: Input UI (multi mode)** — dekat input Weight per-baris (~baris 460-462), tambah dua blok:
```razor
                                                                <div>
                                                                    <label class="form-label-sm">Reorder Level</label>
                                                                    <input class="ctl ctl-sm mono text-end" type="number" step="1" min="0" placeholder="0" @bind="_rows[idx].ReorderLevel" />
                                                                </div>
                                                                <div>
                                                                    <label class="form-label-sm">Reorder Qty</label>
                                                                    <input class="ctl ctl-sm mono text-end" type="number" step="1" min="0" placeholder="0" @bind="_rows[idx].ReorderQty" />
                                                                </div>
```

- [ ] **Step 5: Load path (edit)** — di blok single-load (~baris 634-640, dekat `_weight = v.Weight;`) tambah:
```csharp
            _reorderLevel = v.ReorderLevel;
            _reorderQty = v.ReorderQty;
```
Di blok row-load (~baris 658-665, dekat `Weight = v.Weight,`) tambah ke inisialisasi row:
```csharp
                ReorderLevel = v.ReorderLevel,
                ReorderQty = v.ReorderQty,
```

- [ ] **Step 6: `BuildVariantInputs`** — tambah 2 arg trailing di kedua `new VariantInput(...)` (~baris 876 & 880):

Single (876):
```csharp
                new VariantInput(_barcode, _price, _discountPrice, _cost, _weight, _dimensions, Id is null ? _stock : 0, true, Array.Empty<int>(), _discountPercent, _reorderLevel, _reorderQty)
```
Multi (880):
```csharp
            .Select(r => new VariantInput(r.Barcode, r.Price, r.Discount, r.Cost, r.Weight, r.Dimensions, Id is null ? r.Stock : 0, r.IsActive, r.AttributeValueIds, r.DiscountPercent, r.ReorderLevel, r.ReorderQty))
```

- [ ] **Step 7: Build + jalankan SELURUH test suite**

Run: `dotnet test ErpOne.slnx`
Expected: Build succeeded; SEMUA test PASS (291 + 3 LowStock baru = 294). Pastikan app Visual Studio di-stop dulu.

- [ ] **Step 8: Verifikasi manual (skill `run`/`verify`)**

Jalankan app, sign out/in (agar admin dapat `inventory.low-stock.index`). Buka form produk → set Reorder Level/Qty pada varian → simpan → buka lagi (nilai ter-load). Buat stok di bawah reorder → buka `/inventory/low-stock` (muncul dgn badge Low/Out + suggested qty) & cek widget "stok menipis" di `/dashboard` ikut angka reorder. Smoke headless: route `/inventory/low-stock` → 302→login.

- [ ] **Step 9: Commit (penanda batas task)**

```bash
git add src/ErpOne.Web/Components/Pages/Master/Products/ProductForm.razor
```

---

## Self-Review (untuk penulis plan)

**Spec coverage:**
- ReorderLevel + ReorderQty per varian (entity + DTO + validator + service + form) → Task 1,2,8. ✓
- Migration → Task 3. ✓
- Low = (varian,gudang) qty<=reorder; OutOfStock qty==0; Suggested = ReorderQty else max(reorder-qty,0) → Task 4 impl. ✓
- Halaman /inventory/low-stock (filter gudang, KPI, badge) → Task 7. ✓
- Dashboard pakai reorder level, bentuk DTO tetap → Task 5. ✓
- Permission inventory.low-stock + seeding → Task 6. ✓
- Testing (low list, suggested, out-of-stock, not-tracked, filter, persist via CreateAsync, dashboard) → Task 4,5. ✓

**Placeholder scan:** Tak ada TBD. Satu koreksi inline sengaja di Task 4 Step 4 (`p.ProductId`→`p.Id`) — instruksi eksplisit, bukan placeholder. Fallback migration manual di Task 3 lengkap.

**Type consistency:** `VariantInput`(+ReorderLevel,ReorderQty trailing default 0) & `ProductVariantDto`(+ReorderLevel,ReorderQty) dipakai konsisten di ProductService (Task 2) & form (Task 8). `ProductVariant.ReorderLevel/ReorderQty` (int) & ctor/Update/AddVariant param `int reorderLevel=0, int reorderQty=0` konsisten Task 1↔2↔4-test↔8. `ILowStockService.GetLowStockAsync(int?, CancellationToken)`→`LowStockSummaryDto{Rows,LowCount,OutOfStockCount}`, `LowStockRowDto` fields konsisten Task 4↔7. Verifikasi runtime: `Product` PK `Id`, `ProductVariant.ProductId`, `ProductStock.ProductVariantId/WarehouseId/Quantity`, `Warehouse` ctor `(code,name,?,isActive,isDefault)`, `ProductCategory` ctor `(code,name,?,isActive)`, `CreateProductRequest(name,desc,categoryId,brandId?,unitId?,taxId?,status,variants)` — sudah dikonfirmasi dari kode.
