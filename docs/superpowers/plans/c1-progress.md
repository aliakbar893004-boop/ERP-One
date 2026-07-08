# Tahap C1 — Sales Order — Progress Ledger

Plan: `docs/superpowers/plans/2026-07-01-c1-sales-order.md`
Spec: `docs/superpowers/specs/2026-07-01-c1-sales-order-design.md`
Mode: inline execution (executing-plans), **no git**.
Baseline (post-B2): build 0 warnings, 76 unit + 54 integ = 130 tests.

## Task status

- [x] Task 1: Domain — `SalesOrderStatus` + `SalesOrderLine` (math) + `SalesOrder` (transitions/totals). 13 new unit tests pass.
- [x] Task 2: Application — DTOs, `ISalesOrderService`, validators. 6 validator tests pass. Validators auto-registered via existing assembly scan.
- [x] Task 3: Infrastructure — `AppDbContext` DbSets + fluent mapping (SoNumber unique, enum→string(20), decimals, FK Customer/Warehouse Restrict, lines Cascade, Variant Restrict, Tax SetNull). Build 0/0.
- [x] Task 4: Infrastructure — `SalesOrderService` (mirror `PurchaseOrderService`) + DI + integration tests. 8 integ tests pass (create/number/totals, empty-chain auto-confirm, submit→approve→Confirmed, reject→Draft+note, cancel, SoNumber unique, credit-info sum/exclude/boundaries, zero-limit). Approval engine reused verbatim via `ApprovalDocumentType.SalesOrder`.
- [x] Task 5: EF migration `20260701041415_AddSalesOrder` — content verified (2 tables, unique `IX_SalesOrders_SoNumber`, FK behaviors, Down drops both only, no other tables). `database update` applied.
- [x] Task 6: Web — BootstrapSeeder default SalesOrder chain (1 step, admin role, idempotent) + AppMenus `transactions.sales-orders` ViewOnly→`SalesOrderActions` ([Index,Create,Edit,Delete,Approve]). Settings `ApprovalChainsIndex` already enumerates all `ApprovalDocumentType` → SalesOrder appears, no change. Build 0/0.
- [x] Task 7: Web — `SoIndex.razor` (+ copied scoped `SoIndex.razor.css`); deleted `SalesOrderPlaceholder.razor`.
- [x] Task 8: Web — `SoForm.razor` (+ scoped CSS): Customer + source warehouse, default price `DiscountPrice ?? Price`, live non-blocking credit-limit banner (`RefreshCreditAsync` on customer/variant/qty/price/disc/tax change via `@bind:after`).
- [x] Task 9: Web — `SoDetail.razor` (+ scoped CSS): read-only + approval timeline + contextual Submit/Approve/Reject/Cancel/Edit + read-only credit banner. Dropped GRN/Close branch + "Diterima" column (C2 territory).
- [x] Task 10: Full verification — build 0 warnings; full suite **95 unit + 62 integ = 157 pass**. SalesOrderService confirmed to make no stock movement.

## Review deviations from the drafted plan (applied during execution)
- **Removed** the `Creator_cannot_approve_own_so` integration test: the engine's SoD check (`ApprovalService.EnsureCanAct`) is guarded by `!string.IsNullOrEmpty(creatorUserName)` and throws `ValidationException`; with `NullCurrentUser` (null creator) in tests the rule is inert, so the test as drafted (asserting `InvalidOperationException`, null creator) was invalid. SoD is already covered by engine-level `ApprovalServiceTests` and applies to SalesOrder verbatim — matches B1's omission. Replaced with an explanatory NOTE in the test file.
- **Scoped CSS copies** (`SoIndex/SoForm/SoDetail.razor.css`) copied verbatim from the Po* equivalents — the plan didn't call these out but Blazor scoped CSS is per-component, so they're required for styling parity.
- Credit banner uses inline amber style on a `.pf-alert` base (no `.pf-alert.warn` variant existed).

## C1 COMPLETE (2026-07-01)
All 10 tasks done. Build 0 warnings; 157 tests pass. Remaining: **manual UI walkthrough by user** (see plan Task 10 Step 4). Next: Tahap C2 (Delivery Order) — stock-out + COGS + SO delivery statuses.
