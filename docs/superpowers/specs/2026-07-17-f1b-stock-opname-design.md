# Fase 1b — Stock Opname (Physical Count) — Design

**Tanggal:** 2026-07-17
**Status:** Disetujui (brainstorming) — siap ke writing-plans
**Branch kerja:** `Development`

## Ringkasan

Dokumen hitung-fisik (physical count) formal per gudang, dengan alur approval. Sistem meng-*snapshot* stok on-hand tiap item saat draft dibuat; petugas mencatat qty fisik hasil hitungan; saat dokumen di-*approve* penuh, sistem memposting selisih (variance) sehingga stok sistem = hasil hitungan fisik.

Fitur ini **hidup berdampingan** dengan Stock Adjustment yang sudah ada:
- **Stock Adjustment** (`/inventory/adjustments/new`) — koreksi cepat ad-hoc: input *delta* qty per varian, langsung diterapkan, mengubah moving-average pada delta positif. Tetap ada, untuk koreksi kecil.
- **Stock Opname** (baru) — dokumen formal untuk stok-take terjadwal: hitung fisik → variance → post dengan approval.

## Keputusan Desain (hasil brainstorming)

1. **Koexistensi:** Stock Adjustment & Stock Opname keduanya ada (tujuan berbeda).
2. **Workflow:** `Draft → PendingApproval → Posted` — approval-gated, reuse `IApprovalService` (seperti Stock Transfer / Supplier Payment), dengan separation-of-duties (pembuat tak bisa approve dokumennya sendiri).
3. **Pengisian baris:** auto-populate SEMUA varian yang punya baris stok (`ProductStock`) di gudang terpilih saat draft dibuat; `SystemQty` di-snapshot per baris.
4. **Basis variance:** dihitung terhadap **stok live saat Post** — `delta = PhysicalQty − onHand(saat post)`. Menjamin stok sistem = hasil hitung fisik setelah post, tahan terhadap mutasi antara hitung & approve. `SystemQty` snapshot tetap disimpan untuk laporan variance.
5. **GL:** **tanpa jurnal GL** (konsisten dgn Stock Adjustment & Stock Transfer). Bisa jadi ekstensi 5b (shrinkage/gain) nanti.
6. **Cost handling:** `StockMovement` variance pakai `variant.CostPrice` saat ini untuk selisih positif MAUPUN negatif; **moving-average TIDAK diubah** (opname = koreksi, bukan pembelian — beda dari Stock Adjustment).
7. **Gudang dikunci setelah dibuat:** baris = snapshot spesifik gudang, jadi `WarehouseId` tak bisa diubah saat edit (hanya tanggal/catatan + qty fisik yang bisa diedit selama Draft).

## Arsitektur

### 1. Domain (`src/ErpOne.Domain/Entities/Inventory/`)

```
StockOpnameStatus { Draft, PendingApproval, Posted }

StockOpnameLine
  - Id, StockOpnameId (private set)
  - ProductVariantId
  - SystemQty     // snapshot on-hand saat draft dibuat (untuk laporan variance)
  - PhysicalQty   // hasil hitung fisik (terisi awal = SystemQty)

StockOpname : AuditableEntity
  - Id, OpnameNumber, OpnameDate, WarehouseId, Notes, Status, RejectionNote
  - IReadOnlyCollection<StockOpnameLine> Lines
  - ctor(opnameNumber, opnameDate, warehouseId, notes)  // Status=Draft, WarehouseId>0
  - SetLines(IEnumerable<(int VariantId, int SystemQty, int PhysicalQty)>)  // EnsureDraft, clear+add
  - SetPhysicalCounts(IEnumerable<(int LineId/atau VariantId, int PhysicalQty)>) // update PhysicalQty saja
  - UpdateHeader(opnameDate, notes)  // EnsureDraft — TANPA warehouse
  - Submit()        // EnsureDraft, Lines wajib > 0 → PendingApproval
  - MarkPosted()    // hanya dari PendingApproval → Posted
  - ReturnToDraft(reason)  // hanya dari PendingApproval → Draft + RejectionNote
  - private EnsureDraft()
```

Pola entity: `private set`, ctor privat `// EF Core`, backing `List<>` sebagai `IReadOnlyCollection`, invariant `throw`.

> **Catatan implementasi:** untuk kesederhanaan Form, `SetLines` (dipakai saat auto-populate di CreateAsync) menerima (VariantId, SystemQty, PhysicalQty). Saat edit count-sheet, service memuat entity + `SetLines` ulang dgn SystemQty existing + PhysicalQty baru (SystemQty snapshot TIDAK berubah saat edit), atau gunakan `SetPhysicalCounts`. Plan boleh memilih salah satu; SystemQty harus stabil sejak draft.

### 2. Application (`src/ErpOne.Application/StockOpnames/`)

- `StockOpnameDtos.cs`:
  - `CreateStockOpnameRequest(DateTime OpnameDate, int WarehouseId, string? Notes)` — TANPA lines (lines di-generate server-side).
  - `UpdateStockOpnameRequest(DateTime OpnameDate, string? Notes, IReadOnlyList<StockOpnameCountInput> Counts)` — edit tanggal/catatan + qty fisik.
  - `StockOpnameCountInput(int LineId, int PhysicalQty)`.
  - `StockOpnameLineDto(int Id, int ProductVariantId, string Sku, string ProductName, int SystemQty, int PhysicalQty, int Variance, int OnHandNow)` — `Variance = PhysicalQty − SystemQty` (untuk sheet); `OnHandNow` = on-hand live (info).
  - `StockOpnameDto(...)` — header + WarehouseName + Status + RejectionNote + `CreatedBy` (untuk creator-exclusion di Detail) + Lines + `IReadOnlyList<ApprovalStepDto> ApprovalSteps`.
  - `StockOpnameListItemDto(int Id, string OpnameNumber, DateTime OpnameDate, string WarehouseName, int LineCount, int TotalVariance, string Status)`.
- `IStockOpnameService`:
  - `GetPagedAsync(page, pageSize, search?, StockOpnameStatus? status)`
  - `GetByIdAsync(id)`
  - `CreateAsync(CreateStockOpnameRequest)` — snapshot lines dari stok gudang
  - `UpdateAsync(id, UpdateStockOpnameRequest)` — Draft only
  - `DeleteAsync(id)` — Draft only
  - `SubmitAsync(id)`, `ApproveAsync(id, actingUserName, isInRole)`, `RejectAsync(id, actingUserName, isInRole, reason)`
- `StockOpnameValidators.cs`: `CreateStockOpnameValidator` (WarehouseId > 0); qty fisik >= 0 saat update.

### 3. Infrastructure (`src/ErpOne.Infrastructure/Services/Inventory/StockOpnameService.cs`)

Primary-ctor DI: `AppDbContext db, IApprovalService approval, IStockService stock, IValidator<CreateStockOpnameRequest> validator, IDocumentNumberService docNumbers`.

- `CreateAsync`: validate → `tx` → cek gudang ada → nomor `OPN` via `docNumbers.NextAsync(DocumentTypes.StockOpname, date)` → query semua `ProductStock` di gudang (join varian) → `SetLines((variantId, qty, qty))` (PhysicalQty awal = SystemQty) → save → commit.
- `PostAsync(StockOpname o, ct)` (dipanggil saat fully-approved, ikut tx caller):
  ```
  foreach line:
      delta = line.PhysicalQty − await stock.GetOnHandAsync(line.ProductVariantId, o.WarehouseId, ct)
      if delta == 0: continue
      cost = variant.CostPrice (query)
      db.StockMovements.Add(new StockMovement(variantId, o.WarehouseId, MovementType.Adjustment,
          delta, cost, o.OpnameDate, "StockOpname", o.Id, o.OpnameNumber))
      await db.UpsertStockAsync(variantId, o.WarehouseId, delta, ct)
      // TIDAK ApplyMovingAverage
  o.MarkPosted()
  ```
  Stok akhir = PhysicalQty ≥ 0 → UpsertStockAsync tak pernah negatif.
- `SubmitAsync`/`ApproveAsync`/`RejectAsync`: **persis pola `StockTransferService`** (ResetAsync+SubmitAsync di submit; ApproveAsync→fullyApproved→PostAsync; RejectAsync→ReturnToDraft+ResetAsync). `Fail(string)` helper → `ValidationException`.
- DI: `services.AddScoped<IStockOpnameService, StockOpnameService>();`

### 4. EF wiring (`AppDbContext.cs`)

- DbSets `StockOpnames`, `StockOpnameLines`.
- Config: key, `OpnameNumber` maxlen 30 + unique index, `Notes`/`RejectionNote` maxlen 500, `Status` `HasConversion<string>()` maxlen 20, FK ke `Warehouse` (Restrict), FK line→`ProductVariant` (Restrict), `HasMany(Lines).WithOne().HasForeignKey(StockOpnameId).OnDelete(Cascade)` + field access mode.
- `tablePrefixes`: `[nameof(StockOpname)]="T_"`, `[nameof(StockOpnameLine)]="T_"`.
- NumberSequence `HasData` Id=**14** Code="StockOpname" Prefix="OPN" DateFormat="yyyyMM" Padding=4 ResetPeriod.Monthly Separator="-".
- Migration `AddStockOpname` (2 tabel + InsertData Id=14 + FK/index).

### 5. Enums / konstanta

- `ApprovalDocumentType` += `StockOpname`.
- `DocumentTypes` += `public const string StockOpname = "StockOpname";`.

### 6. Web (`src/ErpOne.Web/`)

- Menu `AppMenus.cs`: grup Inventory, resource `inventory.stock-opname` dgn action set `[ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActPost]` (icon mis. `bi-clipboard-data`).
- `BootstrapSeeder.cs`: seed default chain `ApprovalChainStep(StockOpname, 1, roleName)` (idempotent), setelah blok Stock Transfer.
- Halaman `Components/Pages/Inventory/StockOpname/`:
  - `StockOpnameIndex.razor` (`/inventory/stock-opname`) — `.pi` + chips status + tabel (No, Tgl, Gudang, #Baris, Total Variance, Status). Pola `StockTransferIndex`.
  - `StockOpnameForm.razor` (`/inventory/stock-opname/new`) — `.cf`, pilih gudang + tanggal + catatan → Save → redirect ke count-sheet/detail.
  - `StockOpnameCountSheet.razor` (`/inventory/stock-opname/{Id}/edit`) — Draft only; tabel Produk · Sistem · Fisik(input) · Selisih; simpan qty fisik.
  - `StockOpnameDetail.razor` (`/inventory/stock-opname/{Id}`) — `.pf pf-detail`; header + tabel baris (Sistem/Fisik/Selisih/On-hand) + Approval; tombol Submit/Approve/Reject — **reuse plumbing `ApPaymentDetail`/`StockTransferDetail`** (CascadingParameter `AuthStateTask`, `EvaluateCanApproveAsync` dgn creator-exclusion, inline reject card, `RunAsync`).

### 7. Tests (`tests/ErpOne.IntegrationTests/StockOpnameServiceTests.cs`)

Meniru `StockTransferServiceTests` (SQLite `EnsureCreated`, `IClassFixture<CustomWebApplicationFactory>`, seed chain manual sebelum Submit karena BootstrapSeeder tak jalan di test):
1. **Posting variance benar (dua arah):** seed stok 100; buat opname; set PhysicalQty 120 (surplus) → approve → on-hand 120, 1 StockMovement +20. Kasus kedua PhysicalQty 80 (shortage) → on-hand 80.
2. **Baris selisih-nol no-op:** PhysicalQty == SystemQty → approve → tak ada StockMovement, on-hand tetap.
3. **Basis-live tahan drift:** setelah draft dibuat (snapshot 100), lakukan mutasi (mis. adjustment −10 → on-hand 90); set PhysicalQty 100; approve → on-hand = 100 (delta dihitung 100−90=+10), bukan 100−100=0. Membuktikan basis live.
- Bump `NumberSequenceServiceTests` assert 13→14.

Target: **~317 test** (314 baseline + 3 baru).

## Non-Goals (YAGNI)

- Posting jurnal GL (shrinkage/gain) — ekstensi 5b nanti.
- Baris "item tak dikenal ditemukan" (varian tanpa baris stok di gudang).
- Count sebagian per kategori / filter.
- Siklus freeze / recount / multi-tim counting.

## Batasan yang diketahui

- Hanya varian yang sudah punya baris `ProductStock` di gudang yang dihitung (surplus item yang belum pernah berstok di sana tak bisa dimasukkan di v1).
- Variance tidak berdampak ke GL/nilai buku akuntansi (hanya kuantitas + StockMovement).
- Snapshot `SystemQty` hanya untuk laporan; penyesuaian aktual selalu terhadap stok live saat post.
