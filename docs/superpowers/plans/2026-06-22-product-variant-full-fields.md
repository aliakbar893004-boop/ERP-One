# Product Variant Full Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the multi-variant variant table in `ProductForm` full per-variant fields (HPP, Discount, Barcode, Weight, Dimensions) via expandable caret rows, at parity with single-variant mode.

**Architecture:** Pure UI change in one Blazor component. The multi-variant `_rows` model (`VariantRow`) gains the missing fields + a UI `Expanded` flag; the table renders a caret column plus a per-row detail `<tr>` holding the extra inputs; `LoadVariants` pre-fills them on edit; `BuildVariantInputs` stops hard-coding `null`. No service/DTO/validator/migration changes — both modes already submit `VariantInput`, which `VariantInputValidator` already validates in full.

**Tech Stack:** .NET 10, Blazor (InteractiveServer), Bootstrap 5 + Bootstrap Icons, scoped CSS (`.razor.css`).

**Spec:** `docs/superpowers/specs/2026-06-22-product-variant-full-fields-design.md`

## Global Constraints

- Single file of behavior: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor` (+ its scoped `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor.css`). No other files change.
- Single-variant (simple) mode is unchanged.
- Opening Stock column keeps the F2 behavior: editable on create (`Id is null`), read-only on-hand (`disabled` input) on edit.
- Detail fields (HPP/Discount/Barcode/Weight/Dimensions) are editable in BOTH create and edit.
- `VariantInput` positional record: `(string? Barcode, decimal Price, decimal? DiscountPrice, decimal CostPrice, decimal? Weight, string? Dimensions, int OpeningStock, bool IsActive, IReadOnlyList<int> AttributeValueIds)`.
- `ProductVariantDto` carries `Barcode, Price, DiscountPrice, CostPrice, Weight, Dimensions, Stock` (all available for edit pre-fill).
- No-git repo (same as F1/F2): track progress in `docs/superpowers/plans/variant-fields-progress.md`. Verification per task = `dotnet build src/MyApp.Web/MyApp.Web.csproj` → 0 errors/0 warnings (full-solution `dotnet build MyApp.slnx` currently fails on an unrelated NU1903 SQLite advisory in MyApp.IntegrationTests — out of scope). Tests need only compile, not run.

---

## Task 1: Create-path — model fields, caret/detail markup, CSS, submit wiring

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor.css`

**Interfaces:**
- Consumes: `VariantInput` (signature above); the existing multi-variant `_rows`/`VariantRow`, `GenerateVariants`, `BuildVariantInputs`.
- Produces: `VariantRow` with new properties `Barcode`, `Weight`, `Dimensions`, `Expanded`; a variant table with a caret column + per-row detail panel; `BuildVariantInputs` multi-branch that passes all fields. Task 2 relies on these `VariantRow` property names.

- [ ] **Step 1: Add the new fields to `VariantRow`**

In `ProductForm.razor`, the `VariantRow` class currently is:

```csharp
    private sealed class VariantRow
    {
        public List<int> AttributeValueIds { get; set; } = new();
        public string Combo { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? Discount { get; set; }
        public decimal Cost { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; } = true;
    }
```

Replace it with (adds `Barcode`, `Weight`, `Dimensions`, `Expanded`):

```csharp
    private sealed class VariantRow
    {
        public List<int> AttributeValueIds { get; set; } = new();
        public string Combo { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? Discount { get; set; }
        public decimal Cost { get; set; }
        public string? Barcode { get; set; }
        public decimal? Weight { get; set; }
        public string? Dimensions { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; } = true;
        public bool Expanded { get; set; }
    }
```

(`GenerateVariants` needs no change — the new properties default to `null`/`false` for generated rows.)

- [ ] **Step 2: Replace the variant table markup (caret column + detail row)**

In `ProductForm.razor`, find the current multi-variant table block (the `@if (_rows.Count > 0)` block containing `<table class="table table-sm align-middle">` … through the `<div class="form-text">Cost price &amp; discount can be refined later per variant; defaults to 0 / none.</div>`). Replace the WHOLE block with:

```razor
                        @if (_rows.Count > 0)
                        {
                            <div class="table-responsive">
                                <table class="table table-sm align-middle">
                                    <thead>
                                        <tr>
                                            <th style="width:34px"></th>
                                            <th>Combination</th>
                                            <th style="width:120px">Price</th>
                                            <th style="width:100px">@(Id is null ? "Opening Stock" : "On hand")</th>
                                            <th style="width:40px"></th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        @for (var i = 0; i < _rows.Count; i++)
                                        {
                                            var idx = i;
                                            <tr>
                                                <td class="text-center">
                                                    <button type="button" class="btn btn-sm btn-link p-0 vr-caret"
                                                            title="Detail" @onclick="() => _rows[idx].Expanded = !_rows[idx].Expanded">
                                                        <i class="bi @(_rows[idx].Expanded ? "bi-chevron-down" : "bi-chevron-right")"></i>
                                                    </button>
                                                </td>
                                                <td class="small">@_rows[idx].Combo</td>
                                                <td><input class="form-control form-control-sm text-end" type="number" step="0.01" min="0" @bind="_rows[idx].Price" /></td>
                                                <td>
                                                    @if (Id is null)
                                                    {
                                                        <input class="form-control form-control-sm text-end" type="number" step="1" min="0" @bind="_rows[idx].Stock" />
                                                    }
                                                    else
                                                    {
                                                        <input class="form-control form-control-sm text-end" value="@_rows[idx].Stock.ToString("N0")" disabled />
                                                    }
                                                </td>
                                                <td>
                                                    <button type="button" class="btn btn-sm btn-outline-danger" @onclick="() => _rows.RemoveAt(idx)" title="Remove">
                                                        <i class="bi bi-trash3"></i>
                                                    </button>
                                                </td>
                                            </tr>
                                            @if (_rows[idx].Expanded)
                                            {
                                                <tr class="vr-detail">
                                                    <td></td>
                                                    <td colspan="4">
                                                        <div class="vr-detail-grid">
                                                            <div>
                                                                <label class="form-label-sm">Cost Price (HPP)</label>
                                                                <input class="form-control form-control-sm text-end" type="number" step="0.01" min="0" @bind="_rows[idx].Cost" />
                                                            </div>
                                                            <div>
                                                                <label class="form-label-sm">Discount Price</label>
                                                                <input class="form-control form-control-sm text-end" type="number" step="0.01" min="0" @bind="_rows[idx].Discount" />
                                                            </div>
                                                            <div>
                                                                <label class="form-label-sm">Barcode</label>
                                                                <input class="form-control form-control-sm" maxlength="50" placeholder="optional" @bind="_rows[idx].Barcode" />
                                                            </div>
                                                            <div>
                                                                <label class="form-label-sm">Weight (gram)</label>
                                                                <input class="form-control form-control-sm text-end" type="number" step="0.001" min="0" placeholder="0" @bind="_rows[idx].Weight" />
                                                            </div>
                                                            <div class="vr-detail-dim">
                                                                <label class="form-label-sm">Dimensions (P x L x T)</label>
                                                                <input class="form-control form-control-sm" maxlength="100" placeholder="e.g. 20 x 15 x 5 cm" @bind="_rows[idx].Dimensions" />
                                                            </div>
                                                        </div>
                                                    </td>
                                                </tr>
                                            }
                                        }
                                    </tbody>
                                </table>
                            </div>
                            <div class="form-text"><i class="bi bi-info-circle me-1"></i>Click the arrow on a row to set its HPP, discount, barcode, weight, and dimensions.</div>
                        }
```

- [ ] **Step 3: Wire all fields into `BuildVariantInputs` (multi branch)**

In `ProductForm.razor`, the multi-variant return of `BuildVariantInputs` is currently:

```csharp
        return _rows
            .Select(r => new VariantInput(null, r.Price, r.Discount, r.Cost, null, null, Id is null ? r.Stock : 0, r.IsActive, r.AttributeValueIds))
            .ToList();
```

Replace it with (passes `Barcode`, `Weight`, `Dimensions` instead of `null`):

```csharp
        return _rows
            .Select(r => new VariantInput(r.Barcode, r.Price, r.Discount, r.Cost, r.Weight, r.Dimensions, Id is null ? r.Stock : 0, r.IsActive, r.AttributeValueIds))
            .ToList();
```

- [ ] **Step 4: Add scoped CSS for caret + detail panel**

Append to `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor.css`:

```css
/* ── Variant detail (expandable rows) ────────────────────── */
.vr-caret { color: #64748b; line-height: 1; }
.vr-caret:hover { color: #0f172a; }

.vr-detail > td { background: #f8fafc; border-top: 0; }

.vr-detail-grid {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 0.55rem 0.9rem;
    padding: 0.25rem 0 0.6rem;
}
.vr-detail-dim { grid-column: 1 / -1; }

.form-label-sm {
    display: block;
    font-size: 0.72rem;
    font-weight: 600;
    color: #64748b;
    margin-bottom: 0.15rem;
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/MyApp.Web/MyApp.Web.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Manual smoke (create path)**

Launch the app (`dotnet run --project src/MyApp.Web --urls http://localhost:5103`), log in, go to `/master/products/new`:
1. Toggle "This product has variants", tick **Ukuran** (M, S) + **Warna** (Merah, Biru), Generate → rows show `M / Merah`, `M / Biru`, `S / Merah`, `S / Biru`.
2. Expand a row (caret ▸→▾); set HPP, Discount, Barcode, Weight, Dimensions; set Price + Opening Stock; Save.
3. Confirm at `/inventory/stock-levels` the variant's HPP equals the entered Cost (Moving Average seeded from opening), and (via DB or product edit) Barcode/Weight/Dimensions persisted.

- [ ] **Step 7: Record progress** in `docs/superpowers/plans/variant-fields-progress.md`.

---

## Task 2: Edit-path — pre-fill detail fields from the product

**Files:**
- Modify: `src/MyApp.Web/Components/Pages/Master/Products/ProductForm.razor`

**Interfaces:**
- Consumes: `VariantRow.Barcode/Weight/Dimensions` (Task 1); `ProductVariantDto.Barcode/Weight/Dimensions/DiscountPrice/CostPrice/Stock`.
- Produces: edit-mode rows fully populated so the detail panel pre-fills and an edit save round-trips every field.

- [ ] **Step 1: Populate the new fields in `LoadVariants`**

In `ProductForm.razor`, the multi-variant branch of `LoadVariants` currently adds rows like this:

```csharp
            _rows.Add(new VariantRow
            {
                AttributeValueIds = ids,
                Combo = ComboLabel(ids),
                Price = v.Price,
                Discount = v.DiscountPrice,
                Cost = v.CostPrice,
                Stock = v.Stock,
                IsActive = v.IsActive
            });
```

Replace that `_rows.Add(...)` with (adds `Barcode`, `Weight`, `Dimensions`):

```csharp
            _rows.Add(new VariantRow
            {
                AttributeValueIds = ids,
                Combo = ComboLabel(ids),
                Price = v.Price,
                Discount = v.DiscountPrice,
                Cost = v.CostPrice,
                Barcode = v.Barcode,
                Weight = v.Weight,
                Dimensions = v.Dimensions,
                Stock = v.Stock,
                IsActive = v.IsActive
            });
```

- [ ] **Step 2: Build**

Run: `dotnet build src/MyApp.Web/MyApp.Web.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual smoke (edit path)**

With the app running, open an existing multi-variant product at `/master/products/{id}/edit`:
1. Expand a variant row → HPP, Discount, Barcode, Weight, Dimensions are pre-filled from the saved variant (empty/0 if never set); Opening Stock column shows read-only "On hand".
2. Change a detail field (e.g. Barcode, Weight); Save.
3. Re-open the product → the changed field persisted. Confirm Opening Stock was not altered by the edit.

- [ ] **Step 4: Record progress** in `docs/superpowers/plans/variant-fields-progress.md`.

---

## Final verification (after both tasks)

- [ ] `dotnet build src/MyApp.Web/MyApp.Web.csproj` → 0 errors / 0 warnings.
- [ ] Create: multi-variant product with per-variant HPP seeds Moving Average at that HPP (not 0); barcode/weight/dimensions/discount persist per variant.
- [ ] Edit: detail fields pre-fill and round-trip; Opening Stock stays read-only and unchanged.
- [ ] Single-variant mode unchanged (regression check: create + edit a simple product still works).

## Out of scope

Per-variant SKU editing, image-per-variant, bulk default rows, variant history, the unrelated NU1903 SQLite advisory in MyApp.IntegrationTests.
