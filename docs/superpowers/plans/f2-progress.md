# F2 Progress Ledger

Plan: `docs/superpowers/plans/2026-06-19-f2-stock-model.md`
Spec: `docs/superpowers/specs/2026-06-19-f2-stock-model-design.md`
Mode: subagent-driven, **no git** (repo is not a git repo, same as F1). Progress tracked here.
Verification per task = `dotnet build MyApp.slnx` (0 errors) + (for migration tasks) `dotnet ef database update` applies cleanly. Tests kept compiling, not a gate.

Baseline confirmed: `dotnet build` = 0 errors / 0 warnings; dotnet 10.0.301; EF tools 10.0.9; 7 existing migrations.

## Status

- [x] Task 1: Domain — MovementType, StockMovement, ProductStock, ProductVariant.ApplyMovingAverage — COMPLETE (build 0 err, 6/6 tests pass, review clean)
- [x] Task 2: DbContext config + migration #1 (create tables + backfill) — COMPLETE (build 0 err; migration `20260619013350_AddStockModel` applied; DB empty so backfill no-op; review clean; applied 2 reviewer fixes: warehouse-pinned idempotency guard + single @now timestamp)
- [x] Task 3: Application — IStockService, DTOs, validators — COMPLETE (build 0 err; review clean, zero issues)
- [x] Task 4: Infrastructure — StockService + DI — COMPLETE (build 0 err; review clean; "Important" items judged non-defects by reviewer itself)
- [x] Task 5: Cutover reads to ProductStock + opening movements — COMPLETE (build 0 err; review clean/Approved; implemented by controller since subagent dispatch blocked; applied reviewer fix: `db.ChangeTracker.Clear()` before opening-seed loop in CreateAsync + ImportAsync)
- [x] Task 6: Remove ProductVariant.Stock column (migration #2) — COMPLETE (build 0 err; migration `20260619065901_DropVariantStock` applied; Stock column dropped (sys.columns=0); unit tests 14/14; review clean/Approved; implemented by controller)
- [x] Task 7: AppMenus — Inventory group + sidebar wiring — COMPLETE (build 0 err; AppMenus group + Program.cs `inventory.any` policy + NavMenu.razor section. PLAN CORRECTED: sidebar is hardcoded, not auto-rendered — added Program.cs + NavMenu steps)
- [x] Task 8: Stock Levels page — COMPLETE (build 0 err; StockLevelIndex.razor + scoped .razor.css; review clean/Approved; implemented by controller. PLAN GAP noted: list pages need a companion scoped .razor.css — plan text omitted it)
- [x] Task 9: Stock Adjustment (opname) pages — COMPLETE (build Web 0 err/0 warn; StockAdjustmentForm.razor + companion .razor.css (scoped uf-header/fs-card/table-head/lbl-required); "New Adjustment" button added to StockLevelIndex header (gated inventory.adjustments.create); Index page skipped per plan decision; review clean — spec ✅; applied 2 reviewer fixes: warehouse-selected guard in SaveAsync (I3) + lbl-required on Date label (I2). NOTE: full-solution `dotnet build MyApp.slnx` fails on NU1903 SQLitePCLRaw advisory in IntegrationTests restore — pre-existing/environmental, unrelated to F2; needs a package bump decision separately.)
- [x] Task 10: ProductForm — opening stock (create) / read-only (edit) — COMPLETE (build Web 0 err/0 warn; 3 edits applied to ProductForm.razor across single + multi-variant modes: stock input → "Opening Stock"/editable on create vs read-only on-hand on edit; BuildVariantInputs passes `Id is null ? value : 0`. On-hand reuses ProductVariantDto.Stock already loaded by LoadVariants — no extra service call. Review clean — spec ✅ all 4 branches; applied 2 reviewer UI fixes: removed spurious lbl-required on Opening Stock label + multi-variant edit read-only now a disabled input (consistent with single mode, no column-width jump).)

**ALL 10 TASKS COMPLETE.** Final verification status below.

## Minor findings (for final review triage)

- Task 1: no test exercises `StockMovement` negative-`unitCost` guard (the `if (unitCost < 0)` path). Guard exists; add a test if convenient.
- Task 1: `ApplyMovingAverage` with `inUnitCost = 0` on non-zero stock dilutes HPP (valid for free goods; no guard/test). Observation only.
- Task 1 reviewer "Important" items judged non-defects: `Out`-movement cost is set at service layer (Task 4); zero-qty `ProductStock` rows are intended placeholder rows.
- Task 4: `RecordOpeningAsync` only guards `quantity == 0`, not negative; a negative opening would create an outbound movement (odd for "saldo awal"). Entity guards partially. Consider `if (quantity < 0) throw` at the service boundary.
- Task 4: warehouse existence check runs just before the transaction (negligible race; FK catches it but surfaces a raw `DbUpdateException` instead of `ValidationException`).
- Task 4: redundant negative-on-create guard (service `InvalidOperationException` + `ProductStock` ctor `ArgumentException`) — harmless, slightly confusing.
- Task 4: `GetMovementsByVariantAsync` orders before `Join` — EF translates correctly; style nit.
- Task 5 (PRODUCT-OWNER DECISION): dashboard `outOfStock`/`lowStock` now count only products that have ≥1 `ProductStock` row. A brand-new product with no ledger rows (0 opening) is counted in NEITHER outOfStock nor lowStock (old code counted 0-stock products in outOfStock). Fix if undesired: compute `outOfStock = totalProducts - stockByProduct.Count(x => x.Stock > 0)` or left-join variants→stock. Deferred — empty DB, and spec said dashboard "rest unchanged".

## Notes
