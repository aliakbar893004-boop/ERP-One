# Tahap A — Fondasi Transaksi (Supplier, Customer, Hub Transaksi)

**Tanggal:** 2026-06-23
**Status:** Disetujui (lanjut ke implementasi)

## Konteks & Program Besar

User meminta modul transaksi **Purchase Order (PO)** dan **Sales Order (SO)** ala ERP di
dalam aplikasi `MyApp` yang sudah ada (Blazor Server, Clean Architecture). Karena scope
penuh besar, program dipecah menjadi 3 tahap berurutan, masing-masing punya
spec → rencana → implementasi sendiri:

| Tahap | Isi |
|---|---|
| **A — Fondasi** (dokumen ini) | Master **Supplier** & **Customer** + halaman **hub Transaksi** bergaya mockup |
| **B — Pembelian** | PO (Draft → approval berjenjang → Confirmed) + Goods Receipt (GRN, penerimaan parsial, stok masuk + HPP rata-rata) |
| **C — Penjualan** | SO (Draft → approval → Confirmed) + Delivery Order (DO, pengiriman parsial, stok keluar di HPP) |

### Keputusan lintas-program (berlaku untuk B & C)
- **Lokasi:** masuk ke project `MyApp.Web` yang ada (tambah menu), bukan project terpisah.
- **Alur:** lengkap — PO→GRN, SO→DO.
- **Approval:** berjenjang / multi-level (engine dibangun di Tahap B, dipakai ulang di C).
- **Costing:** HPP rata-rata bergerak (moving average) — sudah didukung
  `ProductVariant.ApplyMovingAverage(...)` + `StockMovement`/`ProductStock`.
- **Multi-currency:** TIDAK ada engine konversi kurs. `DefaultCurrency` hanya kolom kode
  (default `IDR`) untuk ditampilkan. (YAGNI.)

## Pola Existing yang Diikuti

- Blazor Server + Bootstrap 5 + Bootstrap Icons (bukan MudBlazor).
- Application layer **service-based** (bukan MediatR) + **FluentValidation**.
- `AppDbContext : IdentityDbContext`, entitas turunan `AuditableEntity`
  (audit otomatis via `SaveChangesAsync`).
- Otorisasi berbasis permission lewat `AppMenus.cs` (resource + action CRUD) dan
  policy provider; halaman pakai `@attribute [Authorize(Policy = "...")]` dan
  `<AuthorizeView Policy="...">`.
- UI list: tabel + search + komponen `Pager` (15/hal) + `SwalService` untuk konfirmasi hapus.
- UI form: section `fs-card`, validasi inline (`_xxxError`), spinner saat simpan,
  tangani `ValidationException`.

**Catatan:** belum ada entitas `Supplier` maupun `Customer` — dibuat di tahap ini.

## Scope Tahap A

### 1. Entitas Supplier (`src/MyApp.Domain/Entities/Supplier.cs`)
Turunan `AuditableEntity`, private setters, constructor + `Update()` yang memvalidasi.

| Field | Tipe | Aturan |
|---|---|---|
| Code | string | unik, uppercase, `^[A-Za-z0-9-]+$`, ≤20, wajib |
| Name | string | ≤100, wajib |
| ContactPerson | string? | ≤100 |
| Phone | string? | ≤30 |
| Email | string? | ≤100, format email bila diisi |
| Address | string? | ≤300 |
| TaxId (NPWP) | string? | ≤30 |
| PaymentTermDays | int | ≥0, default 0 |
| DefaultCurrency | string | ≤3, default `IDR` |
| BankName | string? | ≤100 |
| BankAccountNumber | string? | ≤50 |
| BankAccountName | string? | ≤100 |
| IsActive | bool | default true |

### 2. Entitas Customer (`src/MyApp.Domain/Entities/Customer.cs`)
Identik dengan Supplier, **kecuali**: 3 field bank dihapus, diganti
`CreditLimit` (decimal(18,2), ≥0, default 0).

### 3. Application layer (per entitas)
Folder `src/MyApp.Application/Suppliers/` & `Customers/`:
- `ISupplierService` / `ICustomerService`: `GetAllAsync`, `GetPagedAsync(search, page)`,
  `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`.
- DTO `record`: `SupplierDto`, `CreateSupplierRequest`, `UpdateSupplierRequest` (idem Customer).
- Validator FluentValidation: `Create…Validator`, `Update…Validator` sesuai aturan field.

### 4. Infrastructure
- `Services/SupplierService.cs`, `CustomerService.cs` (pola `WarehouseService`):
  ctor inject `AppDbContext` + validator; helper `EnsureCodeUniqueAsync`, `ToDto`.
- Registrasi DI di `DependencyInjection.cs`.
- DbSet `Suppliers`, `Customers` di `AppDbContext` + konfigurasi fluent
  (unique index `Code`, decimal `(18,2)` untuk `CreditLimit`, audit otomatis).
- Satu EF migration baru. **Tidak ada** perubahan tabel stok.

### 5. Web — halaman master
- `Components/Pages/Master/Suppliers/SupplierIndex.razor`
  (`@page "/master/suppliers"`, policy `master.suppliers.index`) — tabel
  (Code, Name, Contact, Phone, Status) + search + `Pager` + hapus via `SwalService`.
- `Components/Pages/Master/Suppliers/SupplierForm.razor`
  (`/master/suppliers/new`, `/master/suppliers/{Id:int}/edit`) — form `fs-card`,
  validasi inline, spinner.
- Idem `Customers/` (`/master/customers...`, policy `master.customers.*`).

### 6. Web — Hub Transaksi (gaya mockup)
- `Components/Pages/Transactions/TransactionsHub.razor` (`@page "/transactions"`,
  policy `transactions.hub.view`). Berada di dalam shell aplikasi (topbar+sidebar),
  bukan layar penuh.
- Mengambil bagian **hero + grid kartu** dari mockup `padelzone-court-mockup.html`:
  judul "Transaksi", dua kartu (**Purchase Order** → `/transactions/purchase-orders`,
  **Sales Order** → `/transactions/sales-orders`), aksen biru `#3771EC`, kartu rounded
  + hover-lift. Styling via `TransactionsHub.razor.css` (scoped).
- Visibilitas tiap kartu mengikuti permission `transactions.purchase-orders.index` /
  `transactions.sales-orders.index`.
- **Placeholder pages** untuk kedua route ("Modul sedang dikembangkan") agar navigasi
  tidak rusak dan hub bisa didemokan. Diganti di Tahap B/C.

### 7. Menu & otorisasi (`src/MyApp.Web/Authorization/AppMenus.cs`)
- Grup **Master** (existing): tambah `master.suppliers` (CRUD), `master.customers` (CRUD).
- Grup baru **Transaksi** (`transactions.any`, pola sama `master.any`):
  `transactions.hub` (view), `transactions.purchase-orders` (index, placeholder),
  `transactions.sales-orders` (index, placeholder).
- Tambah entri menu di `NavMenu.razor`.
- Permission baru diberikan ke role admin via seeder yang ada.

### 8. Testing
- **Unit** (`MyApp.UnitTests`): validator Supplier/Customer (code wajib/format/panjang,
  email, `PaymentTermDays`/`CreditLimit` ≥0) + invariant entitas (`Update`, normalisasi Code).
- **Integration** (`MyApp.IntegrationTests`): service Create/Update/Delete + keunikan Code,
  mengikuti pola test yang ada.

## Di luar scope Tahap A
- PO, SO, GRN, DO, engine approval, perubahan stok/costing → Tahap B & C.
- Konversi multi-currency.
- Halaman PO/SO sesungguhnya (hanya placeholder di tahap ini).
