# Product Variant Full Fields — Design Spec

**Date:** 2026-06-22
**Scope:** `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor` (+ `ProductForm.razor.css`)
**Type:** Follow-up enhancement to ProductForm (outside F2; F2 is complete). No-git repo — track progress in a ledger, review per task.

## Problem

In **multi-variant mode**, the variant table only captures `Combination`, `Price`, and `Opening Stock`. The other per-variant fields that **single-variant mode** exposes — **Cost Price (HPP)**, **Discount Price**, **Barcode**, **Weight**, **Dimensions** — are not editable:

- `Cost` and `Discount` exist on the `VariantRow` model but have no input (default 0 / none).
- `Barcode`, `Weight`, `Dimensions` are not on `VariantRow` at all — `BuildVariantInputs` hard-codes them to `null`.

Consequence: multi-variant products are created with HPP = 0, so opening-stock seeding records Moving Average at 0 (observed: Addidas variants showed HPP 0.00). No way to set barcode/weight/dimensions per variant.

## Goal

Bring the multi-variant variant table to **parity** with single-variant mode: every per-variant field is editable, using an **expandable row** layout so the table stays readable.

## Non-goals

- No changes to services, DTOs, validators, or migrations. Both modes already produce `VariantInput`; the `VariantInputValidator` already validates all five fields (verified). The gap is purely UI capture.
- No new per-variant edit screen, no bulk-default inputs.
- Single-variant mode unchanged.

## Design

All changes are in `ProductForm.razor` (+ scoped `.razor.css`).

### Layout — expandable rows (caret)

Multi-variant table:

- **Inline columns (always visible):** caret toggle `▸/▾` · `Combination` · `Price` · `Opening Stock` · delete `🗑`.
  - `Opening Stock`: editable on create; **read-only on-hand** on edit (preserve the F2 behavior already in place).
- **Detail panel (per row, shown when expanded):** a second `<tr>` whose single `<td colspan>` holds a small grid of editable inputs: **HPP (Cost Price)**, **Discount Price**, **Barcode**, **Weight (gram)**, **Dimensions**. Editable in both create and edit.
- Multiple rows may be expanded at once. Expansion is pure UI state (`VariantRow.Expanded`), not persisted.

### Model — `VariantRow`

Add fields: `Barcode (string?)`, `Weight (decimal?)`, `Dimensions (string?)`, `Expanded (bool)`. (`Price`, `Discount`, `Cost`, `Stock`, `IsActive`, `AttributeValueIds`, `Combo` already exist.)

### Data flow

- **GenerateVariants:** new rows default detail fields to empty/0 (`Cost = 0`, `Discount = null`, `Barcode = null`, `Weight = null`, `Dimensions = null`, `Stock = 0`, `Expanded = false`). User fills per row. (Unchanged: `Price`, `Cost` still seed from the now-hidden `_price`/`_cost`, i.e. 0.)
- **LoadVariants (edit):** populate `row.Barcode`, `row.Weight`, `row.Dimensions`, `row.Discount`, `row.Cost` from `ProductVariantDto` (all fields already present on the DTO). `row.Stock` already loaded.
- **BuildVariantInputs (multi):** replace the `null` placeholders —
  `new VariantInput(r.Barcode, r.Price, r.Discount, r.Cost, r.Weight, r.Dimensions, Id is null ? r.Stock : 0, r.IsActive, r.AttributeValueIds)`

### Behavioral effects (intended, consistent with single-variant mode)

- **Create:** per-variant HPP now flows into opening-stock seeding (`CreateAsync` seeds opening movement with `VariantInput.CostPrice`), so Moving Average starts at the entered HPP instead of 0.
- **Edit:** editing a variant's HPP overwrites `CostPrice` directly via `UpdateAsync` — identical to how single-variant edit already behaves (a manual HPP override, not a Moving-Average recompute).

### Validation

No change. `VariantInputValidator` already enforces: `Price ≥ 0`, `CostPrice ≥ 0`, `DiscountPrice ≥ 0` and `≤ Price`, `OpeningStock ≥ 0`, `Weight ≥ 0`, `Dimensions ≤ 100 chars`, `Barcode ≤ 50 chars`. Applies to both modes since both submit `VariantInput`.

### Styling

Add minimal scoped CSS in `ProductForm.razor.css` for the detail panel (light background row, small label/input grid). Reuse existing form-control sizing.

## Existing-data note

Pre-existing multi-variant products (e.g. Addidas) have no barcode/weight/dimensions and HPP 0 because those were never captured. After this change the detail panel shows them as empty/0 (correct — nothing to backfill); the user can fill and save them.

## Verification

- `dotnet build MyApp.slnx` (Web project) → 0 errors.
- Manual smoke: select `Ukuran` + `Warna` → generate → `M/Merah, M/Biru, S/Merah, S/Biru`; expand a row, set HPP/discount/barcode/weight/dimensions; save; confirm Stock Levels HPP and the persisted variant fields. Edit the product → detail fields editable and pre-filled; Opening Stock read-only.

## Out of scope

Per-variant SKU editing, image-per-variant, bulk default rows, variant history.
