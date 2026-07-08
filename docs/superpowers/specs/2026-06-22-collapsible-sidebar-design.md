# Collapsible Sidebar (Icon Rail) — Design Spec

**Date:** 2026-06-22
**Scope:** `src/MyApp.Web/Components/Layout/MainLayout.razor`, `NavMenu.razor`, `MainLayout.razor.css`
**Type:** UI enhancement. No-git repo — ledger + per-task review like F2/variant-fields.

## Problem / Goal

On desktop (`≥641px`) the left sidebar is a fixed 250px and always visible. Add a toggle that collapses it to a **~64px icon rail** (icons only, labels hidden) and expands it back to the full 250px labeled menu, so users can reclaim horizontal space. The collapsed/expanded choice **persists across page reloads** (localStorage).

## Non-goals

- Mobile behavior (`<641px`) is unchanged — the existing hamburger show/hide checkbox governs small screens; the rail-collapse is desktop-only.
- No flyout/submenu on hover in collapsed mode (icons + native `title` tooltip only).
- No animation beyond a simple width transition.

## Design

### State & ownership

`MainLayout` owns a single `bool _navCollapsed`.
- Render the page wrapper as `<div class="page @(_navCollapsed ? "nav-collapsed" : null)">`.
- Pass a toggle hook to the menu: `<NavMenu OnToggle="ToggleNav" />`, where `ToggleNav` flips `_navCollapsed`, persists it (below), and calls `StateHasChanged`.

`MainLayout` is not re-created on navigation within a Blazor Server circuit, so the state survives page-to-page navigation; localStorage adds survival across full reloads.

### Toggle button

`NavMenu` exposes `[Parameter] public EventCallback OnToggle { get; set; }` and renders a hamburger button (`☰`, `bi-list`) in its brand top-row, with `aria-label="Toggle menu"`, calling `OnToggle`. Placement matches the approved mockup (top of the sidebar, beside the brand).

### Label wrapping

Each nav link currently renders `<i class="bi … nav-icon"></i> Bare Text`. The bare text cannot be hidden by CSS alone, so wrap every link's label in `<span class="nav-label">…</span>`. Section headers already use `<span class="nav-section-label">`; the brand uses `.navbar-brand`.

### Collapse styling (`MainLayout.razor.css`, desktop only)

Inside `@media (min-width: 641px)`, gated by `.nav-collapsed`:
- `.nav-collapsed .sidebar { width: 64px; }` (from 250px) + `transition: width .18s ease` on `.sidebar`.
- Hide labels via `::deep` into NavMenu: `.nav-collapsed ::deep .nav-label`, `.nav-collapsed ::deep .nav-section-label`, `.nav-collapsed ::deep .navbar-brand { display: none; }`.
- Center icons: `.nav-collapsed ::deep .nav-link { justify-content: center; }` and drop the icon's right margin in collapsed mode.
- Keep the hamburger visible/centered in collapsed mode.

### Persistence (localStorage, via JS interop)

- Key: `myapp.navCollapsed`, value `"1"`/`"0"`.
- Read: in `OnAfterRenderAsync(firstRender)` (localStorage is unavailable during prerender), call `IJSRuntime.InvokeAsync<string?>("localStorage.getItem", key)`; if `"1"`, set `_navCollapsed = true` and `StateHasChanged()`.
- Write: in `ToggleNav`, `await JS.InvokeVoidAsync("localStorage.setItem", key, _navCollapsed ? "1" : "0")`.
- No custom JS file — call the `localStorage` API directly through `IJSRuntime`.
- Known minor cosmetic effect: because localStorage is read after first render, a user who last left it collapsed may see one brief frame expanded before it snaps to collapsed. Acceptable for v1.

### Accessibility

- Toggle button: `aria-label="Toggle menu"`, `title` "Expand/Collapse".
- Nav links keep their visible text in the DOM (just visually hidden via `display:none` on the label span); add `title` on each `.nav-link` equal to its label so collapsed icons are identifiable on hover. (Acceptable tradeoff; full SR-only text retention can be a later refinement.)

## Verification

- `dotnet build src/MyApp.Web/MyApp.Web.csproj` → 0 errors / 0 warnings.
- Manual smoke (Playwright): toggle collapses sidebar to rail (labels hidden, icons shown, content widens); toggle again restores; reload page → collapsed state persists; navigate between pages → state holds.

## Out of scope

Hover flyout menus, per-user server-side preference, mobile changes, RTL.
