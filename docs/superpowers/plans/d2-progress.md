# Tahap D2 — Layar POS (PosSale) — Progress Ledger

Plan: `docs/superpowers/plans/2026-07-02-d2-pos-sale.md`
Spec: `docs/superpowers/specs/2026-07-02-d2-pos-sale-design.md`
Mode: inline execution (executing-plans), **no git**. JANGAN pakai `--no-build` untuk `dotnet ef`.
Baseline (post-D1): build 0 warnings, 119 unit + 77 integ = 196 tests.

## Task status
- [x] Task 1: Domain — `PosSaleLine` (Recompute subtotal/diskon/total) + `PosSale` (ctor, AddLine, Settle: subtotal→base−diskon transaksi→PPN exclusive→grand→kembalian tunai; CogsTotal; guard settled/empty/diskon>subtotal/tunai kurang bayar). `PosSaleTests.cs` **6 pass**.

## ✅ SELESAI (2026-07-03) — semua Task 1–8 kode & test hijau; sisa: **manual UI walkthrough (Task 8 Step 4) oleh user**
Total suite: **209 pass** (127 unit + 82 integ). Build 0 warnings. Migration `AddPosSale` applied.
**Deviasi tercatat:** (1) mockup `1-lini.html` tak ada → PosRegister dibangun ulang gaya Atlas; (2) print struk pakai CSS global `wwwroot/app.css` (`.pos-receipt`), bukan scoped; (3) test `CreateSale_rejects_insufficient_stock` di-scope ke warehouse (shared DB). Detail per-task di bawah.

- [x] Task 2: Application (2026-07-03) — `PosSaleDtos.cs`, `IPosSaleService.cs`, `PosSaleValidators.cs` + `PosSaleValidatorTests.cs` (2 pass). Build 0 warnings. `PagedResult` ctor = `(items, total, page, pageSize)` ✓.
- [x] Task 3: Infrastructure (2026-07-03) — DbSets `PosSales`/`PosSaleLines` + mapping (precision, unique SaleNumber, FK Shift/Warehouse/PaymentMethod/Tax=Restrict, Lines=Cascade, ProductVariant=Restrict) di `AppDbContext.cs`; `window.appPrint` di `app-interop.js`; migration `20260703014847_AddPosSale` (Up 2 tabel, Down drop keduanya) **applied to DB**. Build 0 warnings. Baris DI ditunda ke Task 4 (per plan). Migrations dir = `src/MyApp.Infrastructure/Persistence/Migrations`.
- [x] Task 4: Infrastructure (2026-07-03) — `PosSaleService.cs` (opsi **(b)** RefId: `refId:null` + `Note=SaleNumber`, satu `SaveChanges`, tanpa `SetRef`); DI `AddScoped<IPosSaleService,PosSaleService>()` + `using`. `PosSaleServiceTests.cs` **5 pass**. Build 0 warnings.
  - **Fix test:** `CreateSale_rejects_insufficient_stock` — DB integ **shared** (`InitializeDatabase`=`EnsureCreated`, tak reset per test), jadi `db.PosSales.AnyAsync()` global tercemar sale test lain. Diperbaiki jadi scoped `AnyAsync(s => s.WarehouseId == wh)` (warehouse unik per test). Validator `CreatePosSaleValidator` auto-terdaftar via `AddValidatorsFromAssemblyContaining<CreateProductValidator>` (Application assembly).
- [x] Task 5: Web (2026-07-03) — `AppMenus.cs`: `PosActions=[ActIndex,ActCreate]` + resource `cashier.pos` di grup Kasir. `NavMenu.razor`: link `cashier/pos` (policy `cashier.pos.create`) + `cashier/sales` (policy `cashier.pos.index`). Build 0 warnings. Policies auto-generated dari resource+actions.
- [x] Task 6: Web (2026-07-03) — `PosLayout.razor`(+`.css`) full-screen shell; `PosRegister.razor`(+`.css`) route `/cashier/pos`. `@code` plan verbatim + tambahan: live `_clock` (System.Threading.Timer, `IDisposable`), `_searchRef` ElementReference (F2 focus). Shortcuts F2/F9/Del/Esc. Build 0 warnings.
  - **DEVIASI dari plan:** mockup `1-lini.html` **TIDAK ADA di repo** (dicari menyeluruh, nihil). Markup + CSS dibangun ulang dari struktur-spec plan Step 2 + bahasa desain **Atlas** (token dari `Cashier/Shifts/*.razor.css`: accent `#0E9F6E`, IBM Plex Sans, kartu rounded). Dua panel: kiri scan+keranjang, kanan ringkasan+bayar.
  - **Struk cetak:** aturan print **global** ditaruh di `wwwroot/app.css` (`.pos-receipt` + `@media print` classic visibility) — bukan scoped `.razor.css` (scoping merusak print-visibility lintas komponen; juga dipakai ulang oleh Task 7 reprint).
  - Verifikasi kontrak: `TaxDto.Rate`, `PaymentMethodDto.Type`=enum `PaymentType` (fully-qualified `MyApp.Domain.Entities.PaymentType.Tunai`), `GetAllAsync()` di ITax/IPaymentMethod service.
  - **BELUM diverifikasi runtime** (butuh manual UI walkthrough Task 8 Step 4).
  - **Enhancement (2026-07-03, permintaan user):** pencarian **live/as-you-type** — input pakai `value=@_term` + `@oninput=OnTermInput` (debounce 250ms, min 2 huruf, `CancellationTokenSource` batalkan ketukan sebelumnya, tanpa auto-add). Enter/tombol tetap `SearchAsync` (auto-add bila barcode cocok persis). `_searchCts` di-dispose. Build 0 warnings.
- [x] Task 7: Web (2026-07-03) — CSS disalin dari `Cashier/Shifts/*` → `Pos/PosSaleIndex.razor.css` & `PosSaleDetail.razor.css`. `PosSaleIndex.razor` route `/cashier/sales` (mirror ShiftIndex, toolbar search + tabel No/Tanggal/Kasir/Metode/Total → detail + Pager, `GetPagedAsync(page,15,search,null)`). `PosSaleDetail.razor` route `/cashier/sales/{Id}` (info + item + ringkasan, tombol **Cetak Ulang**→`appPrint`, struk `.pos-receipt` reuse global print CSS). Build 0 warnings.
- [x] Task 8: Full verification (2026-07-03) — build **0 warnings**; suite **127 unit + 82 integ = 209 pass** (persis target). Invariants `PosSaleService` diverifikasi ulang: 1 transaksi (`BeginTransactionAsync`→satu `SaveChangesAsync`→`CommitAsync`, opsi b), fase-1 cek stok sebelum mutasi, `MovementType.Out`/`-qty`/`CostPrice`, `UpsertStockAsync(-qty)`, COGS snapshot, **tanpa MovingAverage** (0 match), `RecordSale(GrandTotal)`, wajib shift terbuka. **Step 4 manual UI walkthrough → menunggu user.**
