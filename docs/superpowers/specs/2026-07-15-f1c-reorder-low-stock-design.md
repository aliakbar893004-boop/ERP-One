# Fase 1c — Reorder Level & Low-Stock Alert — Design

**Date:** 2026-07-15
**Module:** Inventory · Reorder Level & Alert Stok Minim
**Depends on:** Product/Variant + stock model (`ProductStock`, `StockMovement`), KPI Dashboard (Fase 4e).

## Goal

Tambah ambang **reorder** per SKU (varian) + jumlah saran pesan, lalu tampilkan **alert stok menipis**
di dashboard (mengganti threshold hardcode) dan di halaman khusus `/inventory/low-stock` (daftar per
gudang, bisa difilter). Alert dievaluasi **per (SKU × gudang)** memakai ambang milik varian.

## Decisions (locked)

1. **Per-SKU (varian):** `ReorderLevel` + `ReorderQty` disimpan di `ProductVariant` (satu nilai per SKU, berlaku semua gudang). `ReorderLevel == 0` = tidak dilacak.
2. **Dua field:** `ReorderLevel` (ambang alert) + `ReorderQty` (saran jumlah pesan).
3. **Alert di:** upgrade widget dashboard (pakai reorder level, bukan konstanta 5) **+** halaman baru `/inventory/low-stock`.

## Definisi "low stock" (dipakai bersama)

Baris `ProductStock (varian, gudang)` **menipis** bila `variant.ReorderLevel > 0 && Quantity <= ReorderLevel`.
**Out-of-stock** = subset dgn `Quantity == 0`. **SuggestedOrderQty** = `ReorderQty > 0 ? ReorderQty : max(ReorderLevel − Quantity, 0)`.

## Architecture

Perubahan data model (varian) + satu service query low-stock + satu halaman + upgrade dashboard. Karena
ada perubahan entity → **ada migration EF** (beda dari modul report). Reuse pola `.pi` page & pola service query.

### New files
- `src/ErpOne.Application/Inventory/LowStock/ILowStockService.cs`
- `src/ErpOne.Application/Inventory/LowStock/LowStockDtos.cs`
- `src/ErpOne.Infrastructure/Services/Inventory/LowStockService.cs`
- `src/ErpOne.Web/Components/Pages/Inventory/LowStock/LowStockIndex.razor` → `/inventory/low-stock`
- `tests/ErpOne.IntegrationTests/LowStockServiceTests.cs`
- Migration `AddVariantReorderLevel` (auto-generated di `src/ErpOne.Infrastructure/Persistence/Migrations`).

### Modified files
- `src/ErpOne.Domain/Entities/Master/ProductVariant.cs` — 2 property + validasi + param di ctor/`Update`.
- `src/ErpOne.Domain/Entities/Master/Product.cs` — `AddVariant(... , int reorderLevel = 0, int reorderQty = 0)`.
- `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` — konfigurasi kolom (bila perlu; int non-null default 0).
- `src/ErpOne.Application/Master/Products/ProductDtos.cs` — `VariantInput` + `ProductVariantDto` tambah 2 field.
- `src/ErpOne.Application/Master/Products/CreateProductValidator.cs` (+ update validator bila terpisah) — rule `>= 0`.
- `src/ErpOne.Infrastructure/Services/Master/ProductService.cs` — mapping create/update (`AddVariant`/`Update`) + variant→Dto + `GetDashboardAsync` (low-stock berbasis reorder level, buang konstanta `LowStockThreshold`).
- `src/ErpOne.Web/Components/Pages/Master/Products/*` (form varian) — 2 input kecil (Reorder Level, Reorder Qty) per varian.
- `src/ErpOne.Infrastructure/DependencyInjection.cs` — daftar `ILowStockService`.
- `src/ErpOne.Web/Authorization/AppMenus.cs` — resource `inventory.low-stock` (ViewOnly) di grup Inventory.

## Entity change

`ProductVariant`: tambah `public int ReorderLevel { get; private set; }` + `public int ReorderQty { get; private set; }`.
- Ctor & `Update` terima `int reorderLevel = 0, int reorderQty = 0` (trailing opsional → caller lama aman), set via helper yang menolak nilai `< 0`.
- `Product.AddVariant(...)` teruskan 2 param (trailing opsional). Import (`ProductService` baris ~373) pakai default 0.

## DTOs (low-stock)

```csharp
namespace ErpOne.Application.Inventory.LowStock;

public record LowStockRowDto(
    int VariantId, string Sku, int ProductId, string ProductName,
    int WarehouseId, string WarehouseName,
    int Quantity, int ReorderLevel, int ReorderQty, int SuggestedOrderQty, bool IsOutOfStock);

public record LowStockSummaryDto(IReadOnlyList<LowStockRowDto> Rows, int LowCount, int OutOfStockCount);
```

## Interface

```csharp
public interface ILowStockService
{
    Task<LowStockSummaryDto> GetLowStockAsync(int? warehouseId, CancellationToken ct = default);
}
```

## Logic (LowStockService)

Join `ProductStock` × `ProductVariant` × `Product` × `Warehouse`:
- Filter `v.ReorderLevel > 0 && ps.Quantity <= v.ReorderLevel`, opsional `warehouseId`.
- Project `LowStockRowDto`; `SuggestedOrderQty = v.ReorderQty > 0 ? v.ReorderQty : Math.Max(v.ReorderLevel - ps.Quantity, 0)`; `IsOutOfStock = ps.Quantity == 0`.
- Urut paling parah dulu: `IsOutOfStock desc`, lalu `(Quantity - ReorderLevel)` asc, lalu Sku.
- `LowCount` = jumlah baris; `OutOfStockCount` = baris `IsOutOfStock`.

## Halaman `/inventory/low-stock`

Pola `.pi`: header + KPI (**Items low**, **Out of stock**) + toolbar (filter gudang via `IWarehouseService.GetAllAsync`) + table (SKU · Product · Warehouse · Qty · Reorder Level · Suggested Order + badge **Low**/**Out**). Empty-state bila tak ada. `@attribute [Authorize(Policy = "inventory.low-stock.index")]`, `@rendermode InteractiveServer`. Menu `inventory.low-stock` (ViewOnly) di grup Inventory (setelah `inventory.adjustments`).

## Dashboard update (minimal, bentuk DTO tetap)

`ProductService.GetDashboardAsync`: buang `const int LowStockThreshold = 5`. Hitung himpunan baris `(varian,gudang)` menipis (via reorder level). `LowStockCount` = jumlah **produk** yang punya ≥1 baris menipis (qty>0); `LowStock[]` = produk-produk tsb (top 8, bentuk `LowStockItem` product-level TIDAK berubah → Razor dashboard tak diubah). `OutOfStockCount` tetap = produk total stok 0.

## Form produk

Di editor varian (form produk), tambah 2 input angka kecil per baris varian: **Reorder Level** & **Reorder Qty** (default 0), ter-bind ke `VariantInput.ReorderLevel`/`ReorderQty`. Tampilkan juga di detail varian bila ada.

## Testing

`tests/ErpOne.IntegrationTests/LowStockServiceTests.cs`:
- Seed produk + varian A (ReorderLevel 10, ReorderQty 50) & varian B (ReorderLevel 0). Opening stock: A@gudang qty 8 (menipis, suggested 50), varian lain qty 20 (aman), B qty 1 (tak dilacak → tak muncul). Assert `GetLowStockAsync(null)` berisi A saja, SuggestedOrderQty=50, IsOutOfStock=false; qty 0 → IsOutOfStock true & tetap muncul; filter gudang mempersempit.
- Persist: `CreateAsync`/`UpdateAsync` produk membawa ReorderLevel/ReorderQty ke varian (round-trip via `GetByIdAsync`).
- `GetDashboardAsync`: LowStockCount berbasis reorder level (varian A menipis → produk terhitung; produk dgn semua varian ReorderLevel 0 → tidak).

## Konvensi & batasan

- Solution `ErpOne.slnx`. Build/test `dotnet test ErpOne.slnx`.
- **Migration**: `dotnet ef migrations add AddVariantReorderLevel -p src/ErpOne.Infrastructure -s src/ErpOne.Web`. Integration test SQLite `EnsureCreated` otomatis dapat kolom baru dari model (tak jalankan migration), jadi test tetap jalan; migration untuk SQL Server produksi.
- Commit MANUAL oleh user; git identity repo-local `aliakbar893004-boop`.

## Out of scope

- Threshold beda per gudang (per-varian-per-gudang).
- Auto-buat PO dari alert (cukup saran qty).
- Notifikasi in-app/email (Fase 6).
