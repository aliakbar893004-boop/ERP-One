# F1 — Product → Variant Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the flat `Product` into a parent (`Product`) + `ProductVariant` model where SKU, price, and stock live on the variant, supporting multi-variant products while staying seamless for simple (single-variant) products.

**Architecture:** `Product` becomes an aggregate root owning a `ProductVariant` collection (field-backed, like it already owns `ProductImage`). Each variant owns a `ProductVariantAttribute` join collection pointing at `AttributeValue` (F0 master). SKU base lives on `Product.Code` (auto per category); each variant's `Sku` = `Code` + attribute-code suffix. Existing data is migrated by a custom-SQL EF migration that creates one default variant per existing product. Stock stays a temporary `int` on the variant until F2.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core (SQL Server prod / SQLite in-memory test), FluentValidation, xUnit, Bootstrap + Bootstrap Icons.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-18-f1-product-variant-design.md`. F0 is complete and deployed (Brand/Warehouse/Tax/PaymentMethod/Attribute+AttributeValue exist).
- Rich domain entities inherit `AuditableEntity`; `Id`/fields `private set`; factory ctor + `Update()`; validation in entity (`SetXxx`); field-backed collections exposed as `IReadOnlyList<T>` with `SetPropertyAccessMode(PropertyAccessMode.Field)` in DbContext (mirror existing `Product.Images`).
- `Product` LOSES `Sku, Price, DiscountPrice, Stock, Weight, Dimensions`; KEEPS `Name, Description, CategoryId, Status, Images`; GAINS `Code` (base SKU, auto, locked, unique), `BrandId?`, `BaseUnitId?`, `TaxId?` (all FK `OnDelete SetNull`), and `Variants` (≥1, cascade).
- `Product.Code` = `"{CategoryCode}/{seq:0000}"` (seq = max existing + 1 per category code), locked after creation. `Variant.Sku` = `Code` + suffix; suffix = for each chosen attribute value ordered by attribute name, `"-" + AttributeValue.Code`. No real variant attributes → `Sku = Code`.
- `ProductVariant`: `Sku` (locked, unique index), `Barcode?` (max 50), `Price` (≥0), `DiscountPrice?` (≥0, ≤ Price), `CostPrice` (≥0), `Weight?` (≥0), `Dimensions?` (max 100), `Stock` (int ≥0, TEMPORARY — removed in F2), `IsActive`.
- Every product has ≥1 variant. Each attribute-value combination must be unique within a product.
- Import remains 1-default-variant per row. Images stay at product level.
- DTO/request = records. Validators auto-scanned via `AddValidatorsFromAssemblyContaining<CreateProductValidator>()`.
- Repo is NOT git: skip all `git` steps; checkpoint = `dotnet build MyApp.slnx` + the task's `dotnet test ... --filter`. The CONTROLLER runs build/test/EF commands (implementer subagents lack shell permission) — implementers write files + the failing-test file, then report.
- Build: `dotnet build MyApp.slnx`. Test project: `tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj` (integration, SQLite in-memory via `CustomWebApplicationFactory`, `EnsureCreated()`), `tests/MyApp.UnitTests/MyApp.UnitTests.csproj` (domain).
- EF migration: `dotnet ef migrations add <Name> --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`.
- ⚠️ Task order is fixed and sequential — later tasks depend on earlier types.

---

## Task 1: Domain entities (Product restructure + ProductVariant + ProductVariantAttribute)

**Files:**
- Modify: `src/MyApp.Domain/Entities/Product.cs`
- Create: `src/MyApp.Domain/Entities/ProductVariant.cs`
- Create: `src/MyApp.Domain/Entities/ProductVariantAttribute.cs`
- Test: `tests/MyApp.UnitTests/ProductTests.cs` (rewrite)

**Interfaces produced (later tasks rely on these exact signatures):**
- `ProductVariantAttribute`: `public int Id`, `public int ProductVariantId`, `public int AttributeValueId`; ctor `ProductVariantAttribute(int attributeValueId)`.
- `ProductVariant`: ctor `ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice, decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)`; `void Update(string? barcode, decimal price, decimal? discountPrice, decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)`; `void SetAttributeValues(IEnumerable<int> attributeValueIds)`; `IReadOnlyList<ProductVariantAttribute> Attributes`; props `Id, ProductId, Sku, Barcode, Price, DiscountPrice, CostPrice, Weight, Dimensions, Stock, IsActive`.
- `Product`: ctor `Product(string code, string name, string? description, int? categoryId, int? brandId, int? baseUnitId, int? taxId, ProductStatus status)`; `void Update(string name, string? description, int? categoryId, int? brandId, int? baseUnitId, int? taxId, ProductStatus status)` (Code locked); `ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice, decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)`; `void RemoveVariant(int variantId)`; `void ClearVariants()`; `IReadOnlyList<ProductVariant> Variants`; props `Id, Code, Name, Description, CategoryId, Category, BrandId, BaseUnitId, TaxId, Status`; keeps all image members (`Images`, `MaxImages`, `AddImage`, `RemoveImage`, `SetPrimaryImage`, `PrimaryImage`, `RemainingImageSlots`, `CanAddImages`).

- [ ] **Step 1: Rewrite the failing unit test** — `tests/MyApp.UnitTests/ProductTests.cs`:

```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class ProductTests
{
    private static Product NewProduct() =>
        new("ELK/0001", "Keyboard", "Mechanical", categoryId: 3, brandId: null, baseUnitId: null, taxId: null, ProductStatus.Aktif);

    [Fact]
    public void Create_SetsParentFields_AndCodeLocked()
    {
        var p = NewProduct();
        Assert.Equal("ELK/0001", p.Code);
        Assert.Equal("Keyboard", p.Name);
        Assert.Equal(ProductStatus.Aktif, p.Status);
        Assert.Empty(p.Variants);
    }

    [Fact]
    public void AddVariant_AppendsVariantWithGivenSku()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001", null, 250_000m, null, 0m, null, null, 5, true);
        Assert.Single(p.Variants);
        Assert.Equal("ELK/0001", v.Sku);
        Assert.Equal(250_000m, v.Price);
        Assert.Equal(5, v.Stock);
        Assert.True(v.IsActive);
    }

    [Fact]
    public void Update_ChangesParentFields_ButNotCode()
    {
        var p = NewProduct();
        p.Update("Keyboard Pro", "RGB", categoryId: 3, brandId: 1, baseUnitId: 2, taxId: 1, ProductStatus.Nonaktif);
        Assert.Equal("ELK/0001", p.Code); // locked
        Assert.Equal("Keyboard Pro", p.Name);
        Assert.Equal(1, p.BrandId);
        Assert.Equal(ProductStatus.Nonaktif, p.Status);
    }

    [Fact]
    public void Variant_RejectsNegativePrice_AndDiscountAbovePrice()
    {
        var p = NewProduct();
        Assert.Throws<ArgumentException>(() => p.AddVariant("X", null, -1m, null, 0m, null, null, 0, true));
        Assert.Throws<ArgumentException>(() => p.AddVariant("X", null, 100m, 200m, 0m, null, null, 0, true));
    }

    [Fact]
    public void Variant_SetAttributeValues_ReplacesLinks()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001-M", null, 100m, null, 0m, null, null, 0, true);
        v.SetAttributeValues(new[] { 10, 20 });
        Assert.Equal(2, v.Attributes.Count);
        v.SetAttributeValues(new[] { 30 });
        Assert.Single(v.Attributes);
        Assert.Equal(30, v.Attributes[0].AttributeValueId);
    }

    [Fact]
    public void RemoveVariant_RemovesById()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001", null, 1m, null, 0m, null, null, 0, true);
        // Id is 0 until persisted; RemoveVariant(0) removes the unsaved one
        p.RemoveVariant(v.Id);
        Assert.Empty(p.Variants);
    }
}
```

- [ ] **Step 2: Run unit tests to verify failure**

Run (controller): `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --nologo`
Expected: FAIL — compile errors (new `Product` ctor signature, `AddVariant`, `ProductVariant` not defined).

- [ ] **Step 3: Create `ProductVariantAttribute.cs`**

```csharp
namespace MyApp.Domain.Entities;

/// <summary>Kaitan varian ke satu nilai atribut (mis. Ukuran=M).</summary>
public class ProductVariantAttribute
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int AttributeValueId { get; private set; }

    private ProductVariantAttribute() { } // EF Core

    public ProductVariantAttribute(int attributeValueId)
    {
        AttributeValueId = attributeValueId;
    }
}
```

- [ ] **Step 4: Create `ProductVariant.cs`**

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Varian produk: unit jual nyata dengan SKU, harga, dan stok sendiri.</summary>
public class ProductVariant : AuditableEntity
{
    private readonly List<ProductVariantAttribute> _attributes = new();

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string Sku { get; private set; } = default!;     // dikunci setelah dibuat
    public string? Barcode { get; private set; }
    public decimal Price { get; private set; }
    public decimal? DiscountPrice { get; private set; }
    public decimal CostPrice { get; private set; }          // HPP (Moving Average di F2)
    public decimal? Weight { get; private set; }
    public string? Dimensions { get; private set; }
    public int Stock { get; private set; }                  // SEMENTARA — dihapus di F2
    public bool IsActive { get; private set; }

    public IReadOnlyList<ProductVariantAttribute> Attributes => _attributes;

    private ProductVariant() { } // EF Core

    public ProductVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)
    {
        SetSku(sku);
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        SetStock(stock);
        IsActive = isActive;
    }

    /// <summary>Perbarui; SKU sengaja tidak diubah (dikunci).</summary>
    public void Update(string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)
    {
        Barcode = Trim(barcode);
        SetPrice(price);
        SetDiscountPrice(discountPrice, price);
        SetCostPrice(costPrice);
        SetWeight(weight);
        Dimensions = Trim(dimensions);
        SetStock(stock);
        IsActive = isActive;
    }

    public void SetAttributeValues(IEnumerable<int> attributeValueIds)
    {
        _attributes.Clear();
        foreach (var id in attributeValueIds.Distinct())
            _attributes.Add(new ProductVariantAttribute(id));
    }

    private void SetSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        Sku = sku.Trim();
    }
    private void SetPrice(decimal price)
    {
        if (price < 0) throw new ArgumentException("Price must be >= 0.", nameof(price));
        Price = price;
    }
    private void SetDiscountPrice(decimal? discountPrice, decimal price)
    {
        if (discountPrice is < 0) throw new ArgumentException("Discount price must be >= 0.", nameof(discountPrice));
        if (discountPrice.HasValue && discountPrice.Value > price)
            throw new ArgumentException("Discount price must not exceed the selling price.", nameof(discountPrice));
        DiscountPrice = discountPrice;
    }
    private void SetCostPrice(decimal costPrice)
    {
        if (costPrice < 0) throw new ArgumentException("Cost price must be >= 0.", nameof(costPrice));
        CostPrice = costPrice;
    }
    private void SetWeight(decimal? weight)
    {
        if (weight is < 0) throw new ArgumentException("Weight must be >= 0.", nameof(weight));
        Weight = weight;
    }
    private void SetStock(int stock)
    {
        if (stock < 0) throw new ArgumentException("Stock must be >= 0.", nameof(stock));
        Stock = stock;
    }
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

- [ ] **Step 5: Rewrite `Product.cs`** (keep all image logic; replace flat fields with parent fields + variants)

```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

public class Product : AuditableEntity
{
    /// <summary>Batas jumlah gambar per produk.</summary>
    public const int MaxImages = 5;

    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariant> _variants = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;        // base SKU, dikunci
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int? CategoryId { get; private set; }
    public ProductCategory? Category { get; private set; }
    public int? BrandId { get; private set; }
    public int? BaseUnitId { get; private set; }
    public int? TaxId { get; private set; }
    public ProductStatus Status { get; private set; }

    public IReadOnlyList<ProductImage> Images => _images;
    public IReadOnlyList<ProductVariant> Variants => _variants;

    public ProductImage? PrimaryImage =>
        _images.FirstOrDefault(i => i.IsPrimary) ?? _images.OrderBy(i => i.SortOrder).FirstOrDefault();

    private Product() { } // EF Core

    public Product(string code, string name, string? description, int? categoryId,
        int? brandId, int? baseUnitId, int? taxId, ProductStatus status)
    {
        SetCode(code);
        SetName(name);
        SetDescription(description);
        CategoryId = categoryId;
        BrandId = brandId;
        BaseUnitId = baseUnitId;
        TaxId = taxId;
        Status = status;
    }

    /// <summary>Perbarui data induk; Code sengaja tidak diubah (dikunci sejak pembuatan).</summary>
    public void Update(string name, string? description, int? categoryId,
        int? brandId, int? baseUnitId, int? taxId, ProductStatus status)
    {
        SetName(name);
        SetDescription(description);
        CategoryId = categoryId;
        BrandId = brandId;
        BaseUnitId = baseUnitId;
        TaxId = taxId;
        Status = status;
    }

    public ProductVariant AddVariant(string sku, string? barcode, decimal price, decimal? discountPrice,
        decimal costPrice, decimal? weight, string? dimensions, int stock, bool isActive)
    {
        var v = new ProductVariant(sku, barcode, price, discountPrice, costPrice, weight, dimensions, stock, isActive);
        _variants.Add(v);
        return v;
    }

    public void RemoveVariant(int variantId)
    {
        var v = _variants.FirstOrDefault(x => x.Id == variantId);
        if (v is not null) _variants.Remove(v);
    }

    public void ClearVariants() => _variants.Clear();

    // ── Gambar (tidak berubah dari sebelumnya) ──────────────────────────────
    public int RemainingImageSlots => Math.Max(0, MaxImages - _images.Count);
    public bool CanAddImages(int count) => count >= 0 && _images.Count + count <= MaxImages;

    public ProductImage AddImage(string storedPath, string originalFileName, string contentType, long fileSize)
    {
        if (_images.Count >= MaxImages)
            throw new InvalidOperationException($"Maksimal {MaxImages} gambar per produk.");
        var order = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        var image = new ProductImage(storedPath, originalFileName, contentType, fileSize, order);
        _images.Add(image);
        if (_images.Count == 1) image.SetPrimary(true);
        return image;
    }

    public ProductImage? RemoveImage(int imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return null;
        _images.Remove(image);
        if (image.IsPrimary)
            _images.OrderBy(i => i.SortOrder).FirstOrDefault()?.SetPrimary(true);
        return image;
    }

    public bool SetPrimaryImage(int imageId)
    {
        if (_images.All(i => i.Id != imageId)) return false;
        foreach (var i in _images) i.SetPrimary(i.Id == imageId);
        return true;
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
    private void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
```

- [ ] **Step 6: Run unit tests to verify pass**

Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --nologo`
Expected: PASS (6 tests). NOTE: the rest of the solution will NOT compile yet (ProductService/DTOs reference removed members) — that is expected and fixed in Task 3. The UnitTests project only references Domain, so it builds & passes independently.

> Do NOT run a full `dotnet build MyApp.slnx` as the checkpoint for Task 1 — it will fail until Task 3. The Task 1 checkpoint is the UnitTests project passing.

---

## Task 2: DbContext config + data migration

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/*_RefactorProductToVariant.cs` (generated, then hand-edit Up/Down SQL)
- Test: `tests/MyApp.IntegrationTests/ProductVariantMigrationTests.cs`

**Interfaces consumed:** Task 1 entities.
**Interfaces produced:** `db.ProductVariants`, `db.ProductVariantAttributes` DbSets; `Product` config with `Code` unique, FKs to Brand/Unit/Tax (SetNull), `Variants` cascade + field access; `ProductVariant` config with `Sku` unique, `Stock` etc.; `ProductVariantAttribute` config with FK to `AttributeValue` (Restrict).

- [ ] **Step 1: Update `AppDbContext.cs` — DbSets**

Add after `public DbSet<Product> Products`:
```csharp
public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
```

- [ ] **Step 2: Update `AppDbContext.cs` — replace the `modelBuilder.Entity<Product>` block**

Replace the existing Product config block with:
```csharp
modelBuilder.Entity<Product>(e =>
{
    e.HasKey(p => p.Id);
    e.Property(p => p.Code).HasMaxLength(50).IsRequired();
    e.HasIndex(p => p.Code).IsUnique();
    e.Property(p => p.Name).HasMaxLength(200).IsRequired();
    e.Property(p => p.Description);
    e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

    e.HasOne(p => p.Category)
        .WithMany()
        .HasForeignKey(p => p.CategoryId)
        .OnDelete(DeleteBehavior.SetNull);

    // FK ke master F0 (tanpa navigation property — pakai shadow FK)
    e.HasOne<Brand>().WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.SetNull);
    e.HasOne<Unit>().WithMany().HasForeignKey(p => p.BaseUnitId).OnDelete(DeleteBehavior.SetNull);
    e.HasOne<Tax>().WithMany().HasForeignKey(p => p.TaxId).OnDelete(DeleteBehavior.SetNull);

    e.HasMany(p => p.Images)
        .WithOne()
        .HasForeignKey(i => i.ProductId)
        .OnDelete(DeleteBehavior.Cascade);
    e.Metadata.FindNavigation(nameof(Product.Images))!
        .SetPropertyAccessMode(PropertyAccessMode.Field);

    e.HasMany(p => p.Variants)
        .WithOne()
        .HasForeignKey(v => v.ProductId)
        .OnDelete(DeleteBehavior.Cascade);
    e.Metadata.FindNavigation(nameof(Product.Variants))!
        .SetPropertyAccessMode(PropertyAccessMode.Field);
});

modelBuilder.Entity<ProductVariant>(e =>
{
    e.HasKey(v => v.Id);
    e.Property(v => v.Sku).HasMaxLength(60).IsRequired();
    e.HasIndex(v => v.Sku).IsUnique();
    e.Property(v => v.Barcode).HasMaxLength(50);
    e.Property(v => v.Price).HasPrecision(18, 2);
    e.Property(v => v.DiscountPrice).HasPrecision(18, 2);
    e.Property(v => v.CostPrice).HasPrecision(18, 2);
    e.Property(v => v.Weight).HasPrecision(18, 3);
    e.Property(v => v.Dimensions).HasMaxLength(100);

    e.HasMany(v => v.Attributes)
        .WithOne()
        .HasForeignKey(a => a.ProductVariantId)
        .OnDelete(DeleteBehavior.Cascade);
    e.Metadata.FindNavigation(nameof(ProductVariant.Attributes))!
        .SetPropertyAccessMode(PropertyAccessMode.Field);
});

modelBuilder.Entity<ProductVariantAttribute>(e =>
{
    e.HasKey(a => a.Id);
    e.HasOne<AttributeValue>().WithMany()
        .HasForeignKey(a => a.AttributeValueId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

- [ ] **Step 3: Write the failing migration test** — `tests/MyApp.IntegrationTests/ProductVariantMigrationTests.cs`

Because integration tests use `EnsureCreated()` (no migration run), this test validates the END-STATE schema + the service-level backfill expectation rather than raw migration SQL. It asserts a product created via the new model persists a variant correctly through the schema:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public class ProductVariantMigrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ProductVariantMigrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Schema_PersistsProductWithVariantAndAttributeLinks()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new ProductCategory("MIG", "Migration Cat", null);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product("MIG/0001", "Shirt", null, category.Id, null, null, null, ProductStatus.Aktif);
        var variant = product.AddVariant("MIG/0001-M", null, 100m, null, 0m, null, null, 7, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var loaded = await db.Products
            .Include(p => p.Variants).ThenInclude(v => v.Attributes)
            .AsNoTracking()
            .FirstAsync(p => p.Id == product.Id);

        Assert.Single(loaded.Variants);
        Assert.Equal("MIG/0001-M", loaded.Variants[0].Sku);
        Assert.Equal(7, loaded.Variants[0].Stock);
        Assert.Equal("MIG/0001", loaded.Code);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~ProductVariantMigrationTests" --nologo`
Expected: FAIL — solution does not compile yet (ProductService still references removed members). This stays red until Task 3; note it and proceed. (The migration itself is generated in Step 5; it is verified by the controller against SQL Server in Task 3's checkpoint and via `ef migrations` inspection.)

- [ ] **Step 5: Generate the migration, then hand-edit Up()/Down() for data backfill**

Controller runs:
```
dotnet ef migrations add RefactorProductToVariant --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```
The auto-generated migration will create the new tables/columns and DROP the old Product columns — but EF generates the drops WITHOUT preserving data. Hand-edit `Up()` so the order is: (1) create `ProductVariants` + `ProductVariantAttributes` tables and add `Code/BrandId/BaseUnitId/TaxId` columns to `Products` (keep EF's generated `CreateTable`/`AddColumn` calls); (2) INSERT a default variant per product and backfill `Code` BEFORE the column drops; (3) then the column drops + indexes. Insert this raw SQL block immediately BEFORE the `DropColumn` calls for the old Product fields:

```csharp
// Backfill: 1 varian default per produk (salin SKU/harga/stok lama), lalu isi Products.Code.
migrationBuilder.Sql(@"
    INSERT INTO ProductVariants (ProductId, Sku, Barcode, Price, DiscountPrice, CostPrice, Weight, Dimensions, Stock, IsActive, CreatedAt, CreatedBy)
    SELECT Id, Sku, NULL, Price, DiscountPrice, 0, Weight, Dimensions, Stock, 1, SYSUTCDATETIME(), 'migration'
    FROM Products;");
migrationBuilder.Sql("UPDATE Products SET Code = Sku;");
```

(If `Products.Code` was added as non-nullable, EF will have given it a default; the `UPDATE` overwrites it. If the migration fails because `Code` is required and added before backfill, change the generated `AddColumn<string>("Code", ...)` to `nullable: true`, run the two SQL statements, then leave it — the unique index still applies. Document this in the report.)

For `Down()`, before dropping `ProductVariants`, restore old columns from the first variant per product:
```csharp
migrationBuilder.Sql(@"
    UPDATE p SET p.Sku = v.Sku, p.Price = v.Price, p.DiscountPrice = v.DiscountPrice,
                 p.Stock = v.Stock, p.Weight = v.Weight, p.Dimensions = v.Dimensions
    FROM Products p
    CROSS APPLY (SELECT TOP 1 * FROM ProductVariants pv WHERE pv.ProductId = p.Id ORDER BY pv.Id) v;");
```
(Place this AFTER the generated `AddColumn` calls re-add the old columns and BEFORE `DropTable(ProductVariants)`. Multi-variant products only restore their first variant — documented limitation.)

- [ ] **Step 6: Controller verifies migration compiles & is well-formed**

Run: `dotnet build MyApp.slnx` — expected: FAIL only with ProductService/DTO errors (Task 3), NOT migration syntax errors. Inspect the generated migration file: confirm CreateTable for `ProductVariants` + `ProductVariantAttributes`, AddColumn `Code/BrandId/BaseUnitId/TaxId`, the two backfill `Sql()` blocks present BEFORE the DropColumns, and DropColumn for `Sku/Price/DiscountPrice/Stock/Weight/Dimensions`. Do NOT apply to DB yet (Task 3 checkpoint applies after the app compiles).

---

## Task 3: DTOs, validator, ProductService rework

**Files:**
- Modify: `src/MyApp.Application/Products/ProductDtos.cs`
- Modify: `src/MyApp.Application/Products/CreateProductValidator.cs`
- Modify: `src/MyApp.Application/Products/IProductService.cs`
- Modify: `src/MyApp.Application/Products/ProductImportDtos.cs` (only if it references removed fields — likely unchanged)
- Modify: `src/MyApp.Infrastructure/Services/ProductService.cs`
- Modify: `tests/MyApp.IntegrationTests/IdentityAndServiceTests.cs` (update Product calls to new request shape)
- Modify: `tests/MyApp.IntegrationTests/ProductApiTests.cs` (update to new shape if it constructs requests)
- Test: `tests/MyApp.IntegrationTests/ProductVariantServiceTests.cs` (new)

**Interfaces consumed:** Task 1 entities, Task 2 DbSets.
**Interfaces produced:** the DTO/record shapes below; `IProductService` unchanged method names but new request/DTO types.

- [ ] **Step 1: Rewrite `ProductDtos.cs`**

```csharp
using MyApp.Domain.Entities;

namespace MyApp.Application.Products;

public record ProductImageDto(int Id, string Url, string OriginalFileName, long FileSize, int SortOrder, bool IsPrimary);

public record AttributeValueRefDto(int AttributeValueId, string AttributeName, string ValueCode, string Value);

public record ProductVariantDto(
    int Id, string Sku, string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int Stock, bool IsActive,
    IReadOnlyList<AttributeValueRefDto> Attributes);

public record ProductDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int? CategoryId,
    string? CategoryName,
    int? BrandId,
    string? BrandName,
    int? BaseUnitId,
    string? BaseUnitName,
    int? TaxId,
    string? TaxName,
    ProductStatus Status,
    string? PrimaryImageUrl,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductVariantDto> Variants,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalStock,
    int VariantCount,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    string? CreatedBy);

// SKU/Code di-generate otomatis; tidak di input.
public record VariantInput(
    string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice,
    decimal? Weight, string? Dimensions, int Stock, bool IsActive,
    IReadOnlyList<int> AttributeValueIds);

public record CreateProductRequest(
    string Name, string? Description, int CategoryId,
    int? BrandId, int? BaseUnitId, int? TaxId, ProductStatus Status,
    IReadOnlyList<VariantInput> Variants);

public record UpdateProductRequest(
    string Name, string? Description, int CategoryId,
    int? BrandId, int? BaseUnitId, int? TaxId, ProductStatus Status,
    IReadOnlyList<VariantInput> Variants);

public record ProductImageUpload(string OriginalFileName, string ContentType, byte[] Content);
```

- [ ] **Step 2: Rewrite `CreateProductValidator.cs`**

```csharp
using FluentValidation;

namespace MyApp.Application.Products;

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one variant is required.");
        RuleForEach(x => x.Variants).SetValidator(new VariantInputValidator());
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one variant is required.");
        RuleForEach(x => x.Variants).SetValidator(new VariantInputValidator());
    }
}

public class VariantInputValidator : AbstractValidator<VariantInput>
{
    public VariantInputValidator()
    {
        RuleFor(v => v.Price).GreaterThanOrEqualTo(0);
        RuleFor(v => v.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(v => v.DiscountPrice).GreaterThanOrEqualTo(0).When(v => v.DiscountPrice.HasValue);
        RuleFor(v => v.DiscountPrice).LessThanOrEqualTo(v => v.Price)
            .When(v => v.DiscountPrice.HasValue)
            .WithMessage("Discount price must not exceed the selling price.");
        RuleFor(v => v.Stock).GreaterThanOrEqualTo(0);
        RuleFor(v => v.Weight).GreaterThanOrEqualTo(0).When(v => v.Weight.HasValue);
        RuleFor(v => v.Dimensions).MaximumLength(100);
        RuleFor(v => v.Barcode).MaximumLength(50);
    }
}
```

- [ ] **Step 3: `IProductService.cs`** — unchanged method signatures (the request/DTO types changed underneath). Verify it still reads:
```csharp
Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
Task<bool> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default);
// ... GetAll/GetPaged/GetById/Delete/AddImages/DeleteImage/SetPrimaryImage/Import/GetDashboard unchanged
```
No edit needed unless it referenced removed DTO members.

- [ ] **Step 4: Write the failing service test** — `tests/MyApp.IntegrationTests/ProductVariantServiceTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Attributes;
using MyApp.Application.ProductCategories;
using MyApp.Application.Products;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.IntegrationTests;

public class ProductVariantServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ProductVariantServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_SingleVariant_GeneratesCodeAndSku()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("ELK", "Electronics", null))).Id;
        var dto = await products.CreateAsync(new CreateProductRequest(
            "Keyboard", null, catId, null, null, null, ProductStatus.Aktif,
            new[] { new VariantInput(null, 250_000m, null, 100_000m, null, null, 5, true, Array.Empty<int>()) }));

        Assert.Equal("ELK/0001", dto.Code);
        Assert.Single(dto.Variants);
        Assert.Equal("ELK/0001", dto.Variants[0].Sku); // no attributes -> sku == code
        Assert.Equal(5, dto.TotalStock);
        Assert.Equal(250_000m, dto.MinPrice);
    }

    [Fact]
    public async Task Create_MultiVariant_BuildsSuffixedSkus()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var attrs = scope.ServiceProvider.GetRequiredService<IAttributeService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("APP", "Apparel", null))).Id;
        var size = await attrs.CreateAsync(new CreateAttributeRequest("SIZE", "Size",
            new[] { new AttributeValueInput("M", "Medium"), new AttributeValueInput("L", "Large") }));
        var mId = size.Values.First(v => v.Code == "M").Id;
        var lId = size.Values.First(v => v.Code == "L").Id;

        var dto = await products.CreateAsync(new CreateProductRequest(
            "Tshirt", null, catId, null, null, null, ProductStatus.Aktif,
            new[]
            {
                new VariantInput(null, 50_000m, null, 0m, null, null, 3, true, new[] { mId }),
                new VariantInput(null, 50_000m, null, 0m, null, null, 4, true, new[] { lId }),
            }));

        Assert.Equal(2, dto.VariantCount);
        Assert.Contains(dto.Variants, v => v.Sku == "APP/0001-M");
        Assert.Contains(dto.Variants, v => v.Sku == "APP/0001-L");
        Assert.Equal(7, dto.TotalStock);
    }

    [Fact]
    public async Task Create_RequiresAtLeastOneVariant()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("EMP", "Empty", null))).Id;

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            products.CreateAsync(new CreateProductRequest(
                "NoVariants", null, catId, null, null, null, ProductStatus.Aktif, Array.Empty<VariantInput>())));
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~ProductVariantServiceTests" --nologo`
Expected: FAIL — `ProductService` not yet updated (compile errors).

- [ ] **Step 6: Rewrite `ProductService.cs`**

Full replacement. Key logic: SKU suffix built from chosen attribute values (loaded from `db.AttributeValues` joined to `ProductAttributes` for ordering by attribute name). Complete file:

```csharp
using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.Products;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class ProductService(
    AppDbContext db,
    IFileStorage fileStorage,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : IProductService
{
    private const string ImageFolder = "uploads/products";
    private const int LowStockThreshold = 5;

    private IQueryable<Product> ProductGraph() =>
        db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Attributes);

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await ProductGraph().AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return await ToDtosAsync(items, ct);
    }

    public async Task<PagedResult<ProductDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Code.Contains(search)
                || p.Variants.Any(v => v.Sku.Contains(search)));

        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => p.Id).ToListAsync(ct);

        var items = await ProductGraph().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        items = items.OrderBy(p => p.Name).ToList();
        var dtos = await ToDtosAsync(items, ct);
        return new PagedResult<ProductDto>(dtos, total, page, pageSize);
    }

    public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var p = await ProductGraph().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        return (await ToDtosAsync(new[] { p }, ct))[0];
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new ValidationException([new ValidationFailure(nameof(CreateProductRequest.CategoryId), "Category not found.")]);

        var code = await GenerateCodeAsync(category, ct);
        var valueLabels = await LoadValueSuffixMapAsync(request.Variants.SelectMany(v => v.AttributeValueIds), ct);

        var product = new Product(code, request.Name, request.Description, category.Id,
            request.BrandId, request.BaseUnitId, request.TaxId, request.Status);

        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var sku = BuildSku(code, v.AttributeValueIds, valueLabels);
            if (!usedSkus.Add(sku))
                throw new ValidationException([new ValidationFailure(nameof(CreateProductRequest.Variants),
                    $"Duplicate variant combination produces SKU '{sku}'.")]);
            var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                v.Weight, v.Dimensions, v.Stock, v.IsActive);
            variant.SetAttributeValues(v.AttributeValueIds);
        }

        await EnsureSkusUniqueInDbAsync(usedSkus, ct);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(product.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var product = await db.Products
            .Include(p => p.Variants).ThenInclude(v => v.Attributes)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return false;

        var categoryExists = await db.ProductCategories.AnyAsync(c => c.Id == request.CategoryId, ct);
        if (!categoryExists)
            throw new ValidationException([new ValidationFailure(nameof(UpdateProductRequest.CategoryId), "Category not found.")]);

        product.Update(request.Name, request.Description, request.CategoryId,
            request.BrandId, request.BaseUnitId, request.TaxId, request.Status);

        // Strategi sederhana & aman: ganti penuh daftar varian. Code dikunci; SKU di-generate ulang dari Code.
        var valueLabels = await LoadValueSuffixMapAsync(request.Variants.SelectMany(v => v.AttributeValueIds), ct);
        product.ClearVariants();
        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var sku = BuildSku(product.Code, v.AttributeValueIds, valueLabels);
            if (!usedSkus.Add(sku))
                throw new ValidationException([new ValidationFailure(nameof(UpdateProductRequest.Variants),
                    $"Duplicate variant combination produces SKU '{sku}'.")]);
            var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                v.Weight, v.Dimensions, v.Stock, v.IsActive);
            variant.SetAttributeValues(v.AttributeValueIds);
        }
        await EnsureSkusUniqueInDbAsync(usedSkus, ct, excludeProductId: id);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return false;
        var paths = product.Images.Select(i => i.StoredPath).ToList();
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        foreach (var path in paths) fileStorage.Delete(path);
        return true;
    }

    // ── Image ops (unchanged logic; operate on product.Images) ──────────────
    public async Task<ProductDto?> AddImagesAsync(int productId, IReadOnlyList<ProductImageUpload> uploads, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return null;
        if (uploads.Count == 0) return await GetByIdAsync(productId, ct);
        if (!product.CanAddImages(uploads.Count))
            throw new InvalidOperationException($"Maksimal {Product.MaxImages} gambar per produk (sisa {product.RemainingImageSlots}).");

        var savedPaths = new List<string>();
        try
        {
            foreach (var up in uploads)
            {
                using var ms = new MemoryStream(up.Content);
                var stored = await fileStorage.SaveAsync(ms, up.OriginalFileName, ImageFolder, ct);
                savedPaths.Add(stored.RelativePath);
                product.AddImage(stored.RelativePath, up.OriginalFileName, up.ContentType, stored.Size);
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var path in savedPaths) fileStorage.Delete(path);
            throw;
        }
        return await GetByIdAsync(productId, ct);
    }

    public async Task<bool> DeleteImageAsync(int productId, int imageId, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return false;
        var image = product.RemoveImage(imageId);
        if (image is null) return false;
        await db.SaveChangesAsync(ct);
        fileStorage.Delete(image.StoredPath);
        return true;
    }

    public async Task<bool> SetPrimaryImageAsync(int productId, int imageId, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return false;
        if (!product.SetPrimaryImage(imageId)) return false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Code / SKU generation ───────────────────────────────────────────────
    private async Task<string> GenerateCodeAsync(ProductCategory category, CancellationToken ct) =>
        $"{category.Code}/{await NextCodeSeqAsync(category.Code, ct):0000}";

    private async Task<int> NextCodeSeqAsync(string code, CancellationToken ct)
    {
        var prefix = code + "/";
        var existing = await db.Products.AsNoTracking()
            .Where(p => p.Code.StartsWith(prefix)).Select(p => p.Code).ToListAsync(ct);
        var max = 0;
        foreach (var c in existing)
        {
            var slash = c.LastIndexOf('/');
            if (slash >= 0 && int.TryParse(c[(slash + 1)..], out var n)) max = Math.Max(max, n);
        }
        return max + 1;
    }

    /// <summary>Map AttributeValueId -> (attributeName, valueCode) untuk membangun sufiks SKU urut nama atribut.</summary>
    private async Task<Dictionary<int, (string AttrName, string Code)>> LoadValueSuffixMapAsync(
        IEnumerable<int> valueIds, CancellationToken ct)
    {
        var ids = valueIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.AttributeValues.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Join(db.ProductAttributes.AsNoTracking(), v => v.AttributeId, a => a.Id,
                (v, a) => new { v.Id, AttrName = a.Name, v.Code })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.AttrName, r.Code));
    }

    private static string BuildSku(string code, IReadOnlyList<int> valueIds,
        Dictionary<int, (string AttrName, string Code)> map)
    {
        if (valueIds.Count == 0) return code;
        var parts = valueIds
            .Where(map.ContainsKey)
            .Select(id => map[id])
            .OrderBy(x => x.AttrName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Code);
        var suffix = string.Join("-", parts);
        return string.IsNullOrEmpty(suffix) ? code : $"{code}-{suffix}";
    }

    private async Task EnsureSkusUniqueInDbAsync(IEnumerable<string> skus, CancellationToken ct, int? excludeProductId = null)
    {
        var list = skus.ToList();
        var clash = await db.ProductVariants.AsNoTracking()
            .Where(v => list.Contains(v.Sku) && (excludeProductId == null || v.ProductId != excludeProductId))
            .Select(v => v.Sku).FirstOrDefaultAsync(ct);
        if (clash is not null)
            throw new ValidationException([new ValidationFailure("Variants", $"SKU '{clash}' is already in use.")]);
    }

    // ── Import (1 default variant per row) ───────────────────────────────────
    public async Task<ProductImportResult> ImportAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken ct = default)
    {
        var errors = new List<ProductImportError>();
        var added = 0;
        var categories = await db.ProductCategories.AsNoTracking().ToListAsync(ct);
        var byCode = categories.ToDictionary(c => c.Code.ToUpperInvariant());
        var nextSeq = new Dictionary<int, int>();

        foreach (var row in rows)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.CategoryCode)) throw new InvalidOperationException("Category code is required.");
                if (!byCode.TryGetValue(row.CategoryCode.Trim().ToUpperInvariant(), out var category))
                    throw new InvalidOperationException($"Category code '{row.CategoryCode}' not found.");
                if (string.IsNullOrWhiteSpace(row.Name)) throw new InvalidOperationException("Name is required.");

                var price = ParseDecimal(row.Price, "Price", required: true)!.Value;
                var discount = ParseDecimal(row.DiscountPrice, "Discount price", required: false);
                var stock = ParseInt(row.Stock, "Stock") ?? 0;
                var weight = ParseDecimal(row.Weight, "Weight", required: false);
                var status = ParseStatus(row.Status);

                if (price < 0) throw new InvalidOperationException("Price must be >= 0.");
                if (discount is < 0) throw new InvalidOperationException("Discount price must be >= 0.");
                if (discount.HasValue && discount > price) throw new InvalidOperationException("Discount price must not exceed the selling price.");
                if (stock < 0) throw new InvalidOperationException("Stock must be >= 0.");
                if (weight is < 0) throw new InvalidOperationException("Weight must be >= 0.");

                if (!nextSeq.TryGetValue(category.Id, out var seq))
                    seq = await NextCodeSeqAsync(category.Code, ct);
                nextSeq[category.Id] = seq + 1;

                var code = $"{category.Code}/{seq:0000}";
                var product = new Product(code, row.Name!.Trim(), row.Description, category.Id, null, null, null, status);
                product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, stock, true);
                db.Products.Add(product);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add(new ProductImportError(row.RowNumber, ex.Message));
            }
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return new ProductImportResult(added, errors.Count, errors);
    }

    // ── Dashboard (aggregate from variants) ──────────────────────────────────
    public async Task<ProductDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var totalProducts = await db.Products.CountAsync(ct);
        var totalCategories = await db.ProductCategories.CountAsync(ct);
        var totalStock = await db.ProductVariants.SumAsync(v => (int?)v.Stock, ct) ?? 0;
        var inventoryValue = await db.ProductVariants.SumAsync(v => (decimal?)(v.Price * v.Stock), ct) ?? 0m;
        var activeCount = await db.Products.CountAsync(p => p.Status == ProductStatus.Aktif, ct);

        // Stok per produk = jumlah stok semua variannya.
        var stockByProduct = await db.ProductVariants
            .GroupBy(v => v.ProductId)
            .Select(g => new { ProductId = g.Key, Stock = g.Sum(x => x.Stock) })
            .ToListAsync(ct);
        var outOfStock = stockByProduct.Count(x => x.Stock == 0);
        var lowStock = stockByProduct.Count(x => x.Stock > 0 && x.Stock <= LowStockThreshold);

        var byStatus = await db.Products
            .GroupBy(p => p.Status).Select(g => new StatusCount(g.Key, g.Count())).ToListAsync(ct);

        var categoryNames = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var prodCat = await db.Products.Where(p => p.CategoryId != null)
            .Select(p => new { p.Id, CategoryId = p.CategoryId!.Value }).ToListAsync(ct);
        var stockMap = stockByProduct.ToDictionary(x => x.ProductId, x => x.Stock);
        var byCategory = prodCat
            .GroupBy(p => p.CategoryId)
            .Select(g => new CategoryStock(
                categoryNames.TryGetValue(g.Key, out var name) ? name : "—",
                g.Count(),
                g.Sum(p => stockMap.TryGetValue(p.Id, out var s) ? s : 0)))
            .OrderByDescending(x => x.TotalStock).ToList();

        var lowStockProductIds = stockByProduct
            .Where(x => x.Stock <= LowStockThreshold).OrderBy(x => x.Stock).Take(8).Select(x => x.ProductId).ToList();
        var lowStockProducts = await db.Products.AsNoTracking()
            .Where(p => lowStockProductIds.Contains(p.Id)).Include(p => p.Images).ToListAsync(ct);
        var lowStockItems = lowStockProducts
            .Select(p => new LowStockItem(p.Id, p.Code, p.Name,
                stockMap.TryGetValue(p.Id, out var s) ? s : 0, p.Status,
                p.PrimaryImage is { } img ? "/" + img.StoredPath : null))
            .OrderBy(i => i.Stock).ToList();

        return new ProductDashboardDto(totalProducts, totalCategories, totalStock, inventoryValue,
            activeCount, outOfStock, lowStock, byStatus, byCategory, lowStockItems);
    }

    // ── DTO mapping ──────────────────────────────────────────────────────────
    private async Task<IReadOnlyList<ProductDto>> ToDtosAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        // Resolve names for brand/unit/tax + attribute value labels in bulk.
        var brandIds = products.Where(p => p.BrandId != null).Select(p => p.BrandId!.Value).Distinct().ToList();
        var unitIds = products.Where(p => p.BaseUnitId != null).Select(p => p.BaseUnitId!.Value).Distinct().ToList();
        var taxIds = products.Where(p => p.TaxId != null).Select(p => p.TaxId!.Value).Distinct().ToList();
        var valueIds = products.SelectMany(p => p.Variants).SelectMany(v => v.Attributes)
            .Select(a => a.AttributeValueId).Distinct().ToList();

        var brands = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var taxes = await db.Taxes.AsNoTracking().Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var values = await db.AttributeValues.AsNoTracking().Where(v => valueIds.Contains(v.Id))
            .Join(db.ProductAttributes.AsNoTracking(), v => v.AttributeId, a => a.Id,
                (v, a) => new { v.Id, AttrName = a.Name, v.Code, v.Value })
            .ToDictionaryAsync(x => x.Id, x => (x.AttrName, x.Code, x.Value), ct);

        return products.Select(p => ToDto(p, brands, units, taxes, values)).ToList();
    }

    private static ProductDto ToDto(Product p,
        Dictionary<int, string> brands, Dictionary<int, string> units, Dictionary<int, string> taxes,
        Dictionary<int, (string AttrName, string Code, string Value)> values)
    {
        var images = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id, "/" + i.StoredPath, i.OriginalFileName, i.FileSize, i.SortOrder, i.IsPrimary))
            .ToList();
        var primary = p.PrimaryImage;

        var variants = p.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantDto(
            v.Id, v.Sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions, v.Stock, v.IsActive,
            v.Attributes.Where(a => values.ContainsKey(a.AttributeValueId))
                .Select(a => { var x = values[a.AttributeValueId]; return new AttributeValueRefDto(a.AttributeValueId, x.AttrName, x.Code, x.Value); })
                .ToList())).ToList();

        var prices = variants.Select(v => v.Price).DefaultIfEmpty(0m).ToList();

        return new ProductDto(
            p.Id, p.Code, p.Name, p.Description,
            p.CategoryId, p.Category?.Name,
            p.BrandId, p.BrandId is int b && brands.TryGetValue(b, out var bn) ? bn : null,
            p.BaseUnitId, p.BaseUnitId is int u && units.TryGetValue(u, out var un) ? un : null,
            p.TaxId, p.TaxId is int t && taxes.TryGetValue(t, out var tn) ? tn : null,
            p.Status,
            primary is null ? null : "/" + primary.StoredPath,
            images, variants,
            prices.Min(), prices.Max(),
            variants.Sum(v => v.Stock), variants.Count,
            p.CreatedAt, p.ModifiedAt, p.CreatedBy);
    }

    private static decimal? ParseDecimal(string? s, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(s)) { if (required) throw new InvalidOperationException($"{field} is required."); return null; }
        if (!decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{field} '{s}' is not a valid number.");
        return v;
    }
    private static int? ParseInt(string? s, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{field} '{s}' is not a valid whole number.");
        return v;
    }
    private static ProductStatus ParseStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ProductStatus.Aktif;
        if (Enum.TryParse<ProductStatus>(s.Trim(), ignoreCase: true, out var status)) return status;
        throw new InvalidOperationException($"Status '{s}' is invalid (use: Aktif, Nonaktif, Habis, Arsip).");
    }
}
```

- [ ] **Step 7: Fix existing integration tests that build old Product requests**

In `tests/MyApp.IntegrationTests/IdentityAndServiceTests.cs`, the `Products_GetPaged_*` and `Products_Dashboard_*` tests call `new CreateProductRequest(name, desc, categoryId, price, discount, stock, weight, dims, status)`. Update each call to the new shape with a single default variant. Example replacement for the paged test loop:
```csharp
for (var i = 1; i <= 15; i++)
    await products.CreateAsync(new CreateProductRequest(
        $"Item {i}", null, categoryId, null, null, null, ProductStatus.Aktif,
        new[] { new VariantInput(null, i, null, 0m, null, null, i, true, Array.Empty<int>()) }));
```
And the dashboard test's three creates similarly (e.g. `new VariantInput(null, 100m, null, 0m, null, null, 50, true, [])`). The dashboard assertions (`TotalStock >= 53`, `InventoryValue >= ...`, `OutOfStockCount >= 1`, `LowStockCount >= 1`, `ByCategory` contains "Dashboard", `LowStock` contains "No stock") remain valid because the service now aggregates from variants. Add `using MyApp.Application.Products;` if missing.

Inspect `tests/MyApp.IntegrationTests/ProductApiTests.cs` and update any `CreateProductRequest`/`ProductDto.Sku`/`.Stock`/`.Price` usage to the new shape (`ProductDto.Code`, `ProductDto.TotalStock`, `dto.Variants[0].Price`, etc.).

- [ ] **Step 8: Controller builds, runs targeted + full suite**

Run: `dotnet build MyApp.slnx` → 0 errors.
Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --nologo` → all pass (incl. `ProductVariantServiceTests`, `ProductVariantMigrationTests`, updated identity/api tests).
Run: `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --nologo` → all pass.

- [ ] **Step 9: Controller applies the migration to the database**

Run: `dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`
Expected: `RefactorProductToVariant` applied; existing products each gain one default variant; `Products.Code` populated. (Recommend a DB backup first in real prod.) If it errors on the `Code` non-null ordering, apply the nullable-Code fix from Task 2 Step 5 and re-run.

---

## Task 4: UI — ProductIndex + ProductForm (simple/single-variant mode)

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductIndex.razor`
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`
- Modify: `src/MyApp.Web/Components/_Imports.razor` (add `@using MyApp.Application.Attributes` if not present — needed in Task 5; safe to add now)

**Interfaces consumed:** Task 3 DTOs/service.

- [ ] **Step 1: Read the current pages** — open `ProductIndex.razor` and `ProductForm.razor` in full to preserve their layout/styling, then adapt only what the new model requires.

- [ ] **Step 2: Update `ProductIndex.razor`** — replace SKU/price/stock columns with: `Code`, `Name`, `Category`, `Price` (render `@(item.MinPrice == item.MaxPrice ? item.MinPrice.ToString("N0") : $"{item.MinPrice:N0}–{item.MaxPrice:N0}")`), `Variants` (`@item.VariantCount`), `Stock` (`@item.TotalStock`), `Status`. Search box already posts to `GetPagedAsync` (now also searches variant SKU). Keep the rest of the page (paging, delete, auth) intact. Bind to `ProductDto`'s new members; remove references to `item.Sku`/`item.Price`/`item.Stock`.

- [ ] **Step 3: Update `ProductForm.razor` — parent fields + single-variant mode**

Add parent selectors (Brand, BaseUnit, Tax) as `<select>` populated from the respective services (inject `IBrandService`, `IUnitService`, `ITaxService`; load lists in `OnInitializedAsync`). Replace the old flat Price/Discount/Stock/Weight/Dimensions inputs with a single "default variant" card bound to local fields `_price`, `_discount`, `_cost`, `_stock`, `_weight`, `_dimensions`, `_barcode`, `_variantActive` (default true). On save, build a `CreateProductRequest`/`UpdateProductRequest` with `Variants = new[] { new VariantInput(_barcode, _price, _discount, _cost, _weight, _dimensions, _stock, _variantActive, Array.Empty<int>()) }`. On edit-load, populate parent fields + the first variant's fields from `dto.Variants[0]` (a single-variant product). If the loaded product has >1 variant, show a read-only notice "This product has multiple variants — edit them in the variants section" and disable the simple card (the multi-variant editor arrives in Task 5; until then, for >1 variant just list them read-only). Keep image upload section unchanged. Keep the `ValidationException` → field-error routing pattern; route variant errors (`PropertyName` starting with `Variants`) to a general `_error`.

- [ ] **Step 4: Controller builds & smoke-tests**

Run: `dotnet build MyApp.slnx` → 0 errors.
Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --nologo` → all pass (no behavior regression; UI not unit-tested but must compile).

---

## Task 5: UI — ProductForm multi-variant generator + Import/Dashboard adjustments

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductImport.razor` (only if it references removed fields)
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/Dashboard or wherever GetDashboardAsync is consumed` — verify it still binds (DTO shape unchanged for dashboard; likely no edit).

**Interfaces consumed:** Task 3 service, `IAttributeService`.

- [ ] **Step 1: Add a mode toggle to `ProductForm.razor`** — a switch "Has variants?" Off = the simple card from Task 4. On = the generator below.

- [ ] **Step 2: Build the variant generator (multi-variant mode)**

Inject `IAttributeService`; load all attributes in `OnInitializedAsync`. UI:
- A list of attributes (checkbox to include) each expanding to checkboxes of its values.
- A "Generate variants" button that computes the cartesian product of the selected values across selected attributes, producing one row per combination. Maintain `List<VariantRow>` where `VariantRow { List<int> AttributeValueIds; string SkuPreview; decimal Price; decimal? Discount; decimal Cost; int Stock; bool IsActive; }`. SKU preview is informational (server regenerates authoritative SKU); compute it client-side as `code-or-"(auto)"` + joined value codes ordered by attribute name.
- Editable table: one row per combination — show the attribute values (read-only), editable Price/Discount/Cost/Stock/Active, and a remove-row button.
- On Save, map each `VariantRow` → `VariantInput(barcode:null, Price, Discount, Cost, weight:null, dimensions:null, Stock, IsActive, AttributeValueIds)` and submit the Create/Update request. The service generates the real SKUs and rejects duplicate combinations.

Cartesian product helper (C# in `@code`):
```csharp
private static List<List<int>> CartesianProduct(List<List<int>> groups)
{
    var result = new List<List<int>> { new() };
    foreach (var group in groups)
    {
        var next = new List<List<int>>();
        foreach (var combo in result)
            foreach (var val in group)
                next.Add(new List<int>(combo) { val });
        result = next;
    }
    return result;
}
```
Call with the list of selected value-id lists (one inner list per included attribute). Skip attributes with no selected values.

- [ ] **Step 3: Verify `ProductImport.razor` & dashboard pages compile** — Import UI is unchanged functionally (service still maps a row to a 1-variant product). Dashboard DTO shape is unchanged. Only touch them if they reference removed `ProductDto` members; otherwise leave as-is.

- [ ] **Step 4: Controller builds & runs full suite**

Run: `dotnet build MyApp.slnx` → 0 errors.
Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --nologo` and `dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj --nologo` → all pass.

---

## Self-Review

**1. Spec coverage:**
- §2.1 Product restructure → Task 1 (entity) + Task 2 (DbContext/migration). ✅
- §2.2 ProductVariant (incl. temporary Stock, CostPrice) → Task 1. ✅
- §2.3 ProductVariantAttribute → Task 1 + Task 2 config. ✅
- §3 SKU base + suffix → Task 3 `GenerateCodeAsync`/`BuildSku`; unit test in Task 1. ✅
- §4 Service (CRUD, dashboard from variants, import 1-variant) → Task 3. ✅
- §5.1 Form simple mode → Task 4; multi-variant generator → Task 5. ✅
- §5.2 Index/Import/Dashboard → Task 4 (index) + Task 5 (import/dashboard verify). ✅
- §6 Data migration with backfill + Down() → Task 2 (Step 5) + applied in Task 3 (Step 9). ✅
- §8 Testing (unit/integration/migration) → Task 1 unit, Task 2 migration schema test, Task 3 service/dashboard/import. ✅
- §9 Out of scope (F2 stock, multi-variant import, per-variant images) → not built. ✅

**2. Placeholder scan:** Backend tasks (1–3) carry complete code. UI tasks (4–5) give detailed structured specs + the novel logic (cartesian product, request mapping, SKU preview) as code, and reference the existing pages for boilerplate/styling — appropriate since Blazor markup must adapt to current layout rather than be transcribed blind. No "TBD/TODO".

**3. Type consistency:** `Product` ctor/`Update`/`AddVariant`, `ProductVariant` ctor/`Update`/`SetAttributeValues`, `VariantInput`, `ProductDto`/`ProductVariantDto`/`AttributeValueRefDto`, and service helpers (`GenerateCodeAsync`, `BuildSku`, `LoadValueSuffixMapAsync`, `EnsureSkusUniqueInDbAsync`) are consistent across Tasks 1, 3, 4, 5. `db.ProductVariants`/`db.ProductVariantAttributes` defined in Task 2 and used in Task 3.

---

## Execution Handoff

After Task 5, dispatch a final whole-branch review focused on: the migration backfill correctness, SKU generation/uniqueness across products, dashboard aggregation from variants, and that no removed `Product` member is still referenced anywhere. Then F1 is complete; F2 (stock model) becomes the next plan and will remove `ProductVariant.Stock`.
