# Tahap B1 — Progress Ledger

Plan: `docs/superpowers/plans/2026-06-24-b1-purchase-order.md`
Mode: subagent-driven, **no git** (implementers skip commits; reviewers read changed files directly).
Baseline: build succeeded, 0 warnings (2026-06-24).

## Task status

- [x] Task 1: Enum approval + ApprovalChainStep (Domain) — review clean
- [x] Task 2: ApprovalStep (Domain) — review clean (2 minor, spec-faithful)
- [x] Task 3: PurchaseOrderStatus + PurchaseOrderLine (Domain) — review clean (2 minor, spec-faithful)
- [x] Task 4: PurchaseOrder (Domain) — review clean (3 minor, style/spec-faithful)
- [x] Task 5: Application Approvals (DTO/interface/validator) — review clean
- [x] Task 6: Application PurchaseOrders (DTO/interface/validator) — review clean; controller removed 1 unused using (build verified)
- [x] Task 7: AppDbContext mapping — review clean (perfect match, 59 unit tests pass)
- [x] Task 8: ApprovalChainService + DI + integ test — review clean (1 minor: shared-factory test isolation, self-healing)
- [x] Task 9: ApprovalService engine + DI + integ test — review clean (5/5); test deviation (order-independent empty-chain) sound; dup actingUserName arg is intentional per identity model
- [x] Task 10: PurchaseOrderService + DI + integ test — review clean; 5/5 PO integ tests pass. Also fixed 2 PRE-EXISTING stock test failures (out of B1 scope, see roll-up) to restore green gate.
- [x] Task 11: EF migration — `20260625035633_AddPurchaseOrderAndApproval`; content verified vs Task 7 mapping (unique PoNumber; Supplier/Warehouse Restrict; lines cascade, variant Restrict, tax SetNull; (18,2)/(5,2) decimals; enums nvarchar; chain unique (DocType,StepOrder); instance index (DocType,DocId,StepOrder)). DB update applied; 59 unit + 42 integ pass, 0 warnings. Down() drops only the 4 new tables.
- [x] Task 12: AppMenus + seed default chain — `ActApprove` + `PurchaseOrderActions` (PO now CRUD+approve); `settings.approval-chains` (CRUD) added; new perms auto-granted to admin via `AllPermissions`. BootstrapSeeder seeds idempotent default PO chain (step 1 = admin `roleName`). Build 0 warnings, 101 tests pass. Reviewed clean.
- [x] Task 13: Web PO Index (replace placeholder) — deleted `PurchaseOrderPlaceholder.razor`; created `Transactions/PurchaseOrders/PoIndex.razor` (verbatim). Build 0 warnings.
- [x] Task 14: Web PO Form — created `Transactions/PurchaseOrders/PoForm.razor` (verbatim; master DTO field names pre-verified to match). Build 0 warnings.
- [x] Task 15: Web PO Detail — created `Transactions/PurchaseOrders/PoDetail.razor` (verbatim; SwalService sigs pre-verified). Build 0 warnings.
- [x] Task 16: Web Settings Approval Chain — created `Settings/ApprovalChains/ApprovalChainsIndex.razor` + `ApprovalChainForm.razor` (verbatim); NavMenu entry added by controller. Build 0 warnings.
- [x] Task 17: Full verification (automated) — `dotnet build` 0 warnings; `dotnet test` 59 unit + 42 integ = 101 pass. Step 4: PO flow does not touch stock (PurchaseOrderService has no StockMovement/ProductStock writes; B1 scope). ⏳ Step 3 (manual run-app UI walkthrough) pending user.

## Minor findings roll-up (for final review)

- Task 2 (Minor, spec-faithful): `ApprovalStep.Approve/Reject` don't guard `actedByUserId` against empty/whitespace. Low risk — callers (ApprovalService) pass the resolved username. Revisit only if it becomes user-facing.
- Task 2 (Minor): `Cannot_act_twice` test doesn't assert state unchanged after failed second action. Hardening-only.
- Task 8 (Minor, cross-cutting): integration tests use `IClassFixture` shared factory; `InitializeDatabase()`/`EnsureCreated` doesn't wipe between tests. Later integ tests rely on unique doc IDs / ResetAsync / fresh seeds to stay isolated. Consider a per-test reset helper if flakiness appears.
- Task 10 (PRE-EXISTING bugs, out of B1 scope — fixed to keep gate green; user-approved):
  - **Product delete bug:** `ProductService.DeleteAsync` 500'd (SQLite FK Restrict) when deleting a product with stock history (Product→Variant cascade vs StockMovement/ProductStock Restrict FKs from f2 stock model). Decision: BLOCK delete when product has stock history → throw `ValidationException` (HTTP 400), preserving the immutable ledger. Guard checks `StockMovements` OR `ProductStocks` (consistent with `UpdateAsync`). Tests: `Update_ThenDelete_AsManager_Works` reworked to delete a stockless product; new `Delete_ProductWithStockHistory_Returns400`.
  - **Dashboard test bug:** `Products_Dashboard_AggregatesAndTranslates` passed `costPrice=0m` but asserted `InventoryValue >= 5600` → fixed test inputs to 100m/200m. Also: `OutOfStockCount`/`LowStock` are derived only from `ProductStock` rows, so never-stocked (opening 0) products are intentionally invisible; corrected `LowStock` assertion to "Low stock" and removed a no-op `OutOfStockCount >= 0` assertion (out-of-stock coverage lives in stock-adjustment tests). No production change to `GetDashboardAsync`.
  - **Minor (deferred to final review):** `Update_ThenDelete` doesn't assert the stocked product survives (covered by the new 400 test); cross-test category coupling via `EnsureCategoryAsync` (pre-existing). NOTE for whoever does the dashboard later: whether a `Habis`/never-stocked product should count toward `OutOfStockCount` is an open product question, deliberately not changed here.

## Log

- Task 10 complete (2026-06-25): PurchaseOrderService + DI + 5 integ tests (already implemented in prior session, verified). Reviewed clean. Fixed 2 pre-existing stock test failures (delete-guard + dashboard test) after user approval; 2 Important review findings fixed. Full suite green: 59 unit + 42 integ = 101 pass, 0 warnings. Stopped at Task 10 per user request.
- Task 11 complete (2026-06-25): EF migration `20260625035633_AddPurchaseOrderAndApproval` generated via `dotnet ef migrations add` (EF Core 10.0.9). Migration content reviewed clean vs mapping; DB update applied successfully ("Done."); suite green: 59 unit + 42 integ = 101 pass, 0 warnings. No git (no commit). Next: Task 12 (AppMenus permission + seed default chain).
- Task 12 complete (2026-06-25): `AppMenus.cs` (ActApprove, PurchaseOrderActions, settings.approval-chains CRUD) + `BootstrapSeeder.cs` (default PO chain seed, role = `Identity:ManagerRole`). Build 0 warnings, 101 tests pass. Reviewed clean. Next: Task 13 (Web PO Index, replace placeholder).
- Tasks 13–16 complete (2026-06-25): Web layer — PoIndex (replaced placeholder), PoForm, PoDetail, Settings ApprovalChainsIndex + ApprovalChainForm, NavMenu entry. All verbatim from plan; master DTO + SwalService signatures pre-verified by controller. Each build 0 warnings.
- Task 17 complete (automated, 2026-06-25): full `dotnet build` 0 warnings + `dotnet test` 101 pass (59 unit + 42 integ). **Tahap B1 functionally complete.** Remaining: manual run-app UI walkthrough (Step 3) for the user, and optional final whole-branch review. B2 (penerimaan barang + HPP/stok) is the next tahap.
