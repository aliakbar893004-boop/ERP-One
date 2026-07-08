# Icon Rail Main Layout — Design Spec

**Date:** 2026-06-26
**Status:** Approved (design), pending implementation
**Author:** Ali Akbar (ali.a@acetris.co.id) with Claude Code

## Goal

Replace the current dark blue→purple sidebar with a light **icon rail** that
expands on hover, matching `Documents/Main-Layout-Mockups/3-icon-rail.html`.
The new chrome reuses the emerald + IBM Plex Sans visual language already chosen
for forms ("Atlas" direction), unifying the app's look.

## Reference

- Mockup: `OneDrive - acetris.co.id/Documents/Main-Layout-Mockups/3-icon-rail.html`
- Sibling design system: `form-design-atlas` (emerald `#0E9F6E` / `#077E57` /
  `#E7F8F1`, ink `#0F1B2D`, muted `#64748B`, line `#E9EDF2`, IBM Plex Sans).

## Behavior

- **Icon rail:** sidebar is `74px` wide showing only icons. On `:hover` it
  expands to `248px`, revealing the brand text, section labels, and nav labels
  (all via `opacity` transitions). A soft drop-shadow appears when expanded.
- **Pure CSS.** No JavaScript drives the expansion. This removes the previous
  click-toggle + accordion machinery.
- **Active link:** emerald-soft background (`--accent-soft`) with emerald-deep
  text/icon (`--accent-deep`).
- **Mobile (<641px):** keep the existing hamburger-checkbox drawer, restyled to
  the light theme, with labels always visible (no rail on touch devices).

## Decisions

- **Drop the accordion** section groups. Sections render flat as labels that fade
  in on hover (mockup style). All authorized items are always present.
- **IBM Plex Sans scoped to the chrome** (sidebar + top bar) only — not applied
  globally, to avoid re-typesetting every page. Font already loaded in `App.razor`.
- **Top bar kept as-is functionally** — user dropdown on the right, existing
  "About" link retained. No search box or notification bell added. Restyled to
  the light theme only.

## Files changed

1. **`Components/Layout/NavMenu.razor`**
   - Brand: emerald gradient logo tile (`bi-boxes`) + "MyApp" (text fades in).
   - Remove `#nav-collapse-btn` (collapse hamburger) and all
     `<button class="nav-group-summary">` accordion toggles + `nav-group`/
     `nav-group-items` wrappers. Replace with plain section labels + items.
   - Add bottom "Hover to expand" hint (`bi-arrows-angle-expand` + text).
   - **Preserve every `AuthorizeView` policy gate** unchanged (Home; Master:
     products, categories, units, brands, warehouses, taxes, payment-methods,
     attributes, suppliers, customers; Inventory: stock-levels, adjustments;
     Transaksi: hub, purchase-orders, sales-orders; Settings: users, roles,
     approval-chains, error-log; NotAuthorized login button).
   - Keep the mobile `.navbar-toggler` checkbox + `.nav-scrollable` drawer.

2. **`Components/Layout/NavMenu.razor.css`** — rewrite for light icon rail:
   icons always visible; `.nav-label` / `.nav-section-label` / brand text fade on
   `.sidebar:hover`; emerald active/hover states. Remove accordion/collapse rules.

3. **`Components/Layout/MainLayout.razor`** — keep structure. Top bar restyled
   light (user dropdown + About link retained).

4. **`Components/Layout/MainLayout.razor.css`** — sidebar → white, right border,
   `width:74px` → `248px` on `:hover` with transition + drop-shadow. Page
   background `#F4F6F8`. Define emerald CSS vars for the chrome.

5. **`wwwroot/app.css`** — remove the dead `html.nav-collapsed` block
   (current lines ~71–113). Add light page background if needed.

6. **`wwwroot/js/app-interop.js`** — remove the sidebar-collapse IIFE
   (current lines ~47–95). Keep `appSwal` and `pfScrollTo`.

## Out of scope

- Functional global search and notifications (bell) — visual only, deferred.
- Global typography change (IBM Plex Sans stays scoped to chrome).
- Any change to page/form content or C# logic.

## Constraints / risks

- Layout is **static SSR** (non-interactive Blazor); expansion must be CSS-only.
- Hover-expand is not touch-friendly — handled by the mobile drawer fallback.
- Not a git repository, so this spec is not committed to version control.
