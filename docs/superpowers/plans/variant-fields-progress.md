# Variant Full Fields — Progress Ledger

Plan: `docs/superpowers/plans/2026-06-22-product-variant-full-fields.md`
Spec: `docs/superpowers/specs/2026-06-22-product-variant-full-fields-design.md`
Mode: subagent-driven, no-git (controller implements, dispatched reviewer per task). Verification = `dotnet build src/MyApp.Web/MyApp.Web.csproj` (0 err/0 warn) + manual smoke.

## Status

- [x] Task 1: Create-path — VariantRow fields (Barcode/Weight/Dimensions/Expanded), caret + detail-panel markup, scoped CSS, BuildVariantInputs passes all fields (build 0 err/0 warn via -o temp; bin locked by user's IIS Express). Review clean.
- [x] Task 2: Edit-path — LoadVariants pre-fills Barcode/Weight/Dimensions (build 0 err/0 warn). Review clean.

Combined reviewer verdict: spec ✅ all pass, no Critical/Important/Minor defects, "ready to merge". Smoke test pending (running published copy on :5104 since bin is locked).

## Smoke test results (published copy on :5104)
- CREATE multi-variant: combos = M/Merah, M/Biru, S/Merah, S/Biru ✅ (also confirms the attribute data fix). Row M/Merah: HPP 200000, Disc 230000, Barcode BC-MMERAH-001, Weight 350, Dim "20 x 15 x 5 cm", Stock 12 all persisted; opening movement seeded 12 @ 200000 (MA = entered HPP, not 0) ✅.
- EDIT pre-fill (Task 2): row M/Merah pre-fills HPP/Disc/Weight/Barcode/Dim correctly ✅; Opening Stock column shows read-only "On hand" ✅.
- **FEATURE COMPLETE & VERIFIED.** Both tasks done, review clean, create + edit-prefill smoke pass.

## DISCOVERED BUG (separate, pre-existing F2 — NOT this feature)
- Saving an edit of any product **that has stock** fails: `ProductService.UpdateAsync` (line ~133) does `product.ClearVariants()` (delete-all + re-add). F2's `ProductStocks`/`StockMovements` have FK `OnDelete(Restrict)` to `ProductVariants`, so deleting a stocked variant throws SqlException 547 (`FK_ProductStocks_ProductVariants`). Edit-save is broken for all stocked products. Missed in F2 because Task 10 smoke only viewed the edit form, never saved.
- Proposed fix (needs its own spec/plan): change UpdateAsync to merge variants in place (match by SKU, update fields, add new combos, deactivate-not-delete removed combos) so the stock ledger is preserved. Awaiting user decision.

## Notes
- `dotnet build MyApp.slnx` (full) still fails on unrelated NU1903 SQLite advisory in IntegrationTests — out of scope.
- bin/ locked by user's running IIS Express/VS instance (stale code); used `dotnet build -o $TEMP/...` to verify compile, and `dotnet publish` to a temp folder for the smoke instance.
