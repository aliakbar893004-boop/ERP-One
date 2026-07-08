# F0 Progress Ledger

Plan: `docs/superpowers/plans/2026-06-18-master-data-f0.md`
Mode: subagent-driven, no git (checkpoints = build + test).

- [x] Baseline build green (0 error)
- [x] Task 1: Brand master (build 0 err, BrandServiceTests 2/2, review clean)
- [x] Task 2: Warehouse master (build 0 err, WarehouseServiceTests 1/1, review clean)
- [x] Task 3: Tax master (build 0 err, TaxServiceTests 1/1, review clean)
- [x] Task 4: PaymentMethod master (build 0 err, PaymentMethodServiceTests 1/1, review clean; impl timed out mid-way, controller completed service+pages+wiring+test)
- [x] Task 5: Attribute + AttributeValue master (build 0 err, AttributeServiceTests 2/2, review clean; impl hit session limit after entities+app, controller completed service+wiring+test+pages incl. inline value editor)
- [x] Task 6: EF migration + seed default warehouse (migration 20260618060411_AddMasterDataF0: 6 new tables + unique Code indexes + WH-MAIN seed; no changes to existing tables. Full suite 19/19 integration + 9/9 unit = 28/28. DB update NOT applied — needs SQL Server connection.)

## Status: F0 COMPLETE
- All 6 tasks done, all per-task reviews clean, final whole-branch review = Ready to merge (no Critical/Important).
- Full suite: 19/19 integration + 9/9 unit = 28/28. Build 0 err.
- Migration `20260618060411_AddMasterDataF0` generated (additive-only). DB update NOT applied (needs SQL Server connection).

## Notes / Minor findings (resolved)
- [RESOLVED] AppMenus labels were plural ("Brands" etc.) vs singular existing entries. Normalized all 5 new labels to singular at final review for consistency with NavMenu + existing entries. Build re-verified green.
- [ACCEPTED] Form pages use bare `[Authorize]` + in-code IAuthorizationService check — consistent with all pre-existing forms (UnitForm/ProductForm), not a regression.
- [ACCEPTED] Index search fires query per keystroke — inherited pattern from UnitIndex across all masters.
