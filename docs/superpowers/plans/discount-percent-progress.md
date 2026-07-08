# Discount % Varian + Tampilan Diskon di Kasir — Progress Ledger

Plan: `docs/superpowers/plans/2026-07-03-variant-discount-percent.md`
Spec: `docs/superpowers/specs/2026-07-03-variant-discount-percent-design.md`
Mode: inline execution (executing-plans), **no git**. JANGAN `--no-build` untuk `dotnet ef`.
Baseline: build 0 warnings, 209 test (127 unit + 82 integ).

## Task status
- [x] Task 1: Domain (2026-07-03) — `ProductVariant.DiscountPercent` (`decimal?`) + `SetDiscountPercent` (0..100) di ctor/`Update`; `Product.AddVariant` param `discountPercent` (default null → pemanggil lama aman). `ProductVariantDiscountTests` 3 pass. Build 0 warnings.
- [x] Task 2: Persistence (2026-07-03) — mapping `HasPrecision(18,2)`; migration `20260703035115_AddVariantDiscountPercent` (AddColumn nullable / DropColumn) **applied**. Build 0 warnings.
- [x] Task 3: Application (2026-07-03) — `ProductVariantDto`+`DiscountPercent` (sebelum Attributes); `VariantInput`+`DiscountPercent` (param terakhir, default null); `PosProductOptionDto`+`Price`+`DiscountPercent`; validator `InclusiveBetween(0,100).When(...)`. `ProductVariantValidatorTests` 2 pass (build penuh ditunda ke T4).
- [x] Task 4: Infrastructure (2026-07-03) — `PosSaleService.SearchProductsAsync` kembalikan `Price`+`DiscountPercent`; `ProductService` teruskan `DiscountPercent` di 5 titik (Create/Update AddVariant, Update existing, import=null, DTO mapping). `PosSaleSearchDiscountTests` 3 pass. Build 0 warnings.
- [x] Task 5: Web (2026-07-03) — ProductForm: input **Discount %** (single + tabel kombinasi), kalkulasi dua-arah (% → Discount Price via `@bind:after`; ketik Discount Price → % dikosongkan), field harga (Selling/Discount/Cost) format ribuan `N0` (input teks, parse digit). Load isi %; `BuildVariantInputs`/`VariantRow` bawa %. Build 0 warnings.
- [x] Task 6: Web (2026-07-03) — PosRegister: `CartLine` bawa `OrigPrice`+`DiscPercent`; hasil pencarian & baris keranjang tampil harga asli dicoret + harga efektif + badge `-x%` (bila % ada). CSS `.was`/`.disc-badge`. Build 0 warnings.
- [x] Task 7: Full verification (2026-07-03) — build **0 warnings**; suite **217 pass** (132 unit + 85 integ), persis target. Invariant: harga efektif tetap `DiscountPrice ?? Price`; migration AddColumn nullable (baris lama aman). **Manual UI walkthrough (Step 4) → menunggu user.**

## ✅ SELESAI (2026-07-03) — kode & test hijau; sisa: manual UI walkthrough oleh user.
