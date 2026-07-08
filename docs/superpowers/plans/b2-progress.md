# Tahap B2 — Goods Receipt — Progress Ledger

Plan: `docs/superpowers/plans/2026-06-29-b2-goods-receipt.md`
Spec: `docs/superpowers/specs/2026-06-29-b2-goods-receipt-design.md`
Mode: subagent-driven, **no git** (implementers skip commits; reviewers read changed files directly).
Baseline (B1): build 0 warnings, 101 tests pass.

## Task status

- [x] Task 1: Domain — PO receipt tracking + status transitions — review clean (67 unit tests pass)
- [x] Task 2: Domain — GoodsReceipt + GoodsReceiptLine + status enum — review clean (71 unit tests pass)
- [x] Task 3: Application — DTOs, interface, validators, options (+ PurchaseOrderService.CloseAsync) — review clean after fix (76 unit tests pass). Important fixed: CloseAsync now transaction-wrapped (matches CancelAsync); added UpdateGoodsReceiptValidator tests.
- [x] Task 4: Infrastructure — DbContext mapping — verified verbatim (build clean; FK behaviors + unique index + field-access nav correct)
- [x] Task 5: Infrastructure — GoodsReceiptService (read + draft CRUD) + DI + appsettings — review clean (4 integ tests pass, PostAsync stubbed). 2 Minor (by-design/consistent), logged.
- [x] Task 6: Infrastructure — GoodsReceiptService.PostAsync — review clean after fixes (9 GRN integ tests; full suite 76 unit + 51 integ pass). MA math/signs verified correct. Fixed 2 Important real bugs in multi-line-same-variant case: (1) stale `totalBefore` → added per-variant accumulator; (2) duplicate `ProductStock` row → `UpsertStockAsync` now checks `db.ProductStocks.Local` before DB. Added discriminating test + RefId assertion. Deviation: non-Draft guard throws `InvalidOperationException` (state violation; UI catches both).
- [x] Task 7: Infrastructure — CloseAsync integration coverage — done (10 GRN integ tests pass; PartiallyReceived→Closed + no longer receivable)
- [x] Task 8: EF migration + database update — `20260629084023_AddGoodsReceipt`; content verified vs Task 4 mapping (2 tables + FK behaviors + unique GrnNumber + ReceivedQuantity col default 0; Down drops only those). DB update applied successfully. Build 0 warnings; 76 unit + 52 integ = 128 pass.
- [x] Task 9: Web — AppMenus + nav + appsettings verify — ActPost/ActClose added; PurchaseOrderActions+close; transactions.goods-receipts resource (index/create/edit/delete/post); NavMenu is hardcoded → GRN entry added; appsettings present; admin auto-grant via AllPermissions confirmed. Build 0 warnings.
- [x] Task 10: Web — GrnIndex page — review clean (spec ✅, compile-safe vs IGoodsReceiptService/GoodsReceiptListItemDto, quality Approved). 1 Minor: redundant local `@using MyApp.Domain.Entities` (consistent with PoIndex; not changed).
- [x] Task 11: Web — GrnForm page — review clean (spec PASS, compile-safe vs IGoodsReceiptService/DTOs incl. positional record param order, quality Approved). Build 0/0. Implementer added `@using MyApp.Application.GoodsReceipts` + `@using FluentValidation` (not in _Imports) and an extra `GetReceivablePosAsync()` in edit mode to render the readonly PO name.
- [x] Task 12: Web — GrnDetail page — review clean (spec PASS, compile-safe, quality Approved). Build 0/0. Implementer adapted `Swal.ConfirmAsync(title,text,confirmText)` to the real 3-arg signature.
- [x] Task 13: Web — PoDetail receive progress + Buat GRN + Tutup PO — review clean (spec PASS, shared-DTO `ReceivedQuantity` appended last + single construction site correct, `CloseAsync` sig verified, no regressions, quality Approved). Build 0/0; full suite 76 unit + 52 integ = 128 pass. Added StatusClass arms PartiallyReceived/Received/Closed (allowed polish).
- [x] Task 14: Full verification — DONE. Build 0/0; full suite 76 unit + 52 integ = 128 pass. Final whole-branch review (Opus) verdict: **Ready to merge** — all PostAsync invariants PASS (no inventory mutation on draft; fully transactional post; non-Draft rejected; correct MA math + multi-line-same-variant accumulation; tolerance enforced both gates; PartiallyReceived/Received boundary + Close-only-from-PartiallyReceived correct; full-stack contract + auth coverage clean). No Critical/Important. Applied minor #3 fix (GrnDetail not-found branch + `_loading` flag, mirrors PoDetail) — build re-verified 0/0. Step 4 (manual UI walkthrough) handed to user.

## Post-B2 minors — RESOLVED (2026-07-01)
- (#1) DONE: GrnForm edit readonly PO name now binds to `g.PoNumber` (stored in `_poNumber`); dropped the edit-mode `GetReceivablePosAsync()` lookup.
- (#2) DONE: GrnForm create-mode `SaveAsync` now guards empty PO (`_selectedPoId<=0`) and no received lines before calling the service.
- (#4) DONE: extracted shared `AppDbContext.UpsertStockAsync` (`.Local`-aware) → `src/MyApp.Infrastructure/Persistence/StockWriteExtensions.cs`; both `StockService` and `GoodsReceiptService` now call it and their private copies removed. Negative-new still throws `InvalidOperationException`. StockService also gains the `.Local`-aware fix (harmless/strictly-more-correct for multi-line adjustments).
- Verify: build 0 warnings; full suite 76 unit + 54 integ = 130 pass.
- Still open: manual UI walkthrough (user); minor note (#below) about GrnForm edit route gated by `.create` — plan-mandated, not a defect.

## Final review minors — deferred (user to decide)
- (#1) GrnForm edit readonly PO name via `GetReceivablePosAsync()` lookup → fall back to raw ID if PO no longer receivable. 1-line fix: bind to `g.PoNumber` (already loaded). Cosmetic.
- (#2) GrnForm create-mode save: no client guard for empty PO/lines (server validation catches). UX polish.
- (#4) `GoodsReceiptService.UpsertStockAsync` duplicates `StockService.UpsertStockAsync` (GRN copy is the more-correct `.Local`-aware one). Consolidate later; touches StockService (out of B2 scope).
- Note: GrnForm edit route gated by `.create` not `.edit` — plan-mandated ("edit route reuses create policy; acceptable for B2"), not a defect.

## B2 COMPLETE (2026-06-30)
All 14 tasks done & reviewed. Build 0 warnings; 128 tests pass. Feature ready to merge. Remaining: optional minors above + manual UI walkthrough by user.

## Minor findings roll-up (for final review)

- Task 1 (Minor): `ApplyReceipt` trusts caller-supplied `tolerancePercent` each call; no per-line lock. Safe because `GoodsReceiptService` passes config tolerance consistently. Spec-faithful.
- Task 1 (Minor): `CanReceive` test doesn't `Assert.False` after `MarkReceived` (guard covered elsewhere). Hardening-only.
- Task 5 (Minor): `GetPoForReceiptAsync.RemainingQuantity = Quantity - ReceivedQuantity` (no tolerance). By-design: it's the UI default pre-fill; tolerance is the ceiling enforced by `BuildLines`. No change.
- Task 5 (Minor): `GenerateNumberAsync` lexicographic GrnNumber sort breaks above 9999/month — identical to existing `PurchaseOrderService.GenerateNumberAsync`. Consistent; defer.
- Task 6 (Minor, cross-cutting): `GoodsReceiptService.UpsertStockAsync` duplicates `StockService.UpsertStockAsync` (and now diverges: GRN version checks `.Local` + uses `ProductStock` ctor which throws `ArgumentException` on negative vs StockService `InvalidOperationException`). Harmless (GRN delta always +). Consider extracting a shared stock-writer helper later (touches StockService — out of B2 scope).
- Task 11 (Minor): GrnForm edit mode renders the readonly PO name from `GetReceivablePosAsync()` lookup; if the PO is no longer receivable (e.g. fully received via another GRN) it falls back to the raw numeric ID. `GoodsReceiptDto.PoNumber` already exists and would be cleaner + saves a service call. Defensible; consider switching to `g.PoNumber` at final review.
- Task 11 (Minor): GrnForm create-mode save has no client-side guard for `_selectedPoId>0` / `lines.Count>0` before calling the service (PoForm short-circuits empty lines). Server `ValidationException` catches it; only a UX nicety.
- Task 12 (Minor): GrnDetail has no "not found" branch — if `GetByIdAsync` returns null it shows the loading spinner indefinitely (PoDetail shows a "tidak ditemukan" alert). Consider adding parity branch at final review.

## Log

- (2026-06-29) Plan + spec written; ledger created. Starting Task 1.
- (2026-06-29) Tasks 1–3 complete (domain PO receipt members + GRN entities + Application layer). 76 unit tests pass, build 0 warnings. Task 3 had 1 Important (CloseAsync transaction) fixed + re-verified. Stopped at Task 3 per user request.
- (2026-06-29) Tasks 4–7 complete: DbContext mapping, GoodsReceiptService (draft CRUD + PostAsync), CloseAsync integ coverage. **Full suite green: 76 unit + 51 integration = 127 tests pass, build 0 warnings.** Task 6 caught & fixed 2 real bugs in the multi-line-same-variant case (stale MA `totalBefore`; duplicate `ProductStock` row). 
- (2026-06-29) **Task 8 (EF migration) DEFERRED to tomorrow per user.** State is clean: NO migration file generated (`src/MyApp.Infrastructure/Migrations/*GoodsReceipt*` absent), dev DB NOT modified. Resume = run Task 8 from scratch: `dotnet ef migrations add AddGoodsReceipt --project src/MyApp.Infrastructure --startup-project src/MyApp.Web`, verify content, `database update`. Then Tasks 9–14 (Web + final verification).
- (2026-06-29, later) Resumed: Task 8 migration applied (`20260629084023_AddGoodsReceipt`), Task 9 (AppMenus/nav) done. Started Task 10 (GrnIndex) — file created + builds clean, but subagent stopped per user before review. **Stopped for the day. Resume = REVIEW Task 10 GrnIndex.razor, then Tasks 11 (GrnForm), 12 (GrnDetail), 13 (PoDetail edits — note: also adds `ReceivedQuantity` to PurchaseOrderLineDto + sets it in GetByIdAsync), 14 (full verification + manual walkthrough).** Suite at last full run: 76 unit + 52 integ = 128 pass, build 0 warnings.
