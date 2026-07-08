# Tahap D1 — Kasir/POS: Sesi Kasir (Cashier Shift)

**Tanggal:** 2026-07-02
**Status:** Menunggu review user (hasil brainstorming)
**Program besar:** Kasir/POS (Tahap D). Mengikuti pola program Transaksi (A/B/C).
Tahap D = Penjualan Langsung (POS). Dokumen ini = **D1 (Sesi Kasir)**; **D2 (layar POS + `PosSale`)** dapat spec sendiri.

## Konteks

Fitur baru **Kasir / POS** untuk **penjualan langsung** (walk-in, satu langkah: scan → keranjang → bayar →
struk), berdasarkan mockup `OneDrive - acetris.co.id/Documents/Cashier-Mockups/1-lini.html`.

Karena user memilih **manajemen shift penuh + rekonsiliasi**, fitur dipecah dua sub-proyek berurutan:

- **D1 — Sesi Kasir (dokumen ini):** kasir **buka shift** dgn saldo awal kas, semua transaksi POS terikat ke
  shift terbuka miliknya, lalu **tutup shift** dgn hitung kas fisik vs sistem (selisih). Rincian total per
  metode pembayaran per shift.
- **D2 — Layar POS:** entitas `PosSale`/`PosSaleLine` + layar kasir (scan, keranjang, ringkasan, bayar,
  struk cetak). Saat sebuah `PosSale` diselesaikan, memanggil `CashierShift.RecordSale(...)`.

### Keputusan lintas-D (hasil brainstorming, berlaku juga untuk D2)
- **Model data:** agregat **POS baru & berdiri sendiri** (`PosSale`), BUKAN memperluas `SalesOrder`
  (menghindari beban approval/kredit/delivery). Langkah bayar langsung "selesai" dalam satu DB transaction,
  meniru pola `DeliveryOrderService.PostAsync`: `StockMovement(Out, −qty, variant.CostPrice, refType:"POS")`,
  `db.UpsertStockAsync(−qty)`, snapshot COGS, **tanpa** mengubah moving average. (Detail di D2.)
- **Pembayaran:** multi-metode dari master `PaymentMethod`, **satu metode per transaksi**. Tunai hitung
  kembalian; non-tunai tanpa kembalian. Rekonsiliasi shift memisah total per metode.
- **Pelanggan:** walk-in saja (master `Customer` tidak tersentuh).
- **Struk:** HTML siap-cetak + `window.print()` (D2).
- **Diskon:** diskon per-baris (persen, pola SO) **dan** diskon tingkat-transaksi (D2).
- **Pajak:** **satu tarif pajak per-transaksi**, **exclusive** (ditambah di atas subtotal−diskon), dipilih
  dari master `Tax` (D2).
- **Harga jual:** `variant.DiscountPrice ?? variant.Price` (D2).
- **Stok:** dikurangi di **gudang shift**; transaksi **ditolak bila stok kurang** (domain `ProductStock`
  melarang negatif, konsisten dgn DO). COGS = `variant.CostPrice` di-snapshot, MA tidak berubah. (D2.)
- **Nomor:** `POS-YYYYMMDD-####` (harian) untuk sale; `SHIFT-YYYYMMDD-####` (harian) untuk shift.
- **Di luar scope awal:** void/retur transaksi, kas masuk/keluar tengah-shift (paid-in/out laci),
  split payment, multi-currency.

### Jembatan decomposition (kenapa D1 bisa dibangun & diuji tanpa D2)
Agregat `CashierShift` menyimpan **akumulator total** yang di-update lewat method domain
`RecordSale(paymentMethodId, isCash, amount)`. D1 meng-unit-test method ini langsung (buka → RecordSale ×N →
tutup → cek variance) tanpa perlu `PosSale`. D2 hanya perlu memanggil `RecordSale` di dalam transaksi
penyelesaian sale. Dependensi tetap benar: D1 tidak mereferensikan entitas D2.

## Pola Existing yang Diikuti
- Clean Architecture: Domain (`AuditableEntity`, private setters, invariant via ctor/method) → Application
  (DTO record + interface service + FluentValidation) → Infrastructure (`AppDbContext` mapping + service + DI)
  → Web (Blazor Server, permission via `AppMenus`).
- Enum sebagai string: `.HasConversion<string>().HasMaxLength(20)`. Uang/cost `decimal(18,2)` via
  `.HasPrecision(18, 2)`. Pembulatan `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- Service melempar `FluentValidation.ValidationException` untuk error validasi/duplikasi/aturan.
- Otorisasi permission via `AppMenus.cs` (resource + action), policy `@attribute [Authorize(Policy=...)]` /
  `<AuthorizeView Policy=...>`. Admin auto-grant lewat `AllPermissions`/`BootstrapSeeder`.
- UI list: tabel + search + `Pager` (15/hal) + `SwalService`. UI form: Atlas (`.pf`) + validasi inline + spinner.
- `AuditableEntity` sudah menyediakan `CreatedBy`/`CreatedAt` (audit otomatis). `CashierUserId` disimpan
  eksplisit (Id Identity) agar query "shift terbuka milik user" tahan terhadap perubahan username.

## Scope D1

### 1. Domain (`src/MyApp.Domain/Entities`)

#### 1a. `CashierShiftStatus.cs` (baru)
`Open`, `Closed`.

#### 1b. `CashierShift.cs` (baru, `AuditableEntity`, private setters)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| ShiftNumber | string | unik, auto `SHIFT-YYYYMMDD-####`, di-set saat buka |
| WarehouseId | int | required (>0), gudang penjualan shift |
| CashierUserId | string | required, Id user Identity pemilik shift |
| CashierName | string | required, snapshot nama kasir saat buka |
| Status | CashierShiftStatus | default `Open` |
| OpenedAt | DateTime | required, waktu buka |
| OpeningFloat | decimal | ≥ 0, (18,2) — saldo awal kas laci |
| CashSalesTotal | decimal | ≥ 0, (18,2) — akumulator penjualan tunai |
| ClosedAt | DateTime? | waktu tutup |
| CountedCash | decimal? | ≥ 0, (18,2) — kas fisik terhitung saat tutup |
| CashVariance | decimal? | `CountedCash − ExpectedCash`, di-set saat tutup |
| ClosingNote | string? | ≤ 500 |
| Totals | IReadOnlyCollection\<CashierShiftTotal\> | child, akses field |

- `decimal ExpectedCash => OpeningFloat + CashSalesTotal` (computed, tidak dipetakan).
- `decimal TotalSalesAmount => Totals.Sum(t => t.TotalAmount)` (computed).
- `int TransactionCount => Totals.Sum(t => t.TransactionCount)` (computed).

**Method:**
- ctor(`shiftNumber, warehouseId, cashierUserId, cashierName, openingFloat, openedAt`): validasi
  (`warehouseId>0`, id/nama tidak kosong, `openingFloat≥0`); `Status=Open`; `CashSalesTotal=0`.
- `void RecordSale(int paymentMethodId, bool isCash, decimal amount)`:
  - `amount` = **total tagihan sale** (grand total), BUKAN uang tunai diterima — laci hanya bertambah sebesar
    total penjualan; kembalian tidak menambah kas. Jadi expected drawer cash = saldo awal + Σ total penjualan tunai.
  - guard `Status==Open` (else `InvalidOperationException`);
  - guard `paymentMethodId>0` & `amount>0` (`ArgumentException`);
  - cari/insert `CashierShiftTotal` untuk `paymentMethodId`; `TotalAmount += amount`, `TransactionCount += 1`;
  - bila `isCash`: `CashSalesTotal += amount`.
- `void Close(decimal countedCash, string? note, DateTime closedAt)`:
  - guard `Status==Open` (else throw); guard `countedCash≥0`;
  - `CountedCash=countedCash`; `CashVariance = countedCash − ExpectedCash`; `ClosedAt=closedAt`;
    `ClosingNote=note` (≤500, di-trim); `Status=Closed`.

#### 1c. `CashierShiftTotal.cs` (baru, child)
| Field | Tipe | Aturan |
|---|---|---|
| Id | int | PK |
| CashierShiftId | int | FK |
| PaymentMethodId | int | FK master PaymentMethod |
| TotalAmount | decimal | ≥ 0, (18,2) |
| TransactionCount | int | ≥ 0 |

### 2. Application (`src/MyApp.Application/CashierShifts/`)

- **`ICashierShiftService`**:
  - `Task<CashierShiftDto?> GetCurrentAsync(string userId, CancellationToken)` — shift `Open` milik user (atau null).
  - `Task<CashierShiftDto> OpenAsync(string userId, string userName, OpenShiftRequest, CancellationToken)` —
    tolak bila user sudah punya shift `Open`; validasi gudang ada & aktif; generate `ShiftNumber`; simpan.
  - `Task<bool> CloseAsync(int shiftId, string userId, CloseShiftRequest, CancellationToken)` — hanya shift
    `Open` **milik user tsb**; hitung variance; simpan.
  - `Task<CashierShiftDto?> GetByIdAsync(int id, CancellationToken)` — detail/laporan (header + total per metode).
  - `Task<PagedResult<CashierShiftListItemDto>> GetPagedAsync(int page, int pageSize, string? search, CashierShiftStatus? status, CancellationToken)` — riwayat + filter.
- **DTO records** (`CashierShiftDtos.cs`):
  - `CashierShiftListItemDto(int Id, string ShiftNumber, int WarehouseId, string WarehouseName, string CashierName, DateTime OpenedAt, DateTime? ClosedAt, decimal TotalSalesAmount, string Status)`.
  - `ShiftMethodTotalDto(int PaymentMethodId, string MethodName, decimal TotalAmount, int TransactionCount)`.
  - `CashierShiftDto(int Id, string ShiftNumber, int WarehouseId, string WarehouseName, string CashierUserId, string CashierName, string Status, DateTime OpenedAt, decimal OpeningFloat, decimal CashSalesTotal, decimal ExpectedCash, DateTime? ClosedAt, decimal? CountedCash, decimal? CashVariance, string? ClosingNote, decimal TotalSalesAmount, int TransactionCount, IReadOnlyList<ShiftMethodTotalDto> MethodTotals)`.
  - `OpenShiftRequest(int WarehouseId, decimal OpeningFloat)`.
  - `CloseShiftRequest(decimal CountedCash, string? ClosingNote)`.
- **Validators** (`CashierShiftValidators.cs`): Open (`WarehouseId>0`, `OpeningFloat≥0`); Close
  (`CountedCash≥0`, `ClosingNote` ≤500). Aturan "1 shift terbuka/user", "gudang aktif", "kepemilikan shift"
  divalidasi **di service** (butuh state DB).

### 3. Infrastructure (`src/MyApp.Infrastructure`)

- **`Services/CashierShiftService.cs`** (pola service transaksional yang ada):
  - ctor inject `AppDbContext` + validators.
  - `OpenAsync`: `BeginTransaction`; tolak bila `AnyAsync(s => s.CashierUserId==userId && s.Status==Open)`
    (`Fail("Anda masih punya shift terbuka. Tutup dulu sebelum membuka yang baru.")`); validasi
    `Warehouse` ada & `IsActive`; `GenerateShiftNumberAsync`; buat & simpan; Commit; kembalikan `GetByIdAsync`.
  - `CloseAsync`: `BeginTransaction`; load shift; bila null → false; bila
    `Status!=Open` → `Fail("Shift sudah ditutup.")`; bila `CashierUserId!=userId` →
    `Fail("Anda hanya bisa menutup shift milik sendiri.")`; `shift.Close(...)`; simpan; Commit.
  - `GetCurrentAsync`/`GetByIdAsync`/`GetPagedAsync`: proyeksi read-only (`AsNoTracking`), join Warehouse +
    PaymentMethod (nama metode) untuk `MethodTotals`. `GenerateShiftNumberAsync` pola sama seperti
    `GenerateNumberAsync` DO (prefix per tanggal, ambil terakhir, +1, `D4`).
- **`Persistence/AppDbContext.cs`**:
  - `DbSet<CashierShift> CashierShifts`, `DbSet<CashierShiftTotal> CashierShiftTotals`.
  - Fluent: `ShiftNumber` unique; `Status` enum→string(20); decimal (18,2) untuk semua uang; FK `Warehouse`
    `Restrict`; `Totals` `Cascade` + FK `PaymentMethod` `Restrict`; nav `Totals` `PropertyAccessMode.Field`;
    **filtered unique index** pada `CashierUserId` `WHERE Status='Open'` (pengaman DB 1-shift-terbuka/user).
- **`DependencyInjection.cs`**: `AddScoped<ICashierShiftService, CashierShiftService>()`.
- **Migration**: satu migration baru `AddCashierShift` — 2 tabel + filtered unique index. `Down()` membuang keduanya.
- **appsettings**: tidak ada section baru.

### 4. Web (`src/MyApp.Web/Components/Pages/Cashier/Shifts/`)

- **`ShiftIndex.razor`** (`/cashier/shifts`, policy `cashier.shifts.index`) — banner status: bila user punya
  shift terbuka → tampilkan ringkas + link detail; bila tidak → tombol **Buka Shift** (`Policy=...create`) yang
  membuka form (pilih gudang aktif, input saldo awal). Tabel riwayat (No. Shift, Gudang, Kasir, Buka, Tutup,
  Total Penjualan, Status badge) + search + filter status + `Pager` (15/hal). (Mirror pola *Index* modul lain.)
- **`ShiftDetail.razor`** (`/cashier/shifts/{Id:int}`, policy `cashier.shifts.index`) — info shift (No., Gudang,
  Kasir, Buka, Saldo Awal, Status) + tabel **total per metode** (metode, jumlah transaksi, total) + kartu
  rekonsiliasi (Expected Cash, dan setelah tutup: Counted/Variance + catatan). Bila `Open` & **milik user**:
  tombol **Tutup Shift** (`Policy=...close`) → form (kas fisik terhitung + catatan) → simpan & tampilkan selisih.
  Setelah tutup → read-only. (Mirror pola *Detail* modul lain, gaya Atlas.)
- **Menu & otorisasi** (`Authorization/AppMenus.cs`): resource baru
  `new("cashier.shifts", "Sesi Kasir", "bi-cash-stack", [ActIndex, ActCreate, ActClose])` (`ActClose` sudah ada
  dari B2). Entri `NavMenu.razor` grup **Kasir** (hardcoded, pola sama seperti entri GRN/DO). Admin auto-grant
  via `AllPermissions`/`BootstrapSeeder`.
- Gaya: konsisten dgn modul lain — accent/font variabel (`var(--app-accent*)`, `var(--app-font)`), rem untuk
  font-size (mengikuti preset Appearance).

### 5. Testing

- **Unit** (`MyApp.UnitTests`):
  - `CashierShift.Open`: set Status/OpeningFloat, CashSalesTotal=0.
  - `RecordSale`: akumulasi `TotalAmount`/`TransactionCount` per metode; `CashSalesTotal` hanya bertambah bila
    `isCash`; tolak saat `Closed`; tolak `amount≤0`/`paymentMethodId≤0`; multi-metode terpisah benar.
  - `Close`: hitung `CashVariance` (kurang/lebih/pas), set `Closed`; tolak bila sudah `Closed`; tolak
    `countedCash<0`; `ExpectedCash` benar.
  - Validator Open & Close.
- **Integration** (`MyApp.IntegrationTests`):
  - `OpenAsync` sukses set nomor unik harian; **tolak shift terbuka kedua** utk user sama; tolak gudang tidak
    aktif/tidak ada.
  - `CloseAsync` hitung variance benar; **tolak menutup shift milik user lain**; tolak shift sudah `Closed`.
  - `GetCurrentAsync` kembalikan shift terbuka user (atau null).
  - Akumulasi `RecordSale` tercermin di `MethodTotals`/`TotalSalesAmount` pada `GetByIdAsync`
    (memanggil domain method langsung dalam seed, karena `PosSale` belum ada).

## Di luar scope D1
- Entitas `PosSale`/`PosSaleLine` + layar kasir + struk → **D2**.
- Kas masuk/keluar tengah-shift (paid-in/out laci).
- Void/retur, split payment, multi-currency.
- Laporan lintas-shift/rekap harian gabungan (potensi lanjutan).
