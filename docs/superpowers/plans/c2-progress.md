# Tahap C2 — Delivery Order — Progress Ledger

Plan: `docs/superpowers/plans/2026-07-01-c2-delivery-order.md`
Spec: `docs/superpowers/specs/2026-07-01-c2-delivery-order-design.md`
Mode: inline execution (executing-plans), **no git**.
Baseline (post-C1): build 0 warnings, 95 unit + 62 integ = 157 tests.

## Task status

- [x] Task 1: Domain — SO delivery tracking. `SalesOrderStatus` += `PartiallyDelivered/Delivered/Closed`; `SalesOrderLine.DeliveredQuantity/IsFullyDelivered/ApplyDelivery` (STRICT, no tolerance); `SalesOrder.CanDeliver/MarkPartiallyDelivered/MarkDelivered/Close`. Unit tests appended to `SalesOrderTests.cs`.
- [x] Task 2: Domain — `DeliveryOrderStatus{Draft,Posted}` + `DeliveryOrderLine` (ctor without UnitCost, `SetUnitCost`) + `DeliveryOrder`. `DeliveryOrderTests.cs`. (Tasks 1+2 unit tests: 21 pass in filter.)
- [x] Task 3: Application — `DeliveryOrderDtos`/`IDeliveryOrderService`/`DeliveryOrderValidators` (line request has NO UnitCost; no options/tolerance). `ISalesOrderService.CloseAsync` added + `SalesOrderService.CloseAsync` implemented (transaction-wrapped). Validator tests 3 pass. Build 0/0.
- [x] Task 4: Infrastructure — `AppDbContext` DbSets `DeliveryOrders`/`DeliveryOrderLines` + mapping (DoNumber unique, enum string(20), UnitCost(18,2), SO Restrict / lines Cascade / Variant Restrict / SalesOrderLine Restrict, field-access nav). `SalesOrderLine.DeliveredQuantity` by convention.
- [x] Task 5: Infrastructure — `DeliveryOrderService` (read + draft CRUD + GetDeliverableSos/GetSoForDelivery; **PostAsync stubbed `NotImplementedException`**) + DI registration + `DeliveryOrderServiceTests.cs` (4 tests pass: GetSoForDelivery remaining, CreateDraft number/no-stock-move, over-delivery strict rejected, DeleteDraft).

- [x] Task 6: Infrastructure — `DeliveryOrderService.PostAsync` implemented (two-phase: validate availability at `SO.WarehouseId` for ALL lines w/ per-variant accumulation → then mutate). Writes `StockMovement(Out, -qty, variant.CostPrice, refType "DO")`, `db.UpsertStockAsync(-qty)`, `line.SetUnitCost(variant.CostPrice)`, `soLine.ApplyDelivery`, SO status (all fully → MarkDelivered else MarkPartiallyDelivered), `doc.Post()`. **MA never touched.** 4 post integration tests pass (partial→full + SO status; Out movement Quantity=-5 + COGS snapshot + CostPrice unchanged; insufficient-stock rejected w/ no mutation; Post-twice rejected). DO integ total: 8 pass.

- [x] Task 7: `SalesOrderService.GetCreditInfoAsync` widened to count Confirmed + PartiallyDelivered + Delivered (EF-translatable OR). CloseAsync integration coverage added. Tests: 10 DO + 8 SO integ = 18 pass (C1 SO credit tests unaffected — they only use Confirmed SOs).

- [x] Task 8: Migration `20260701093738_AddDeliveryOrder` — content verified (DeliveredQuantity col default 0 on SalesOrderLines + DeliveryOrders/DeliveryOrderLines tables; FK SO Restrict, lines Cascade, Variant/SOLine Restrict; unique DoNumber; Down drops all three). **`database update` applied.** Build 0 warnings; full suite **111 unit + 72 integ = 183 pass**.

- [x] Task 9: `AppMenus` — `DeliveryOrderActions` helper + resource `transactions.delivery-orders` [Index,Create,Edit,Delete,Post]; `ActClose` added to `SalesOrderActions`. `NavMenu` — Delivery Order entry (bi-truck) under transactions.any. Admin auto-grant via AllPermissions. Web build 0 warnings.

- [x] Task 10: `DoIndex.razor` (+ scoped css). **User deviation: matched the Product index instead of GrnIndex** — copied `ProductIndex.razor.css` → `DoIndex.razor.css` (same pi/kpi/toolbar/card family), badges `b-ok` (Posted)/`b-off` (Draft). KPI Total DO/Draft/Posted; table No.DO/SO/Customer/Tanggal/Qty/Status + view + delete-draft. Web build 0 warnings.

- [x] Task 11: `DoForm.razor` (+ copied `GrnForm.razor.css`). Mirrors GrnForm: pick deliverable SO, auto-fill rows from remaining qty, **qty-delivered only (no Unit Cost column)** — COGS captured at Post. Save Draft. Guards: no SO / no line. Web build 0 warnings.
- [x] Task 12: `DoDetail.razor` (+ copied `GrnDetail.razor.css`). Mirrors GrnDetail: info dl (DO#, SO# link, Customer, Gudang, Tanggal Kirim, dibuat/oleh), items table with HPP/unit + Subtotal (0.00 while Draft, snapshot COGS after Post), **Post** button (policy .post, Swal confirm "Stok akan dikurangi dan COGS dicatat"). StatusClass Draft→b-draft/Posted→b-done. Web build 0 warnings.

- [x] Task 13: `SalesOrderLineDto += DeliveredQuantity` + updated the single positional site in `SalesOrderService.GetByIdAsync`. `SoDetail.razor`: Terkirim column (`@l.DeliveredQuantity / @l.Quantity`), tfoot colspan 5→6, Confirmed/PartiallyDelivered action branch (Buat DO → `/transactions/delivery-orders/new?soId=`, Tutup SO → `CloseSoAsync`), StatusClass extended (PartiallyDelivered→b-info/Delivered→b-done/Closed→b-closed). `SoIndex.razor` StatusClass parity (Step 3b). Web build 0 warnings.
- [x] Task 14: **Build 0 warnings + full suite 111 unit + 72 integ = 183 pass** (verified 2026-07-02). PostAsync invariants re-confirmed (phase-1 availability check pre-mutation; Out/-qty at variant.CostPrice; UpsertStockAsync(-qty); SetUnitCost COGS snapshot; NO ApplyMovingAverage; non-Draft rejected). Manual UI walkthrough handed to user.

## ⚠️ Build-environment fixes applied during Task 14 (2026-07-02) — NOT part of the C2 plan
1. **`src/MyApp.Application/MyApp.Application.csproj` was corrupted** — something auto-added ~38 explicit `<Compile>/<Content>/<None>` items pointing at `obj/Debug`, `obj/Release`, and a stray `obj/verifybin/` folder, plus empty `<Folder>` includes. This caused CS0579 (duplicate assembly attributes from both Debug+Release) / CS2001. Restored to the minimal SDK-style form (PropertyGroup + Domain ProjectReference + FluentValidation package refs), matching sibling projects. Not caused by C2 work.
2. **NU1903 (RESOLVED):** `Microsoft.OpenApi 2.0.0` (transitive via `Microsoft.AspNetCore.OpenApi 10.0.9`) had high-severity advisory GHSA-v5pm-xwqc-g5wc (affected `<= 2.7.4`, fixed `2.7.5` on the 2.x line). Per user choice, pinned a direct `PackageReference Include="Microsoft.OpenApi" Version="2.7.5"` in `MyApp.Web.csproj`. Normal `dotnet build` + `dotnet test` (audit enabled) now pass: **0 warnings, 183 tests green**.

## C2 COMPLETE (code) — Tasks 1–14 done. Only Task 14 Step 4 (manual UI walkthrough) + the NU1903 decision remain with the user.

## (historical) STOPPED after Task 12 (2026-07-01, per user request) — resume = Task 13
Remaining: 13 (SoDetail delivered-progress column + Buat DO + Tutup SO; **SalesOrderLineDto += DeliveredQuantity** + update the single positional site in `SalesOrderService.GetByIdAsync`; add C1-status→delivery-status arms; **SoIndex StatusClass parity Step 3b** for PartiallyDelivered/Delivered/Closed), 14 (full build + `dotnet test` + manual UI walkthrough handed to user). Deviation recorded: DoIndex matched Product index; DoForm/DoDetail mirror Grn* per user confirmation.

## (historical) STOPPED after Task 5 (2026-07-01, per user request)

State is clean & builds: solution compiles 0 warnings; new C2 unit + the 4 DO integration tests green. **`DeliveryOrderService.PostAsync` is a deliberate `NotImplementedException` stub** — no DO can be posted yet, and NO stock movement code exists yet. **NO EF migration generated** (`SalesOrderLine.DeliveredQuantity` column + DeliveryOrder tables NOT yet in the DB — Task 8 pending). Dev DB unchanged since C1.

### Resume = Task 6: implement `DeliveryOrderService.PostAsync`
Plan Task 6 (`docs/superpowers/plans/2026-07-01-c2-delivery-order.md`, ~line 1134): two-phase (validate availability for ALL lines at `SO.WarehouseId` with per-variant accumulation → then mutate), `StockMovement(MovementType.Out, -QuantityDelivered, variant.CostPrice, refType "DO")`, `db.UpsertStockAsync(..., -qty)`, `line.SetUnitCost(variant.CostPrice)`, `soLine.ApplyDelivery(qty)`, SO status (all fully delivered → `MarkDelivered` else `MarkPartiallyDelivered`), `doc.Post()`. **Never calls `ApplyMovingAverage`.** Add its 4 integration tests (partial→full stock-out + SO status; Out movement `Quantity == -5` + COGS snapshot + CostPrice unchanged; insufficient-stock rejected w/ no mutation; Post-twice rejected).

Then remaining: Task 7 (CloseAsync integ coverage + widen `GetCreditInfoAsync` to Confirmed+PartiallyDelivered+Delivered), Task 8 (EF migration `AddDeliveryOrder` + `database update`), Task 9 (AppMenus delivery-orders resource + ActClose on sales-orders + NavMenu), Tasks 10–12 (DoIndex/DoForm/DoDetail + scoped css), Task 13 (SoDetail delivered progress + Buat DO + Tutup SO; + SoIndex StatusClass parity — added Step 3b), Task 14 (full verification + manual walkthrough).

## Review deviation already applied in plan
- Removed nothing; the plan's `Creator`-style pitfalls don't apply to DO. Added Task 13 Step 3b (SoIndex badge parity for new statuses) during plan review.
