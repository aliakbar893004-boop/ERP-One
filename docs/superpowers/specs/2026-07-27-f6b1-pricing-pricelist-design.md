# Fase 6b-1 — Pricing Foundation: Price List + Guardrail Diskon (Design Spec)

**Tanggal:** 2026-07-27
**Branch:** Development
**Status:** Disetujui untuk implementasi

---

## 0. Konteks & posisi dalam roadmap

`docs/DEVELOPMENT-PLAN.md` Fase 6 mencantumkan *"Price List / Promo / Diskon terpusat"* sebagai satu
butir nice-to-have. Setelah dibedah, kebutuhan nyatanya adalah **subsistem pricing** dengan ukuran
setara Fase 3 (Finance) — bukan satu modul. Karena itu dipecah tiga:

| Sub-fase | Isi | Status |
|---|---|---|
| **6b-1** | Seam `IPricingService`, Price List + tier qty, assignment, guardrail diskon server-side | **Spec ini** |
| 6b-2 | Promo terjadwal per item (%, nominal, fixed price), pemilihan 1 promo terbaik, jejak perhitungan | Spec menyusul |
| 6b-3 | Diskon tingkat transaksi (dengan alokasi ke baris) + Buy-X-Get-Y | Spec menyusul |

Urutan ini bukan selera: 6b-2 dan 6b-3 sama-sama butuh seam + harga dasar dari 6b-1, dan 6b-3
satu-satunya yang mengubah skema dokumen transaksi (`PosSale`, `SalesOrder`). Mencampurnya membuat
satu migration raksasa yang sulit diuji dan sulit di-rollback.

### Keadaan sekarang (hasil verifikasi kode)

- Harga tersimpan di `ProductVariant`: `Price`, `DiscountPrice?` (harga coret), `DiscountPercent?`
  (penanda tampilan). Semuanya per SKU, **tanpa periode dan tanpa segmen pelanggan**.
- `PosRegister.razor:423` — harga keranjang = `DiscountPrice ?? Price`; kasir mengisi
  `DiscountPercent` manual per baris.
- `SoForm.razor:319` — `UnitPrice` diprefill `DiscountPrice ?? Price`; diskon % manual per baris.
- `PosSaleService.cs:99` — server **mempercayai penuh** `UnitPrice` & `DiscountPercent` kiriman
  client. Tidak ada validasi harga server-side sama sekali.
- `PosSale` **tidak punya `CustomerId`** — transaksi POS murni walk-in.
- `Customer` belum punya konsep price level/segmen.

### Keputusan yang mengunci desain

| Aspek | Keputusan |
|---|---|
| Harga dasar | Price list dengan tier qty; di-assign ke customer; POS pakai price list default per gudang |
| Aturan gabung | **Cascade**: price list = harga dasar → (6b-2) hanya **1 promo terbaik** di atasnya, tidak menumpuk |
| Diskon manual | Tetap boleh, dibatasi **maks % per role**, divalidasi server |
| Cakupan dokumen | **POS + Sales Order**. AR Invoice/DO ikut otomatis karena men-snapshot dari SO/POS |
| Jenis promo (6b-2/6b-3) | %, nominal, fixed price, BOGO, diskon total transaksi — **di luar scope 6b-1** |

### Di luar scope 6b-1

- Promo apa pun (periode, target kategori/brand, fixed price promosional) → 6b-2.
- BOGO & diskon tingkat transaksi → 6b-3.
- Customer/member di POS → tidak dikerjakan; POS tetap walk-in, harga dasar dari gudang.
- Validasi harga di DO, AR Invoice, dan retur → sengaja tidak dilakukan (lihat §4.3).
- Approval untuk diskon berlebih → ditolak langsung, tanpa jalur approval.
- **`CreatePosSaleRequest.TransactionDiscount` yang sudah ada** (dipakai di
  `PosSaleService.cs:106` → `sale.Settle(...)`) tidak divalidasi guardrail di 6b-1. Guardrail 6b-1
  murni per baris. Diskon tingkat transaksi — termasuk alokasinya ke baris agar laba kotor benar —
  adalah materi 6b-3. Artinya setelah 6b-1 masih ada satu jalur diskon yang belum dibatasi; ini
  diketahui, bukan terlewat.

---

## 1. Model data

### 1.1 Entity baru

**`PriceList`** — `ErpOne.Domain/Entities/Master/PriceList.cs`

| Field | Tipe | Catatan |
|---|---|---|
| `Id` | int | PK |
| `Code` | string(20) | unik, disimpan uppercase (pola `Customer.Code`) |
| `Name` | string(100) | wajib |
| `Description` | string(255)? | |
| `IsActive` | bool | |

Turunan `AuditableEntity`. **Tanpa periode berlaku** — dimensi waktu adalah urusan promo (6b-2).
Price list adalah daftar harga *struktural* (Retail / Grosir / Reseller) yang jarang berubah;
menaruh tanggal di price list *dan* di promo menciptakan dua sumber kebenaran untuk pertanyaan
"harga apa yang berlaku hari ini".

**`PriceListLine`** — `ErpOne.Domain/Entities/Master/PriceListLine.cs`

| Field | Tipe | Catatan |
|---|---|---|
| `Id` | int | PK |
| `PriceListId` | int | FK → `PriceList`, cascade delete |
| `ProductVariantId` | int | FK → `ProductVariant`, restrict delete |
| `MinQty` | int | ≥ 1, default 1 |
| `UnitPrice` | decimal(18,2) | ≥ 0 |

Unique index `(PriceListId, ProductVariantId, MinQty)`.

**Tier qty = beberapa baris dengan `MinQty` berbeda**, bukan tabel terpisah:

```
PriceList "GROSIR" / SKU BAJU-M-RED
  MinQty  1  -> 90.000
  MinQty 10  -> 85.000
  MinQty 50  -> 78.000
```

Varian yang tidak terdaftar berarti "tidak diatur oleh price list ini" dan jatuh ke harga master —
sehingga price list tidak wajib memuat seluruh katalog.

**`PricingSetting`** — `ErpOne.Domain/Entities/Settings/PricingSetting.cs`

Single-row (Id = 1), pola `CostingSetting`/`PostingConfiguration`.

| Field | Tipe | Catatan |
|---|---|---|
| `Id` | int | selalu 1 |
| `DefaultMaxDiscountPercent` | decimal(5,2) | 0..100, **di-seed 100** |

Seed 100 membuat rilis ini **tidak breaking**: sebelum admin mengetatkan batas per role, perilaku
hari ini (diskon bebas) tetap berlaku dan tidak ada transaksi berjalan yang tiba-tiba ditolak.

### 1.2 Perubahan entity yang sudah ada

| Entity | Tambahan | Alasan |
|---|---|---|
| `Customer` | `PriceListId` int? (FK, restrict) | Assignment segmen harga |
| `Warehouse` | `DefaultPriceListId` int? (FK, restrict) | Sumber harga dasar POS (dari gudang shift aktif) |
| `ApplicationRole` | `MaxDiscountPercent` decimal(5,2)? | Batas diskon per role |

`MaxDiscountPercent` **nullable** dengan sengaja: `0` berarti "role ini tidak boleh memberi diskon
sama sekali" (aturan yang sah dan mungkin diinginkan untuk kasir), sedangkan `null` berarti "tidak
diatur, pakai default global". Kolom non-nullable tidak bisa membedakan keduanya.

Konstruktor/`Update()` `Customer` dan `Warehouse` ditambah parameter `priceListId`/`defaultPriceListId`.
`ApplicationRole` adalah kelas Identity dengan property publik settable (bukan domain encapsulated),
jadi cukup tambah property + konfigurasi EF.

### 1.3 Mapping EF & migration

Mapping ditulis **inline di `AppDbContext.OnModelCreating`** — proyek ini tidak memakai kelas
`IEntityTypeConfiguration` terpisah (tidak ada folder `Configurations`). Seed baris tunggal
`PricingSetting` lewat `HasData` dengan tanggal statik, pola `CostingSetting`
(`AppDbContext.cs:1036-1047`).

Ketiga entity **wajib** didaftarkan di dictionary `tablePrefixes` (`AppDbContext.cs:1123`) dengan
prefix `M_` (master). Ada pengaman di `AppDbContext.cs:1202-1207`: entity bisnis yang belum
terdaftar membuat pembangunan model **gagal** — jadi kelalaian ini tidak bisa lolos diam-diam.

Satu migration: `AddPricingFoundation` — 3 tabel (`M_PriceLists`, `M_PriceListLines`,
`M_PricingSettings`) + 3 kolom + seed.

---

## 2. Seam `IPricingService`

Pola mengikuti `ICostingService` (`ErpOne.Application/Costing/ICostingService.cs`): interface tipis di
Application, implementasi di Infrastructure, pemanggil tidak tahu isi aturannya. Inilah yang membuat
6b-2/6b-3 bisa menambah promo **tanpa menyentuh POS dan SO lagi**.

`ErpOne.Application/Pricing/IPricingService.cs`:

```csharp
namespace ErpOne.Application.Pricing;

public enum PriceSource { VariantPrice, VariantDiscountPrice, PriceList }

public sealed record PriceRequest(
    int ProductVariantId,
    int Quantity,
    int? CustomerId,
    int? WarehouseId,
    DateOnly OnDate);

public sealed record PriceResult(
    decimal UnitPrice,        // harga dasar hasil resolusi (sebelum diskon manual)
    decimal ListPrice,        // ProductVariant.Price — dasar badge harga coret
    PriceSource Source,
    int? PriceListId,
    string? PriceListName,
    int? MatchedMinQty);

public interface IPricingService
{
    Task<PriceResult> ResolveAsync(PriceRequest req, CancellationToken ct = default);

    /// <summary>Batch — wajib dipakai POS search & prefill SO agar tidak N+1.</summary>
    Task<IReadOnlyList<PriceResult>> ResolveManyAsync(
        IReadOnlyList<PriceRequest> reqs, CancellationToken ct = default);

    /// <summary>Batas diskon efektif: MAX dari role yang nilainya terisi, else default global.</summary>
    Task<decimal> GetMaxDiscountPercentAsync(
        IEnumerable<string> roleNames, CancellationToken ct = default);
}
```

`ResolveManyAsync` bukan kemewahan: `SearchProductsAsync` POS mengembalikan puluhan baris per
ketikan, dan resolusi per baris akan menghasilkan N+1 query di jalur paling sensitif latensi di
aplikasi ini.

**`OnDate` belum dipakai di 6b-1** dan itu disadari. Ia dimasukkan sekarang karena 6b-2 (promo
terjadwal) pasti membutuhkannya, dan menambahkannya nanti berarti menyentuh seluruh pemanggil (POS
search, POS submit, SO prefill, SO create/update). Satu field hari ini lebih murah daripada refactor
lintas layer nanti. Ini satu-satunya elemen yang sengaja dibangun untuk kebutuhan sub-fase
berikutnya. Implementasi 6b-1 menerima dan mengabaikannya.

Implementasi: `ErpOne.Infrastructure/Services/Pricing/PricingService.cs`, registrasi scoped di DI
bersama service lain.

---

## 3. Algoritma resolusi harga

```
1. Pilih price list aktif:
     Customer.PriceListId        (jika CustomerId ada DAN price list IsActive)
  -> else Warehouse.DefaultPriceListId  (jika WarehouseId ada DAN price list IsActive)
  -> else null

2. Jika ada price list:
     cari PriceListLine untuk (priceListId, variantId) dengan MinQty <= Quantity,
     ambil MinQty TERBESAR
       -> ketemu: UnitPrice = baris.UnitPrice
                  Source = PriceList, MatchedMinQty = baris.MinQty

3. Jika langkah 2 tidak menghasilkan (price list null / varian tak terdaftar):
     UnitPrice = DiscountPrice ?? Price
     Source    = DiscountPrice.HasValue ? VariantDiscountPrice : VariantPrice

4. ListPrice = ProductVariant.Price  (selalu, apa pun sumbernya)
```

**Customer menang atas gudang**: customer Grosir yang belanja di outlet mana pun tetap mendapat harga
Grosir. Kalau gudang yang menang, assignment per customer jadi tidak bisa diandalkan.

**Semua kegagalan adalah fallback, bukan error.** Price list dinonaktifkan, varian belum terdaftar,
`WarehouseId` null — semuanya menghasilkan harga master. Kesalahan konfigurasi pricing tidak boleh
menghentikan penjualan di kasir.

---

## 4. Guardrail diskon

### 4.1 Kenapa satu metrik, bukan dua

Ada dua celah hari ini: client bisa mengirim `UnitPrice` apa pun **dan** `DiscountPercent` apa pun.
Membatasi `DiscountPercent` saja tidak menutup apa-apa — pengguna cukup menurunkan `UnitPrice`.
Karena itu keduanya diukur sebagai satu angka: **penyimpangan total dari harga engine.**

### 4.2 Rumus

```
R = harga hasil IPricingService.ResolveAsync (server; TIDAK dari client)
U = UnitPrice kiriman client
D = DiscountPercent kiriman client

hargaEfektif  = U * (1 - D / 100)
penyimpangan% = (1 - hargaEfektif / R) * 100

tolak jika penyimpangan% > batasEfektif
```

- Penyimpangan **negatif** (harga di atas harga engine) selalu lolos — itu bukan kebocoran margin.
- `R = 0` → validasi baris itu dilewati (hindari bagi nol; harga master 0 berarti belum diatur).
- Pembulatan perbandingan pada 2 desimal, `MidpointRounding.AwayFromZero` — konsisten dengan
  seluruh perhitungan uang di domain (`SalesOrderLine.Recompute`).

**Batas efektif** = `MAX(MaxDiscountPercent)` dari role user yang nilainya non-null; kalau semua null
→ `PricingSetting.DefaultMaxDiscountPercent`. MAX (bukan MIN) karena menambah role ke seorang user
seharusnya menambah wewenang, bukan menguranginya.

### 4.3 Titik penerapan

| Tempat | Tindakan |
|---|---|
| `PosSaleService.CreateSaleAsync` | Resolve ulang tiap baris dengan `WarehouseId` = `CashierShift.WarehouseId`, `CustomerId` = null. Validasi penyimpangan; tolak dengan pesan menyebut SKU + batas efektif + penyimpangan yang diminta |
| `SalesOrderService.CreateAsync` / `UpdateAsync` | Sama; `CustomerId` dan `WarehouseId` diambil dari header `SalesOrder` (`SalesOrder.WarehouseId` sudah ada) |

`UnitPrice` hasil negosiasi **tetap dihormati** selama dalam batas — sales B2B tidak kehilangan ruang
nego, hanya dibatasi.

**Yang sengaja TIDAK divalidasi:** Delivery Order, AR Invoice, Sales Return, POS Refund. Semuanya
men-snapshot harga dari dokumen sumber yang sudah lolos validasi. Memvalidasi ulang di sana justru
merusak: harga engine bisa berubah setelah SO disetujui, dan invoice lama akan menolak dirinya
sendiri saat di-edit.

Role user diteruskan ke service sebagai **parameter method** `IReadOnlyList<string>? roleNames`,
mengikuti pola yang sudah ada di `CreateSaleAsync(userId, userName, shiftId, request)` — halaman Blazor
membacanya dari cascading `Task<AuthenticationState>` (`PosRegister.razor:335-337`) lalu
melewatkannya.

Dua alasan **tidak** memakai jalur lain:

- **Bukan lewat request DTO.** DTO datang dari client; kalau `roleNames` ada di dalamnya, siapa pun
  bisa mengirim `["Administrator"]` dan guardrail-nya tak ada artinya.
- **Bukan lewat `ICurrentUser`.** Implementasinya (`HttpContextCurrentUser`) bergantung pada
  `IHttpContextAccessor`, dan di Blazor Server interaktif `HttpContext` tidak tersedia setelah render
  awal — roles akan kosong tanpa gejala apa pun, dan batas per role diam-diam berhenti berlaku. POS
  memang sudah menghindari jalur ini untuk `userId`/`userName` dengan alasan yang sama.

`roleNames` bernilai `null`/kosong berarti "tidak ada role terisi" → jatuh ke
`PricingSetting.DefaultMaxDiscountPercent`. Default parameter `null` dipilih agar ~13 test integrasi
yang sudah ada tetap ter-kompilasi dan perilakunya tidak berubah; konsekuensinya, pemanggil baru yang
lupa mengisi akan mendapat batas global, bukan penolakan. Trade-off ini diterima karena hanya ada dua
pemanggil nyata (POS & SO) dan keduanya diubah di fase ini.

---

## 5. Halaman & permission

### 5.1 Resource baru (`Web/Authorization/AppMenus.cs` saja)

Permission **tidak** perlu diseed manual: `Web/Infrastructure/BootstrapSeeder.cs:44` sudah memberikan
`AppMenus.AllPermissions` ke role admin secara idempotent. Menambah resource di `AppMenus.cs` cukup.

| Resource | Grup | Actions |
|---|---|---|
| `master.price-lists` | Master | index, create, edit, delete |
| `settings.pricing` | Settings | index, edit |

### 5.2 Halaman baru

- `Components/Pages/Master/PriceLists/PriceListIndex.razor` — desain global `.pi` (list + KPI + chips).
  Nama singular + `Index` mengikuti konvensi master yang ada (`CustomerIndex.razor`, `WarehouseIndex.razor`).
- `Components/Pages/Master/PriceLists/PriceListForm.razor` — `.cf` (Atlas). Header (Code, Name,
  Description, IsActive) + editor baris inline: pilih varian, `MinQty`, `UnitPrice` — pola baris item
  `SoForm.razor`. Tanpa halaman Detail terpisah, konsisten dengan master lain.
- `Components/Pages/Settings/Pricing/PricingSettingIndex.razor` — meniru
  `Settings/Costing/CostingSettingIndex.razor`.

### 5.3 Perubahan halaman yang sudah ada

| Halaman | Perubahan |
|---|---|
| `Settings/RoleForm.razor` | Field "Max Discount %" (kosong = pakai default global; teks bantu menyebut nilai default yang berlaku) |
| `Master/Customers/CustomerForm.razor` | Dropdown Price List (opsional) |
| `Master/Warehouses/WarehouseForm.razor` | Dropdown Default Price List (opsional) |
| `Cashier/Pos/PosRegister.razor` | Harga dari engine (gudang = shift aktif) via `ResolveManyAsync`; badge nama price list di hasil pencarian; harga coret reuse `.disc-badge` yang sudah ada (`PosRegister.razor:109`) |
| `Transactions/SalesOrders/SoForm.razor` | Prefill `UnitPrice` dari engine saat varian dipilih **dan saat Quantity berubah**; tampilkan nama price list per baris |

**Re-resolve saat qty berubah itu wajib, bukan penyempurnaan.** Tanpa itu tier qty tidak pernah
terpakai: user mengisi qty 50 tetapi harga tetap harga tier 1. Ini perilaku yang paling mudah
terlewat di modul ini.

Tidak ada elemen UI baru yang perlu didesain — seluruhnya reuse `.pi`/`.cf` + `.disc-badge`.

---

## 6. Error handling

| Kondisi | Perilaku |
|---|---|
| Price list nonaktif / varian tak terdaftar / `WarehouseId` null | Fallback ke harga master — **bukan** error |
| Hapus price list yang masih dipakai customer atau warehouse | Ditolak dengan pesan (pola restrict-delete master lain) |
| Hapus price list yang tidak dipakai | Boleh; baris ikut terhapus (cascade) |
| Diskon melebihi batas | Validasi gagal; pesan menyebut SKU, batas efektif, dan penyimpangan yang diminta |
| `MinQty` duplikat untuk varian yang sama dalam satu price list | Ditolak (unique index + FluentValidation) |
| `MinQty` < 1 atau `UnitPrice` < 0 | Ditolak validator |
| Harga master `R = 0` | Guardrail dilewati untuk baris itu |
| `Code` price list duplikat | Ditolak (unique index + validator, pola master lain) |

---

## 7. Rencana test

Pola mengikuti `PurchaseOrderServiceTests`. Baseline sekarang: 166 unit + 225 integration, semua hijau.

`ErpOne.UnitTests` di proyek ini **tidak menyentuh database** — isinya murni domain & validator.
Karena itu logika hitung pricing diekstrak ke helper statik murni `ErpOne.Application/Pricing/PriceMath.cs`
(pemilihan tier, penyimpangan, batas efektif) sehingga bisa diuji sebagai unit, sementara
`PricingService` (yang menyentuh DB) diuji sebagai integrasi. Pemisahan ini juga membuat aturan inti
bisa dibaca tanpa membaca query EF.

**Unit (`ErpOne.UnitTests`) — `PriceMath` + invarian entity**

- `PickTier`: qty 9 → `MinQty` 1; qty 10 → tier 10; qty 60 → tier 50 (ambil terbesar ≤ qty); daftar
  kosong → null; qty di bawah tier terkecil → null.
- `DeviationPercent`: override harga saja; diskon % saja; keduanya sekaligus; harga di atas harga
  engine (negatif); `R = 0` → dianggap lolos.
- `EffectiveMaxDiscountPercent`: MAX lintas role; semua null → default global; role bernilai 0 →
  hasil 0 (tidak boleh diskon).
- Invarian entity: `PriceListLine` menolak `MinQty < 1` dan `UnitPrice < 0`; `PriceList.Code`
  dinormalkan uppercase.

**Integration — resolusi di atas DB**

- Pemilihan tier lewat query nyata; rantai fallback tiga tingkat termasuk `Source` yang benar.
- Prioritas customer di atas default gudang.
- Price list nonaktif diabaikan (fallback), tidak melempar exception.

**Integration (`ErpOne.IntegrationTests`)**

- CRUD price list + unique constraint `(PriceListId, ProductVariantId, MinQty)` dan `Code`.
- Restrict delete: price list yang dipakai customer/warehouse tidak bisa dihapus.
- **POS mengirim `UnitPrice` palsu → baris tersimpan memakai harga engine, bukan kiriman client**
  (regression test untuk celah `PosSaleService.cs:99`).
- SO dengan penyimpangan di atas batas → ditolak; pesan memuat SKU.
- SO dengan penyimpangan dalam batas → tersimpan dengan `UnitPrice` nego utuh.
- Customer punya price list & gudang punya price list berbeda → harga customer yang dipakai.
- Price list dinonaktifkan setelah SO dibuat → SO lama tetap bisa dibuka & di-edit (harga snapshot).

---

## 8. Ringkasan berkas

**Baru**

```
Domain/Entities/Master/PriceList.cs
Domain/Entities/Master/PriceListLine.cs
Domain/Entities/Settings/PricingSetting.cs
Infrastructure/Persistence/Migrations/      (AddPricingFoundation)
Application/Pricing/IPricingService.cs
Application/Pricing/PricingDtos.cs
Application/Pricing/PriceMath.cs            (helper murni, diuji sebagai unit)
Application/Master/PriceLists/IPriceListService.cs
Application/Master/PriceLists/PriceListDtos.cs
Application/Master/PriceLists/PriceListValidators.cs
Application/Settings/Pricing/IPricingSettingService.cs
Infrastructure/Services/Pricing/PricingService.cs
Infrastructure/Services/Master/PriceListService.cs
Infrastructure/Services/Settings/PricingSettingService.cs
Web/Components/Pages/Master/PriceLists/PriceListIndex.razor
Web/Components/Pages/Master/PriceLists/PriceListForm.razor
Web/Components/Pages/Settings/Pricing/PricingSettingIndex.razor
```

**Diubah**

```
Domain/Entities/Master/Customer.cs          (+PriceListId)
Domain/Entities/Master/Warehouse.cs         (+DefaultPriceListId)
Infrastructure/Identity/ApplicationRole.cs  (+MaxDiscountPercent)
Infrastructure/Persistence/AppDbContext.cs  (3 DbSet + 3 blok mapping + 3 entri tablePrefixes)
Infrastructure/DependencyInjection.cs       (registrasi 3 service)
Infrastructure/Services/Cashier/PosSaleService.cs         (resolve + validasi, +roleNames)
Infrastructure/Services/Transactions/SalesOrderService.cs (resolve + validasi, +roleNames)
Application/Cashier/PosSales/IPosSaleService.cs           (+roleNames)
Application/Transactions/SalesOrders/ISalesOrderService.cs (+roleNames)
Web/Authorization/AppMenus.cs               (2 resource)
Web/Components/Pages/Settings/RoleForm.razor
Web/Components/Pages/Master/Customers/CustomerForm.razor
Web/Components/Pages/Master/Warehouses/WarehouseForm.razor
Web/Components/Pages/Cashier/Pos/PosRegister.razor
Web/Components/Pages/Transactions/SalesOrders/SoForm.razor
```

---

## 9. Kriteria selesai

1. Admin bisa membuat price list dengan tier qty, meng-assign ke customer dan ke gudang.
2. POS menampilkan harga hasil price list gudang shift aktif, dengan badge nama price list.
3. SO memprefill harga sesuai price list customer, dan **berubah saat qty melewati batas tier**.
4. Harga & diskon kiriman client tidak lagi dipercaya: harga dasar selalu dihitung server.
5. Diskon di atas batas role ditolak dengan pesan yang jelas; dalam batas tetap lolos.
6. Instalasi lama tanpa price list dan tanpa batas role berperilaku persis seperti sebelumnya.
7. `dotnet build` bersih (0 warning) dan seluruh test hijau.
