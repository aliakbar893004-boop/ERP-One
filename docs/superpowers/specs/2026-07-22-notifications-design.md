# Fase 6 — Notifikasi In-App — Design

**Tanggal:** 2026-07-22
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`

## Ringkasan

Notifikasi in-app **computed/derived**: dihitung real-time dari state saat ini per user, tanpa tabel, event, atau hook. Sebuah ikon lonceng di top-bar menampilkan jumlah item aktif; klik membuka popover berisi grup notifikasi yang me-link ke halaman terkait. Item hilang otomatis begitu kondisinya selesai (mis. dokumen sudah di-approve, stok terisi, invoice lunas). Tak ada persistensi, mark-as-read, atau push.

## Keputusan brainstorming (2026-07-22)

1. **Model computed/derived** (bukan persisted event-driven) — ketiga sumber semuanya derivable dari state; hindari kompleksitas hook/dedup/cleanup.
2. **Live count, tanpa read-state** — badge = jumlah item aktif; tak ada tabel `UserNotificationState`.
3. **Targeting per user:** approval = per-**role** (role step Pending, pembuat dikecualikan); stok menipis & jatuh tempo = per-**izin** halaman terkait. Disetujui.

## Arsitektur

### 1. Sumber notifikasi (semua di-gate)

- **Approval menunggu saya** — `ApprovalStep` berstatus `Pending` yang merupakan step aktif (StepOrder terkecil yang Pending untuk dokumennya) dengan `RoleName ∈ roles(user)` **dan** pembuat dokumen ≠ user (separation of duties, konsisten dgn `EvaluateCanApproveAsync` di halaman Detail). Dikelompokkan per `ApprovalDocumentType`. Tiap grup me-link ke index modul terkait, difilter status PendingApproval.
  - Pemetaan `ApprovalDocumentType → (label, route, permission)` disimpan sebagai tabel statis di service. Doc type yang dicakup: `PurchaseOrder`, `SalesOrder`, `SupplierPayment`, `StockTransfer`, `StockOpname`, `PurchaseReturn`, `SalesReturn`, `PosSaleVoid` (dan lainnya bila punya index & route yang jelas; yang tak punya route dilewati).
- **Stok menipis** — jumlah (SKU × gudang) dengan `qty ≤ ReorderLevel` (reuse `ILowStockService`; bila tak ada method count, hitung dari `ProductStocks` join `ProductVariant.ReorderLevel`). Satu grup → `/inventory/low-stock`. Di-gate izin `inventory.low-stock.index`.
- **Jatuh tempo** — invoice dengan `Outstanding > 0` & `DueDate ≤ AsOf + N` (N = 7 hari, termasuk yang sudah lewat/overdue), status bukan Cancelled.
  - **AR** (CustomerInvoice) → `/reports/ar-aging`, di-gate `reports.ar-aging`.
  - **AP** (SupplierInvoice) → `/reports/ap-aging`, di-gate `reports.ap-aging`.
  - `Outstanding = GrandTotal − PaidAmount − CreditedAmount` (sudah ada pasca Fase 2a/2b).

### 2. Application (`src/ErpOne.Application/Notifications/`)

- `NotificationDtos.cs`:
  ```
  record NotificationGroupDto(string Key, string Label, string Icon, int Count, string Url, string Severity);
  record NotificationSummaryDto(int TotalCount, IReadOnlyList<NotificationGroupDto> Groups);
  ```
  `Severity ∈ {"info","warn","danger"}` untuk styling (mis. overdue = danger, low-stock = warn, approval = info).
- `INotificationService`:
  ```
  Task<NotificationSummaryDto> GetForUserAsync(string userName, IReadOnlyCollection<string> roles,
      Func<string,bool> hasPermission, DateTime asOf, CancellationToken ct = default);
  ```
  Delegate `hasPermission` (dan `roles`) mengikuti pola `isInRole` pada `IApprovalService` — layer Application tak menyentuh `ClaimsPrincipal`. `asOf` untuk window jatuh tempo (testable).
- Konstanta: `DueSoonDays = 7`.

### 3. Infrastructure (`src/ErpOne.Infrastructure/Services/Notifications/NotificationService.cs`)

- Primary-ctor DI: `AppDbContext db` (+ opsional `ILowStockService`). `services.AddScoped<INotificationService, NotificationService>()`.
- `GetForUserAsync`:
  1. **Approvals:** query `db.ApprovalSteps` Status=Pending, RoleName ∈ roles; pastikan step tsb adalah step Pending terkecil dokumennya; keluarkan dokumen yang pembuatnya = userName (join ke tabel dokumen per type, atau simpan `CreatedBy` — lihat catatan). Group by DocumentType → count. Untuk tiap DocumentType yang punya count>0 & ada di pemetaan, buat `NotificationGroupDto` (label/route/permission dari pemetaan; skip bila `!hasPermission(perm)`; approval umumnya di-gate `.approve`).
  2. **Low stock:** bila `hasPermission("inventory.low-stock.index")`, hitung count; bila >0, tambah grup.
  3. **AR/AP due:** bila izin terkait, hitung count invoice due≤asOf+N & Outstanding>0; tambah grup.
  4. `TotalCount = Σ Groups.Count`.
- **Creator-exclusion:** `ApprovalStep` tak menyimpan pembuat dokumen. Opsi: (a) join per-doc-type ke `CreatedBy` (akurat, tapi 8 join) — atau (b) sederhanakan: exclusion di sumber approval didasarkan role saja, dan creator-exclusion diverifikasi saat aksi approve (sudah ada). **Keputusan plan:** untuk akurasi & konsistensi UX, lakukan creator-exclusion via join `CreatedBy` per doc type memakai pemetaan yang sama; bila terlalu berat, dokumentasikan sebagai batasan (notif mungkin menampilkan dokumen milik sendiri, tapi tombol Approve tetap terkunci). Rekomendasi: mulai tanpa creator-exclusion (hitung by role saja) untuk kesederhanaan, karena aksi approve sudah aman; catat sebagai known limitation.

> Catatan: pendekatan tanpa creator-exclusion membuat query jauh lebih sederhana (murni `ApprovalSteps`), dan tetap benar secara keamanan (approve tetap ditolak untuk creator). Trade-off: badge bisa menghitung dokumen sendiri. Diterima untuk v1.

### 4. Web (`src/ErpOne.Web/`)

- Komponen `Components/Layout/NotificationBell.razor` (atau bagian dari top-bar `MainLayout`): ikon `bi-bell` + badge count. Reuse pola popover top-bar yang sudah ada (mis. Appearance popover). Isi popover: daftar grup (ikon severity · label · count) sebagai link `href` ke `Url`; footer opsional. Kosong → "Tidak ada notifikasi".
- Resolusi user: dari `AuthenticationStateProvider` / `Task<AuthenticationState>` (CascadingParameter) — ambil `userName`, `roles` (`ClaimsPrincipal.IsInRole` → kumpulkan role claims), dan `hasPermission` = `p => user.HasClaim(AppMenus.ClaimType, p)` (atau `IAuthorizationService`). Panggil `INotificationService.GetForUserAsync(...)` di `OnInitializedAsync`; refresh saat popover dibuka.
- Tanpa izin apa pun / tak ada item → lonceng tetap tampil tanpa badge (atau badge 0).
- **Tidak** dipasang di `PosLayout` (kasir) — hanya `MainLayout`. (Opsional; putuskan di plan.)

### 5. Tests (`tests/ErpOne.IntegrationTests/NotificationServiceTests.cs`)

Pola service test (SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`). Helper seed dokumen PendingApproval + chain, low-stock, invoice due.
1. **Approval per role:** dokumen PendingApproval di step role R → `GetForUserAsync` dgn roles berisi R memunculkan grup doc type itu dgn count benar; roles tanpa R → tak muncul.
2. **Permission gating:** low-stock/AR/AP grup hanya muncul bila `hasPermission` mengizinkan; `hasPermission = _ => false` → grup non-approval hilang.
3. **Low stock count:** seed SKU×gudang ≤ reorder → count benar.
4. **Due window:** invoice due dalam N hari & overdue dihitung; invoice due jauh di masa depan / lunas / cancelled tidak.
5. **TotalCount = Σ groups.**

## Non-Goals (YAGNI)

- Persistensi / histori notifikasi, mark-as-read, per-notification state.
- Push / email / SignalR real-time (badge di-refresh saat load/ buka popover).
- Preferensi notifikasi per user, mute, snooze.
- Notifikasi untuk event yang tak derivable dari state.
- Pemasangan di layar POS.

## Batasan yang diketahui

- Badge di-refresh saat halaman load & popover dibuka (bukan real-time). Cukup untuk kebutuhan operasional.
- v1 tanpa creator-exclusion pada hitungan approval (aksi approve tetap aman); dapat ditambah kemudian bila perlu.
- Doc type approval tanpa halaman index/route yang jelas dilewati dari grup.
