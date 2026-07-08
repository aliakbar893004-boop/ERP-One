# Tahap D1 — Sesi Kasir — Progress Ledger

Plan: `docs/superpowers/plans/2026-07-02-d1-cashier-shift.md`
Spec: `docs/superpowers/specs/2026-07-02-d1-cashier-shift-design.md`
Mode: inline execution (executing-plans), **no git**.
Baseline (post-C2): build 0 warnings, 111 unit + 72 integ = 183 tests.

## Task status

- [x] Task 1: Domain — `CashierShiftStatus{Open,Closed}`, `CashierShiftTotal` (ctor+Add), `CashierShift` (ctor/RecordSale/Close, computed ExpectedCash/TotalSalesAmount/TransactionCount). `CashierShiftTests.cs` 6 pass. (Fix: koreksi aritmetika assertion TotalSalesAmount 180k→150k di test — kode benar.)
- [ ] Task 2: Application — DTOs/ICashierShiftService/validators + validator tests.
- [x] Task 3: Infrastructure — DbContext DbSets `CashierShifts`/`CashierShiftTotals` + mapping (ShiftNumber unique, enum string(20), decimals 18/2, Warehouse Restrict, Totals Cascade, PaymentMethod Restrict, field-access nav, **filtered unique index `CashierUserId WHERE [Status]='Open'`**). Migration `20260702055641_AddCashierShift` (2 tabel + filtered index; creates only CashierShift, tak sentuh DO). `database update` applied. Build 0 warnings; full suite **119 unit + 72 integ = 191 pass**.

  ⚠️ **INSIDEN TOOLING (teratasi):** langkah `dotnet ef migrations remove --no-build` awal memakai assembly stale → menghapus **file** migration C2 `20260701093738_AddDeliveryOrder` DAN me-revert DB-nya (DeliveryOrder tables + history row terhapus). Diperbaiki: regenerasi konten AddDeliveryOrder (CashierShift disembunyikan sementara dari model), rename balik ke ID asli `20260701093738`, lalu tambah AddCashierShift. `database update` menerapkan ulang KEDUANYA bersih. **Akibat:** data DeliveryOrder di dev DB (bila ada) hilang saat revert — dev DB only, test pakai SQLite, tak ada dampak test/produksi. **Pelajaran: JANGAN pakai `--no-build` untuk perintah `ef` di sesi ini** (assembly stale = sumber semua kekacauan). Dev app (PID `dotnet run`) juga dihentikan karena mengunci bin Web — **perlu di-start ulang oleh user.**
- [x] Task 4: `CashierShiftService` (Open/Close/GetCurrent/GetById/GetPaged + GenerateNumber SHIFT-YYYYMMDD-####) + DI registration. `CashierShiftServiceTests.cs` 5 integ pass (open+get-current, tolak shift kedua via filtered index, tolak gudang non-aktif, close variance + owner-only + no-double-close, RecordSale totals surface).
- [x] Task 5: `AppMenus` — `CashierShiftActions [Index,Create,Close]` + resource `cashier.shifts` grup "Kasir". `Program.cs` policy `cashier.any`. `NavMenu` grup Kasir (bi-cash-stack). Admin auto-grant via AllPermissions. Web build 0 warnings.
- [x] Task 6: `ShiftIndex.razor` (+ copied `DoIndex.razor.css`) — banner shift terbuka / form Buka Shift (gudang aktif + saldo awal), toolbar search + filter status, tabel riwayat + Pager. Pakai `IWarehouseService.GetAllAsync().Where(IsActive)`. Build 0 warnings.
- [x] Task 7: `ShiftDetail.razor` (+ copied `DoDetail.razor.css`) — info shift, kartu Rekonsiliasi (Expected/Counted/Selisih warna), tabel Total per Metode, form Tutup Shift (owner+Open only). Build 0 warnings.
- [x] Task 8: Full verification — build **0 warnings**, suite **119 unit + 77 integ = 196 pass**. Invariants: Open tolak shift kedua (query + filtered unique index), Close owner-only + variance = counted − expected, RecordSale hanya method domain (siap D2). Manual UI walkthrough → user.

## D1 SELESAI (kode) — Tasks 1–8. Sisa: manual UI walkthrough + user start ulang dev app.
Untuk D2 (layar POS + PosSale): panggil `CashierShift.RecordSale(paymentMethodId, isCash, amount=grandTotal)` di dalam transaksi penyelesaian sale, terikat ke shift terbuka milik user (`GetCurrentAsync`).
