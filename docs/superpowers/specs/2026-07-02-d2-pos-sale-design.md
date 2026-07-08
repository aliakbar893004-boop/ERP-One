# Tahap D2 — Kasir/POS: Layar Penjualan Langsung (PosSale)

**Tanggal:** 2026-07-02
**Status:** Disetujui (lanjut ke rencana implementasi)
**Program besar:** Kasir/POS (Tahap D). **D1 (Sesi Kasir) selesai.** Dokumen ini = **D2 (Layar POS)**.

## Konteks

Layar kasir untuk **penjualan langsung** (walk-in), persis mockup
`OneDrive - acetris.co.id/Documents/Cashier-Mockups/1-lini.html`: scan/cari produk → keranjang →
ringkasan (subtotal, diskon, PPN, total) → bayar (tunai/non-tunai + kembalian) → cetak struk.
Transaksi **langsung selesai** dalam satu langkah: mengurangi stok keluar di gudang shift, mencatat
COGS, dan menambah akumulator shift lewat `CashierShift.RecordSale(...)` (jembatan dari D1).

D2 memakai ulang keputusan lintas-D yang dikunci di
`docs/superpowers/specs/2026-07-02-d1-cashier-shift-design.md`.

### Keputusan D2 (hasil brainstorming)
- **Layout:** chrome **layar penuh khusus** (tanpa sidebar rail) — top bar gudang/kasir/no struk/shift/jam.
- **Diskon:** **per-baris (persen)** DAN **tingkat-transaksi (nominal Rp)**.
- **Pajak:** **satu tarif per-transaksi, exclusive** (dipilih dari master `Tax` aktif; opsi "Tanpa PPN").
- **Riwayat + cetak ulang:** ADA (daftar transaksi POS + detail + tombol cetak ulang).
- **Shortcut keyboard:** lengkap — F2 cari, F4 diskon, F9 bayar, Del hapus baris, Esc batalkan.
- **Harga jual:** `variant.DiscountPrice ?? variant.Price`.
- **Pembayaran:** multi-metode dari master `PaymentMethod`, **satu metode per transaksi**. Tunai hitung
  kembalian; non-tunai tanpa kembalian.
- **Stok:** dikurangi & dicek di **gudang shift**; **tolak bila kurang** (domain `ProductStock` larang
  negatif). COGS = `variant.CostPrice` di-snapshot; **MA tidak berubah** (meniru `DeliveryOrder.PostAsync`).
- **Nomor:** `POS-YYYYMMDD-####` (harian).
- **Pelanggan:** walk-in (tak ada `Customer`).

## Pola Existing yang Diikuti
- Clean Architecture (Domain → Application → Infrastructure → Web), `AuditableEntity`, private setters,
  invariant via ctor/method.
- Enum string `.HasConversion<string>().HasMaxLength(20)`; uang `decimal(18,2)`; pembulatan
  `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- Helper stok bersama `db.UpsertStockAsync(variantId, warehouseId, delta, ct)` (guard negatif).
- `StockMovement(variantId, warehouseId, MovementType.Out, -qty, variant.CostPrice, saleDate, refType:"POS", refId, note:SaleNumber)`.
- Service melempar `ValidationException` (helper `Fail`). Otorisasi via `AppMenus`/policy.
- Current user disuplai halaman (claim `NameIdentifier` + `Identity.Name`), pola `ShiftIndex`/`SoDetail`.
- Perhitungan baris meniru `SalesOrderLine` (subtotal/diskon persen), tetapi **pajak di tingkat transaksi**
  (bukan per baris seperti SO).

## Scope D2

### 1. Domain (`src/MyApp.Domain/Entities`)

#### 1a. `PosSale.cs` (baru, `AuditableEntity`, private setters)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| SaleNumber | string | unik, auto `POS-YYYYMMDD-####` |
| CashierShiftId | int | FK shift (required) |
| WarehouseId | int | snapshot gudang shift |
| SaleDate | DateTime | required |
| PaymentMethodId | int | FK (required) |
| IsCashPayment | bool | snapshot `PaymentType==Tunai` |
| TaxId | int? | FK Tax (nullable = tanpa PPN) |
| TaxRateSnapshot | decimal | 0..100, snapshot |
| TransactionDiscount | decimal | ≥0, (18,2), nominal |
| Subtotal | decimal | (18,2) = Σ LineTotal |
| TaxTotal | decimal | (18,2) |
| GrandTotal | decimal | (18,2) |
| AmountTendered | decimal | (18,2) |
| ChangeGiven | decimal | (18,2) |
| CogsTotal | decimal | (18,2) = Σ qty×UnitCost |
| Lines | IReadOnlyCollection\<PosSaleLine\> | child, akses field |

**Konstruksi & perhitungan:** ctor(saleNumber, cashierShiftId, warehouseId, saleDate, paymentMethodId,
isCash, taxId, taxRateSnapshot) validasi id/nomor; method `AddLine(variantId, sku, name, qty, unitPrice,
discountPercent, unitCost)` (guard qty>0, harga/cost≥0, diskon 0..100) menambah `PosSaleLine`;
method `Settle(decimal transactionDiscount, decimal amountTendered)`:
- guard ≥1 baris, `transactionDiscount ≥ 0` dan `≤ Subtotal`;
- `Subtotal = ΣLineTotal`; `Base = Subtotal − transactionDiscount`;
  `TaxTotal = Round(Base × TaxRateSnapshot/100)`; `GrandTotal = Base + TaxTotal`;
  `CogsTotal = Σ(qty×UnitCost)`;
- bila `IsCashPayment`: guard `amountTendered ≥ GrandTotal`, `ChangeGiven = amountTendered − GrandTotal`;
  bila non-tunai: `AmountTendered = GrandTotal`, `ChangeGiven = 0`;
- set `TransactionDiscount`. (Dipanggil sekali; baris immutable setelah settle.)

#### 1b. `PosSaleLine.cs` (baru)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| PosSaleId | int | FK |
| ProductVariantId | int | required |
| VariantSku | string | snapshot |
| ProductName | string | snapshot |
| Quantity | int | >0 |
| UnitPrice | decimal | ≥0, (18,2) |
| DiscountPercent | decimal | 0..100 |
| UnitCost | decimal | ≥0, (18,2), COGS snapshot |
| LineSubtotal | decimal | (18,2) = qty×UnitPrice |
| LineDiscount | decimal | (18,2) = LineSubtotal×%/100 |
| LineTotal | decimal | (18,2) = LineSubtotal−LineDiscount |

ctor(variantId, sku, name, qty, unitPrice, discountPercent, unitCost) → validasi + `Recompute()` (pola
`SalesOrderLine`).

### 2. Application (`src/MyApp.Application/PosSales/`)
- **`IPosSaleService`**:
  - `Task<IReadOnlyList<PosProductOptionDto>> SearchProductsAsync(int warehouseId, string? term, CancellationToken)` — varian aktif yang cocok SKU/nama/**barcode** (barcode exact diprioritaskan), + `UnitPrice = DiscountPrice ?? Price` + on-hand di gudang. Batas hasil (mis. 20).
  - `Task<PosSaleDto> CreateSaleAsync(string userId, CreatePosSaleRequest, CancellationToken)` — selesai transaksi, kembalikan DTO untuk struk.
  - `Task<PosSaleDto?> GetByIdAsync(int id, CancellationToken)` — cetak ulang/detail.
  - `Task<PagedResult<PosSaleListItemDto>> GetPagedAsync(int page, int pageSize, string? search, int? shiftId, CancellationToken)` — riwayat.
- **DTO** (`PosSaleDtos.cs`):
  - `PosProductOptionDto(int VariantId, string Sku, string ProductName, string? Barcode, decimal UnitPrice, int OnHand)`.
  - `PosSaleLineRequest(int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent)`.
  - `CreatePosSaleRequest(int PaymentMethodId, int? TaxId, decimal TransactionDiscount, decimal AmountTendered, IReadOnlyList<PosSaleLineRequest> Lines)`.
  - `PosSaleLineDto(int Id, int ProductVariantId, string VariantSku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal)`.
  - `PosSaleDto(int Id, string SaleNumber, int CashierShiftId, int WarehouseId, string WarehouseName, string CashierName, DateTime SaleDate, int PaymentMethodId, string PaymentMethodName, bool IsCashPayment, int? TaxId, decimal TaxRateSnapshot, decimal TransactionDiscount, decimal Subtotal, decimal TaxTotal, decimal GrandTotal, decimal AmountTendered, decimal ChangeGiven, IReadOnlyList<PosSaleLineDto> Lines)`.
  - `PosSaleListItemDto(int Id, string SaleNumber, DateTime SaleDate, string CashierName, string PaymentMethodName, decimal GrandTotal)`.
- **Validator** (`PosSaleValidators.cs`): `CreatePosSaleRequest` (`PaymentMethodId>0`, `TransactionDiscount≥0`, `AmountTendered≥0`, `Lines` not empty), line (`ProductVariantId>0`, `Quantity>0`, `UnitPrice≥0`, `DiscountPercent 0..100`). Aturan stok/shift/kembalian tunai divalidasi di service/domain.

### 3. Infrastructure (`src/MyApp.Infrastructure`)
- **`Services/PosSaleService.cs`** (pola `DeliveryOrderService`):
  - ctor inject `AppDbContext` + validators.
  - `CreateSaleAsync`: validate; `BeginTransaction`; muat **shift `Open` milik `userId`** (else `Fail("Tidak ada shift terbuka.")`); gudang = `shift.WarehouseId`; muat `PaymentMethod` (aktif) → `isCash = Type==Tunai`; muat `Tax` bila `TaxId` (rate snapshot) else rate 0.
    - **Fase-1** cek stok semua baris di `shift.WarehouseId` dgn akumulasi per varian (pola DO); bila kurang → `Fail(pesan per item)`.
    - Bangun `PosSale` + generate `SaleNumber`; untuk tiap baris muat `ProductVariant` (aktif) → `AddLine(..., unitCost: variant.CostPrice)`, tulis `StockMovement(Out, −qty, variant.CostPrice, refType:"POS")`, `UpsertStockAsync(−qty)`.
    - `sale.Settle(transactionDiscount, amountTendered)`.
    - muat `shift` (tracked) → `shift.RecordSale(paymentMethodId, isCash, sale.GrandTotal)`.
    - simpan; commit; kembalikan `GetByIdAsync`.
  - `SearchProductsAsync`: `ProductVariants` aktif join `Product` (nama), filter term (barcode==term OR Sku contains OR ProductName contains), `UnitPrice = DiscountPrice ?? Price`, `OnHand = Σ ProductStock(variant, warehouse)`. Take 20.
  - `GetByIdAsync`/`GetPagedAsync`: proyeksi read-only + join Warehouse/PaymentMethod nama.
  - `GenerateNumberAsync` pola `POS-{saleDate:yyyyMMdd}-####`.
- **`Persistence/AppDbContext.cs`**: `DbSet<PosSale>`/`DbSet<PosSaleLine>` + mapping (SaleNumber unique;
  decimals 18/2; FK `CashierShift`/`Warehouse`/`PaymentMethod` `Restrict`, `Tax` `Restrict` nullable,
  lines `Cascade`, `ProductVariant` di line `Restrict`; nav field-access).
- **Migration** `AddPosSale` (2 tabel). **DI**: `AddScoped<IPosSaleService, PosSaleService>()`.
- **JS interop**: tambah `window.appPrint = () => window.print();` di `wwwroot/js/app-interop.js`.

### 4. Web (`src/MyApp.Web/Components/Pages/Cashier/Pos/`)
- **Layout** `Components/Layout/PosLayout.razor` — layar penuh, top bar (brand, gudang, kasir, no struk
  berjalan, shift, jam via JS clock), `@Body`. Tanpa NavMenu rail.
- **`PosRegister.razor`** (`/cashier/pos`, policy `cashier.pos.create`, `@layout PosLayout`,
  `@rendermode InteractiveServer`):
  - Butuh shift `Open` milik user (via `ICashierShiftService.GetCurrentAsync`); bila tidak ada → kartu
    ajakan **Buka Shift** (link `/cashier/shifts`).
  - Panel kiri: scan/cari (debounce → `SearchProductsAsync(shift.WarehouseId, term)`; barcode exact → auto-tambah), tabel keranjang (state klien `List<CartLine>`): qty +/−, diskon%/baris, hapus; tampil stok on-hand.
  - Panel kanan: ringkasan (subtotal, diskon transaksi nominal [F4], pilih PPN dari Tax aktif, total),
    pembayaran (pilih `PaymentMethod` aktif; tunai → input bayar + quick-amount + kembalian; non-tunai
    sembunyikan kembalian), tombol **Bayar & Cetak Struk** [F9] → `CreateSaleAsync` → tampilkan struk + `appPrint()`.
  - Shortcut: F2 fokus cari, F4 fokus diskon, F9 bayar, Del hapus baris terpilih, Esc batalkan (kosongkan keranjang). Via `@onkeydown` di root + `preventDefault`.
  - Setelah sukses: reset keranjang untuk transaksi berikutnya.
- **Struk**: komponen `Receipt.razor` (atau markup print-only) format thermal, `@media print` sembunyikan
  chrome; dipicu `appPrint()`.
- **`PosSaleIndex.razor`** (`/cashier/sales`, policy `cashier.pos.index`) — riwayat (No, Tanggal, Kasir,
  Metode, Total) + search + `Pager`.
- **`PosSaleDetail.razor`** (`/cashier/sales/{Id:int}`, policy `cashier.pos.index`) — detail + tombol
  **Cetak Ulang** (`appPrint()`).
- **Menu/otorisasi** (`AppMenus.cs`): resource `cashier.pos` `[ActIndex, ActCreate]` grup **Kasir**; entri
  NavMenu grup Kasir ("Kasir (POS)" → `/cashier/pos`, "Riwayat Penjualan" → `/cashier/sales`). `cashier.any`
  sudah ada dari D1 → grup Kasir tampil. Admin auto-grant.

### 5. Testing
- **Unit** (`PosSaleTests`): perhitungan (LineTotal diskon%, Subtotal, diskon transaksi nominal, PPN
  exclusive, GrandTotal, kembalian tunai); guard (`transactionDiscount > Subtotal` ditolak; tunai
  `amountTendered < GrandTotal` ditolak; non-tunai set tendered=grand & change=0; ≥1 baris); `CogsTotal`.
  Validator.
- **Integration** (`PosSaleServiceTests`): CreateSale kurangi `ProductStock` + `StockMovement(Out,
  refType "POS")`; `UnitCost`=variant.CostPrice & **CostPrice/MA tak berubah**; `shift.RecordSale`
  terakumulasi (tunai→`CashSalesTotal`, non-tunai tidak); stok kurang → ditolak tanpa mutasi & tanpa
  PosSale; wajib shift terbuka (tanpa shift → ditolak); nomor `POS-` harian; `GetByIdAsync` struk lengkap;
  `SearchProductsAsync` cocokkan barcode/SKU/nama + on-hand.

## Di luar scope D2
- Void/retur transaksi & reversal (koreksi via stock opname).
- Kas masuk/keluar tengah-shift; split payment; multi-currency.
- Integrasi printer thermal ESC/POS langsung (pakai print browser).
- Pelanggan/loyalty; diskon per-item nominal.
