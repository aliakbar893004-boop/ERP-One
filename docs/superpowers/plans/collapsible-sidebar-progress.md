# Collapsible Sidebar — Progress Ledger

Plan: `docs/superpowers/plans/2026-06-22-collapsible-sidebar.md`
Spec: `docs/superpowers/specs/2026-06-22-collapsible-sidebar-design.md`
Mode: subagent-driven, no-git. Verify = `dotnet build src/MyApp.Web/MyApp.Web.csproj` (0/0; use `-o $TEMP/...` if bin locked) + manual smoke.

## Status
- [x] Task 1: NavMenu — ☰ toggle button + `OnToggle` EventCallback param + 14 labels wrapped in `<span class="nav-label">` + title attrs + `.nav-collapse-btn` CSS. Build 0/0 (via -o, bin locked).
- [x] Task 2: MainLayout — `_navCollapsed` state + `nav-collapsed` class, `OnToggle="ToggleNavAsync"`, `@inject IJSRuntime`, `OnAfterRenderAsync` reads localStorage `myapp.navCollapsed`, `ToggleNavAsync` writes it; `.sidebar` width transition + `@media(min-width:641px)` collapse rules (64px, `::deep` hides label/section/brand, centers icons). Build 0/0.

Reviewer verdict: spec ✅ all 5 met; 1 Important defect — `.nav-collapse-btn` not hidden on mobile (double hamburger + localStorage cross-breakpoint contamination). FIXED: added `@media (max-width:640.98px){ .nav-collapse-btn{display:none} }` to NavMenu.razor.css. Re-build 0/0.

### MECHANISM CHANGED after smoke (root-caused a "belum berjalan" bug)
The original plan used Blazor interactivity (`@onclick`/`EventCallback`/`IJSRuntime`/`_navCollapsed`). **That does not work here:** `App.razor` renders `<Routes />` with NO `@rendermode`, so the layout + NavMenu are **static SSR** (only individual pages opt into InteractiveServer). The toggle did nothing — confirmed via smoke (width stayed 250, localStorage null). This is exactly why the existing mobile `.navbar-toggler` is a CSS checkbox, not Blazor.

**Reimplemented (static-SSR friendly):**
- NavMenu: plain `<button id="nav-collapse-btn">` (no `@onclick`/no `OnToggle` param/no `@code`).
- MainLayout: reverted to fully static (no `_navCollapsed`/`OnAfterRender`/`IJSRuntime`); plain `<div class="page">`.
- State = `nav-collapsed` class on `<html>` (survives enhanced navigation). Collapse CSS moved to **global** `wwwroot/app.css` (`html.nav-collapsed .sidebar{width:64px}` + hide labels/section/brand, center icons), `@media(min-width:641px)`. `.sidebar` width transition stays in MainLayout.razor.css.
- JS in `wwwroot/js/app-interop.js`: delegated click on `#nav-collapse-btn` toggles localStorage + `apply()`; `apply()` sets the `<html>` class; runs at parse + `DOMContentLoaded` + **`Blazor.addEventListener('enhancedload', apply)`** (registered via polling since `Blazor` loads after this script). The enhancedload hook was the key fix — enhanced nav resets `<html>`, and `document.addEventListener('enhancedload')` is the WRONG API (must be `Blazor.addEventListener`).

### Smoke test RESULT (published/-o copy on :5104) — ALL PASS
- Toggle: 250px ↔ 64px rail; labels/section/brand hidden; icons centered; content widens. (screenshot 20-sb-collapsed.png)
- Persist across full reload: collapsed stays collapsed, localStorage="1".
- Persist across enhanced NavLink navigation AND full page nav: stays collapsed (verified width 64 after nav to /master/units).
- Mobile button hidden (<641px) — fix from reviewer applied.

Status: FEATURE COMPLETE & runtime-verified.

### Final re-review (post-rewrite) — spec ✅, no Critical; 2 fixes applied
- Important: unbounded `setTimeout` poll for `window.Blazor` (leak if blazor.web.js never loads) → bounded to 200 attempts (~10s) in app-interop.js.
- Minor: `html.nav-collapsed .nav-link`/`.nav-icon` not scoped → scoped under `.sidebar` in app.css (future-proofing; `.nav-link` is a Bootstrap class).
- Minor (not fixed, no-op): `nav-collapsed` ghost class added to `<html>` on the Login page (AccountLayout has no sidebar/navbar → zero visual effect). Left as-is.
- Re-build 0 errors. Behavior unchanged (guards only) — no re-smoke needed.

FILES CHANGED (final): NavMenu.razor, NavMenu.razor.css, MainLayout.razor, MainLayout.razor.css, wwwroot/app.css, wwwroot/js/app-interop.js.

## Follow-up tweaks (same session)
1. **Toggle moved to right of brand**: NavMenu brand row reordered (brand then ☰); `.nav-collapse-btn { margin-left:auto }` pushes it right; rail mode resets margin to 0 so the lone ☰ stays centered. Verified: btn.x > brand.x.
2. **Collapsible nav section groups (accordion)**: all 3 groups (Master/Inventory/Settings) wrapped in `<div class="nav-group" data-nav-group="x">` with a `<button class="nav-group-summary">` header + chevron and a `.nav-group-items` wrapper. JS-class pattern (consistent with sidebar): delegated click toggles `.collapsed` + persists `myapp.navGroup.<name>` in localStorage; `applyGroups()` restores on load/DOMContentLoaded/enhancedload. Default open. CSS: `.nav-group.collapsed .nav-group-items{display:none}` + chevron rotate; in rail mode summaries hidden and items force-shown (icons always visible). Verified via smoke: collapse/expand/persist-across-reload/rail-interaction all PASS.

Re-review (follow-up tweaks) — spec ✅, 2 fixes applied:
- Important: `.nav-scrollable` inline `onclick` (mobile auto-close) fired on ALL clicks incl. the new group-summary buttons → on mobile, collapsing a group closed the whole menu; on desktop it silently flipped the hidden mobile toggler checkbox. FIXED: guarded with `if(event.target.closest('a'))` so it only fires for actual nav-link clicks.
- Minor (a11y): added `aria-expanded="true"` default on each `.nav-group-summary`; JS now toggles it in `applyGroups()` and the click handler.
- Re-build 0 errors; accordion re-smoked PASS after fixes.

## Notes
- Use `dotnet build -o "$TEMP/dir"` to verify compile while bin is locked; `dotnet publish` does NOT work when bin is locked (it builds project refs into their own bin first).
