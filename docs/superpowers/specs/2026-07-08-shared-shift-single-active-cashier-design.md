# Kasir/POS — Shift Bersama Multi-User + Single-Active-Cashier

**Tanggal:** 2026-07-08
**Status:** Menunggu review user (hasil brainstorming)
**Program besar:** Kasir/POS (Tahap D). Melanjutkan D1 (Sesi Kasir) & D2 (Layar POS).
**Referensi:** `2026-07-02-d1-cashier-shift-design.md` (model shift & rekonsiliasi yang diubah di sini).

## Konteks & Tujuan

Model shift saat ini **dimiliki satu user**: `CashierShift.CashierUserId`/`CashierName` menandai pemilik,
`OpenAsync` menolak shift kedua **per user**, dan filtered unique index adalah
`CashierUserId WHERE Status='Open'`. `PosSaleService.CreateSaleAsync(userId, ...)` mencari shift lewat
`CashierUserId == userId`, dan `PosSale` **tidak menyimpan** siapa kasir yang melayani (struk/laporan memakai
nama pemilik shift).

Permintaan user mengubah dua perilaku:

1. **Shift bersama multi-user, satu per gudang.** Shift tidak lagi "milik" user. Semua kasir bisa mencatat
   penjualan ke shift terbuka milik sebuah **gudang**. Maksimal **satu shift terbuka per gudang**. Setiap
   transaksi mencatat **kasir yang benar-benar melayani** (bukan sekadar pembuka shift).
2. **Satu halaman kasir aktif per user (single-active-cashier).** User yang sama **tidak boleh** membuka
   `/cashier/pos` di dua tab/perangkat sekaligus. Halaman kedua **diblokir** (bukan takeover) dengan layar
   terkunci.

### Keputusan hasil brainstorming (mengikat)

- **Cakupan shift:** per **gudang** (satu shift terbuka per outlet), bukan global.
- **Atribusi penjualan:** **dicatat per-transaksi** — tambah kolom kasir (user Id + nama) di `PosSale`.
- **Penutupan shift:** **hanya pembuka** shift yang boleh menutup (aturan kepemilikan sekarang dipertahankan
  **khusus untuk close**).
- **Kunci halaman:** **blokir** halaman kedua (tidak ada takeover).
- **Attach ke shift di POS (Keputusan A):** resolusi **in-page** di `/cashier/pos` — 0 shift terbuka →
  kartu "buka shift dulu"; tepat 1 → auto-attach; ≥2 (multi-outlet) → pemilih outlet ringkas. Satu route,
  menu tetap.
- **Mekanisme kunci (Keputusan B):** **registry singleton in-memory** (`userId → {token, since}`).
  **Asumsi: satu instance server web.** Bila kelak multi-instance di belakang load balancer, kunci harus
  dipindah ke penyimpanan bersama (di luar scope).

## Pola Existing yang Diikuti

- Clean Architecture: Domain (`AuditableEntity`, private setters, invariant via ctor/method) → Application
  (DTO record + interface service + FluentValidation) → Infrastructure (`AppDbContext` mapping + service +
  DI) → Web (Blazor Server, permission via `AppMenus`).
- Enum→string, uang `decimal(18,2)`, `Math.Round(v, 2, MidpointRounding.AwayFromZero)`.
- Service melempar `FluentValidation.ValidationException` untuk error aturan/duplikasi.
- Nomor shift `SHIFT-YYYYMMDD-####`, sale `POS-YYYYMMDD-####`.

## Scope Perubahan

### 1. Domain (`src/MyApp.Domain/Entities`)

#### 1a. `CashierShift.cs`
- **Tanpa perubahan skema field.** `CashierUserId`/`CashierName` **berubah makna** menjadi **"dibuka oleh"**
  (opener). Invariant domain tidak berubah; ctor & `RecordSale`/`Close` tetap.
- Catatan: aturan "satu shift terbuka per gudang" dan "hanya pembuka boleh menutup" **ditegakkan di service**
  (butuh state DB / identitas pemanggil), bukan di domain — konsisten dgn D1.

#### 1b. `PosSale.cs`
- Tambah dua field (private setter): `string CashierUserId`, `string CashierName`.
- Ctor menerima `cashierUserId`, `cashierName` (setelah param yang ada); validasi tidak kosong
  (`ArgumentException`), `Trim()`. Field-field lain & `Settle`/`AddLine` tidak berubah.

### 2. Application (`src/MyApp.Application`)

#### 2a. `CashierShifts/ICashierShiftService.cs`
- **Hapus** `GetCurrentAsync(string userId, ...)` (semantik per-user sudah salah).
- **Tambah**:
  - `Task<IReadOnlyList<CashierShiftDto>> GetOpenShiftsAsync(CancellationToken)` — semua shift `Open`
    (untuk banner shift & resolusi POS).
  - `Task<CashierShiftDto?> GetOpenShiftByWarehouseAsync(int warehouseId, CancellationToken)` — shift `Open`
    gudang tsb (atau null).
- `OpenAsync`, `CloseAsync`, `GetByIdAsync`, `GetPagedAsync` — tanda tangan tetap.

#### 2b. `PosSales/IPosSaleService.cs`
- `CreateSaleAsync` berubah tanda tangan:
  `Task<PosSaleDto> CreateSaleAsync(string userId, string userName, int shiftId, CreatePosSaleRequest request, CancellationToken)`.
  (Tambah `userName` + `shiftId` eksplisit; POS mengirim shift yang di-attach.)
- DTO: `PosSaleDto` & `PosSaleListItemDto` **tetap** punya field `CashierName` — sekarang diisi dari
  `PosSale.CashierName` (operator asli), bukan nama pemilik shift.
- `CreatePosSaleRequest` **tidak berubah** (shiftId lewat parameter, bukan body).

#### 2c. Validators
- `CashierShiftValidators`: tak berubah (aturan gudang/kepemilikan di service).
- `PosSaleValidators`: tak berubah.

### 3. Infrastructure (`src/MyApp.Infrastructure`)

#### 3a. `Services/CashierShiftService.cs`
- `OpenAsync`: ganti guard per-user → **per-gudang**:
  `AnyAsync(s => s.WarehouseId == request.WarehouseId && s.Status == Open)` →
  `Fail("Gudang ini sudah punya shift terbuka.")`. Validasi gudang ada & aktif tetap. Opener disimpan dari
  `userId/userName`.
- `CloseAsync`: **tak berubah** (tetap `shift.CashierUserId != userId → Fail("Anda hanya bisa menutup shift
  milik sendiri.")`). Pesan boleh disesuaikan: *"Hanya pembuka shift yang boleh menutup."*
- **Hapus** `GetCurrentAsync`. **Tambah** `GetOpenShiftsAsync` (proyeksi read-only, urut `OpenedAt`) &
  `GetOpenShiftByWarehouseAsync` (reuse `GetByIdAsync` setelah cari id).

#### 3b. `Services/PosSaleService.cs`
- `CreateSaleAsync`: hapus lookup `CashierUserId == userId`; sebagai gantinya **muat shift by `shiftId`** +
  assert `Status == Open` (else `Fail("Shift tidak terbuka.")`). Stempel `userId/userName` saat membuat
  `PosSale`. Sisa alur (cek stok, StockMovement, UpsertStock, `RecordSale`, commit) tetap.
- `GetByIdAsync` & `GetPagedAsync`: `CashierName` diambil dari **`sale.CashierName`** langsung (hapus join
  ke `CashierShift.CashierName`).

#### 3c. `Persistence/AppDbContext.cs`
- `CashierShift`: ganti
  `HasIndex(x => x.CashierUserId).IsUnique().HasFilter("[Status] = 'Open'")` →
  `HasIndex(x => x.WarehouseId).IsUnique().HasFilter("[Status] = 'Open'")`.
- `PosSale`: map `CashierUserId` (`HasMaxLength(450).IsRequired()`),
  `CashierName` (`HasMaxLength(256).IsRequired()`).

#### 3d. Migration baru `SharedShiftPerWarehouse`
- Drop index lama `CashierUserId WHERE Status='Open'`; buat index baru `WarehouseId WHERE Status='Open'`.
  - **Caveat data:** bila DB sudah punya ≥2 shift `Open` di gudang yang sama, pembuatan index gagal —
    harus ditutup manual dulu. (Dev data D1/D2 diasumsikan ≤1/gudang.)
- Tambah kolom `PosSales.CashierUserId`, `PosSales.CashierName` **nullable**, lalu **backfill** dari opener
  shift (`UPDATE p SET CashierUserId = s.CashierUserId, CashierName = s.CashierName FROM PosSales p JOIN
  CashierShifts s ON s.Id = p.CashierShiftId`), lalu `AlterColumn` → `NOT NULL`.
- `Down()`: kebalikannya (drop 2 kolom; balik index ke `CashierUserId`).

#### 3e. `Web/…/Services/PosSessionRegistry.cs` (baru) + DI
- **Lokasi:** `src/MyApp.Web/Services/PosSessionRegistry.cs` (murni in-memory, urusan sirkuit UI).
- Interface `IPosSessionRegistry`:
  - `bool TryAcquire(string userId, string token)` — sukses bila belum ada entry untuk `userId`, **atau**
    entry ada dgn token sama (re-render/reconnect). Gagal bila token berbeda (sesi lain aktif).
  - `void Release(string userId, string token)` — hapus hanya bila token cocok (dispose sesi lama tak
    mengusir sesi baru).
  - `DateTime? ActiveSince(string userId)` — untuk pesan "dibuka sejak …".
- Implementasi: `ConcurrentDictionary<string, (string Token, DateTime Since)>`, thread-safe.
- **DI:** `builder.Services.AddSingleton<IPosSessionRegistry, PosSessionRegistry>()` di `Program.cs`.

### 4. Web (`src/MyApp.Web/Components/Pages/Cashier`)

#### 4a. `Pos/PosRegister.razor`
- Inject `IPosSessionRegistry`. State: `_blocked` (bool), `_token` (Guid string per sirkuit), `_shift`
  (dipilih), `_openShifts` (list), `_pickOutlet` (bool).
- `OnInitializedAsync`:
  1. Ambil `_userId/_userName`.
  2. `_token = Guid.NewGuid().ToString()`; `if (!registry.TryAcquire(_userId, _token)) { _blocked = true; return; }`.
  3. `_openShifts = await Shifts.GetOpenShiftsAsync()`. Resolusi (Keputusan A): 0 → biarkan `_shift=null`
     (kartu "belum ada shift"); 1 → `_shift = _openShifts[0]`; ≥2 → `_pickOutlet = true`.
  4. Muat taxes/methods/clock seperti sekarang.
- Render:
  - `_blocked` → **layar terkunci**: ikon gembok, "Kasir sedang dibuka di tab/perangkat lain"
    (+ "dibuka sejak {ActiveSince:HH:mm}"), tombol **Coba lagi** (re-`TryAcquire`; bila sukses lanjut resolusi)
    dan link **Keluar** ke `/cashier/shifts`.
  - `_pickOutlet` → daftar outlet (nama gudang + no shift + pembuka) → klik memilih `_shift`.
  - `_shift is null` (dan tidak blocked/pick) → kartu "Belum ada shift terbuka" (existing).
  - Selain itu → grid POS existing.
- Header: `@_shift.WarehouseName · @_userName` (operator login), bukan `_shift.CashierName`.
- `PayAsync`: `await Pos.CreateSaleAsync(_userId, _userName, _shift.Id, req)`; setelah sukses refresh shift
  via `_shift = await Shifts.GetOpenShiftByWarehouseAsync(_shift.WarehouseId)`.
- `Dispose`: `registry.Release(_userId, _token)` (selain timer/cts existing).

#### 4b. `Shifts/ShiftIndex.razor`
- Ganti `_current` (single, `GetCurrentAsync`) → `_openShifts` (`GetOpenShiftsAsync`).
- Banner: bila ada shift terbuka, tampilkan **daftar** ("Shift terbuka: {no} · {gudang} · dibuka {jam}")
  masing-masing dengan tombol **Masuk Kasir** (`→ /cashier/pos`) & **Buka detail**.
- Tombol **Buka Shift** (Policy `cashier.shifts.create`) **selalu tampil** (tidak lagi disembunyikan saat
  user punya shift) — gate per-gudang ditegakkan `OpenAsync`. Pesan error duplikasi gudang ditampilkan.

#### 4c. `Shifts/ShiftDetail.razor`
- Bila shift `Open`: tombol **Tutup Shift** hanya untuk **pembuka** (tampil bila `shift.CashierUserId ==
  _userId`; user lain melihat info tanpa tombol). Tambah tombol **Masuk Kasir** untuk shift `Open`.
  (Verifikasi teks/label saat implementasi; pola Atlas dipertahankan.)

#### 4d. Menu & otorisasi
- Tidak ada resource/permission baru. `cashier.pos.create` & `cashier.shifts.*` tetap.

### 5. Testing

- **Unit** (`MyApp.UnitTests`):
  - `PosSale` ctor menyimpan `CashierUserId/CashierName`; tolak kosong.
  - `PosSessionRegistry`: acquire pertama sukses; acquire kedua token beda gagal; acquire ulang token sama
    sukses; `Release` token beda tak menghapus; setelah `Release` token benar bisa acquire lagi.
- **Integration** (`MyApp.IntegrationTests`):
  - `OpenAsync`: **tolak shift kedua di gudang yang sama**; **izinkan** user yang sama membuka shift di
    **gudang berbeda**; tolak gudang tak aktif/tak ada.
  - `CloseAsync`: **hanya pembuka** bisa menutup (user lain ditolak); hitung variance benar.
  - `GetOpenShiftsAsync`/`GetOpenShiftByWarehouseAsync` kembalikan yang benar.
  - `CreateSaleAsync`: **dua user berbeda** mencatat sale ke **satu shift** gudang; tiap `PosSale`
    menyimpan operatornya; `MethodTotals`/`TotalSalesAmount` shift terakumulasi benar; `GetByIdAsync`
    menampilkan `CashierName` = operator (bukan pembuka).
  - Filtered unique index baru menolak dua shift Open segudang di level DB (opsional bila mudah diuji).

## Di luar scope

- **Takeover** sesi (ambil-alih) — sengaja ditolak; hanya blokir.
- **Kunci terdistribusi** multi-instance (load balancer) — asumsi satu instance server.
- **Laporan rekap penjualan per-kasir** — data kini mendukung (kolom baru), tapi UI laporan tidak dibuat di
  sini.
- Void/retur, split payment, kas masuk/keluar tengah-shift, multi-currency (tetap di luar scope seperti D1).
