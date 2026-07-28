# Fase 6b-1 Pricing Foundation — Progress & Handoff

**Terakhir dikerjakan:** 2026-07-27
**Branch:** Development
**Spec:** `docs/superpowers/specs/2026-07-27-f6b1-pricing-pricelist-design.md`
**Plan:** `docs/superpowers/plans/2026-07-27-f6b1-pricing-pricelist.md`

---

## Status ringkas

**Kode: 11/11 task SELESAI.** Build 0 warning/0 error. Test **195 unit + 264 integration** hijau
(baseline sebelum fase ini: 166 + 225 → tambahan 68 test).

**Skema DB: sudah diterapkan.** `Program.cs:181` menjalankan `db.Database.MigrateAsync()` saat startup,
jadi `20260727073357_AddPricingFoundation` otomatis masuk saat aplikasi dijalankan. Tidak ada langkah
manual `dotnet ef database update` yang tertinggal.

**Yang BELUM: verifikasi manual di aplikasi.** Ini satu-satunya langkah plan yang belum dijalankan.

---

## LANGKAH BERIKUTNYA (mulai dari sini besok)

### 1. Konfirmasi perbaikan race `NotificationBell`

Jalankan aplikasi, buka **Costing Method index** (`/settings/costing`) dan **Product index**
(`/master/products`) — dua halaman yang sebelumnya melempar:

```
System.InvalidOperationException: A second operation was started on this context instance
before a previous operation completed.
```

- **Hilang** → diagnosis terkonfirmasi, lanjut ke langkah 2.
- **Masih muncul** → kirim stack trace; kembali ke Phase 1 systematic-debugging, JANGAN menambal
  di atas tambalan.

### 2. Checklist verifikasi manual pricing

1. `/master/price-lists` — buat "GROSIR" dengan tier MinQty 1 / 10 / 50 untuk satu SKU
2. Assign GROSIR ke satu customer; assign price list lain sebagai default satu gudang
3. `/settings/pricing` — ubah default max discount, simpan, muat ulang (harus persist)
4. Settings → Role — isi Max Discount % pada satu role (kosong ≠ 0!)
5. SO baru untuk customer itu: harga terisi otomatis dari price list;
   **ubah qty ke 10 lalu 50 → harga HARUS ikut berubah** (ini bagian paling mudah gagal)
6. Coba diskon di atas batas role → harus ditolak dengan pesan menyebut SKU
7. POS di gudang ber-price list: harga hasil pencarian ikut price list, bukan harga master

### 3. Pekerjaan yang masih terbuka (opsional, setelah verifikasi)

- **Race POS search — risiko yang diperlebar fase ini.** `SearchProductsAsync` kini ~5 query
  (dari 2, karena memanggil `ResolveManyAsync`), sedangkan `PosRegister.razor:391` (`OnTermInput`)
  membatalkan token tapi query yang sudah jalan tidak berhenti seketika. Mengetik cepat di kasir bisa
  memicu exception `DbContext` yang sama. Polanya rapuh sejak sebelum fase ini, tapi sekarang mudah
  kena. Belum diperbaiki.
- **Leftover:** `SalesOrderVariantOptionDto` (`SalesOrderDtos.cs:25`) masih membawa field
  `DiscountPrice` yang tidak lagi dipakai `SoForm` sejak harga datang dari engine. Dibiarkan sengaja
  (menyentuh DTO + service + test di luar cakupan); bersihkan saat 6b-2 menyentuh area sama.
- **Checkbox usang di `docs/DEVELOPMENT-PLAN.md`:** Fase 0 (5 item) dan Fase 4 (Stock Ledger,
  Valuation, Sales, Purchase, Gross Profit, Dashboard KPI) masih `[ ]` padahal sudah ada di kode.
  Sudah ditawarkan ke user, belum dieksekusi.

---

## Commit yang belum dijalankan (user commit manual)

Urutan sesuai task. Kalau semua sudah jalan, boleh digabung — tapi urutan ini menjaga riwayat tetap
bisa dibaca per lapisan.

```bash
# Task 1
git add src/ErpOne.Domain/Entities/Master/PriceList.cs src/ErpOne.Domain/Entities/Master/PriceListLine.cs \
        src/ErpOne.Domain/Entities/Settings/PricingSetting.cs src/ErpOne.Domain/Entities/Master/Customer.cs \
        src/ErpOne.Domain/Entities/Master/Warehouse.cs src/ErpOne.Infrastructure/Identity/ApplicationRole.cs \
        src/ErpOne.Infrastructure/Persistence/AppDbContext.cs src/ErpOne.Infrastructure/Persistence/Migrations \
        tests/ErpOne.UnitTests/PriceListDomainTests.cs tests/ErpOne.IntegrationTests/PricingSchemaTests.cs
git commit -m "feat(pricing): PriceList domain + EF mapping + migration"

# Task 2
git add src/ErpOne.Application/Pricing/PriceMath.cs tests/ErpOne.UnitTests/PriceMathTests.cs
git commit -m "feat(pricing): PriceMath — tier picking, deviation, effective max discount"

# Task 3
git add src/ErpOne.Application/Pricing/IPricingService.cs \
        src/ErpOne.Infrastructure/Services/Pricing/PricingService.cs \
        src/ErpOne.Infrastructure/DependencyInjection.cs \
        tests/ErpOne.IntegrationTests/PricingServiceTests.cs
git commit -m "feat(pricing): IPricingService seam + resolution (price list, tier, fallback)"

# Task 4 + 8 (lookup varian ikut di sini)
git add src/ErpOne.Application/PriceLists src/ErpOne.Infrastructure/Services/Master/PriceListService.cs \
        tests/ErpOne.IntegrationTests/PriceListServiceTests.cs
git commit -m "feat(pricing): PriceList CRUD service + validators"

# Task 5
git add src/ErpOne.Application/Pricing/IPricingSettingService.cs \
        src/ErpOne.Infrastructure/Services/Settings/PricingSettingService.cs \
        tests/ErpOne.IntegrationTests/PricingSettingServiceTests.cs
git commit -m "feat(pricing): global pricing setting (default max discount)"

# Task 6
git add src/ErpOne.Application/Transactions/SalesOrders/ISalesOrderService.cs \
        src/ErpOne.Infrastructure/Services/Transactions/SalesOrderService.cs \
        tests/ErpOne.IntegrationTests/SalesOrderPricingGuardrailTests.cs
git commit -m "feat(pricing): server-resolved price + discount guardrail on Sales Order"

# Task 7
git add src/ErpOne.Application/Cashier/PosSales/IPosSaleService.cs \
        src/ErpOne.Infrastructure/Services/Cashier/PosSaleService.cs \
        tests/ErpOne.IntegrationTests/PosSalePricingGuardrailTests.cs
git commit -m "feat(pricing): POS uses server-resolved price + discount guardrail"

# Task 8
git add src/ErpOne.Web/Authorization/AppMenus.cs \
        src/ErpOne.Web/Components/Pages/Master/PriceLists \
        src/ErpOne.Web/Components/Pages/Settings/Pricing
git commit -m "feat(pricing): Price List pages + pricing settings page + menu"

# Task 9
git add src/ErpOne.Application/Master/Customers/CustomerDtos.cs \
        src/ErpOne.Application/Master/Warehouses/WarehouseDtos.cs \
        src/ErpOne.Infrastructure/Services/Master/CustomerService.cs \
        src/ErpOne.Infrastructure/Services/Master/WarehouseService.cs \
        src/ErpOne.Web/Components/Pages/Master/Customers/CustomerForm.razor \
        src/ErpOne.Web/Components/Pages/Master/Warehouses/WarehouseForm.razor \
        src/ErpOne.Web/Components/Pages/Settings/RoleForm.razor \
        tests/ErpOne.IntegrationTests/PricingAssignmentTests.cs
git commit -m "feat(pricing): assign price list to customer & warehouse, max discount per role"

# Task 10
git add src/ErpOne.Web/Components/Pages/Cashier/Pos/PosRegister.razor \
        src/ErpOne.Web/Components/Pages/Transactions/SalesOrders/SoForm.razor \
        src/ErpOne.Web/wwwroot/app.css
git commit -m "feat(pricing): POS & SO forms resolve price from engine (tier-aware)"

# Task 11 + docs
git add docs/DEVELOPMENT-PLAN.md docs/superpowers/specs/2026-07-27-f6b1-pricing-pricelist-design.md \
        docs/superpowers/plans/2026-07-27-f6b1-pricing-pricelist.md \
        docs/superpowers/plans/f6b1-progress.md
git commit -m "docs(pricing): 6b-1 spec, plan, progress; split remaining work into 6b-2/6b-3"

# TERPISAH — bukan bagian 6b-1, perbaikan bug fitur notifikasi
git add src/ErpOne.Web/Components/Layout/NotificationBell.razor
git commit -m "fix(notifications): resolve NotificationService in its own DI scope"
```

---

## Keputusan desain yang mengunci implementasi

| Aspek | Keputusan |
|---|---|
| Harga dasar | Price list + tier qty; **MinQty terbesar ≤ qty** yang menang |
| Prioritas | `Customer.PriceListId` **menang** atas `Warehouse.DefaultPriceListId` |
| Fallback | price list → `DiscountPrice` → `Price`. Semua kegagalan = fallback, **bukan error** |
| Guardrail | **Satu metrik penyimpangan** dari harga engine → menutup override `UnitPrice` *dan* `DiscountPercent` sekaligus |
| Batas efektif | MAX dari role yang terisi; `null` = pakai default global; **`0` ≠ `null`** (0 = tak boleh diskon) |
| Non-breaking | `PricingSetting.DefaultMaxDiscountPercent` di-seed **100** |
| `roleNames` | Parameter method dari `AuthenticationState` — **bukan** DTO (client bisa palsu), **bukan** `ICurrentUser` (HttpContext null di Blazor interaktif) |
| Tidak divalidasi | DO, AR Invoice, retur, POS Refund — snapshot dari dokumen yang sudah lolos |
| POS | Tanpa customer (walk-in); harga dari price list default gudang shift aktif |

---

## Koreksi terhadap plan yang ditemukan saat eksekusi

Semua sudah ditulis balik ke plan, tapi dicatat di sini supaya tidak terulang:

1. Ctor `Warehouse` punya `isDefault` di posisi ke-5 → `defaultPriceListId` argumen **ke-6**
2. Ctor `Product` butuh **8** argumen (plan menulis 7)
3. `ProductStatus` berbahasa Indonesia: **`Aktif`**, tidak ada `Active`
4. Ada API resmi `Product.AddVariant(...)` — refleksi untuk set `ProductId` tidak diperlukan
5. Namespace Application **flat & plural**: `ErpOne.Application.SalesOrders`, bukan `...Transactions.SalesOrders`
6. Mapping EF **inline di `OnModelCreating`**; tidak ada folder `Configurations`
7. Permission auto-seed dari `AppMenus.AllPermissions` — `BootstrapSeeder` tidak perlu disentuh
8. Method POS-nya `CreateSaleAsync`, bukan `CreateAsync`
9. Unit test di proyek ini **tanpa DB** → aturan hitung diekstrak ke `PriceMath` (helper murni)

## Bug yang tertangkap test saat eksekusi

- **`PriceListService.BaseQuery`**: `OrderBy` **setelah** proyeksi ke DTO yang memuat subquery
  `Lines.Count` → EF gagal menerjemahkan query. `GetAllAsync` & `GetActiveAsync` dua-duanya akan
  meledak. Diperbaiki: urut di level entity, `Project()` paling akhir. Tertangkap oleh test yang
  ditambahkan di luar daftar plan.
