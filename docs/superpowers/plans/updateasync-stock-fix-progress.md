# UpdateAsync Stock-FK Fix — Progress

Bug: editing a product that has stock failed (SqlException 547) — `ProductService.UpdateAsync` did `product.ClearVariants()` (delete-all + re-add) but `StockMovements`/`ProductStocks` FK to `ProductVariants` is `OnDelete(Restrict)`. Found during the variant-fields smoke; deferred as backlog; now fixed.

Decision (user): removed stocked/ledgered variants → **deactivate (IsActive=false) + notify**; removed variants with no stock/history → hard-delete (clean).

## Change (merge in place, no ClearVariants)
- `ProductVariant.Deactivate()` (Domain) — sets IsActive=false.
- `ProductUpdateResult(bool Found, IReadOnlyList<string> DeactivatedVariantSkus)` (Application/ProductDtos).
- `IProductService.UpdateAsync` → returns `Task<ProductUpdateResult>` (was `Task<bool>`).
- `ProductService.UpdateAsync` rewritten: match existing variants by SKU → `Update()`+`SetAttributeValues()`; new combos → `AddVariant()`; removed → query StockMovements/ProductStocks, deactivate those with ledger (collect SKUs), `RemoveVariant()` those without.
- `ProductEndpoints.cs` PUT → `(await service.UpdateAsync(...)).Found`.
- `SwalService.AlertAsync` + `app-interop.js` `appSwal.alert` (blocking info modal).
- `ProductForm.SaveAsync` → captures result; if `DeactivatedVariantSkus.Count>0` awaits `Swal.AlertAsync` before navigating.

## Verification — build 0/0 + runtime smoke on :5104 (published `-o` copy; bin locked by VS)
- Test A: edit-save product 3 (4 stocked variants) → redirect to /master/products, **0 error** (FK 547 gone). Name updated, variants intact.
- Test B: remove stocked variant RUN/0001-41 → modal "Varian dinonaktifkan … RUN/0001-41"; DB: variant IsActive=0, **ProductStock qty 10 preserved**; others active.
- Smoke artifacts restored (product name reverted; variant 20 reactivated).
- Re-review dispatched (merge correctness, no-stock delete path, contract ripple, notification timing).

NOTE: no-stock hard-delete path not exercised at runtime (no zero-stock removable variant existed); logic is the pre-existing RemoveVariant path, low risk. Tests project still blocked by NU1903 (out of scope) — verification per F1/F2 policy = build + manual smoke. No test references UpdateAsync/ClearVariants, so the contract change + dead-code removal don't break test compile.

## Re-review — adjudicated + fixes applied (re-build 0/0, re-smoke PASS)
- CRITICAL #1 (SetAttributeValues clear+re-add → claimed EF dup-key conflict): **FALSE POSITIVE** — `ProductVariantAttribute` has a surrogate identity PK (`HasKey(a => a.Id)`), so clear+re-add = delete old Ids + insert new Ids, no key conflict; runtime Test A (4 matched variants, SetAttributeValues called on all) saved with 0 errors. Applied the reviewer's diff-check anyway as an optimization (skip rewrite when combo unchanged).
- Important #2: guard `if (v.IsActive)` before Deactivate()+report — already-inactive variants no longer re-notified every save.
- Important #3: moved the deactivation `Swal.AlertAsync` BEFORE the image upload so the notice isn't lost if upload throws.
- Minor #4: removed dead `Product.ClearVariants()` (re-exposed the FK-547 trap). Confirmed no caller in src or tests.
- Minor #5 (REST PUT discards DeactivatedVariantSkus): left as-is — out of scope; Blazor UI is the primary consumer and handles it.

FIX COMPLETE & runtime-verified.
