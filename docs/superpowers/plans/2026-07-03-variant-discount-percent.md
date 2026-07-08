# Discount % Varian + Tampilan Diskon di Kasir — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) atau subagent-driven-development. Steps pakai checkbox (`- [ ]`).

**Goal:** Tambah `DiscountPercent` (disimpan) ke varian produk dengan kalkulasi dua-arah ↔ Discount Price di form, format ribuan pada field harga, dan tampilkan diskon (persen/harga) di layar Kasir.

**Architecture:** Clean Architecture (Domain → Application → Infrastructure → Web/Blazor). `DiscountPercent` = metadata tampilan; harga jual efektif tetap `DiscountPrice ?? Price`. Saat % diisi di form, Discount Price di-set = `round(Price×(1−%/100))` supaya efektif konsisten.

**Tech Stack:** .NET 10, EF Core (SQL Server), Blazor Server + Bootstrap 5, FluentValidation, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-03-variant-discount-percent-design.md`

## Global Constraints
- **No VCS:** ganti "Commit" dgn build+test; catat progres di `docs/superpowers/plans/discount-percent-progress.md`.
- **Baseline:** build 0 warnings (`TreatWarningsAsErrors=true`), **209 test** (127 unit + 82 integ).
- **JANGAN `--no-build` untuk `dotnet ef`.** Pastikan dev app mati sebelum build/test.
- Uang `decimal(18,2)`; `Math.Round(v,2,MidpointRounding.AwayFromZero)`.
- Harga jual efektif = `DiscountPrice ?? Price` (tak berubah).

---

### Task 1: Domain — `ProductVariant.DiscountPercent` + unit tests

**Files:**
- Modify: `src/MyApp.Domain/Entities/ProductVariant.cs`
- Modify: `src/MyApp.Domain/Entities/Product.cs` (`AddVariant` signature)
- Test: `tests/MyApp.UnitTests/ProductVariantDiscountTests.cs`

**Interfaces (Produces):**
- `ProductVariant` prop `decimal? DiscountPercent`.
- ctor + `Update(...)` menerima `decimal? discountPercent` sebagai **parameter terakhir**.
- `Product.AddVariant(..., bool isActive, decimal? discountPercent)` — `discountPercent` param terakhir.

- [ ] **Step 1: Failing test** — `tests/MyApp.UnitTests/ProductVariantDiscountTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class ProductVariantDiscountTests
{
    private static ProductVariant Make(decimal? pct) =>
        new("SKU-1", null, price: 100_000m, discountPrice: 90_000m, costPrice: 40_000m,
            weight: null, dimensions: null, isActive: true, discountPercent: pct);

    [Fact]
    public void Stores_discount_percent_when_valid()
    {
        Assert.Equal(10m, Make(10m).DiscountPercent);
        Assert.Null(Make(null).DiscountPercent);
    }

    [Fact]
    public void Rejects_out_of_range_percent()
    {
        Assert.Throws<ArgumentException>(() => Make(-1m));
        Assert.Throws<ArgumentException>(() => Make(101m));
    }

    [Fact]
    public void Update_sets_percent()
    {
        var v = Make(null);
        v.Update(null, 100_000m, 80_000m, 40_000m, null, null, true, 20m);
        Assert.Equal(20m, v.DiscountPercent);
        Assert.Equal(80_000m, v.DiscountPrice);
    }
}
```

- [ ] **Step 2: Run → fail.** `dotnet test tests/MyApp.UnitTests --filter FullyQualifiedName~ProductVariantDiscountTests` → FAIL (param belum ada).

- [ ] **Step 3: `ProductVariant`** — tambah prop + helper + parameter. Tambahkan prop setelah `DiscountPrice`:

```csharp
    public decimal? DiscountPrice { get; private set; }
    public decimal? DiscountPercent { get; private set; }
```

Ubah **ctor** (tambah param terakhir + panggil helper):

```csharp
    public ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null)
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
    }
```

Ubah **Update** (tambah param terakhir + panggil helper):

```csharp
    public void Update(string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null)
    {
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetDiscountPercent(discountPercent);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        IsActive = isActive;
    }
```

Tambah helper (setelah `SetDiscountPrice`):

```csharp
    private void SetDiscountPercent(decimal? discountPercent)
    {
        if (discountPercent is { } p && (p < 0 || p > 100))
            throw new ArgumentException("Discount percent must be 0..100.", nameof(discountPercent));
        DiscountPercent = discountPercent;
    }
```

- [ ] **Step 4: `Product.AddVariant`** — tambah param terakhir & teruskan:

```csharp
    public ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, bool isActive, decimal? discountPercent = null)
    {
        var v = new ProductVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, isActive, discountPercent);
        _variants.Add(v);
        return v;
    }
```

- [ ] **Step 5: Run → pass** (`~ProductVariantDiscountTests` → 3 pass). `dotnet build` 0 warnings. Catat Task 1.

*(Default param `= null` menjaga pemanggil lama tetap kompilasi; Task 4 mengisi nilai nyata.)*

---

### Task 2: Persistence — mapping + migration

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` (mapping `ProductVariant`, ~baris 92)
- Create (via EF): migration `AddVariantDiscountPercent`

- [ ] **Step 1: Mapping** — di `modelBuilder.Entity<ProductVariant>`, setelah `e.Property(v => v.DiscountPrice).HasPrecision(18, 2);` tambahkan:

```csharp
            e.Property(v => v.DiscountPercent).HasPrecision(18, 2);
```

- [ ] **Step 2: Build** `dotnet build` (full, tanpa error).

- [ ] **Step 3: Migration.** Pastikan dev app mati. `dotnet ef migrations add AddVariantDiscountPercent -p src/MyApp.Infrastructure -s src/MyApp.Web` (TANPA --no-build). Verifikasi Up `AddColumn<decimal>("DiscountPercent", "ProductVariants", nullable: true, precision: 18, scale: 2)`, Down `DropColumn`.

- [ ] **Step 4: Apply.** `dotnet ef database update -p src/MyApp.Infrastructure -s src/MyApp.Web`. `dotnet build` 0 warnings. Catat Task 2.

*(Migrations dir: `src/MyApp.Infrastructure/Persistence/Migrations`.)*

---

### Task 3: Application — DTOs + validator + tests

**Files:**
- Modify: `src/MyApp.Application/Products/ProductDtos.cs`
- Modify: `src/MyApp.Application/Products/CreateProductValidator.cs`
- Modify: `src/MyApp.Application/PosSales/PosSaleDtos.cs`
- Test: `tests/MyApp.UnitTests/ProductVariantValidatorTests.cs`

**Interfaces (Produces):**
- `ProductVariantDto` + `decimal? DiscountPercent` (param terakhir).
- `VariantInput` + `decimal? DiscountPercent` (param terakhir).
- `PosProductOptionDto(int VariantId, string Sku, string ProductName, string? Barcode, decimal UnitPrice, int OnHand, decimal Price, decimal? DiscountPercent)`.

- [ ] **Step 1: `ProductVariantDto`** — tambah `decimal? DiscountPercent` di akhir daftar field non-koleksi (sebelum `Attributes`):

```csharp
public record ProductVariantDto(
    int Id, string Sku, string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int Stock, bool IsActive, decimal? DiscountPercent,
    IReadOnlyList<AttributeValueRefDto> Attributes);
```

- [ ] **Step 2: `VariantInput`** — tambah param terakhir:

```csharp
public record VariantInput(
    string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int OpeningStock, bool IsActive,
    IReadOnlyList<int> AttributeValueIds, decimal? DiscountPercent = null);
```

- [ ] **Step 3: `PosProductOptionDto`** — di `PosSaleDtos.cs` ganti record:

```csharp
public record PosProductOptionDto(int VariantId, string Sku, string ProductName, string? Barcode, decimal UnitPrice, int OnHand, decimal Price, decimal? DiscountPercent);
```

- [ ] **Step 4: Validator** — di `VariantInputValidator` tambahkan:

```csharp
        RuleFor(v => v.DiscountPercent).InclusiveBetween(0, 100).When(v => v.DiscountPercent.HasValue);
```

- [ ] **Step 5: Validator test** — `tests/MyApp.UnitTests/ProductVariantValidatorTests.cs`:

```csharp
using MyApp.Application.Products;
using Xunit;

namespace MyApp.UnitTests;

public class ProductVariantValidatorTests
{
    private static VariantInput Input(decimal? pct) =>
        new(null, 100m, 90m, 40m, null, null, 0, true, [], pct);

    [Fact]
    public void Accepts_null_and_in_range_percent()
    {
        var v = new VariantInputValidator();
        Assert.True(v.Validate(Input(null)).IsValid);
        Assert.True(v.Validate(Input(0m)).IsValid);
        Assert.True(v.Validate(Input(100m)).IsValid);
    }

    [Fact]
    public void Rejects_out_of_range_percent()
    {
        var v = new VariantInputValidator();
        Assert.False(v.Validate(Input(-1m)).IsValid);
        Assert.False(v.Validate(Input(101m)).IsValid);
    }
}
```

- [ ] **Step 6: Run → pass** (`~ProductVariantValidatorTests` → 2). `dotnet build` **akan gagal** karena pemakai `ProductVariantDto`/`PosProductOptionDto` (ProductService, PosSaleService) belum menyuplai field baru → diperbaiki Task 4. **Jangan build penuh di sini**; cukup jalankan filter test unit (project UnitTests referensi Application saja):

Run: `dotnet test tests/MyApp.UnitTests --filter FullyQualifiedName~ProductVariantValidatorTests`
Expected: PASS (2). Catat Task 3 (build penuh ditunda ke Task 4).

---

### Task 4: Infrastructure — service mapping + integration test

**Files:**
- Modify: `src/MyApp.Infrastructure/Services/ProductService.cs` (baris ~85, ~157, ~166, ~373, ~487)
- Modify: `src/MyApp.Infrastructure/Services/PosSaleService.cs` (`SearchProductsAsync`)
- Test: `tests/MyApp.IntegrationTests/PosSaleSearchDiscountTests.cs`

**Interfaces (Consumes):** `VariantInput.DiscountPercent`, `ProductVariantDto.DiscountPercent`, `PosProductOptionDto` field baru (Task 3); `Product.AddVariant/Update` (Task 1).

- [ ] **Step 1: Failing integration test** — `tests/MyApp.IntegrationTests/PosSaleSearchDiscountTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.PosSales;
using MyApp.Application.Stock;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class PosSaleSearchDiscountTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PosSaleSearchDiscountTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    // Seed 1 varian; percentDiscount → DiscountPrice dihitung pemanggil (di sini kita set eksplisit).
    private static async Task<(int wh, string sku)> SeedVariantAsync(IServiceProvider sp,
        decimal price, decimal? discountPrice, decimal? discountPercent)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        var product = new Product($"PR{id}", $"Produk {id}", null, null, null, null, null, ProductStatus.Aktif);
        db.Warehouses.Add(wh); db.Products.Add(product);
        await db.SaveChangesAsync();
        var v = product.AddVariant($"SK{id}", $"BC{id}", price, discountPrice, 40_000m, null, null, true, discountPercent);
        await db.SaveChangesAsync();
        await sp.GetRequiredService<IStockService>().RecordOpeningAsync(v.Id, wh.Id, 10, 40_000m);
        return (wh.Id, v.Sku);
    }

    [Fact]
    public async Task Search_returns_original_price_and_percent_for_percent_discount()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: 90_000m, discountPercent: 10m);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);       // harga asli
        Assert.Equal(90_000m, opt.UnitPrice);    // efektif = discount price
        Assert.Equal(10m, opt.DiscountPercent);
    }

    [Fact]
    public async Task Search_returns_null_percent_for_price_only_discount()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: 80_000m, discountPercent: null);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);
        Assert.Equal(80_000m, opt.UnitPrice);
        Assert.Null(opt.DiscountPercent);
    }

    [Fact]
    public async Task Search_no_discount_price_equals_unit()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, sku) = await SeedVariantAsync(sp, price: 100_000m, discountPrice: null, discountPercent: null);
        var opt = Assert.Single(await sp.GetRequiredService<IPosSaleService>().SearchProductsAsync(wh, sku));
        Assert.Equal(100_000m, opt.Price);
        Assert.Equal(100_000m, opt.UnitPrice);
    }
}
```

- [ ] **Step 2: Run → fail** (kompilasi: `SearchProductsAsync` belum kembalikan field baru; `AddVariant` 9-arg sudah ada dari Task 1).

- [ ] **Step 3: `PosSaleService.SearchProductsAsync`** — sertakan `DiscountPercent` di proyeksi & DTO. Ganti isi method:

```csharp
    public async Task<IReadOnlyList<PosProductOptionDto>> SearchProductsAsync(int warehouseId, string? term, CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, v.Barcode, ProductName = p.Name, v.Price, v.DiscountPrice, v.DiscountPercent };

        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Barcode == term || x.Sku.Contains(term) || x.ProductName.Contains(term));

        var rows = await q.OrderBy(x => x.ProductName).Take(20)
            .Select(x => new { x.Id, x.Sku, x.Barcode, x.ProductName, x.Price, x.DiscountPrice, x.DiscountPercent })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var stock = await db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductVariantId))
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        return rows.Select(r => new PosProductOptionDto(
            r.Id, r.Sku, r.ProductName, r.Barcode,
            r.DiscountPrice ?? r.Price,
            stock.FirstOrDefault(s => s.VariantId == r.Id)?.Qty ?? 0,
            r.Price, r.DiscountPercent)).ToList();
    }
```

- [ ] **Step 4: `ProductService`** — teruskan `DiscountPercent` di 4 titik:

Baris ~85 (Create AddVariant):
```csharp
            var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent);
```

Baris ~157 (Update existing):
```csharp
                existing.Update(v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent);
```

Baris ~166 (Update AddVariant):
```csharp
                var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                    v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent);
```

Baris ~373 (Import — DI LUAR SCOPE, teruskan null eksplisit agar tetap kompilasi & jelas):
```csharp
                product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, true, null);
```

Baris ~487 (DTO mapping) — sisipkan `v.DiscountPercent` sebelum koleksi Attributes:
```csharp
        var variants = p.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantDto(
            v.Id, v.Sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions,
            stockByVariant.TryGetValue(v.Id, out var q) ? q : 0, v.IsActive, v.DiscountPercent,
            v.Attributes.Where(a => values.ContainsKey(a.AttributeValueId))
                .Select(a => { var x = values[a.AttributeValueId]; return new AttributeValueRefDto(a.AttributeValueId, x.AttrName, x.Code, x.Value); })
                .ToList())).ToList();
```

- [ ] **Step 5: Build + test.** `dotnet build` 0 warnings. `dotnet test tests/MyApp.IntegrationTests --filter FullyQualifiedName~PosSaleSearchDiscountTests` → PASS (3). Catat Task 4.

---

### Task 5: Web — `ProductForm` discount% + format harga

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`

**Interfaces (Consumes):** `VariantInput.DiscountPercent`, `ProductVariantDto.DiscountPercent`.

- [ ] **Step 1: Field state** — tambah field discount% single & helper. Di blok `@code` setelah `private decimal? _discountPrice;`:

```csharp
    private decimal? _discountPercent;
```

Di `VariantRow` (setelah `public decimal? Discount { get; set; }`):
```csharp
        public decimal? DiscountPercent { get; set; }
```

- [ ] **Step 2: Format & kalkulasi helpers** — tambah di akhir `@code` (sebelum `}` penutup class terakhir):

```csharp
    // ── Format harga (ribuan) & kalkulasi diskon ────────────────────────────
    private static string FmtPrice(decimal v) => v > 0 ? v.ToString("N0") : "";
    private static string FmtPriceN(decimal? v) => v is > 0 ? v.Value.ToString("N0") : "";
    private static decimal DigitsToDecimal(object? raw)
        => decimal.TryParse(new string((raw?.ToString() ?? "").Where(char.IsDigit).ToArray()), out var v) ? v : 0m;
    private static decimal? DigitsToDecimalN(object? raw)
    {
        var s = new string((raw?.ToString() ?? "").Where(char.IsDigit).ToArray());
        return s.Length == 0 ? null : (decimal.TryParse(s, out var v) ? v : null);
    }
    private static decimal? ApplyPct(decimal price, decimal? pct)
        => pct is { } p ? Math.Round(price * (1 - p / 100m), 2, MidpointRounding.AwayFromZero) : null;

    // Single-item handlers
    private void OnPriceInput(ChangeEventArgs e)
    {
        _price = DigitsToDecimal(e.Value);
        if (_discountPercent is not null) _discountPrice = ApplyPct(_price, _discountPercent);
    }
    private void OnDiscPriceInput(ChangeEventArgs e) { _discountPrice = DigitsToDecimalN(e.Value); _discountPercent = null; }
    private void OnCostInput(ChangeEventArgs e) => _cost = DigitsToDecimal(e.Value);
    private void OnDiscPctChanged() { if (_discountPercent is { } p && (p < 0 || p > 100)) _discountPercent = Math.Clamp(p, 0, 100); _discountPrice = ApplyPct(_price, _discountPercent); }

    // Row handlers
    private void OnRowPriceInput(VariantRow r, ChangeEventArgs e)
    {
        r.Price = DigitsToDecimal(e.Value);
        if (r.DiscountPercent is not null) r.Discount = ApplyPct(r.Price, r.DiscountPercent);
    }
    private void OnRowDiscPriceInput(VariantRow r, ChangeEventArgs e) { r.Discount = DigitsToDecimalN(e.Value); r.DiscountPercent = null; }
    private void OnRowCostInput(VariantRow r, ChangeEventArgs e) => r.Cost = DigitsToDecimal(e.Value);
    private void OnRowDiscPctChanged(VariantRow r) { if (r.DiscountPercent is { } p && (p < 0 || p > 100)) r.DiscountPercent = Math.Clamp(p, 0, 100); r.Discount = ApplyPct(r.Price, r.DiscountPercent); }
```

- [ ] **Step 3: Single-item markup** — ganti 3 input harga + tambah Discount % (baris ~270–286). Ganti Selling Price, Discount Price, Cost Price, dan sisipkan Discount %:

```razor
                                <div class="c6">
                                    <label class="fl">Selling Price <span class="req">*</span></label>
                                    <div class="ig"><span class="pre">Rp</span>
                                        <input class="ctl mono pad" type="text" inputmode="numeric" placeholder="0"
                                               value="@FmtPrice(_price)" @oninput="OnPriceInput" /></div>
                                </div>
                                <div class="c6">
                                    <label class="fl">Discount %</label>
                                    <div class="ig"><span class="pre">%</span>
                                        <input class="ctl mono pad" type="number" step="0.01" min="0" max="100" placeholder="optional"
                                               @bind="_discountPercent" @bind:after="OnDiscPctChanged" /></div>
                                </div>
                                <div class="c6">
                                    <label class="fl">Discount Price</label>
                                    <div class="ig"><span class="pre">Rp</span>
                                        <input class="ctl mono pad" type="text" inputmode="numeric" placeholder="optional"
                                               value="@FmtPriceN(_discountPrice)" @oninput="OnDiscPriceInput" /></div>
                                </div>
                                <div class="c6">
                                    <label class="fl">Cost Price (HPP)</label>
                                    <div class="ig"><span class="pre">Rp</span>
                                        <input class="ctl mono pad" type="text" inputmode="numeric" placeholder="0"
                                               value="@FmtPrice(_cost)" @oninput="OnCostInput" /></div>
                                </div>
```

- [ ] **Step 4: Combination row markup** — Price cell (baris ~382) jadikan format:

```razor
                                                    <td><input class="ctl ctl-sm mono text-end" type="text" inputmode="numeric" value="@FmtPrice(_rows[idx].Price)" @oninput="e => OnRowPriceInput(_rows[idx], e)" /></td>
```

Di detail grid (baris ~406–412) ganti Cost & Discount Price + tambah Discount %:

```razor
                                                                <div>
                                                                    <label class="form-label-sm">Cost Price (HPP)</label>
                                                                    <input class="ctl ctl-sm mono text-end" type="text" inputmode="numeric" value="@FmtPrice(_rows[idx].Cost)" @oninput="e => OnRowCostInput(_rows[idx], e)" />
                                                                </div>
                                                                <div>
                                                                    <label class="form-label-sm">Discount %</label>
                                                                    <input class="ctl ctl-sm mono text-end" type="number" step="0.01" min="0" max="100" placeholder="optional" @bind="_rows[idx].DiscountPercent" @bind:after="() => OnRowDiscPctChanged(_rows[idx])" />
                                                                </div>
                                                                <div>
                                                                    <label class="form-label-sm">Discount Price</label>
                                                                    <input class="ctl ctl-sm mono text-end" type="text" inputmode="numeric" placeholder="optional" value="@FmtPriceN(_rows[idx].Discount)" @oninput="e => OnRowDiscPriceInput(_rows[idx], e)" />
                                                                </div>
```

- [ ] **Step 5: Load populates %** — di load single (baris ~580, setelah `_discountPrice = v.DiscountPrice;`):

```csharp
            _discountPercent = v.DiscountPercent;
```

Di load rows (baris ~603, setelah `Discount = v.DiscountPrice,`):
```csharp
                DiscountPercent = v.DiscountPercent,
```

- [ ] **Step 6: BuildVariantInputs passes %** — ganti kedua `new VariantInput(...)` (baris ~819, ~823):

```csharp
                new VariantInput(_barcode, _price, _discountPrice, _cost, _weight, _dimensions, Id is null ? _stock : 0, true, Array.Empty<int>(), _discountPercent)
```
```csharp
            .Select(r => new VariantInput(r.Barcode, r.Price, r.Discount, r.Cost, r.Weight, r.Dimensions, Id is null ? r.Stock : 0, r.IsActive, r.AttributeValueIds, r.DiscountPercent))
```

- [ ] **Step 7: Build** `dotnet build` 0 warnings. Catat Task 5. (Verifikasi UI form → Task 7 Step 4 user.)

---

### Task 6: Web — `PosRegister` tampilkan diskon

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor`
- Modify: `src/MyApp.Web/Components/Pages/Cashier/Pos/PosRegister.razor.css`

**Interfaces (Consumes):** `PosProductOptionDto.Price`, `.DiscountPercent`.

- [ ] **Step 1: CartLine bawa harga asli + %** — di `class CartLine` tambah:

```csharp
        public decimal OrigPrice { get; set; }
        public decimal? DiscPercent { get; set; }
```

- [ ] **Step 2: AddToCart isi field baru** — ganti pembuatan CartLine:

```csharp
        else _cart.Add(new CartLine { VariantId = p.VariantId, Sku = p.Sku, Name = p.ProductName, UnitPrice = p.UnitPrice, OnHand = p.OnHand, OrigPrice = p.Price, DiscPercent = p.DiscountPercent });
```

- [ ] **Step 3: Hasil pencarian tampil diskon** — di `.pos-results` ganti isi tombol `.res`:

```razor
                            <button class="res" @onclick="() => AddToCart(r)">
                                <span class="res-nm">@r.ProductName</span>
                                <span class="res-sku mono">@r.Sku</span>
                                <span class="res-stk @(r.OnHand <= 0 ? "out" : "")">stok @r.OnHand</span>
                                <span class="res-pr mono">
                                    @if (r.UnitPrice < r.Price)
                                    {
                                        <span class="was mono">@r.Price.ToString("N0")</span>
                                        @if (r.DiscountPercent is { } dp) { <span class="disc-badge">-@dp.ToString("0.##")%</span> }
                                    }
                                    <span>@r.UnitPrice.ToString("N0")</span>
                                </span>
                            </button>
```

- [ ] **Step 4: Baris keranjang tampil diskon** — di `.ci-sku` baris keranjang, tambah harga asli dicoret + badge. Ganti div `ci-sku`:

```razor
                                            <div class="ci-sku mono">
                                                @l.Sku · stok @l.OnHand
                                                @if (l.OrigPrice > l.UnitPrice)
                                                {
                                                    <span class="was mono">@l.OrigPrice.ToString("N0")</span>
                                                    @if (l.DiscPercent is { } dp) { <span class="disc-badge">-@dp.ToString("0.##")%</span> }
                                                }
                                            </div>
```

- [ ] **Step 5: CSS** — di `PosRegister.razor.css` tambahkan:

```css
.was { text-decoration:line-through; color:var(--muted); margin-right:6px; font-size:12px; }
.disc-badge { display:inline-block; background:var(--accent-soft); color:var(--accent-deep); font-weight:700;
              font-size:11px; padding:1px 7px; border-radius:99px; margin-right:6px; }
.res-pr .was { font-size:12px; }
```

- [ ] **Step 6: Build** `dotnet build` 0 warnings. Catat Task 6. (Verifikasi UI → Task 7 Step 4 user.)

---

### Task 7: Full verification

- [ ] **Step 1:** Dev app mati. `dotnet build` → 0 warnings.
- [ ] **Step 2:** `dotnet test` → hijau. Ekspektasi: 127+3+2=**132 unit**, 82+3=**85 integ**, total **217** (konfirmasi & catat).
- [ ] **Step 3: Invariants:** harga jual efektif tetap `DiscountPrice ?? Price` (POS `UnitPrice`); `DiscountPercent` hanya tampilan; migration hanya `AddColumn` nullable (baris lama aman).
- [ ] **Step 4: Manual UI walkthrough (user):**
  1. Master Produk → edit varian: isi **Discount %** → Discount Price terisi otomatis; isi Discount Price manual → % kosong; field harga tampil ribuan; simpan → buka lagi, nilai benar.
  2. Kasir POS → cari produk berdiskon: hasil & keranjang tampil harga asli dicoret + harga discount + badge % (bila %), atau harga discount saja (bila hanya discount price). Bayar → harga efektif terpakai.

## Self-Review (penulis)
- **Spec coverage:** Model→T1/T2; App DTO/validator→T3; service→T4; ProductForm→T5; POS display→T6; testing→per-task+T7. Import out-of-scope (teruskan null). ✓
- **Type consistency:** `discountPercent` param terakhir di ctor/`Update`/`AddVariant` (T1) dipakai konsisten T4. `VariantInput`/`ProductVariantDto`/`PosProductOptionDto` field baru dipakai konsisten T3→T4→T5/T6. Handler form set field yang sama yg dikirim `BuildVariantInputs`. ✓
- **Placeholder:** semua step berisi kode nyata; titik ambiguitas format (N0 whole-rupiah, digit-only parse) eksplisit. ✓
- **Build-order gotcha:** T3 memecah kompilasi penuh (pemakai DTO); sengaja ditandai — build penuh dipulihkan di T4. ✓
