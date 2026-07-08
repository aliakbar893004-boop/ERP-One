# Collapsible Sidebar (Icon Rail) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a desktop toggle that collapses the left sidebar to a 64px icon rail (and back to the 250px labeled menu), with the choice persisted across reloads via localStorage.

**Architecture:** `MainLayout` owns a `_navCollapsed` bool, applies a `nav-collapsed` class on `.page`, and passes an `OnToggle` `EventCallback` to `NavMenu`, which renders the hamburger and wraps each link label in a hideable `<span class="nav-label">`. Collapse styling lives in `MainLayout.razor.css` (using `::deep` to reach NavMenu), scoped to `@media (min-width: 641px)`. Persistence uses `IJSRuntime` to read/write `localStorage` directly (no custom JS file).

**Tech Stack:** .NET 10, Blazor (InteractiveServer), Bootstrap Icons, scoped CSS, JS interop (`IJSRuntime`).

**Spec:** `docs/superpowers/specs/2026-06-22-collapsible-sidebar-design.md`

## Global Constraints

- Only three files change: `src/MyApp.Web/Components/Layout/NavMenu.razor`, `src/MyApp.Web/Components/Layout/MainLayout.razor`, `src/MyApp.Web/Components/Layout/MainLayout.razor.css`.
- Rail collapse is desktop-only (`@media (min-width: 641px)`); mobile (`<641px`) behavior unchanged (existing `.navbar-toggler` checkbox governs it).
- Rail width = **64px**; full width = **250px** (the existing `.sidebar` width). Width transition `.18s ease`.
- localStorage key = `myapp.navCollapsed`, values `"1"` (collapsed) / `"0"` (expanded).
- localStorage is read in `OnAfterRenderAsync(firstRender)` only (unavailable during prerender).
- Keep every nav link's visible text in the DOM (hidden via `display:none` on `.nav-label`, not removed).
- No-git repo: track progress in `docs/superpowers/plans/collapsible-sidebar-progress.md`. Verification per task = `dotnet build src/MyApp.Web/MyApp.Web.csproj` → 0 errors/0 warnings (use `-o "$TEMP/<dir>"` if `bin/` is locked by a running IIS Express/VS instance). Tests need only compile.

---

## Task 1: NavMenu — toggle button + label wrapping

**Files:**
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor`
- Modify: `src/MyApp.Web/Components/Layout/NavMenu.razor.css`

**Interfaces:**
- Produces: `NavMenu` with `[Parameter] public EventCallback OnToggle { get; set; }`; a hamburger button (`.nav-collapse-btn`) in the brand row that invokes `OnToggle`; every nav link's label text wrapped in `<span class="nav-label">…</span>`. Task 2 consumes `OnToggle` and the `.nav-label` class.

- [ ] **Step 1: Add the toggle button to the brand top-row + an `@code` block with the parameter**

Replace the current brand block (lines 1–5):

```razor
<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid">
        <a class="navbar-brand" href="">MyApp.Web</a>
    </div>
</div>
```

with (adds a hamburger that calls `OnToggle`):

```razor
<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid d-flex align-items-center">
        <button type="button" class="nav-collapse-btn" aria-label="Toggle menu" title="Expand/Collapse menu" @onclick="OnToggle">
            <i class="bi bi-list"></i>
        </button>
        <a class="navbar-brand ms-2" href="">MyApp.Web</a>
    </div>
</div>
```

At the END of the file, add an `@code` block:

```razor
@code {
    [Parameter] public EventCallback OnToggle { get; set; }
}
```

- [ ] **Step 2: Wrap every nav link label in `<span class="nav-label">` and add a `title`**

For EACH of the 14 `<NavLink>` elements, change the bare label text into a `nav-label` span and add a `title` to the link so the icon is identifiable when collapsed. Transformation pattern (apply to all):

Before:
```razor
<NavLink class="nav-link" href="master/products">
    <i class="bi bi-box-seam-fill nav-icon" aria-hidden="true"></i> Product
</NavLink>
```
After:
```razor
<NavLink class="nav-link" href="master/products" title="Product">
    <i class="bi bi-box-seam-fill nav-icon" aria-hidden="true"></i> <span class="nav-label">Product</span>
</NavLink>
```

Apply to all 14 links with these exact labels (href unchanged):
- Home (`href=""`, `Match="NavLinkMatch.All"` — keep the Match attribute), Product, Product Category, Unit, Brand, Warehouse, Tax, Payment Method, Attribute, Stock Levels, Stock Adjustment, User, Role, Error Log.

The section headers already use `<span class="nav-section-label">…</span>` — leave them as-is.

- [ ] **Step 3: Style the toggle button in `NavMenu.razor.css`**

Append to `src/MyApp.Web/Components/Layout/NavMenu.razor.css` (same scope as the button, so no `::deep` needed):

```css
/* Sidebar collapse toggle */
.nav-collapse-btn {
    background: none;
    border: none;
    color: rgba(255, 255, 255, 0.75);
    font-size: 1.2rem;
    line-height: 1;
    padding: 0.25rem 0.45rem;
    cursor: pointer;
    border-radius: 6px;
}
.nav-collapse-btn:hover {
    background: rgba(255, 255, 255, 0.12);
    color: #fff;
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/MyApp.Web/MyApp.Web.csproj` (add `-o "$TEMP/sb-verify"` if bin is locked).
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. (No visual change yet — `OnToggle` is unwired, `.nav-label` not yet hidden.)

- [ ] **Step 5: Record progress** in `docs/superpowers/plans/collapsible-sidebar-progress.md`.

---

## Task 2: MainLayout — state, wiring, collapse CSS, persistence

**Files:**
- Modify: `src/MyApp.Web/Components/Layout/MainLayout.razor`
- Modify: `src/MyApp.Web/Components/Layout/MainLayout.razor.css`

**Interfaces:**
- Consumes: `NavMenu.OnToggle` and `.nav-label` (Task 1).
- Produces: working collapse toggle with localStorage persistence.

- [ ] **Step 1: Add the `nav-collapsed` class and wire `OnToggle`**

In `MainLayout.razor`, change the page wrapper and NavMenu usage. Replace lines 3–6:

```razor
<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>
```

with:

```razor
<div class="page @(_navCollapsed ? "nav-collapsed" : null)">
    <div class="sidebar">
        <NavMenu OnToggle="ToggleNavAsync" />
    </div>
```

- [ ] **Step 2: Add state + persistence to the `@code` block**

In `MainLayout.razor`, add `@inject IJSRuntime JS` near the top (after `@inherits LayoutComponentBase`). Inside the existing `@code { … }` block, add these members (alongside the existing `Initials`/`AvatarClass`):

```csharp
    private const string NavKey = "myapp.navCollapsed";
    private bool _navCollapsed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var saved = await JS.InvokeAsync<string?>("localStorage.getItem", NavKey);
        if (saved == "1")
        {
            _navCollapsed = true;
            StateHasChanged();
        }
    }

    private async Task ToggleNavAsync()
    {
        _navCollapsed = !_navCollapsed;
        await JS.InvokeVoidAsync("localStorage.setItem", NavKey, _navCollapsed ? "1" : "0");
    }
```

(`@inject IJSRuntime JS` requires no extra using — `Microsoft.JSInterop` is in `_Imports.razor` by Blazor default; if the build reports `IJSRuntime` not found, add `@using Microsoft.JSInterop` to `MainLayout.razor`.)

- [ ] **Step 3: Add collapse styling to `MainLayout.razor.css`**

First, give `.sidebar` a width transition. Replace the existing `.sidebar` rule (lines 11–13):

```css
.sidebar {
    background-image: linear-gradient(180deg, rgb(5, 39, 103) 0%, #3a0647 70%);
}
```

with:

```css
.sidebar {
    background-image: linear-gradient(180deg, rgb(5, 39, 103) 0%, #3a0647 70%);
    transition: width 0.18s ease;
}
```

Then append a new block at the END of `MainLayout.razor.css`. These rules depend on the `.nav-collapsed` class (on `.page`, MainLayout's own DOM) and reach into the `NavMenu` child via `::deep`:

```css
/* ── Collapsible sidebar (desktop icon rail) ─────────────── */
@media (min-width: 641px) {
    .nav-collapsed .sidebar { width: 64px; }

    .nav-collapsed ::deep .nav-label,
    .nav-collapsed ::deep .nav-section-label,
    .nav-collapsed ::deep .navbar-brand {
        display: none;
    }

    .nav-collapsed ::deep .nav-link {
        justify-content: center;
    }

    .nav-collapsed ::deep .nav-icon {
        margin-right: 0;
        font-size: 1.1rem;
    }

    .nav-collapsed ::deep .container-fluid {
        justify-content: center;
    }
}
```

(The toggle button's own appearance is styled in `NavMenu.razor.css` — Task 1 Step 3. These rules only handle the collapsed *state*.)

- [ ] **Step 4: Build**

Run: `dotnet build src/MyApp.Web/MyApp.Web.csproj` (add `-o "$TEMP/sb-verify"` if bin locked).
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Manual smoke (Playwright or browser)**

Run the app, log in:
1. Click the ☰ in the sidebar → sidebar narrows to a 64px rail; labels, section headers, and brand text hidden; icons centered; main content widens.
2. Click ☰ again → restores to 250px labeled menu.
3. Collapse, then reload the page → sidebar comes back collapsed (localStorage persisted).
4. Collapse, navigate Home → Product → state stays collapsed.

- [ ] **Step 6: Record progress** in `docs/superpowers/plans/collapsible-sidebar-progress.md`.

---

## Final verification (after both tasks)

- [ ] `dotnet build src/MyApp.Web/MyApp.Web.csproj` → 0 errors / 0 warnings.
- [ ] Toggle collapses to icon rail and restores; content reflows.
- [ ] Collapsed state persists across full reload and across in-app navigation.
- [ ] Mobile (`<641px`) unchanged — existing hamburger show/hide still works; rail rule does not apply.

## Out of scope

Hover flyout menus, server-side per-user preference, RTL, mobile changes.
