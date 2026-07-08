# Master Data F0 — Master Sederhana Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Menambahkan master data fondasi (Brand, Warehouse, Tax, PaymentMethod, Attribute + AttributeValue) yang murni aditif — tanpa mengubah data Product yang sudah ada.

**Architecture:** Mengikuti pola vertical-slice yang sudah ada di repo: rich domain entity (`AuditableEntity`) → `IXxxService` + DTO + FluentValidation (Application) → `XxxService` + konfigurasi inline di `AppDbContext` (Infrastructure) → halaman Blazor di `Components/Pages/Master` + resource otorisasi di `AppMenus`. Service-based, bukan MediatR.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core (SQL Server prod / SQLite in-memory test), FluentValidation, xUnit, Bootstrap + Bootstrap Icons.

## Global Constraints

- **Tidak mengubah** entity `Product`, `ProductCategory`, `Unit`, atau data yang sudah ada. F0 murni aditif.
- Setiap entity master mewarisi `MyApp.Domain.Common.AuditableEntity`; `Id` ber-`private set`; field lain `private set`; ada factory constructor + method `Update()`; validasi argumen di dalam entity (`SetXxx`).
- `Code` selalu dinormalisasi `Trim().ToUpperInvariant()` di dalam entity (lihat `Unit`/`ProductCategory`).
- Uniqueness `Code` divalidasi di service (lempar `FluentValidation.ValidationException`) **dan** index unik di DbContext.
- DTO = `record`. Request create/update = `record` terpisah. Validator: `Create<Entity>Validator` + `Update<Entity>Validator`.
- Service didaftarkan di `src/MyApp.Infrastructure/DependencyInjection.cs`. Validator otomatis ter-scan via `AddValidatorsFromAssemblyContaining<CreateProductValidator>()` — tidak perlu registrasi manual.
- Halaman Blazor: index di `/master/<route>`, form di `/master/<route>/new` dan `/master/<route>/{Id:int}/edit`. Pakai `@rendermode InteractiveServer`. Reuse komponen yang sudah ada: `Pager`, `SwalService` (`Swal`), `AppMenus.Perm`.
- Otorisasi: tambah `AppResource` di grup "Master" pada `AppMenus.Groups` dengan aksi CRUD; permission otomatis ter-generate (`<resource>.index/create/edit/delete`).
- Test: integration test berbasis `CustomWebApplicationFactory` (SQLite in-memory, `EnsureCreated()` — DbSet baru otomatis dibuat, tanpa migrasi untuk test). Letakkan di `tests/MyApp.IntegrationTests/`.
- Migrasi EF Core untuk **prod**: `dotnet ef migrations add <Name> --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`. Satu migrasi di akhir F0 (Task 6).
- Build & test: `dotnet build MyApp.slnx` dan `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj`.
- Git: repo ini **bukan** git repository. Lewati semua langkah `git commit`; sebagai gantinya, di akhir tiap task jalankan `dotnet build` + test task tersebut sebagai "checkpoint".

---

## RESEP: Master CRUD Sederhana (WAJIB DIBACA — dipakai Task 1–4)

Empat master pertama (Brand, Warehouse, Tax, PaymentMethod) identik strukturnya kecuali field & validasinya. Bagian ini adalah template lengkap; tiap task hanya memberi nilai substitusi + kode unik (entity/DTO/validator/config/test). **Service & 2 halaman Blazor di bawah ini identik — substitusi token saja.**

Token substitusi per task ada di tabel masing-masing task: `{Entity}` (mis. `Brand`), `{entity}` (camel, `brand`), `{route}` (kebab, `brands`), `{resource}` (mis. `master.brands`), `{Title}` (mis. `Brands`).

### R1. Service template — `src/MyApp.Infrastructure/Services/{Entity}Service.cs`

Sama persis seperti `UnitService.cs`, ganti `Unit`→`{Entity}`, `Units`→DbSet `{Entity}s`, namespace `MyApp.Application.Units`→`MyApp.Application.{Entity}s`. Bentuk kanonik (untuk Brand):

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.Brands;          // {Entity}s
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class BrandService(                // {Entity}Service
    AppDbContext db,
    IValidator<CreateBrandRequest> createValidator,
    IValidator<UpdateBrandRequest> updateValidator) : IBrandService
{
    public async Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Brands.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<BrandDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Brands.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<BrandDto>(items, total, page, pageSize);
    }

    public async Task<BrandDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Brands.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Brand(request.Code, request.Name, request.Description);  // ← argumen sesuai ctor entity
        db.Brands.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateBrandRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Brands.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.Description);  // ← argumen sesuai Update() entity
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Brands.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Brands.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Brands.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateBrandRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static BrandDto ToDto(Brand x) =>      // ← sesuaikan field DTO per task
        new(x.Id, x.Code, x.Name, x.Description, x.CreatedAt, x.CreatedBy);
}
```

> Untuk task dengan field tambahan (Warehouse/Tax/PaymentMethod), `ToDto`, argumen `new {Entity}(...)`, dan `entity.Update(...)` menyesuaikan field tambahan — ditunjukkan eksplisit di tiap task.

### R2. Index page template — `src/MyApp.Web/Components/Pages/Master/{Entity}s/{Entity}Index.razor`

Salin `UnitIndex.razor` persis, ganti: `master.units`→`{resource}`, `IUnitService`→`I{Entity}Service`, `UnitService`→`{Entity}Service`, `UnitDto`→`{Entity}Dto`, route `/master/units`→`/master/{route}`, judul `Units`→`{Title}`, teks "unit"→"{entity}". Kolom tabel default: `#`, `Code`, `Name`, `Description`, `Created`. Task yang punya kolom tambahan menyebutkannya eksplisit.

### R3. Form page template — `src/MyApp.Web/Components/Pages/Master/{Entity}s/{Entity}Form.razor`

Salin `UnitForm.razor` persis dengan substitusi token yang sama seperti R2, dan tambahkan input untuk field tambahan task tersebut (ditunjukkan eksplisit per task). Pola binding field & error handling (`ValidationException` → `_codeError`/`_nameError`/...) sama seperti `UnitForm`.

### R4. DI registration — `src/MyApp.Infrastructure/DependencyInjection.cs`

Tambahkan satu baris di blok "Application services":
```csharp
services.AddScoped<I{Entity}Service, {Entity}Service>();
```

### R5. Menu/otorisasi — `src/MyApp.Web/Authorization/AppMenus.cs`

Tambahkan satu baris di `new("Master", [...])`:
```csharp
new("{resource}", "{Title}", "{icon}", CRUD),
```

### R6. Nav link — `src/MyApp.Web/Components/Layout/NavMenu.razor`

Tambahkan blok `<AuthorizeView Policy="{resource}.index">` di dalam grup Master (pola sama seperti link Unit).

---

## Task 1: Brand master (contoh kanonik penuh)

> Brand = `Code` + `Name` + `Description` (sama persis pola `Unit`). Field `LogoPath` dari spec **ditunda** (butuh upload file) — dicatat untuk fase lanjutan.

**Files:**
- Create: `src/MyApp.Domain/Entities/Brand.cs`
- Create: `src/MyApp.Application/Brands/IBrandService.cs`
- Create: `src/MyApp.Application/Brands/BrandDtos.cs`
- Create: `src/MyApp.Application/Brands/CreateBrandValidator.cs`
- Create: `src/MyApp.Infrastructure/Services/BrandService.cs`
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` (DbSet + config)
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Brands/BrandIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Brands/BrandForm.razor`
- Test: `tests/MyApp.IntegrationTests/BrandServiceTests.cs`

**Substitusi resep:** `{Entity}`=Brand, `{entity}`=brand, `{route}`=brands, `{resource}`=master.brands, `{Title}`=Brands, `{icon}`=`bi-bookmark-star-fill`.

**Interfaces:**
- Produces: `Brand` entity (`Brand(string code, string name, string? description)`, `Update(string, string, string?)`); `IBrandService` (signatur identik `IUnitService`); `BrandDto(int Id, string Code, string Name, string? Description, DateTime CreatedAt, string? CreatedBy)`; `CreateBrandRequest(string Code, string Name, string? Description)`; `UpdateBrandRequest(...)`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.IntegrationTests/BrandServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Brands;
using FluentValidation;
using Xunit;

namespace MyApp.IntegrationTests;

public class BrandServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public BrandServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBrandService>();

        var created = await svc.CreateAsync(new CreateBrandRequest("nke", "Nike", "Sportswear"));
        Assert.Equal("NKE", created.Code);             // ToUpperInvariant
        Assert.NotEqual(default, created.CreatedAt);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Nike", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBrandService>();

        await svc.CreateAsync(new CreateBrandRequest("DUP", "First", null));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CreateBrandRequest("dup", "Second", null)));
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal kompilasi**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~BrandServiceTests"`
Expected: FAIL — `IBrandService`/`CreateBrandRequest` tidak ada (compile error).

- [ ] **Step 3: Buat entity `Brand`**

`src/MyApp.Domain/Entities/Brand.cs` (salin pola `Unit.cs`):
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Merek produk.</summary>
public class Brand : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    private Brand() { } // EF Core

    public Brand(string code, string name, string? description)
    {
        SetCode(code); SetName(name); SetDescription(description);
    }

    public void Update(string code, string name, string? description)
    {
        SetCode(code); SetName(name); SetDescription(description);
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

    private void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
```

- [ ] **Step 4: Buat Application layer (interface, DTO, validator)**

`src/MyApp.Application/Brands/IBrandService.cs`:
```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Brands;

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<BrandDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<BrandDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateBrandRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

`src/MyApp.Application/Brands/BrandDtos.cs`:
```csharp
namespace MyApp.Application.Brands;

public record BrandDto(int Id, string Code, string Name, string? Description, DateTime CreatedAt, string? CreatedBy);
public record CreateBrandRequest(string Code, string Name, string? Description);
public record UpdateBrandRequest(string Code, string Name, string? Description);
```

`src/MyApp.Application/Brands/CreateBrandValidator.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.Brands;

public class CreateBrandValidator : AbstractValidator<CreateBrandRequest>
{
    public CreateBrandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public class UpdateBrandValidator : AbstractValidator<UpdateBrandRequest>
{
    public UpdateBrandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
```

- [ ] **Step 5: Buat `BrandService`** menggunakan **Resep R1** (kode kanonik sudah ditulis untuk Brand di atas — salin apa adanya ke `src/MyApp.Infrastructure/Services/BrandService.cs`).

- [ ] **Step 6: Daftarkan DbSet + config di `AppDbContext.cs`**

Tambah DbSet (setelah `Units`):
```csharp
public DbSet<Brand> Brands => Set<Brand>();
```
Tambah blok config di `OnModelCreating` (setelah blok `Unit`):
```csharp
modelBuilder.Entity<Brand>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Code).HasMaxLength(20).IsRequired();
    e.HasIndex(x => x.Code).IsUnique();
    e.Property(x => x.Name).HasMaxLength(100).IsRequired();
    e.Property(x => x.Description).HasMaxLength(300);
});
```

- [ ] **Step 7: Daftarkan service (Resep R4)** — tambah di `DependencyInjection.cs`:
```csharp
services.AddScoped<IBrandService, BrandService>();
```

- [ ] **Step 8: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~BrandServiceTests"`
Expected: PASS (2 test).

- [ ] **Step 9: Tambah menu + nav + halaman Blazor**

- `AppMenus.cs` (Resep R5): `new("master.brands", "Brands", "bi-bookmark-star-fill", CRUD),`
- `NavMenu.razor` (Resep R6): blok nav untuk `master.brands.index` → `href="master/brands"`, ikon `bi-bookmark-star-fill`, label "Brand".
- `BrandIndex.razor` (Resep R2) dan `BrandForm.razor` (Resep R3) dengan substitusi Brand. Form: placeholder Code `e.g. NKE`, Name `e.g. Nike`.

- [ ] **Step 10: Build penuh (checkpoint)**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 error.

---

## Task 2: Warehouse master (+ gudang default)

> Field tambahan: `Address?` (string), `IsActive` (bool, default true), `IsDefault` (bool, default false). Hanya boleh ada **satu** gudang default — di-enforce di service. Sebuah gudang default di-seed (Task 6 migrasi memuat seed; service juga punya helper).

**Files:**
- Create: `src/MyApp.Domain/Entities/Warehouse.cs`
- Create: `src/MyApp.Application/Warehouses/IWarehouseService.cs`
- Create: `src/MyApp.Application/Warehouses/WarehouseDtos.cs`
- Create: `src/MyApp.Application/Warehouses/CreateWarehouseValidator.cs`
- Create: `src/MyApp.Infrastructure/Services/WarehouseService.cs`
- Modify: `AppDbContext.cs`, `DependencyInjection.cs`, `AppMenus.cs`, `NavMenu.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Warehouses/WarehouseIndex.razor`, `WarehouseForm.razor`
- Test: `tests/MyApp.IntegrationTests/WarehouseServiceTests.cs`

**Substitusi resep:** `{Entity}`=Warehouse, `{entity}`=warehouse, `{route}`=warehouses, `{resource}`=master.warehouses, `{Title}`=Warehouses, `{icon}`=`bi-building-fill`.

**Interfaces:**
- Produces: `Warehouse(string code, string name, string? address, bool isActive, bool isDefault)`, `Update(string code, string name, string? address, bool isActive, bool isDefault)`, method `SetAsDefault()`/`ClearDefault()`; `WarehouseDto(int Id, string Code, string Name, string? Address, bool IsActive, bool IsDefault, DateTime CreatedAt, string? CreatedBy)`; `CreateWarehouseRequest(string Code, string Name, string? Address, bool IsActive, bool IsDefault)`; `UpdateWarehouseRequest(...)`. Plus `IWarehouseService.GetDefaultAsync()` (dipakai F2).

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.IntegrationTests/WarehouseServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Warehouses;
using Xunit;

namespace MyApp.IntegrationTests;

public class WarehouseServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public WarehouseServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task SettingSecondDefault_UnsetsFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IWarehouseService>();

        var a = await svc.CreateAsync(new CreateWarehouseRequest("WH-A", "Gudang A", null, true, true));
        var b = await svc.CreateAsync(new CreateWarehouseRequest("WH-B", "Gudang B", null, true, true));

        var def = await svc.GetDefaultAsync();
        Assert.NotNull(def);
        Assert.Equal("WH-B", def!.Code);          // default terbaru menang

        var reloadedA = await svc.GetByIdAsync(a.Id);
        Assert.False(reloadedA!.IsDefault);        // A tidak lagi default
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~WarehouseServiceTests"`
Expected: FAIL (compile error — tipe belum ada).

- [ ] **Step 3: Buat entity `Warehouse.cs`**
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Gudang / lokasi penyimpanan stok.</summary>
public class Warehouse : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDefault { get; private set; }

    private Warehouse() { } // EF Core

    public Warehouse(string code, string name, string? address, bool isActive, bool isDefault)
    {
        SetCode(code); SetName(name); SetAddress(address);
        IsActive = isActive; IsDefault = isDefault;
    }

    public void Update(string code, string name, string? address, bool isActive, bool isDefault)
    {
        SetCode(code); SetName(name); SetAddress(address);
        IsActive = isActive; IsDefault = isDefault;
    }

    public void SetAsDefault() => IsDefault = true;
    public void ClearDefault() => IsDefault = false;

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

    private void SetAddress(string? address) =>
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
}
```

- [ ] **Step 4: Application layer**

`IWarehouseService.cs` — sama seperti `IBrandService` plus satu method:
```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Warehouses;

public interface IWarehouseService
{
    Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<WarehouseDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<WarehouseDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<WarehouseDto?> GetDefaultAsync(CancellationToken ct = default);
    Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateWarehouseRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

`WarehouseDtos.cs`:
```csharp
namespace MyApp.Application.Warehouses;

public record WarehouseDto(int Id, string Code, string Name, string? Address, bool IsActive, bool IsDefault, DateTime CreatedAt, string? CreatedBy);
public record CreateWarehouseRequest(string Code, string Name, string? Address, bool IsActive, bool IsDefault);
public record UpdateWarehouseRequest(string Code, string Name, string? Address, bool IsActive, bool IsDefault);
```

`CreateWarehouseValidator.cs` — sama pola Brand (Code/Name), plus `RuleFor(x => x.Address).MaximumLength(300);`. Buat `CreateWarehouseValidator` & `UpdateWarehouseValidator`.

- [ ] **Step 5: Buat `WarehouseService.cs`** — pakai Resep R1 (substitusi Warehouse) dengan perbedaan:
  - `ToDto`: `new(x.Id, x.Code, x.Name, x.Address, x.IsActive, x.IsDefault, x.CreatedAt, x.CreatedBy)`
  - `new Warehouse(request.Code, request.Name, request.Address, request.IsActive, request.IsDefault)`
  - `entity.Update(request.Code, request.Name, request.Address, request.IsActive, request.IsDefault)`
  - Tambah method & helper enforce single-default:
```csharp
public async Task<WarehouseDto?> GetDefaultAsync(CancellationToken ct = default)
{
    var x = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(e => e.IsDefault, ct);
    return x is null ? null : ToDto(x);
}

// Panggil SEBELUM SaveChanges di Create & Update bila request.IsDefault == true:
private async Task ClearOtherDefaultsAsync(int? exceptId, CancellationToken ct)
{
    var others = await db.Warehouses.Where(e => e.IsDefault && (exceptId == null || e.Id != exceptId)).ToListAsync(ct);
    foreach (var o in others) o.ClearDefault();
}
```
  Di `CreateAsync`: setelah `db.Warehouses.Add(entity)` dan sebelum `SaveChangesAsync`, jika `request.IsDefault` → `await ClearOtherDefaultsAsync(null, ct);`. Di `UpdateAsync`: setelah `entity.Update(...)`, jika `request.IsDefault` → `await ClearOtherDefaultsAsync(id, ct);`.

- [ ] **Step 6: DbContext** — DbSet `public DbSet<Warehouse> Warehouses => Set<Warehouse>();` + config:
```csharp
modelBuilder.Entity<Warehouse>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Code).HasMaxLength(20).IsRequired();
    e.HasIndex(x => x.Code).IsUnique();
    e.Property(x => x.Name).HasMaxLength(100).IsRequired();
    e.Property(x => x.Address).HasMaxLength(300);
});
```

- [ ] **Step 7: DI** — `services.AddScoped<IWarehouseService, WarehouseService>();`

- [ ] **Step 8: Jalankan test**

Run: `dotnet test ... --filter "FullyQualifiedName~WarehouseServiceTests"`
Expected: PASS.

- [ ] **Step 9: Menu + nav + halaman**
  - `AppMenus.cs`: `new("master.warehouses", "Warehouses", "bi-building-fill", CRUD),`
  - `NavMenu.razor`: nav `master.warehouses.index` → `master/warehouses`.
  - `WarehouseIndex.razor` (Resep R2) + kolom tambahan `Default` (badge bila `item.IsDefault`) dan `Active`.
  - `WarehouseForm.razor` (Resep R3) + input `Address` (textarea), checkbox `IsActive`, checkbox `IsDefault`. State field: `_address`, `_isActive` (default true), `_isDefault`. Kirim ke `CreateWarehouseRequest`/`UpdateWarehouseRequest`.

- [ ] **Step 10: Build (checkpoint)** — `dotnet build MyApp.slnx` → 0 error.

---

## Task 3: Tax master

> Field tambahan: `Rate` (decimal, persen 0–100), `IsInclusive` (bool). `Description?` tetap ada.

**Files:** sama pola Task 1 untuk path `Tax`/`Taxes`. Test: `tests/MyApp.IntegrationTests/TaxServiceTests.cs`.

**Substitusi resep:** `{Entity}`=Tax, `{entity}`=tax, `{route}`=taxes, `{resource}`=master.taxes, `{Title}`=Taxes, `{icon}`=`bi-percent`.

**Interfaces:**
- Produces: `Tax(string code, string name, decimal rate, bool isInclusive, string? description)`, `Update(...)`; `TaxDto(int Id, string Code, string Name, decimal Rate, bool IsInclusive, string? Description, DateTime CreatedAt, string? CreatedBy)`; `CreateTaxRequest(string Code, string Name, decimal Rate, bool IsInclusive, string? Description)`; `UpdateTaxRequest(...)`.

- [ ] **Step 1: Test gagal** — `TaxServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Taxes;
using Xunit;

namespace MyApp.IntegrationTests;

public class TaxServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public TaxServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_PersistsRate()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxService>();
        var created = await svc.CreateAsync(new CreateTaxRequest("PPN", "PPN 11%", 11m, false, null));
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(11m, fetched!.Rate);
        Assert.False(fetched.IsInclusive);
    }
}
```

- [ ] **Step 2: Test gagal** — Run filter `~TaxServiceTests` → FAIL (compile).

- [ ] **Step 3: Entity `Tax.cs`**
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pajak (mis. PPN 11%).</summary>
public class Tax : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public decimal Rate { get; private set; }       // persen, 0..100
    public bool IsInclusive { get; private set; }
    public string? Description { get; private set; }

    private Tax() { }

    public Tax(string code, string name, decimal rate, bool isInclusive, string? description)
    {
        SetCode(code); SetName(name); SetRate(rate); IsInclusive = isInclusive; SetDescription(description);
    }

    public void Update(string code, string name, decimal rate, bool isInclusive, string? description)
    {
        SetCode(code); SetName(name); SetRate(rate); IsInclusive = isInclusive; SetDescription(description);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
    private void SetRate(decimal rate)
    {
        if (rate is < 0 or > 100) throw new ArgumentException("Rate must be between 0 and 100.", nameof(rate));
        Rate = rate;
    }
    private void SetDescription(string? d) => Description = string.IsNullOrWhiteSpace(d) ? null : d.Trim();
}
```

- [ ] **Step 4: Application layer** — `ITaxService` (signatur standar, tipe `TaxDto`/`CreateTaxRequest`/`UpdateTaxRequest`); `TaxDtos.cs` (record di atas); `CreateTaxValidator.cs`:
```csharp
using FluentValidation;
namespace MyApp.Application.Taxes;

public class CreateTaxValidator : AbstractValidator<CreateTaxRequest>
{
    public CreateTaxValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rate).InclusiveBetween(0, 100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
public class UpdateTaxValidator : AbstractValidator<UpdateTaxRequest>
{
    public UpdateTaxValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rate).InclusiveBetween(0, 100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
```

- [ ] **Step 5: `TaxService.cs`** — Resep R1 (substitusi Tax) dengan:
  - `ToDto`: `new(x.Id, x.Code, x.Name, x.Rate, x.IsInclusive, x.Description, x.CreatedAt, x.CreatedBy)`
  - `new Tax(request.Code, request.Name, request.Rate, request.IsInclusive, request.Description)`
  - `entity.Update(request.Code, request.Name, request.Rate, request.IsInclusive, request.Description)`

- [ ] **Step 6: DbContext** — `public DbSet<Tax> Taxes => Set<Tax>();` + config:
```csharp
modelBuilder.Entity<Tax>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Code).HasMaxLength(20).IsRequired();
    e.HasIndex(x => x.Code).IsUnique();
    e.Property(x => x.Name).HasMaxLength(100).IsRequired();
    e.Property(x => x.Rate).HasPrecision(5, 2);
    e.Property(x => x.Description).HasMaxLength(300);
});
```

- [ ] **Step 7: DI** — `services.AddScoped<ITaxService, TaxService>();`

- [ ] **Step 8: Test** — Run filter `~TaxServiceTests` → PASS.

- [ ] **Step 9: Menu + nav + halaman**
  - `AppMenus.cs`: `new("master.taxes", "Taxes", "bi-percent", CRUD),`
  - `NavMenu.razor`: nav `master.taxes.index` → `master/taxes`.
  - `TaxIndex.razor` (Resep R2) + kolom `Rate` (`@item.Rate%`) dan `Inclusive`.
  - `TaxForm.razor` (Resep R3) + input number `Rate` (`type="number"`, step 0.01, bind `_rate` decimal), checkbox `IsInclusive`, textarea Description.

- [ ] **Step 10: Build** — `dotnet build MyApp.slnx` → 0 error.

---

## Task 4: PaymentMethod master

> Field tambahan: `Type` (enum `PaymentType`: Tunai/Transfer/Kartu/QRIS), `IsActive` (bool). Enum disimpan sebagai string (pola `ProductStatus`).

**Files:** sama pola; tambah `src/MyApp.Domain/Entities/PaymentType.cs` (enum). Route `payment-methods`. Test: `PaymentMethodServiceTests.cs`.

**Substitusi resep:** `{Entity}`=PaymentMethod, `{entity}`=paymentMethod, `{route}`=payment-methods, `{resource}`=master.payment-methods, `{Title}`=Payment Methods, `{icon}`=`bi-credit-card-2-front-fill`.

**Interfaces:**
- Produces: enum `PaymentType { Tunai, Transfer, Kartu, QRIS }`; `PaymentMethod(string code, string name, PaymentType type, bool isActive)`, `Update(...)`; `PaymentMethodDto(int Id, string Code, string Name, PaymentType Type, bool IsActive, DateTime CreatedAt, string? CreatedBy)`; `CreatePaymentMethodRequest(string Code, string Name, PaymentType Type, bool IsActive)`; `UpdatePaymentMethodRequest(...)`.

- [ ] **Step 1: Test gagal** — `PaymentMethodServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.PaymentMethods;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.IntegrationTests;

public class PaymentMethodServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PaymentMethodServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_PersistsType()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();
        var created = await svc.CreateAsync(new CreatePaymentMethodRequest("QRIS", "QRIS", PaymentType.QRIS, true));
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(PaymentType.QRIS, fetched!.Type);
        Assert.True(fetched.IsActive);
    }
}
```

- [ ] **Step 2: Test gagal** — filter `~PaymentMethodServiceTests` → FAIL (compile).

- [ ] **Step 3: Enum + entity**

`src/MyApp.Domain/Entities/PaymentType.cs`:
```csharp
namespace MyApp.Domain.Entities;

public enum PaymentType { Tunai, Transfer, Kartu, QRIS }
```
`src/MyApp.Domain/Entities/PaymentMethod.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Metode pembayaran POS.</summary>
public class PaymentMethod : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public PaymentType Type { get; private set; }
    public bool IsActive { get; private set; }

    private PaymentMethod() { }

    public PaymentMethod(string code, string name, PaymentType type, bool isActive)
    {
        SetCode(code); SetName(name); Type = type; IsActive = isActive;
    }

    public void Update(string code, string name, PaymentType type, bool isActive)
    {
        SetCode(code); SetName(name); Type = type; IsActive = isActive;
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
}
```

- [ ] **Step 4: Application layer** — `IPaymentMethodService` (signatur standar); `PaymentMethodDtos.cs` (record di atas, `using MyApp.Domain.Entities;` untuk `PaymentType`); `CreatePaymentMethodValidator.cs` (Code/Name standar + `RuleFor(x => x.Type).IsInEnum();`). Buat Create & Update validator.

- [ ] **Step 5: `PaymentMethodService.cs`** — Resep R1 (substitusi PaymentMethod, DbSet `PaymentMethods`) dengan:
  - `ToDto`: `new(x.Id, x.Code, x.Name, x.Type, x.IsActive, x.CreatedAt, x.CreatedBy)`
  - `new PaymentMethod(request.Code, request.Name, request.Type, request.IsActive)`
  - `entity.Update(request.Code, request.Name, request.Type, request.IsActive)`

- [ ] **Step 6: DbContext** — `public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();` + config:
```csharp
modelBuilder.Entity<PaymentMethod>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Code).HasMaxLength(20).IsRequired();
    e.HasIndex(x => x.Code).IsUnique();
    e.Property(x => x.Name).HasMaxLength(100).IsRequired();
    e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
});
```

- [ ] **Step 7: DI** — `services.AddScoped<IPaymentMethodService, PaymentMethodService>();`

- [ ] **Step 8: Test** — filter `~PaymentMethodServiceTests` → PASS.

- [ ] **Step 9: Menu + nav + halaman**
  - `AppMenus.cs`: `new("master.payment-methods", "Payment Methods", "bi-credit-card-2-front-fill", CRUD),`
  - `NavMenu.razor`: nav `master.payment-methods.index` → `master/payment-methods`.
  - `PaymentMethodIndex.razor` (Resep R2) + kolom `Type` dan `Active`.
  - `PaymentMethodForm.razor` (Resep R3) + `<select>` untuk `Type` (`@foreach` `Enum.GetValues<PaymentType>()`), checkbox `IsActive`. Tidak ada field Description (hapus blok description dari template).

- [ ] **Step 10: Build** — `dotnet build MyApp.slnx` → 0 error.

---

## Task 5: Attribute + AttributeValue master (parent-child)

> `Attribute` adalah aggregate root yang memiliki koleksi `AttributeValue` (pola sama seperti `Product` memiliki `ProductImage`). UI: list Attribute; form Attribute mengelola nilai-nilainya inline.

**Files:**
- Create: `src/MyApp.Domain/Entities/ProductAttribute.cs` (class `ProductAttribute`), `src/MyApp.Domain/Entities/AttributeValue.cs`
- Create: `src/MyApp.Application/Attributes/IAttributeService.cs`, `AttributeDtos.cs`, `CreateAttributeValidator.cs`
- Create: `src/MyApp.Infrastructure/Services/AttributeService.cs`
- Modify: `AppDbContext.cs`, `DependencyInjection.cs`, `AppMenus.cs`, `NavMenu.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Attributes/AttributeIndex.razor`, `AttributeForm.razor`
- Test: `tests/MyApp.IntegrationTests/AttributeServiceTests.cs`

> **Catatan penamaan:** entity diberi nama `ProductAttribute` (bukan `Attribute`) untuk menghindari bentrok dengan `System.Attribute`. Route/menu tetap `attributes`.

**Substitusi:** `{route}`=attributes, `{resource}`=master.attributes, `{Title}`=Attributes, `{icon}`=`bi-sliders`.

**Interfaces:**
- Produces:
  - `ProductAttribute(string code, string name)`, `Update(string code, string name)`, `AddValue(string code, string value)`, `RemoveValue(int valueId)`, `IReadOnlyList<AttributeValue> Values`.
  - `AttributeValue(string code, string value)` dengan `Id`, `AttributeId`, `Code`, `Value`.
  - `AttributeDto(int Id, string Code, string Name, IReadOnlyList<AttributeValueDto> Values, DateTime CreatedAt, string? CreatedBy)`; `AttributeValueDto(int Id, string Code, string Value)`.
  - `CreateAttributeRequest(string Code, string Name, IReadOnlyList<AttributeValueInput> Values)`; `UpdateAttributeRequest(...)`; `AttributeValueInput(string Code, string Value)`.
  - `IAttributeService`: `GetAllAsync`, `GetPagedAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` (signatur tipe `AttributeDto`/request di atas).

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.IntegrationTests/AttributeServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Attributes;
using Xunit;

namespace MyApp.IntegrationTests;

public class AttributeServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AttributeServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_WithValues_PersistsChildren()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAttributeService>();

        var created = await svc.CreateAsync(new CreateAttributeRequest("SIZE", "Ukuran",
        [
            new AttributeValueInput("S", "Small"),
            new AttributeValueInput("M", "Medium"),
            new AttributeValueInput("L", "Large"),
        ]));

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(3, fetched!.Values.Count);
        Assert.Contains(fetched.Values, v => v.Code == "M" && v.Value == "Medium");
    }

    [Fact]
    public async Task Update_ReplacesValues()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAttributeService>();

        var created = await svc.CreateAsync(new CreateAttributeRequest("COLOR", "Warna",
            [ new AttributeValueInput("RED", "Merah") ]));

        await svc.UpdateAsync(created.Id, new UpdateAttributeRequest("COLOR", "Warna",
            [ new AttributeValueInput("RED", "Merah"), new AttributeValueInput("BLU", "Biru") ]));

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(2, fetched!.Values.Count);
    }
}
```

- [ ] **Step 2: Jalankan, pastikan gagal** — filter `~AttributeServiceTests` → FAIL (compile).

- [ ] **Step 3: Buat entity `AttributeValue` & `ProductAttribute`**

`src/MyApp.Domain/Entities/AttributeValue.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Nilai dari sebuah atribut (mis. S/M/L untuk Ukuran).</summary>
public class AttributeValue : AuditableEntity
{
    public int Id { get; private set; }
    public int AttributeId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    private AttributeValue() { }

    public AttributeValue(string code, string value)
    {
        SetCode(code); SetValue(value);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Value code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", nameof(value));
        Value = value.Trim();
    }
}
```
`src/MyApp.Domain/Entities/ProductAttribute.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Atribut varian (mis. Ukuran, Warna) — memiliki koleksi nilai.</summary>
public class ProductAttribute : AuditableEntity
{
    private readonly List<AttributeValue> _values = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public IReadOnlyList<AttributeValue> Values => _values;

    private ProductAttribute() { }

    public ProductAttribute(string code, string name)
    {
        SetCode(code); SetName(name);
    }

    public void Update(string code, string name)
    {
        SetCode(code); SetName(name);
    }

    public AttributeValue AddValue(string code, string value)
    {
        var v = new AttributeValue(code, value);
        _values.Add(v);
        return v;
    }

    public void RemoveValue(int valueId)
    {
        var v = _values.FirstOrDefault(x => x.Id == valueId);
        if (v is not null) _values.Remove(v);
    }

    public void ClearValues() => _values.Clear();

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        Code = code.Trim().ToUpperInvariant();
    }
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }
}
```

- [ ] **Step 4: Application layer**

`src/MyApp.Application/Attributes/AttributeDtos.cs`:
```csharp
namespace MyApp.Application.Attributes;

public record AttributeValueDto(int Id, string Code, string Value);
public record AttributeDto(int Id, string Code, string Name, IReadOnlyList<AttributeValueDto> Values, DateTime CreatedAt, string? CreatedBy);

public record AttributeValueInput(string Code, string Value);
public record CreateAttributeRequest(string Code, string Name, IReadOnlyList<AttributeValueInput> Values);
public record UpdateAttributeRequest(string Code, string Name, IReadOnlyList<AttributeValueInput> Values);
```
`src/MyApp.Application/Attributes/IAttributeService.cs`:
```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Attributes;

public interface IAttributeService
{
    Task<IReadOnlyList<AttributeDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<AttributeDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<AttributeDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AttributeDto> CreateAsync(CreateAttributeRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateAttributeRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```
`src/MyApp.Application/Attributes/CreateAttributeValidator.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.Attributes;

public class CreateAttributeValidator : AbstractValidator<CreateAttributeRequest>
{
    public CreateAttributeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Values).ChildRules(v =>
        {
            v.RuleFor(i => i.Code).NotEmpty().MaximumLength(20);
            v.RuleFor(i => i.Value).NotEmpty().MaximumLength(100);
        });
    }
}

public class UpdateAttributeValidator : AbstractValidator<UpdateAttributeRequest>
{
    public UpdateAttributeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Values).ChildRules(v =>
        {
            v.RuleFor(i => i.Code).NotEmpty().MaximumLength(20);
            v.RuleFor(i => i.Value).NotEmpty().MaximumLength(100);
        });
    }
}
```

- [ ] **Step 5: `AttributeService.cs`**

`src/MyApp.Infrastructure/Services/AttributeService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Attributes;
using MyApp.Application.Common;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class AttributeService(
    AppDbContext db,
    IValidator<CreateAttributeRequest> createValidator,
    IValidator<UpdateAttributeRequest> updateValidator) : IAttributeService
{
    public async Task<IReadOnlyList<AttributeDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.ProductAttributes.AsNoTracking().Include(a => a.Values)
            .OrderBy(a => a.Name).Select(a => ToDto(a)).ToListAsync(ct);

    public async Task<PagedResult<AttributeDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.ProductAttributes.AsNoTracking().Include(a => a.Values).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => a.Name.Contains(search) || a.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => ToDto(a)).ToListAsync(ct);

        return new PagedResult<AttributeDto>(items, total, page, pageSize);
    }

    public async Task<AttributeDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await db.ProductAttributes.AsNoTracking().Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : ToDto(a);
    }

    public async Task<AttributeDto> CreateAsync(CreateAttributeRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var attr = new ProductAttribute(request.Code, request.Name);
        foreach (var v in request.Values) attr.AddValue(v.Code, v.Value);

        db.ProductAttributes.Add(attr);
        await db.SaveChangesAsync(ct);
        return ToDto(attr);
    }

    public async Task<bool> UpdateAsync(int id, UpdateAttributeRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var attr = await db.ProductAttributes.Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (attr is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);

        attr.Update(request.Code, request.Name);
        attr.ClearValues();                          // ganti penuh daftar nilai
        foreach (var v in request.Values) attr.AddValue(v.Code, v.Value);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var attr = await db.ProductAttributes.Include(x => x.Values)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (attr is null) return false;
        db.ProductAttributes.Remove(attr);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.ProductAttributes.AsNoTracking()
            .AnyAsync(a => a.Code == normalized && (excludeId == null || a.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateAttributeRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static AttributeDto ToDto(ProductAttribute a) =>
        new(a.Id, a.Code, a.Name,
            a.Values.Select(v => new AttributeValueDto(v.Id, v.Code, v.Value)).ToList(),
            a.CreatedAt, a.CreatedBy);
}
```

- [ ] **Step 6: DbContext** — DbSet + config + akses field koleksi (pola `Product.Images`):
```csharp
public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
public DbSet<AttributeValue> AttributeValues => Set<AttributeValue>();
```
Config:
```csharp
modelBuilder.Entity<ProductAttribute>(e =>
{
    e.HasKey(a => a.Id);
    e.Property(a => a.Code).HasMaxLength(20).IsRequired();
    e.HasIndex(a => a.Code).IsUnique();
    e.Property(a => a.Name).HasMaxLength(100).IsRequired();

    e.HasMany(a => a.Values)
        .WithOne()
        .HasForeignKey(v => v.AttributeId)
        .OnDelete(DeleteBehavior.Cascade);

    e.Metadata.FindNavigation(nameof(ProductAttribute.Values))!
        .SetPropertyAccessMode(PropertyAccessMode.Field);
});

modelBuilder.Entity<AttributeValue>(e =>
{
    e.HasKey(v => v.Id);
    e.Property(v => v.Code).HasMaxLength(20).IsRequired();
    e.Property(v => v.Value).HasMaxLength(100).IsRequired();
});
```

- [ ] **Step 7: DI** — `services.AddScoped<IAttributeService, AttributeService>();`

- [ ] **Step 8: Test** — filter `~AttributeServiceTests` → PASS (2 test).

- [ ] **Step 9: Menu + nav + halaman**
  - `AppMenus.cs`: `new("master.attributes", "Attributes", "bi-sliders", CRUD),`
  - `NavMenu.razor`: nav `master.attributes.index` → `master/attributes`.
  - `AttributeIndex.razor` (Resep R2, route `/master/attributes`) + kolom `Values` (tampilkan jumlah / chip nilai, mis. `@string.Join(", ", item.Values.Select(v => v.Value))`). Tanpa kolom Description.
  - `AttributeForm.razor`: route `/master/attributes/new` & `/master/attributes/{Id:int}/edit`. Field Code + Name (pola UnitForm). Tambah editor nilai: list `_values` (`List<AttributeValueInput>` lokal), tombol "Add value" menambah baris (input Code + Value), tombol hapus per baris. Saat Save kirim `CreateAttributeRequest(_code, _name, _values)` / `UpdateAttributeRequest(...)`. Tangani `ValidationException` seperti UnitForm.

- [ ] **Step 10: Build** — `dotnet build MyApp.slnx` → 0 error.

---

## Task 6: Migrasi EF Core + seed gudang default

> Satu migrasi untuk semua tabel F0, plus seed satu `Warehouse` default agar F2 punya tujuan stok.

**Files:**
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/*` (di-generate)
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` (HasData seed warehouse default)

- [ ] **Step 1: Tambah seed gudang default di `OnModelCreating`** (di dalam blok `modelBuilder.Entity<Warehouse>`):
```csharp
e.HasData(new
{
    Id = 1,
    Code = "WH-MAIN",
    Name = "Gudang Utama",
    Address = (string?)null,
    IsActive = true,
    IsDefault = true,
    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    CreatedBy = (string?)"system"
});
```
> Catatan: `HasData` butuh nilai statik (bukan `DateTime.UtcNow`) — gunakan tanggal tetap di atas.

- [ ] **Step 2: Generate migrasi**

Run:
```bash
dotnet ef migrations add AddMasterDataF0 --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```
Expected: file migrasi baru di `src/MyApp.Infrastructure/Persistence/Migrations/` berisi `CreateTable` untuk `Brands`, `Warehouses`, `Taxes`, `PaymentMethods`, `ProductAttributes`, `AttributeValues`, plus `InsertData` untuk warehouse default.

- [ ] **Step 3: Verifikasi isi migrasi** — buka file migrasi, pastikan ada keenam tabel + index unik `Code` + insert WH-MAIN. Tidak ada perubahan pada tabel `Products`/`ProductCategories`/`Units` (kalau ada, berarti ada perubahan tak disengaja — perbaiki).

- [ ] **Step 4: Build penuh**

Run: `dotnet build MyApp.slnx`
Expected: 0 error.

- [ ] **Step 5: Seluruh test suite**

Run: `dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj`
Expected: semua test PASS (Brand, Warehouse, Tax, PaymentMethod, Attribute + test lama).

- [ ] **Step 6 (opsional, butuh DB): terapkan migrasi**

Run: `dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`
Expected: tabel ter-update di SQL Server. (Lewati jika tidak ada koneksi DB di mesin pengembang.)

---

## Self-Review (diisi penulis rencana)

**1. Spec coverage (§ spec → task):**
- §4 Brand → Task 1 (LogoPath ditunda, dicatat). ✅
- §4 Warehouse (Address/IsActive/IsDefault) + gudang default → Task 2 + Task 6 seed. ✅
- §4 Tax (Rate/IsInclusive) → Task 3. ✅
- §4 PaymentMethod (Type/IsActive) → Task 4. ✅
- §3.3 Attribute + AttributeValue → Task 5. ✅
- §7 Otorisasi & nav untuk tiap master → Step 9 tiap task. ✅
- §8 F0 "buat gudang default di sini" → Task 6. ✅
- Di luar F0 (Product→Variant, stok, Supplier/Customer, transaksi) → **bukan** lingkup rencana ini (F1–F4). ✅

**2. Placeholder scan:** Tidak ada "TBD/TODO/implement later". Resep R1/R2/R3 berisi kode lengkap; tiap task memberi substitusi eksplisit + kode unik. ✅

**3. Type consistency:** Nama service `I{Entity}Service`, DTO `{Entity}Dto`, request `Create/Update{Entity}Request` konsisten antar task & resep. Entity `ProductAttribute` (bukan `Attribute`) konsisten dipakai di entity, DbContext (`ProductAttributes`), dan service. `PaymentType` enum dipakai di entity, DTO, dan validator. ✅

---

## Execution Handoff

Rencana ini fokus **F0** saja. Setelah F0 selesai & teruji, fase berikut (F1 refactor Product→Variant, F2 model stok, F3 Supplier/Customer) ditulis sebagai rencana terpisah karena melibatkan migrasi data berisiko.
