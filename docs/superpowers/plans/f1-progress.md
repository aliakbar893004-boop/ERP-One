# F1 Progress Ledger

Plan: `docs/superpowers/plans/2026-06-18-f1-product-variant.md`
Mode: inline execution, no git, **testing deprioritized per user** (kept tests compiling; not run as gate). Verification = `dotnet build` (0 errors) + clean migration apply.

## Status: F1 COMPLETE
- [x] Task 1: Domain — `ProductVariant`, `ProductVariantAttribute`, restructured `Product` (Code/Brand/Unit/Tax + Variants). Unit tests rewritten (kept image tests) — UnitTests project passed 8/8 at the time.
- [x] Task 2: DbContext config (7 DbSets, variant/attribute config, parent shadow FKs) + migration `20260618065842_RefactorProductToVariant`.
- [x] Task 3: DTOs (ProductDto/ProductVariantDto/VariantInput/AttributeValueRefDto), validators (≥1 variant, per-variant rules), full `ProductService` rewrite (SKU base+suffix gen, dashboard & import aggregate from variants). Existing integration tests (ProductApiTests, IdentityAndServiceTests) updated to new shape to keep solution compiling.
- [x] Task 4: `ProductIndex` (Code, price range, variant count, total stock); `ProductForm` parent selectors (Brand/Unit/Tax) + simple single-variant mode.
- [x] Task 5: `ProductForm` multi-variant generator (attribute/value selection → cartesian product → editable rows). Import/Dashboard pages needed no changes.

## Migration handling (important)
- EF scaffolded `Sku`→`Code` RENAME (preserves value) + DROP of Price/DiscountPrice/Stock/Weight/Dimensions.
- Hand-edited so order is: rename → add parent cols → create variant tables → **backfill INSERT (1 default variant/product copying SKU/price/stock; CostPrice=0)** → indexes/FKs → drop old columns. Down() re-adds columns, backfills from first variant (multi-variant restores first only — documented), then renames back.
- Applied to SQL Server DB cleanly. Unique Sku index created post-backfill ⇒ implicit proof backfill produced unique non-null SKUs.

## Verification
- `dotnet build MyApp.slnx` = 0 errors / 0 warnings.
- `dotnet ef database update` = applied OK.
- Test suite NOT run as a gate (per user); tests left in a compiling state.

## Notes
- `ProductVariant.Stock` is TEMPORARY — F2 (stock model) migrates it to per-warehouse StockMovement and removes the column.
- Multi-variant import deferred; per-variant images deferred.
