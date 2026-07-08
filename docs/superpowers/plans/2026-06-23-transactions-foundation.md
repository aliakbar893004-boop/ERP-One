# Tahap A — Fondasi Transaksi (Supplier, Customer, Hub) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tambah master data Supplier & Customer dan halaman hub Transaksi (gaya mockup) ke `MyApp.Web`, sebagai fondasi modul Purchase Order & Sales Order.

**Architecture:** Ikuti pola Clean Architecture yang ada persis seperti fitur `Warehouse`/`Brand`: entitas `AuditableEntity` (Domain) → DTO record + interface service + FluentValidation (Application) → `AppDbContext` mapping + service implementasi + DI (Infrastructure) → halaman Blazor Server `Index`/`Form` + permission `AppMenus` (Web). Hub Transaksi adalah halaman Blazor di dalam shell aplikasi dengan CSS scoped meniru mockup; kartu PO/SO menuju halaman placeholder yang diganti di Tahap B/C.

**Tech Stack:** .NET 10, Blazor Server (InteractiveServer), EF Core 10 (SQL Server), FluentValidation, Bootstrap 5 + Bootstrap Icons, xUnit.

## Global Constraints

- `TreatWarningsAsErrors=true` — kode harus bebas warning (Directory.Build.props).
- `Nullable=enable`, `ImplicitUsings=enable`.
- Entitas: properti `private set`, mutasi lewat constructor/`Update()`, validasi invariant di domain (lempar `ArgumentException`).
- `Code`: dinormalisasi `Trim().ToUpperInvariant()`, unik per entitas, regex `^[A-Za-z0-9-]+$`, ≤20.
- Service melempar `FluentValidation.ValidationException` untuk error validasi & duplikasi Code (pola `EnsureCodeUniqueAsync`).
- DTO berupa `record` di namespace Application.
- `DefaultCurrency` default `"IDR"` (kolom kode, BUKAN konversi kurs).
- Decimal presisi `(18,2)` untuk uang (`CreditLimit`).
- Permission baru otomatis diberikan ke role admin via `BootstrapSeeder` (yang meng-grant `AppMenus.AllPermissions`) — tidak perlu seeding manual.
- Perintah build: `dotnet build MyApp.slnx`. Test: `dotnet test`. Migration: `dotnet ef` dengan `--project src/MyApp.Infrastructure --startup-project src/MyApp.Web`.
- **Jangan** mengubah tabel/logika stok di Tahap A.

---

### Task 1: Entitas Supplier (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/Supplier.cs`
- Test: `tests/MyApp.UnitTests/SupplierDomainTests.cs`

**Interfaces:**
- Produces: `Supplier` dengan ctor `Supplier(string code, string name, string? contactPerson, string? phone, string? email, string? address, string? taxId, int paymentTermDays, string? defaultCurrency, string? bankName, string? bankAccountNumber, string? bankAccountName, bool isActive)` dan `void Update(...)` dengan parameter identik. Properti getter: `Id, Code, Name, ContactPerson, Phone, Email, Address, TaxId, PaymentTermDays, DefaultCurrency, BankName, BankAccountNumber, BankAccountName, IsActive`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/SupplierDomainTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class SupplierDomainTests
{
    private static Supplier Make(string code = "sup-1", int term = 30, string? currency = "idr") =>
        new(code, "PT Sumber Makmur", "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", term, currency, "BCA", "123", "PT SM", true);

    [Fact]
    public void Ctor_normalizes_code_and_currency()
    {
        var s = Make(code: "sup-1", currency: "idr");
        Assert.Equal("SUP-1", s.Code);
        Assert.Equal("IDR", s.DefaultCurrency);
    }

    [Fact]
    public void Ctor_blank_currency_defaults_to_IDR()
    {
        var s = Make(currency: "  ");
        Assert.Equal("IDR", s.DefaultCurrency);
    }

    [Fact]
    public void Ctor_requires_code()
    {
        Assert.Throws<ArgumentException>(() => Make(code: "  "));
    }

    [Fact]
    public void Ctor_rejects_negative_payment_term()
    {
        Assert.Throws<ArgumentException>(() => Make(term: -1));
    }

    [Fact]
    public void Update_changes_fields()
    {
        var s = Make();
        s.Update("SUP-2", "PT Baru", null, null, null, null, null, 0, "USD", null, null, null, false);
        Assert.Equal("SUP-2", s.Code);
        Assert.Equal("PT Baru", s.Name);
        Assert.Null(s.ContactPerson);
        Assert.Equal(0, s.PaymentTermDays);
        Assert.Equal("USD", s.DefaultCurrency);
        Assert.False(s.IsActive);
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter SupplierDomainTests`
Expected: FAIL kompilasi — `Supplier` belum ada.

- [ ] **Step 3: Implementasi entitas**

`src/MyApp.Domain/Entities/Supplier.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pemasok / vendor untuk transaksi pembelian.</summary>
public class Supplier : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? TaxId { get; private set; }
    public int PaymentTermDays { get; private set; }
    public string DefaultCurrency { get; private set; } = "IDR";
    public string? BankName { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? BankAccountName { get; private set; }
    public bool IsActive { get; private set; }

    private Supplier() { } // EF Core

    public Supplier(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        string? bankName, string? bankAccountNumber, string? bankAccountName, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, bankName, bankAccountNumber, bankAccountName, isActive);
    }

    public void Update(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        string? bankName, string? bankAccountNumber, string? bankAccountName, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, bankName, bankAccountNumber, bankAccountName, isActive);
    }

    private void Apply(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        string? bankName, string? bankAccountNumber, string? bankAccountName, bool isActive)
    {
        SetCode(code);
        SetName(name);
        ContactPerson = Clean(contactPerson);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        TaxId = Clean(taxId);
        SetPaymentTermDays(paymentTermDays);
        SetCurrency(defaultCurrency);
        BankName = Clean(bankName);
        BankAccountNumber = Clean(bankAccountNumber);
        BankAccountName = Clean(bankAccountName);
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

    private void SetPaymentTermDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Payment term days cannot be negative.", nameof(days));
        PaymentTermDays = days;
    }

    private void SetCurrency(string? currency) =>
        DefaultCurrency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter SupplierDomainTests`
Expected: PASS (5 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Domain/Entities/Supplier.cs tests/MyApp.UnitTests/SupplierDomainTests.cs
git commit -m "feat(domain): add Supplier entity"
```

---

### Task 2: Entitas Customer (Domain)

**Files:**
- Create: `src/MyApp.Domain/Entities/Customer.cs`
- Test: `tests/MyApp.UnitTests/CustomerDomainTests.cs`

**Interfaces:**
- Produces: `Customer` dengan ctor `Customer(string code, string name, string? contactPerson, string? phone, string? email, string? address, string? taxId, int paymentTermDays, string? defaultCurrency, decimal creditLimit, bool isActive)` dan `void Update(...)` parameter identik. Getter: `Id, Code, Name, ContactPerson, Phone, Email, Address, TaxId, PaymentTermDays, DefaultCurrency, CreditLimit, IsActive`.

- [ ] **Step 1: Tulis test yang gagal**

`tests/MyApp.UnitTests/CustomerDomainTests.cs`:
```csharp
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.UnitTests;

public class CustomerDomainTests
{
    private static Customer Make(string code = "cust-1", int term = 14, decimal limit = 1000m) =>
        new(code, "Toko Jaya", "Sari", "0813", "c@d.com", "Jl. Melati",
            "02.345", term, "idr", limit, true);

    [Fact]
    public void Ctor_normalizes_code_and_currency()
    {
        var c = Make();
        Assert.Equal("CUST-1", c.Code);
        Assert.Equal("IDR", c.DefaultCurrency);
    }

    [Fact]
    public void Ctor_rejects_negative_credit_limit()
    {
        Assert.Throws<ArgumentException>(() => Make(limit: -1m));
    }

    [Fact]
    public void Ctor_rejects_negative_payment_term()
    {
        Assert.Throws<ArgumentException>(() => Make(term: -5));
    }

    [Fact]
    public void Update_changes_fields()
    {
        var c = Make();
        c.Update("CUST-2", "Toko Baru", null, null, null, null, null, 30, "USD", 0m, false);
        Assert.Equal("CUST-2", c.Code);
        Assert.Equal(0m, c.CreditLimit);
        Assert.False(c.IsActive);
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter CustomerDomainTests`
Expected: FAIL kompilasi — `Customer` belum ada.

- [ ] **Step 3: Implementasi entitas**

`src/MyApp.Domain/Entities/Customer.cs`:
```csharp
using MyApp.Domain.Common;

namespace MyApp.Domain.Entities;

/// <summary>Pelanggan untuk transaksi penjualan.</summary>
public class Customer : AuditableEntity
{
    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? TaxId { get; private set; }
    public int PaymentTermDays { get; private set; }
    public string DefaultCurrency { get; private set; } = "IDR";
    public decimal CreditLimit { get; private set; }
    public bool IsActive { get; private set; }

    private Customer() { } // EF Core

    public Customer(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, creditLimit, isActive);
    }

    public void Update(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        Apply(code, name, contactPerson, phone, email, address, taxId, paymentTermDays,
            defaultCurrency, creditLimit, isActive);
    }

    private void Apply(string code, string name, string? contactPerson, string? phone, string? email,
        string? address, string? taxId, int paymentTermDays, string? defaultCurrency,
        decimal creditLimit, bool isActive)
    {
        SetCode(code);
        SetName(name);
        ContactPerson = Clean(contactPerson);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        TaxId = Clean(taxId);
        SetPaymentTermDays(paymentTermDays);
        SetCurrency(defaultCurrency);
        SetCreditLimit(creditLimit);
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

    private void SetPaymentTermDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Payment term days cannot be negative.", nameof(days));
        PaymentTermDays = days;
    }

    private void SetCreditLimit(decimal limit)
    {
        if (limit < 0)
            throw new ArgumentException("Credit limit cannot be negative.", nameof(limit));
        CreditLimit = limit;
    }

    private void SetCurrency(string? currency) =>
        DefaultCurrency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter CustomerDomainTests`
Expected: PASS (4 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Domain/Entities/Customer.cs tests/MyApp.UnitTests/CustomerDomainTests.cs
git commit -m "feat(domain): add Customer entity"
```

---

### Task 3: Application layer Supplier (DTO + interface + validator)

**Files:**
- Create: `src/MyApp.Application/Suppliers/SupplierDtos.cs`
- Create: `src/MyApp.Application/Suppliers/ISupplierService.cs`
- Create: `src/MyApp.Application/Suppliers/SupplierValidators.cs`
- Test: `tests/MyApp.UnitTests/SupplierValidatorTests.cs`

**Interfaces:**
- Consumes: `MyApp.Application.Common.PagedResult<T>` (sudah ada).
- Produces:
  - `SupplierDto(int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency, string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive, DateTime CreatedAt, string? CreatedBy)`
  - `CreateSupplierRequest(string Code, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency, string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive)`
  - `UpdateSupplierRequest(...)` — field identik dengan Create.
  - `ISupplierService` dengan `GetAllAsync`, `GetPagedAsync(int page, int pageSize, string? search, CancellationToken)`, `GetByIdAsync(int)`, `CreateAsync(CreateSupplierRequest)`, `UpdateAsync(int, UpdateSupplierRequest)`, `DeleteAsync(int)`.

- [ ] **Step 1: Tulis test validator yang gagal**

`tests/MyApp.UnitTests/SupplierValidatorTests.cs`:
```csharp
using FluentValidation.TestHelper;
using MyApp.Application.Suppliers;
using Xunit;

namespace MyApp.UnitTests;

public class SupplierValidatorTests
{
    private readonly CreateSupplierValidator _v = new();

    private static CreateSupplierRequest Valid() =>
        new("SUP-1", "PT Sumber", "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", 30, "IDR", "BCA", "123", "PT SM", true);

    [Fact]
    public void Valid_request_passes() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Blank_code_fails() =>
        _v.TestValidate(Valid() with { Code = "" }).ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Bad_code_chars_fail() =>
        _v.TestValidate(Valid() with { Code = "A B" }).ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Bad_email_fails() =>
        _v.TestValidate(Valid() with { Email = "not-an-email" }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Negative_term_fails() =>
        _v.TestValidate(Valid() with { PaymentTermDays = -1 }).ShouldHaveValidationErrorFor(x => x.PaymentTermDays);
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter SupplierValidatorTests`
Expected: FAIL kompilasi — tipe belum ada.

- [ ] **Step 3a: Buat DTOs**

`src/MyApp.Application/Suppliers/SupplierDtos.cs`:
```csharp
namespace MyApp.Application.Suppliers;

public record SupplierDto(
    int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive,
    DateTime CreatedAt, string? CreatedBy);

public record CreateSupplierRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive);

public record UpdateSupplierRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    string? BankName, string? BankAccountNumber, string? BankAccountName, bool IsActive);
```

- [ ] **Step 3b: Buat interface service**

`src/MyApp.Application/Suppliers/ISupplierService.cs`:
```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Suppliers;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<SupplierDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3c: Buat validators**

`src/MyApp.Application/Suppliers/SupplierValidators.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.Suppliers;

public class CreateSupplierValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierValidator() => RulesFor(this);

    internal static void RulesFor(AbstractValidator<CreateSupplierRequest> v)
    {
        v.RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        v.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        v.RuleFor(x => x.ContactPerson).MaximumLength(100);
        v.RuleFor(x => x.Phone).MaximumLength(30);
        v.RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.Address).MaximumLength(300);
        v.RuleFor(x => x.TaxId).MaximumLength(30);
        v.RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        v.RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        v.RuleFor(x => x.BankName).MaximumLength(100);
        v.RuleFor(x => x.BankAccountNumber).MaximumLength(50);
        v.RuleFor(x => x.BankAccountName).MaximumLength(100);
    }
}

public class UpdateSupplierValidator : AbstractValidator<UpdateSupplierRequest>
{
    public UpdateSupplierValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactPerson).MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.TaxId).MaximumLength(30);
        RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        RuleFor(x => x.BankName).MaximumLength(100);
        RuleFor(x => x.BankAccountNumber).MaximumLength(50);
        RuleFor(x => x.BankAccountName).MaximumLength(100);
    }
}
```

Note: `RulesFor` helper digunakan agar aturan Create tidak diduplikasi; `UpdateSupplierValidator` ditulis eksplisit karena tipenya berbeda (`UpdateSupplierRequest`). (FluentValidation tidak berbagi aturan antar tipe record yang berbeda.)

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter SupplierValidatorTests`
Expected: PASS (5 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Application/Suppliers tests/MyApp.UnitTests/SupplierValidatorTests.cs
git commit -m "feat(application): add Supplier DTOs, service interface, validators"
```

---

### Task 4: Application layer Customer (DTO + interface + validator)

**Files:**
- Create: `src/MyApp.Application/Customers/CustomerDtos.cs`
- Create: `src/MyApp.Application/Customers/ICustomerService.cs`
- Create: `src/MyApp.Application/Customers/CustomerValidators.cs`
- Test: `tests/MyApp.UnitTests/CustomerValidatorTests.cs`

**Interfaces:**
- Consumes: `MyApp.Application.Common.PagedResult<T>`.
- Produces:
  - `CustomerDto(int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency, decimal CreditLimit, bool IsActive, DateTime CreatedAt, string? CreatedBy)`
  - `CreateCustomerRequest(string Code, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency, decimal CreditLimit, bool IsActive)`
  - `UpdateCustomerRequest(...)` — identik dengan Create.
  - `ICustomerService` dengan signatur sama seperti `ISupplierService` (ganti tipe Supplier→Customer).

- [ ] **Step 1: Tulis test validator yang gagal**

`tests/MyApp.UnitTests/CustomerValidatorTests.cs`:
```csharp
using FluentValidation.TestHelper;
using MyApp.Application.Customers;
using Xunit;

namespace MyApp.UnitTests;

public class CustomerValidatorTests
{
    private readonly CreateCustomerValidator _v = new();

    private static CreateCustomerRequest Valid() =>
        new("CUST-1", "Toko Jaya", "Sari", "0813", "c@d.com", "Jl. Melati",
            "02.345", 14, "IDR", 1000m, true);

    [Fact]
    public void Valid_request_passes() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Blank_code_fails() =>
        _v.TestValidate(Valid() with { Code = "" }).ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Negative_credit_limit_fails() =>
        _v.TestValidate(Valid() with { CreditLimit = -1m }).ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact]
    public void Bad_email_fails() =>
        _v.TestValidate(Valid() with { Email = "nope" }).ShouldHaveValidationErrorFor(x => x.Email);
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.UnitTests --filter CustomerValidatorTests`
Expected: FAIL kompilasi.

- [ ] **Step 3a: Buat DTOs**

`src/MyApp.Application/Customers/CustomerDtos.cs`:
```csharp
namespace MyApp.Application.Customers;

public record CustomerDto(
    int Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string DefaultCurrency,
    decimal CreditLimit, bool IsActive, DateTime CreatedAt, string? CreatedBy);

public record CreateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive);

public record UpdateCustomerRequest(
    string Code, string Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? TaxId, int PaymentTermDays, string? DefaultCurrency,
    decimal CreditLimit, bool IsActive);
```

- [ ] **Step 3b: Buat interface service**

`src/MyApp.Application/Customers/ICustomerService.cs`:
```csharp
using MyApp.Application.Common;

namespace MyApp.Application.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<CustomerDto>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 3c: Buat validators**

`src/MyApp.Application/Customers/CustomerValidators.cs`:
```csharp
using FluentValidation;

namespace MyApp.Application.Customers;

public class CreateCustomerValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactPerson).MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.TaxId).MaximumLength(30);
        RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
    }
}

public class UpdateCustomerValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactPerson).MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.TaxId).MaximumLength(30);
        RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.UnitTests --filter CustomerValidatorTests`
Expected: PASS (4 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Application/Customers tests/MyApp.UnitTests/CustomerValidatorTests.cs
git commit -m "feat(application): add Customer DTOs, service interface, validators"
```

---

### Task 5: Mapping persistence (AppDbContext) untuk Supplier & Customer

**Files:**
- Modify: `src/MyApp.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Consumes: `Supplier`, `Customer` (Task 1, 2).
- Produces: `db.Suppliers` (`DbSet<Supplier>`), `db.Customers` (`DbSet<Customer>`).

- [ ] **Step 1: Tambah DbSet**

Di `AppDbContext.cs`, setelah baris `public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();` (baris 26), tambahkan:
```csharp
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
```

- [ ] **Step 2: Tambah konfigurasi fluent**

Di `OnModelCreating`, tepat sebelum blok `modelBuilder.Entity<ProductAttribute>(e =>` (baris 206), sisipkan:
```csharp
        modelBuilder.Entity<Supplier>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContactPerson).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(30);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(100);
            e.Property(x => x.BankAccountNumber).HasMaxLength(50);
            e.Property(x => x.BankAccountName).HasMaxLength(100);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContactPerson).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(30);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
        });
```

- [ ] **Step 3: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

- [ ] **Step 4: Commit**

```bash
git add src/MyApp.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat(persistence): map Supplier and Customer entities"
```

---

### Task 6: SupplierService + DI + integration test

**Files:**
- Create: `src/MyApp.Infrastructure/Services/SupplierService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/SupplierServiceTests.cs`

**Interfaces:**
- Consumes: `ISupplierService`, `CreateSupplierRequest`, `UpdateSupplierRequest`, `SupplierDto` (Task 3); `db.Suppliers` (Task 5); `IValidator<...>` (auto-registered via `AddValidatorsFromAssemblyContaining`).
- Produces: `SupplierService : ISupplierService`.

- [ ] **Step 1: Tulis integration test yang gagal**

`tests/MyApp.IntegrationTests/SupplierServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Suppliers;
using FluentValidation;
using Xunit;

namespace MyApp.IntegrationTests;

public class SupplierServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static CreateSupplierRequest New(string code, string name) =>
        new(code, name, "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", 30, "IDR", "BCA", "123", "PT SM", true);

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        var created = await svc.CreateAsync(New("sup-x", "PT Sumber"));
        Assert.Equal("SUP-X", created.Code);
        Assert.Equal(30, created.PaymentTermDays);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("PT Sumber", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        await svc.CreateAsync(New("SUP-DUP", "First"));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(New("sup-dup", "Second")));
    }

    [Fact]
    public async Task Update_And_Delete_Work()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        var created = await svc.CreateAsync(New("SUP-UPD", "Awal"));
        var ok = await svc.UpdateAsync(created.Id,
            new UpdateSupplierRequest("SUP-UPD", "Berubah", null, null, null, null, null, 0, "USD", null, null, null, false));
        Assert.True(ok);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Berubah", fetched!.Name);
        Assert.Equal("USD", fetched.DefaultCurrency);

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests --filter SupplierServiceTests`
Expected: FAIL — `ISupplierService` belum terdaftar di DI (resolve error) atau kompilasi gagal.

- [ ] **Step 3a: Implementasi service**

`src/MyApp.Infrastructure/Services/SupplierService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.Suppliers;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class SupplierService(
    AppDbContext db,
    IValidator<CreateSupplierRequest> createValidator,
    IValidator<UpdateSupplierRequest> updateValidator) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<SupplierDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<SupplierDto>(items, total, page, pageSize);
    }

    public async Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Supplier(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.BankName, request.BankAccountNumber, request.BankAccountName, request.IsActive);
        db.Suppliers.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Suppliers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.BankName, request.BankAccountNumber, request.BankAccountName, request.IsActive);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Suppliers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Suppliers.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateSupplierRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static SupplierDto ToDto(Supplier x) =>
        new(x.Id, x.Code, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.TaxId,
            x.PaymentTermDays, x.DefaultCurrency, x.BankName, x.BankAccountNumber, x.BankAccountName,
            x.IsActive, x.CreatedAt, x.CreatedBy);
}
```

- [ ] **Step 3b: Daftarkan di DI**

Di `src/MyApp.Infrastructure/DependencyInjection.cs`: tambahkan `using MyApp.Application.Suppliers;` di blok using, dan setelah baris `services.AddScoped<IPaymentMethodService, PaymentMethodService>();` tambahkan:
```csharp
        services.AddScoped<ISupplierService, SupplierService>();
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests --filter SupplierServiceTests`
Expected: PASS (3 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Infrastructure/Services/SupplierService.cs src/MyApp.Infrastructure/DependencyInjection.cs tests/MyApp.IntegrationTests/SupplierServiceTests.cs
git commit -m "feat(infrastructure): add SupplierService + DI registration"
```

---

### Task 7: CustomerService + DI + integration test

**Files:**
- Create: `src/MyApp.Infrastructure/Services/CustomerService.cs`
- Modify: `src/MyApp.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyApp.IntegrationTests/CustomerServiceTests.cs`

**Interfaces:**
- Consumes: `ICustomerService`, `CreateCustomerRequest`, `UpdateCustomerRequest`, `CustomerDto` (Task 4); `db.Customers` (Task 5).
- Produces: `CustomerService : ICustomerService`.

- [ ] **Step 1: Tulis integration test yang gagal**

`tests/MyApp.IntegrationTests/CustomerServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Customers;
using FluentValidation;
using Xunit;

namespace MyApp.IntegrationTests;

public class CustomerServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CustomerServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static CreateCustomerRequest New(string code, string name, decimal limit = 1000m) =>
        new(code, name, "Sari", "0813", "c@d.com", "Jl. Melati", "02.345", 14, "IDR", limit, true);

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await svc.CreateAsync(New("cust-x", "Toko Jaya"));
        Assert.Equal("CUST-X", created.Code);
        Assert.Equal(1000m, created.CreditLimit);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Toko Jaya", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        await svc.CreateAsync(New("CUST-DUP", "First"));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(New("cust-dup", "Second")));
    }

    [Fact]
    public async Task Update_And_Delete_Work()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await svc.CreateAsync(New("CUST-UPD", "Awal"));
        var ok = await svc.UpdateAsync(created.Id,
            new UpdateCustomerRequest("CUST-UPD", "Berubah", null, null, null, null, null, 30, "IDR", 5000m, false));
        Assert.True(ok);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Berubah", fetched!.Name);
        Assert.Equal(5000m, fetched.CreditLimit);

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal**

Run: `dotnet test tests/MyApp.IntegrationTests --filter CustomerServiceTests`
Expected: FAIL.

- [ ] **Step 3a: Implementasi service**

`src/MyApp.Infrastructure/Services/CustomerService.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.Customers;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Infrastructure.Services;

public class CustomerService(
    AppDbContext db,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);

    public async Task<PagedResult<CustomerDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, total, page, pageSize);
    }

    public async Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.Customers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCodeUniqueAsync(request.Code, null, ct);

        var entity = new Customer(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.CreditLimit, request.IsActive);
        db.Customers.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.Customers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        await EnsureCodeUniqueAsync(request.Code, id, ct);
        entity.Update(request.Code, request.Name, request.ContactPerson, request.Phone,
            request.Email, request.Address, request.TaxId, request.PaymentTermDays, request.DefaultCurrency,
            request.CreditLimit, request.IsActive);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Customers.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.Customers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var exists = await db.Customers.AsNoTracking()
            .AnyAsync(e => e.Code == normalized && (excludeId == null || e.Id != excludeId), ct);
        if (exists)
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(CreateCustomerRequest.Code), $"Code '{normalized}' is already in use.")
            ]);
    }

    private static CustomerDto ToDto(Customer x) =>
        new(x.Id, x.Code, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.TaxId,
            x.PaymentTermDays, x.DefaultCurrency, x.CreditLimit, x.IsActive, x.CreatedAt, x.CreatedBy);
}
```

- [ ] **Step 3b: Daftarkan di DI**

Di `src/MyApp.Infrastructure/DependencyInjection.cs`: tambahkan `using MyApp.Application.Customers;` dan setelah baris `services.AddScoped<ISupplierService, SupplierService>();` tambahkan:
```csharp
        services.AddScoped<ICustomerService, CustomerService>();
```

- [ ] **Step 4: Jalankan test, pastikan lulus**

Run: `dotnet test tests/MyApp.IntegrationTests --filter CustomerServiceTests`
Expected: PASS (3 test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Infrastructure/Services/CustomerService.cs src/MyApp.Infrastructure/DependencyInjection.cs tests/MyApp.IntegrationTests/CustomerServiceTests.cs
git commit -m "feat(infrastructure): add CustomerService + DI registration"
```

---

### Task 8: EF migration (tabel Suppliers & Customers)

**Files:**
- Create: `src/MyApp.Infrastructure/Persistence/Migrations/<timestamp>_AddSupplierAndCustomer.cs` (digenerate)
- Modify: `src/MyApp.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (digenerate)

**Interfaces:**
- Consumes: mapping dari Task 5.

- [ ] **Step 1: Generate migration**

Run:
```bash
dotnet ef migrations add AddSupplierAndCustomer --project src/MyApp.Infrastructure --startup-project src/MyApp.Web --output-dir Persistence/Migrations
```
Expected: file migration baru + snapshot terupdate, build succeeded.

- [ ] **Step 2: Verifikasi isi migration**

Buka file `<timestamp>_AddSupplierAndCustomer.cs`. Pastikan `Up()` membuat tabel `Suppliers` dan `Customers` dengan kolom sesuai (termasuk `CreditLimit decimal(18,2)`, `DefaultCurrency nvarchar(3)`, index unik pada `Code`, kolom audit `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy`). Tidak boleh ada perubahan ke tabel lain (stok/produk).

- [ ] **Step 3: Terapkan ke database & verifikasi**

Run:
```bash
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.Web
```
Expected: "Done." tanpa error. (Jika koneksi DB lokal tidak tersedia, lewati update; integration test memakai database test sendiri via `CustomWebApplicationFactory`.)

- [ ] **Step 4: Jalankan seluruh test**

Run: `dotnet test`
Expected: semua test PASS (test factory menerapkan migration ke DB test).

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Infrastructure/Persistence/Migrations
git commit -m "feat(persistence): add migration for Suppliers and Customers"
```

---

### Task 9: Daftarkan permission (AppMenus) + policy `transactions.any`

**Files:**
- Modify: `src/MyApp.Web/Authorization/AppMenus.cs`
- Modify: `src/MyApp.Web/Program.cs`

**Interfaces:**
- Produces: permission `master.suppliers.{index,create,edit,delete}`, `master.customers.{...}`, `transactions.hub.index`, `transactions.purchase-orders.index`, `transactions.sales-orders.index`; policy `transactions.any`.

- [ ] **Step 1: Tambah resource Master di AppMenus**

Di `src/MyApp.Web/Authorization/AppMenus.cs`, di dalam grup `"Master"` (setelah baris `new("master.attributes", ...)`), tambahkan:
```csharp
            new("master.suppliers",  "Supplier",  "bi-truck",         CRUD),
            new("master.customers",  "Customer",  "bi-person-vcard-fill", CRUD),
```

- [ ] **Step 2: Tambah grup Transaksi di AppMenus**

Di `AppMenus.cs`, tambahkan grup baru pada array `Groups` setelah grup `"Inventory"` (sebelum grup `"Settings"`):
```csharp
        new("Transaksi",
        [
            new("transactions.hub",             "Transaksi",      "bi-grid-1x2-fill",      ViewOnly),
            new("transactions.purchase-orders", "Purchase Order", "bi-cart-plus-fill",     ViewOnly),
            new("transactions.sales-orders",    "Sales Order",    "bi-bag-check-fill",     ViewOnly),
        ]),
```

- [ ] **Step 3: Tambah policy `transactions.any` di Program.cs**

Di `src/MyApp.Web/Program.cs`, setelah blok `options.AddPolicy("inventory.any", ...)` (berakhir ~baris 102), tambahkan:
```csharp
    // Tampilkan grup Transaksi jika memiliki setidaknya satu izin transactions.*.index
    options.AddPolicy("transactions.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("transactions."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));
```

- [ ] **Step 4: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning. (Permission baru otomatis di-grant ke role admin saat startup berikutnya via `BootstrapSeeder`.)

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Web/Authorization/AppMenus.cs src/MyApp.Web/Program.cs
git commit -m "feat(web): register supplier/customer/transactions permissions and policy"
```

---

### Task 10: Halaman Web Supplier (Index + Form) + entri NavMenu

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Master/Suppliers/SupplierIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Suppliers/SupplierForm.razor`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `ISupplierService`, DTO Supplier (Task 3); `SwalService`, `Pager`, `AppMenus` (existing); permission `master.suppliers.*` (Task 9).

- [ ] **Step 1: Buat halaman Index**

`src/MyApp.Web/Components/Pages/Master/Suppliers/SupplierIndex.razor`:
```razor
@page "/master/suppliers"
@attribute [Authorize(Policy = "master.suppliers.index")]
@rendermode InteractiveServer
@inject ISupplierService SupplierService
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>Suppliers</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0 fw-semibold">Suppliers</h1>
    <AuthorizeView Policy="master.suppliers.create">
        <Authorized>
            <a class="btn btn-primary btn-sm" href="/master/suppliers/new">
                <i class="bi bi-plus-lg me-1"></i>Add New
            </a>
        </Authorized>
    </AuthorizeView>
</div>

<div class="search-card mb-4">
    <input class="form-control" placeholder="Search code or name..."
           @bind="_search" @bind:event="oninput" @onkeyup="OnSearchKeyUp" />
</div>

@if (_page is null)
{
    <div class="text-center py-5 text-muted">
        <div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...
    </div>
}
else if (_page.Total == 0)
{
    <div class="empty-state">
        <div class="empty-icon">&#128230;</div>
        <p class="empty-text">
            @if (string.IsNullOrEmpty(_search))
            {
                <span>No suppliers found. <a href="/master/suppliers/new">Add the first one.</a></span>
            }
            else
            {
                <span>No results for "<strong>@_search</strong>".</span>
            }
        </p>
    </div>
}
else
{
    <div class="data-card">
        <div class="data-card-header">
            <span class="text-muted small">
                Showing @((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total) of @_page.Total
            </span>
        </div>
        <div class="table-responsive">
            <table class="table table-hover align-middle mb-0">
                <thead class="table-head">
                    <tr>
                        <th class="ps-3" style="width:60px">#</th>
                        <th style="width:140px">Code</th>
                        <th>Name</th>
                        <th>Contact</th>
                        <th>Phone</th>
                        <th style="width:80px">Active</th>
                        <th class="text-end pe-3" style="width:120px"></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in _page.Items)
                    {
                        <tr>
                            <td class="ps-3 text-muted small">@item.Id</td>
                            <td><span class="badge bg-light text-dark border">@item.Code</span></td>
                            <td class="fw-medium">@item.Name</td>
                            <td class="text-muted small">@(item.ContactPerson ?? "—")</td>
                            <td class="text-muted small">@(item.Phone ?? "—")</td>
                            <td>
                                @if (item.IsActive)
                                {
                                    <span class="badge bg-success">Active</span>
                                }
                                else
                                {
                                    <span class="badge bg-secondary">Inactive</span>
                                }
                            </td>
                            <td class="text-end pe-3 text-nowrap">
                                <AuthorizeView Policy="master.suppliers.edit">
                                    <Authorized>
                                        <a class="btn btn-sm btn-outline-primary me-1"
                                           href="@($"/master/suppliers/{item.Id}/edit")">
                                            <i class="bi bi-pencil"></i>
                                        </a>
                                    </Authorized>
                                </AuthorizeView>
                                <AuthorizeView Policy="master.suppliers.delete">
                                    <Authorized>
                                        <button class="btn btn-sm btn-outline-danger"
                                                @onclick="() => DeleteAsync(item.Id, item.Name)" title="Delete">
                                            <i class="bi bi-trash3"></i>
                                        </button>
                                    </Authorized>
                                </AuthorizeView>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>

        @if (_page.TotalPages > 1)
        {
            <div class="data-card-footer d-flex justify-content-end">
                <Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" />
            </div>
        }
    </div>
}

@code {
    private const int PageSize = 15;
    private PagedResult<SupplierDto>? _page;
    private int _currentPage = 1;
    private string? _search;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        _page = await SupplierService.GetPagedAsync(_currentPage, PageSize, _search);

    private async Task OnSearchKeyUp(KeyboardEventArgs e)
    {
        _currentPage = 1;
        await LoadAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _currentPage = page;
        await LoadAsync();
    }

    private async Task DeleteAsync(int id, string name)
    {
        if (!await Swal.ConfirmAsync("Delete supplier?", $"\"{name}\" will be permanently removed."))
            return;

        await SupplierService.DeleteAsync(id);
        await LoadAsync();
        await Swal.ToastAsync("success", "Supplier deleted");
    }
}
```

- [ ] **Step 2: Buat halaman Form**

`src/MyApp.Web/Components/Pages/Master/Suppliers/SupplierForm.razor`:
```razor
@page "/master/suppliers/new"
@page "/master/suppliers/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@inject ISupplierService SupplierService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/master/suppliers"><i class="bi bi-arrow-left me-1"></i>Back</a>
    <h4 class="uf-title">@Title</h4>
</div>

@if (_loading)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else if (_notFound)
{
    <div class="alert alert-warning">Supplier not found.</div>
}
else
{
    <div class="row g-4">
        <div class="col-12 col-lg-9 col-xl-8">
            @if (_error is not null)
            {
                <div class="alert alert-danger d-flex align-items-center gap-2 mb-3 py-2">
                    <span>&#9888;</span> @_error
                </div>
            }

            <div class="fs-card mb-4">
                <div class="fs-card-title">Supplier Information</div>
                <div class="row g-3">
                    <div class="col-12 col-md-4">
                        <label class="form-label lbl-required">Code</label>
                        <input class="form-control text-uppercase @(_codeError is not null ? "is-invalid" : "")"
                               placeholder="e.g. SUP-001" @bind="_code" @bind:event="oninput" maxlength="20" />
                        @if (_codeError is not null) { <div class="invalid-feedback">@_codeError</div> }
                    </div>
                    <div class="col-12 col-md-8">
                        <label class="form-label lbl-required">Name</label>
                        <input class="form-control @(_nameError is not null ? "is-invalid" : "")"
                               placeholder="e.g. PT Sumber Makmur" @bind="_name" @bind:event="oninput" maxlength="100" />
                        @if (_nameError is not null) { <div class="invalid-feedback">@_nameError</div> }
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Contact Person</label>
                        <input class="form-control" @bind="_contactPerson" maxlength="100" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Phone</label>
                        <input class="form-control" @bind="_phone" maxlength="30" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Email</label>
                        <input class="form-control @(_emailError is not null ? "is-invalid" : "")"
                               @bind="_email" @bind:event="oninput" maxlength="100" />
                        @if (_emailError is not null) { <div class="invalid-feedback">@_emailError</div> }
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Tax ID (NPWP)</label>
                        <input class="form-control" @bind="_taxId" maxlength="30" />
                    </div>
                    <div class="col-12">
                        <label class="form-label">Address</label>
                        <textarea class="form-control" rows="2" maxlength="300" @bind="_address"></textarea>
                    </div>
                </div>
            </div>

            <div class="fs-card mb-4">
                <div class="fs-card-title">Finance</div>
                <div class="row g-3">
                    <div class="col-12 col-md-4">
                        <label class="form-label">Payment Term (days)</label>
                        <input type="number" min="0" class="form-control" @bind="_paymentTermDays" />
                    </div>
                    <div class="col-12 col-md-4">
                        <label class="form-label">Default Currency</label>
                        <input class="form-control text-uppercase" @bind="_currency" maxlength="3" placeholder="IDR" />
                    </div>
                    <div class="col-12 col-md-4">
                        <label class="form-label">Bank Name</label>
                        <input class="form-control" @bind="_bankName" maxlength="100" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Bank Account Number</label>
                        <input class="form-control" @bind="_bankAccountNumber" maxlength="50" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Bank Account Name</label>
                        <input class="form-control" @bind="_bankAccountName" maxlength="100" />
                    </div>
                    <div class="col-12">
                        <div class="form-check">
                            <input class="form-check-input" type="checkbox" id="chkActive" @bind="_isActive" />
                            <label class="form-check-label" for="chkActive">Active</label>
                        </div>
                    </div>
                </div>
            </div>

            <div class="d-flex gap-2 justify-content-end pt-1">
                <button class="btn btn-primary btn-sm px-3" @onclick="SaveAsync" disabled="@_saving">
                    @if (_saving)
                    {
                        <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                    }
                    else
                    {
                        <i class="bi bi-floppy2-fill me-1"></i>
                    }
                    Save
                </button>
                <a class="btn btn-outline-secondary btn-sm" href="/master/suppliers">
                    <i class="bi bi-x-lg me-1"></i>Cancel
                </a>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public int? Id { get; set; }

    private string _code = string.Empty;
    private string _name = string.Empty;
    private string? _contactPerson, _phone, _email, _address, _taxId;
    private int _paymentTermDays;
    private string _currency = "IDR";
    private string? _bankName, _bankAccountNumber, _bankAccountName;
    private bool _isActive = true;
    private bool _loading = true, _saving, _notFound;
    private string? _error, _codeError, _nameError, _emailError;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private string Title => Id is null ? "Add Supplier" : "Edit Supplier";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null
            ? AppMenus.Perm("master.suppliers", "create")
            : AppMenus.Perm("master.suppliers", "edit");

        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded)
        {
            Nav.NavigateTo("/master/suppliers");
            return;
        }

        if (Id is int id)
        {
            var s = await SupplierService.GetByIdAsync(id);
            if (s is null) { _notFound = true; }
            else
            {
                _code = s.Code; _name = s.Name; _contactPerson = s.ContactPerson; _phone = s.Phone;
                _email = s.Email; _address = s.Address; _taxId = s.TaxId; _paymentTermDays = s.PaymentTermDays;
                _currency = s.DefaultCurrency; _bankName = s.BankName; _bankAccountNumber = s.BankAccountNumber;
                _bankAccountName = s.BankAccountName; _isActive = s.IsActive;
            }
        }

        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = _codeError = _nameError = _emailError = null;
        _saving = true;
        try
        {
            if (Id is int id)
                await SupplierService.UpdateAsync(id, new UpdateSupplierRequest(_code, _name, _contactPerson, _phone,
                    _email, _address, _taxId, _paymentTermDays, _currency, _bankName, _bankAccountNumber, _bankAccountName, _isActive));
            else
                await SupplierService.CreateAsync(new CreateSupplierRequest(_code, _name, _contactPerson, _phone,
                    _email, _address, _taxId, _paymentTermDays, _currency, _bankName, _bankAccountNumber, _bankAccountName, _isActive));

            Nav.NavigateTo("/master/suppliers");
        }
        catch (ValidationException ex)
        {
            foreach (var e in ex.Errors)
            {
                if (e.PropertyName == nameof(CreateSupplierRequest.Code)) _codeError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateSupplierRequest.Name)) _nameError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateSupplierRequest.Email)) _emailError = e.ErrorMessage;
                else _error = e.ErrorMessage;
            }
        }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Tambah entri NavMenu (grup Master)**

Di `src/MyApp.Web/Components/Layout/NavMenu.razor`, di dalam `<div class="nav-group-items">` grup Master, setelah blok `AuthorizeView Policy="master.attributes.index"` (sebelum penutup `</div>` grup), tambahkan:
```razor
                <AuthorizeView Policy="master.suppliers.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="master/suppliers" title="Supplier">
                                <i class="bi bi-truck nav-icon" aria-hidden="true"></i> <span class="nav-label">Supplier</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
                <AuthorizeView Policy="master.customers.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="master/customers" title="Customer">
                                <i class="bi bi-person-vcard-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Customer</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
```

- [ ] **Step 4: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

- [ ] **Step 5: Commit**

```bash
git add src/MyApp.Web/Components/Pages/Master/Suppliers src/MyApp.Web/Components/Layout/NavMenu.razor
git commit -m "feat(web): add Supplier index/form pages and nav entry"
```

---

### Task 11: Halaman Web Customer (Index + Form) + entri NavMenu

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Master/Customers/CustomerIndex.razor`
- Create: `src/MyApp.Web/Components/Pages/Master/Customers/CustomerForm.razor`

**Interfaces:**
- Consumes: `ICustomerService`, DTO Customer (Task 4); permission `master.customers.*` (Task 9); entri NavMenu Customer sudah ditambah di Task 10 Step 3.

- [ ] **Step 1: Buat halaman Index**

`src/MyApp.Web/Components/Pages/Master/Customers/CustomerIndex.razor`:
```razor
@page "/master/customers"
@attribute [Authorize(Policy = "master.customers.index")]
@rendermode InteractiveServer
@inject ICustomerService CustomerService
@inject NavigationManager Nav
@inject SwalService Swal

<PageTitle>Customers</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0 fw-semibold">Customers</h1>
    <AuthorizeView Policy="master.customers.create">
        <Authorized>
            <a class="btn btn-primary btn-sm" href="/master/customers/new">
                <i class="bi bi-plus-lg me-1"></i>Add New
            </a>
        </Authorized>
    </AuthorizeView>
</div>

<div class="search-card mb-4">
    <input class="form-control" placeholder="Search code or name..."
           @bind="_search" @bind:event="oninput" @onkeyup="OnSearchKeyUp" />
</div>

@if (_page is null)
{
    <div class="text-center py-5 text-muted">
        <div class="spinner-border spinner-border-sm me-2" role="status"></div>Loading...
    </div>
}
else if (_page.Total == 0)
{
    <div class="empty-state">
        <div class="empty-icon">&#129489;</div>
        <p class="empty-text">
            @if (string.IsNullOrEmpty(_search))
            {
                <span>No customers found. <a href="/master/customers/new">Add the first one.</a></span>
            }
            else
            {
                <span>No results for "<strong>@_search</strong>".</span>
            }
        </p>
    </div>
}
else
{
    <div class="data-card">
        <div class="data-card-header">
            <span class="text-muted small">
                Showing @((_page.Page - 1) * PageSize + 1)–@Math.Min(_page.Page * PageSize, _page.Total) of @_page.Total
            </span>
        </div>
        <div class="table-responsive">
            <table class="table table-hover align-middle mb-0">
                <thead class="table-head">
                    <tr>
                        <th class="ps-3" style="width:60px">#</th>
                        <th style="width:140px">Code</th>
                        <th>Name</th>
                        <th>Contact</th>
                        <th>Phone</th>
                        <th style="width:80px">Active</th>
                        <th class="text-end pe-3" style="width:120px"></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in _page.Items)
                    {
                        <tr>
                            <td class="ps-3 text-muted small">@item.Id</td>
                            <td><span class="badge bg-light text-dark border">@item.Code</span></td>
                            <td class="fw-medium">@item.Name</td>
                            <td class="text-muted small">@(item.ContactPerson ?? "—")</td>
                            <td class="text-muted small">@(item.Phone ?? "—")</td>
                            <td>
                                @if (item.IsActive)
                                {
                                    <span class="badge bg-success">Active</span>
                                }
                                else
                                {
                                    <span class="badge bg-secondary">Inactive</span>
                                }
                            </td>
                            <td class="text-end pe-3 text-nowrap">
                                <AuthorizeView Policy="master.customers.edit">
                                    <Authorized>
                                        <a class="btn btn-sm btn-outline-primary me-1"
                                           href="@($"/master/customers/{item.Id}/edit")">
                                            <i class="bi bi-pencil"></i>
                                        </a>
                                    </Authorized>
                                </AuthorizeView>
                                <AuthorizeView Policy="master.customers.delete">
                                    <Authorized>
                                        <button class="btn btn-sm btn-outline-danger"
                                                @onclick="() => DeleteAsync(item.Id, item.Name)" title="Delete">
                                            <i class="bi bi-trash3"></i>
                                        </button>
                                    </Authorized>
                                </AuthorizeView>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>

        @if (_page.TotalPages > 1)
        {
            <div class="data-card-footer d-flex justify-content-end">
                <Pager Page="_page.Page" TotalPages="_page.TotalPages" OnPageChanged="GoToPageAsync" />
            </div>
        }
    </div>
}

@code {
    private const int PageSize = 15;
    private PagedResult<CustomerDto>? _page;
    private int _currentPage = 1;
    private string? _search;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        _page = await CustomerService.GetPagedAsync(_currentPage, PageSize, _search);

    private async Task OnSearchKeyUp(KeyboardEventArgs e)
    {
        _currentPage = 1;
        await LoadAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _currentPage = page;
        await LoadAsync();
    }

    private async Task DeleteAsync(int id, string name)
    {
        if (!await Swal.ConfirmAsync("Delete customer?", $"\"{name}\" will be permanently removed."))
            return;

        await CustomerService.DeleteAsync(id);
        await LoadAsync();
        await Swal.ToastAsync("success", "Customer deleted");
    }
}
```

- [ ] **Step 2: Buat halaman Form**

`src/MyApp.Web/Components/Pages/Master/Customers/CustomerForm.razor`:
```razor
@page "/master/customers/new"
@page "/master/customers/{Id:int}/edit"
@attribute [Authorize]
@rendermode InteractiveServer
@using FluentValidation
@inject ICustomerService CustomerService
@inject IAuthorizationService Auth
@inject NavigationManager Nav

<PageTitle>@Title</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/master/customers"><i class="bi bi-arrow-left me-1"></i>Back</a>
    <h4 class="uf-title">@Title</h4>
</div>

@if (_loading)
{
    <div class="text-center py-5"><div class="spinner-border text-primary" role="status"></div></div>
}
else if (_notFound)
{
    <div class="alert alert-warning">Customer not found.</div>
}
else
{
    <div class="row g-4">
        <div class="col-12 col-lg-9 col-xl-8">
            @if (_error is not null)
            {
                <div class="alert alert-danger d-flex align-items-center gap-2 mb-3 py-2">
                    <span>&#9888;</span> @_error
                </div>
            }

            <div class="fs-card mb-4">
                <div class="fs-card-title">Customer Information</div>
                <div class="row g-3">
                    <div class="col-12 col-md-4">
                        <label class="form-label lbl-required">Code</label>
                        <input class="form-control text-uppercase @(_codeError is not null ? "is-invalid" : "")"
                               placeholder="e.g. CUST-001" @bind="_code" @bind:event="oninput" maxlength="20" />
                        @if (_codeError is not null) { <div class="invalid-feedback">@_codeError</div> }
                    </div>
                    <div class="col-12 col-md-8">
                        <label class="form-label lbl-required">Name</label>
                        <input class="form-control @(_nameError is not null ? "is-invalid" : "")"
                               placeholder="e.g. Toko Jaya" @bind="_name" @bind:event="oninput" maxlength="100" />
                        @if (_nameError is not null) { <div class="invalid-feedback">@_nameError</div> }
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Contact Person</label>
                        <input class="form-control" @bind="_contactPerson" maxlength="100" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Phone</label>
                        <input class="form-control" @bind="_phone" maxlength="30" />
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Email</label>
                        <input class="form-control @(_emailError is not null ? "is-invalid" : "")"
                               @bind="_email" @bind:event="oninput" maxlength="100" />
                        @if (_emailError is not null) { <div class="invalid-feedback">@_emailError</div> }
                    </div>
                    <div class="col-12 col-md-6">
                        <label class="form-label">Tax ID (NPWP)</label>
                        <input class="form-control" @bind="_taxId" maxlength="30" />
                    </div>
                    <div class="col-12">
                        <label class="form-label">Address</label>
                        <textarea class="form-control" rows="2" maxlength="300" @bind="_address"></textarea>
                    </div>
                </div>
            </div>

            <div class="fs-card mb-4">
                <div class="fs-card-title">Finance</div>
                <div class="row g-3">
                    <div class="col-12 col-md-4">
                        <label class="form-label">Payment Term (days)</label>
                        <input type="number" min="0" class="form-control" @bind="_paymentTermDays" />
                    </div>
                    <div class="col-12 col-md-4">
                        <label class="form-label">Default Currency</label>
                        <input class="form-control text-uppercase" @bind="_currency" maxlength="3" placeholder="IDR" />
                    </div>
                    <div class="col-12 col-md-4">
                        <label class="form-label">Credit Limit</label>
                        <input type="number" min="0" step="0.01" class="form-control" @bind="_creditLimit" />
                    </div>
                    <div class="col-12">
                        <div class="form-check">
                            <input class="form-check-input" type="checkbox" id="chkActive" @bind="_isActive" />
                            <label class="form-check-label" for="chkActive">Active</label>
                        </div>
                    </div>
                </div>
            </div>

            <div class="d-flex gap-2 justify-content-end pt-1">
                <button class="btn btn-primary btn-sm px-3" @onclick="SaveAsync" disabled="@_saving">
                    @if (_saving)
                    {
                        <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                    }
                    else
                    {
                        <i class="bi bi-floppy2-fill me-1"></i>
                    }
                    Save
                </button>
                <a class="btn btn-outline-secondary btn-sm" href="/master/customers">
                    <i class="bi bi-x-lg me-1"></i>Cancel
                </a>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public int? Id { get; set; }

    private string _code = string.Empty;
    private string _name = string.Empty;
    private string? _contactPerson, _phone, _email, _address, _taxId;
    private int _paymentTermDays;
    private string _currency = "IDR";
    private decimal _creditLimit;
    private bool _isActive = true;
    private bool _loading = true, _saving, _notFound;
    private string? _error, _codeError, _nameError, _emailError;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private string Title => Id is null ? "Add Customer" : "Edit Customer";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateTask;
        var perm = Id is null
            ? AppMenus.Perm("master.customers", "create")
            : AppMenus.Perm("master.customers", "edit");

        if (!(await Auth.AuthorizeAsync(state.User, perm)).Succeeded)
        {
            Nav.NavigateTo("/master/customers");
            return;
        }

        if (Id is int id)
        {
            var c = await CustomerService.GetByIdAsync(id);
            if (c is null) { _notFound = true; }
            else
            {
                _code = c.Code; _name = c.Name; _contactPerson = c.ContactPerson; _phone = c.Phone;
                _email = c.Email; _address = c.Address; _taxId = c.TaxId; _paymentTermDays = c.PaymentTermDays;
                _currency = c.DefaultCurrency; _creditLimit = c.CreditLimit; _isActive = c.IsActive;
            }
        }

        _loading = false;
    }

    private async Task SaveAsync()
    {
        _error = _codeError = _nameError = _emailError = null;
        _saving = true;
        try
        {
            if (Id is int id)
                await CustomerService.UpdateAsync(id, new UpdateCustomerRequest(_code, _name, _contactPerson, _phone,
                    _email, _address, _taxId, _paymentTermDays, _currency, _creditLimit, _isActive));
            else
                await CustomerService.CreateAsync(new CreateCustomerRequest(_code, _name, _contactPerson, _phone,
                    _email, _address, _taxId, _paymentTermDays, _currency, _creditLimit, _isActive));

            Nav.NavigateTo("/master/customers");
        }
        catch (ValidationException ex)
        {
            foreach (var e in ex.Errors)
            {
                if (e.PropertyName == nameof(CreateCustomerRequest.Code)) _codeError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateCustomerRequest.Name)) _nameError = e.ErrorMessage;
                else if (e.PropertyName == nameof(CreateCustomerRequest.Email)) _emailError = e.ErrorMessage;
                else _error = e.ErrorMessage;
            }
        }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

- [ ] **Step 4: Commit**

```bash
git add src/MyApp.Web/Components/Pages/Master/Customers
git commit -m "feat(web): add Customer index/form pages"
```

---

### Task 12: Hub Transaksi (page + CSS) + grup nav + halaman placeholder PO/SO

**Files:**
- Create: `src/MyApp.Web/Components/Pages/Transactions/TransactionsHub.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/TransactionsHub.razor.css`
- Create: `src/MyApp.Web/Components/Pages/Transactions/PurchaseOrderPlaceholder.razor`
- Create: `src/MyApp.Web/Components/Pages/Transactions/SalesOrderPlaceholder.razor`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: permission `transactions.hub.index`, `transactions.purchase-orders.index`, `transactions.sales-orders.index`, policy `transactions.any` (Task 9).
- Produces: route `/transactions`, `/transactions/purchase-orders`, `/transactions/sales-orders`.

- [ ] **Step 1: Buat halaman hub**

`src/MyApp.Web/Components/Pages/Transactions/TransactionsHub.razor`:
```razor
@page "/transactions"
@attribute [Authorize(Policy = "transactions.hub.index")]
@rendermode InteractiveServer

<PageTitle>Transaksi</PageTitle>

<section class="txhub">
    <p class="txhub-eyebrow">Transaksi</p>
    <h1 class="txhub-title">Pusat <span class="accent">Transaksi</span></h1>
    <p class="txhub-sub">Pilih jenis transaksi untuk memulai — pembelian dari supplier atau penjualan ke customer.</p>

    <div class="txhub-grid">
        <AuthorizeView Policy="transactions.purchase-orders.index">
            <Authorized>
                <a class="txhub-card" href="/transactions/purchase-orders" aria-label="Buka Purchase Order">
                    <span class="txhub-icon">
                        <i class="bi bi-cart-plus-fill"></i>
                    </span>
                    <h3>Purchase Order</h3>
                    <p>Buat dan kelola pesanan pembelian ke supplier.</p>
                    <span class="txhub-go">Buka Purchase Order <i class="bi bi-arrow-right"></i></span>
                </a>
            </Authorized>
        </AuthorizeView>

        <AuthorizeView Policy="transactions.sales-orders.index">
            <Authorized>
                <a class="txhub-card" href="/transactions/sales-orders" aria-label="Buka Sales Order">
                    <span class="txhub-icon">
                        <i class="bi bi-bag-check-fill"></i>
                    </span>
                    <h3>Sales Order</h3>
                    <p>Buat dan kelola pesanan penjualan ke customer.</p>
                    <span class="txhub-go">Buka Sales Order <i class="bi bi-arrow-right"></i></span>
                </a>
            </Authorized>
        </AuthorizeView>
    </div>
</section>
```

- [ ] **Step 2: Buat CSS scoped (gaya mockup)**

`src/MyApp.Web/Components/Pages/Transactions/TransactionsHub.razor.css`:
```css
.txhub {
    --tx-blue: #3771EC;
    --tx-blue-soft: #EAF1FE;
    --tx-line: #E6E9EF;
    --tx-muted: #5B6472;
    max-width: 900px;
    margin: 0 auto;
    text-align: center;
    padding-top: 24px;
}

.txhub-eyebrow {
    font-size: 12px; font-weight: 700; letter-spacing: .22em;
    text-transform: uppercase; color: var(--tx-blue); margin: 0 0 12px;
}

.txhub-title {
    margin: 0; font-weight: 900; letter-spacing: -0.03em; line-height: 1.02;
    font-size: clamp(28px, 4vw, 42px);
}
.txhub-title .accent { color: var(--tx-blue); }

.txhub-sub {
    margin: 14px auto 0; max-width: 460px; font-size: 16px; color: var(--tx-muted);
}

.txhub-grid {
    display: grid; gap: 20px; grid-template-columns: repeat(2, 1fr);
    padding: 36px 0 24px;
}
@media (max-width: 680px) { .txhub-grid { grid-template-columns: 1fr; } }

.txhub-card {
    position: relative; overflow: hidden; text-decoration: none; color: inherit;
    background: #fff; border: 1px solid var(--tx-line); border-radius: 18px;
    padding: 26px 24px 24px; display: flex; flex-direction: column; gap: 12px; text-align: left;
    transition: transform .18s ease, box-shadow .18s ease, border-color .18s ease;
}
.txhub-card::before {
    content: ""; position: absolute; left: 0; top: 0; bottom: 0; width: 4px;
    background: var(--tx-blue); transform: scaleY(0); transform-origin: top; transition: transform .2s ease;
}
.txhub-card:hover {
    transform: translateY(-6px); box-shadow: 0 18px 38px rgba(26,32,44,.12); border-color: transparent;
}
.txhub-card:hover::before { transform: scaleY(1); }

.txhub-icon {
    width: 56px; height: 56px; border-radius: 14px; background: var(--tx-blue-soft);
    display: grid; place-items: center; color: #171f33; font-size: 26px;
}
.txhub-card h3 { margin: 0; font-size: 21px; font-weight: 800; letter-spacing: -0.01em; }
.txhub-card p { margin: 0; font-size: 14px; color: var(--tx-muted); }
.txhub-go {
    margin-top: 4px; font-size: 13px; font-weight: 700; color: var(--tx-blue);
    display: inline-flex; align-items: center; gap: 6px;
}
```

- [ ] **Step 3: Buat halaman placeholder PO & SO**

`src/MyApp.Web/Components/Pages/Transactions/PurchaseOrderPlaceholder.razor`:
```razor
@page "/transactions/purchase-orders"
@attribute [Authorize(Policy = "transactions.purchase-orders.index")]
@rendermode InteractiveServer

<PageTitle>Purchase Order</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/transactions"><i class="bi bi-arrow-left me-1"></i>Transaksi</a>
    <h4 class="uf-title">Purchase Order</h4>
</div>

<div class="alert alert-info d-flex align-items-center gap-2">
    <i class="bi bi-tools"></i>
    <span>Modul Purchase Order sedang dikembangkan (Tahap B).</span>
</div>
```

`src/MyApp.Web/Components/Pages/Transactions/SalesOrderPlaceholder.razor`:
```razor
@page "/transactions/sales-orders"
@attribute [Authorize(Policy = "transactions.sales-orders.index")]
@rendermode InteractiveServer

<PageTitle>Sales Order</PageTitle>

<div class="uf-header mb-4">
    <a class="back-link" href="/transactions"><i class="bi bi-arrow-left me-1"></i>Transaksi</a>
    <h4 class="uf-title">Sales Order</h4>
</div>

<div class="alert alert-info d-flex align-items-center gap-2">
    <i class="bi bi-tools"></i>
    <span>Modul Sales Order sedang dikembangkan (Tahap C).</span>
</div>
```

- [ ] **Step 4: Tambah grup nav Transaksi**

Di `src/MyApp.Web/Components/Layout/NavMenu.razor`, setelah grup `inventory` (penutup `</AuthorizeView>` di sekitar baris 134, sebelum grup `settings`), tambahkan:
```razor
        <AuthorizeView Policy="transactions.any" Context="txCtx">
            <Authorized>
                <div class="nav-group" data-nav-group="transactions">
                <button type="button" class="nav-group-summary" aria-expanded="true">
                    <span class="nav-section-label">Transaksi</span>
                    <i class="bi bi-chevron-down nav-group-chev" aria-hidden="true"></i>
                </button>
                <div class="nav-group-items">
                <AuthorizeView Policy="transactions.hub.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="transactions" Match="NavLinkMatch.All" title="Transaksi">
                                <i class="bi bi-grid-1x2-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Transaksi</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
                <AuthorizeView Policy="transactions.purchase-orders.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="transactions/purchase-orders" title="Purchase Order">
                                <i class="bi bi-cart-plus-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Purchase Order</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
                <AuthorizeView Policy="transactions.sales-orders.index">
                    <Authorized>
                        <div class="nav-item px-3">
                            <NavLink class="nav-link" href="transactions/sales-orders" title="Sales Order">
                                <i class="bi bi-bag-check-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Sales Order</span>
                            </NavLink>
                        </div>
                    </Authorized>
                </AuthorizeView>
                </div>
                </div>
            </Authorized>
        </AuthorizeView>
```

- [ ] **Step 5: Verifikasi build**

Run: `dotnet build MyApp.slnx`
Expected: Build succeeded, 0 warning.

- [ ] **Step 6: Verifikasi manual (run app)**

Run: `dotnet run --project src/MyApp.Web` lalu login sebagai admin. Periksa:
- Sidebar menampilkan grup **Master** dengan **Supplier** & **Customer**, dan grup **Transaksi**.
- `/master/suppliers` & `/master/customers`: tambah/edit/hapus berfungsi, validasi Code duplikat & email muncul.
- `/transactions`: tampil hero + dua kartu bergaya mockup (aksen biru, hover-lift); klik kartu menuju halaman placeholder.

- [ ] **Step 7: Commit**

```bash
git add src/MyApp.Web/Components/Pages/Transactions src/MyApp.Web/Components/Layout/NavMenu.razor
git commit -m "feat(web): add Transactions hub, nav group, and PO/SO placeholders"
```

---

## Self-Review (terhadap spec)

**Spec coverage:**
- Entitas Supplier (§2) → Task 1. Customer (§3) → Task 2.
- Application layer + validator (§4) → Task 3, 4.
- Service + DI + DbContext + migration (§4, §8) → Task 5, 6, 7, 8.
- Halaman master (§5) → Task 10, 11.
- Hub Transaksi gaya mockup + placeholder (§6) → Task 12.
- Menu & otorisasi (§7) → Task 9 (permissions/policy) + Task 10/12 (NavMenu).
- Testing (§9) → unit test di Task 1–4, integration test di Task 6–7, run penuh di Task 8.

**Catatan:** "Default warehouse seeding" tidak relevan untuk Supplier/Customer. Tidak ada perubahan stok (sesuai "di luar scope"). Permission auto-grant ke admin via `BootstrapSeeder` (Global Constraints) — tidak perlu task seeding terpisah.

**Type consistency:** Signatur ctor `Supplier`/`Customer`, DTO field, dan urutan argumen pada service & halaman sudah dicocokkan lintas task. `transactions.hub` memakai action `index` (ViewOnly) — policy halaman `transactions.hub.index` konsisten dengan AppMenus.
