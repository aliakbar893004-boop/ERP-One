# Notifikasi In-App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A top-bar bell that shows a live count of actionable items for the current user — approvals pending for their roles, low stock, and due AR/AP invoices — computed on demand with no persistence, each group linking to its page. Role/permission-gated.

**Architecture:** New `INotificationService.GetForUserAsync(userName, roles, hasPermission, asOf)` computes grouped counts from current state (`ApprovalSteps`, `ILowStockService`, `CustomerInvoices`/`SupplierInvoices`). A static-rendered `NotificationBell` component in `MainLayout` resolves the user's roles + permission claims from the auth state, calls the service, and renders a Bootstrap dropdown of groups. No tables, events, or hooks.

**Tech Stack:** .NET 10, EF Core (SQLite in-memory per test class), xUnit, Blazor Server (static-rendered layout + Bootstrap dropdown), FluentValidation (n/a here).

## Global Constraints

- **Computed/derived, live count, no persistence** — no new entities/migrations. Item disappears when its condition clears.
- **Application layer never touches `ClaimsPrincipal`** — the service takes `string userName`, `IReadOnlyCollection<string> roles`, and `Func<string,bool> hasPermission` (mirrors `IApprovalService`'s `isInRole`).
- **Permission claim check:** permissions live on the principal as claims of type `AppMenus.ClaimType` (`"permission"`) — confirmed by `PermissionAuthorizationHandler` (`context.User.HasClaim(AppMenus.ClaimType, requirement.Permission)`). So the web layer passes `hasPermission = p => user.HasClaim(AppMenus.ClaimType, p)`.
- **Approval targeting:** only the **active** pending step counts (lowest `StepOrder` still `Pending` for a document), and only if its `RoleName ∈ roles` AND `hasPermission("{resource}.approve")`. No creator-exclusion in v1 (approve action already enforces it; documented limitation).
- **Due window:** `DueSoonDays = 7`; invoice counts if `Outstanding > 0`, not Cancelled, `DueDate ≤ asOf + 7` (includes overdue). `Outstanding = GrandTotal − PaidAmount − CreditedAmount`.
- **Gating:** low-stock group needs `inventory.low-stock.index`; AR `reports.ar-aging.index`; AP `reports.ap-aging.index`.
- **`TotalCount = Σ Groups.Count`.**
- **Bell is static-rendered** (no `@rendermode`), fetching in `OnInitializedAsync`; runs once per page render. Links are plain `<a href>`; popover is a Bootstrap dropdown (no interactivity). Only in `MainLayout`, not `PosLayout`.

---

### Task 1: Application — DTOs + interface

**Files:**
- Create: `src/ErpOne.Application/Notifications/NotificationDtos.cs`
- Create: `src/ErpOne.Application/Notifications/INotificationService.cs`

**Interfaces:**
- Produces: `NotificationGroupDto`, `NotificationSummaryDto`, `INotificationService`.

- [ ] **Step 1: Create DTOs**

```csharp
// src/ErpOne.Application/Notifications/NotificationDtos.cs
namespace ErpOne.Application.Notifications;

/// <summary>One actionable group shown in the notification popover.</summary>
public record NotificationGroupDto(string Key, string Label, string Icon, int Count, string Url, string Severity);

public record NotificationSummaryDto(int TotalCount, IReadOnlyList<NotificationGroupDto> Groups);
```

- [ ] **Step 2: Create the interface**

```csharp
// src/ErpOne.Application/Notifications/INotificationService.cs
namespace ErpOne.Application.Notifications;

public interface INotificationService
{
    /// <summary>Compute actionable notifications for a user. roles = the user's role names;
    /// hasPermission(permKey) gates non-approval groups; asOf drives the due-soon window.</summary>
    Task<NotificationSummaryDto> GetForUserAsync(string userName, IReadOnlyCollection<string> roles,
        Func<string, bool> hasPermission, DateTime asOf, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ErpOne.Application -clp:ErrorsOnly`
Expected: 0 errors/0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/ErpOne.Application/Notifications/
git commit -m "feat(notifications): DTOs + INotificationService interface"
```

---

### Task 2: Infrastructure — `NotificationService` + DI + tests

**Files:**
- Create: `src/ErpOne.Infrastructure/Services/Notifications/NotificationService.cs`
- Modify: `src/ErpOne.Infrastructure/DependencyInjection.cs` (+ `using ErpOne.Application.Notifications;` + registration)
- Test: `tests/ErpOne.IntegrationTests/NotificationServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `ILowStockService`, `ApprovalStep`/`ApprovalStepStatus`, `ApprovalDocumentType`, `CustomerInvoice`/`SupplierInvoice`.
- Produces: `NotificationService : INotificationService`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
// tests/ErpOne.IntegrationTests/NotificationServiceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Notifications;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class NotificationServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public NotificationServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static Func<string, bool> AllPerms => _ => true;
    private static Func<string, bool> NoPerms => _ => false;

    [Fact]
    public async Task Approval_group_shows_for_matching_role_only()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Seed a pending PO approval step at role "Managers", document id unique.
        var docId = 900000 + Guid.NewGuid().GetHashCode() % 1000;
        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.PurchaseOrder, Math.Abs(docId), 1, "Managers"));
        await db.SaveChangesAsync();

        var forManager = await svc.GetForUserAsync("u1", ["Managers"], AllPerms, DateTime.Today);
        Assert.Contains(forManager.Groups, g => g.Key == "approval:PurchaseOrder" && g.Count >= 1);

        var forOther = await svc.GetForUserAsync("u1", ["Cashiers"], AllPerms, DateTime.Today);
        Assert.DoesNotContain(forOther.Groups, g => g.Key == "approval:PurchaseOrder");
    }

    [Fact]
    public async Task Approval_group_gated_by_approve_permission()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.PurchaseOrder, 950001, 1, "Managers"));
        await db.SaveChangesAsync();

        var gated = await svc.GetForUserAsync("u1", ["Managers"], NoPerms, DateTime.Today);
        Assert.DoesNotContain(gated.Groups, g => g.Key == "approval:PurchaseOrder");
    }

    [Fact]
    public async Task Non_approval_groups_gated_by_permission()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var none = await svc.GetForUserAsync("u1", [], NoPerms, DateTime.Today);
        Assert.DoesNotContain(none.Groups, g => g.Key is "low-stock" or "ar-due" or "ap-due");
    }

    [Fact]
    public async Task Ar_due_counts_only_within_window_and_unpaid()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var custId = await SeedCustomerAsync(db);
        // Due in 3 days, unpaid -> counts. Due in 60 days -> not. Paid -> not.
        db.CustomerInvoices.Add(NewCustomerInvoice(custId, DateTime.Today.AddDays(3), 1000m));
        db.CustomerInvoices.Add(NewCustomerInvoice(custId, DateTime.Today.AddDays(60), 1000m));
        var paid = NewCustomerInvoice(custId, DateTime.Today.AddDays(2), 1000m); paid.ApplyPayment(1000m);
        db.CustomerInvoices.Add(paid);
        await db.SaveChangesAsync();

        var res = await svc.GetForUserAsync("u1", [], p => p == "reports.ar-aging.index", DateTime.Today);
        var arGroup = res.Groups.Single(g => g.Key == "ar-due");
        Assert.Equal(1, arGroup.Count);
    }

    [Fact]
    public async Task TotalCount_is_sum_of_groups()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        db.ApprovalSteps.Add(new ApprovalStep(ApprovalDocumentType.SalesOrder, 970001, 1, "Managers"));
        await db.SaveChangesAsync();
        var res = await svc.GetForUserAsync("u1", ["Managers"], p => p.EndsWith(".approve"), DateTime.Today);
        Assert.Equal(res.Groups.Sum(g => g.Count), res.TotalCount);
    }

    // --- seed helpers: confirm ctor shapes against the entities ---
    private static async Task<int> SeedCustomerAsync(AppDbContext db)
    {
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cust = new Customer($"CU{id}", $"PT {id}", null, null, null, null, null, 30, "IDR", 0m, true);
        db.Customers.Add(cust);
        await db.SaveChangesAsync();
        return cust.Id;
    }

    private static CustomerInvoice NewCustomerInvoice(int customerId, DateTime dueDate, decimal amount)
    {
        var inv = new CustomerInvoice($"CINV-{Guid.NewGuid():N}"[..14], customerId, "IDR",
            DateTime.Today, dueDate, null, null);
        inv.SetLines([new CustomerInvoiceLine(1, 1, 1, 1, amount, 0m, 0m)]);
        return inv;
    }
}
```

> **Verify-before-embed:** `Customer` ctor (11 args, from DeliveryOrderServiceTests), `CustomerInvoice`/`CustomerInvoiceLine` ctors (confirmed Task earlier), `ApprovalStep` ctor `(docType, docId, stepOrder, roleName)` (confirmed), `ApprovalStepStatus.Pending`. DueDate must be `≥ InvoiceDate` (ctor guard) — the paid one uses AddDays(2) ≥ today OK. Group `Key` strings must match the service.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ErpOne.IntegrationTests --filter NotificationServiceTests`
Expected: FAIL — no registered `INotificationService`.

- [ ] **Step 3: Implement `NotificationService`**

```csharp
// src/ErpOne.Infrastructure/Services/Notifications/NotificationService.cs
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.LowStock;
using ErpOne.Application.Notifications;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class NotificationService(AppDbContext db, ILowStockService lowStock) : INotificationService
{
    private const int DueSoonDays = 7;

    // ApprovalDocumentType -> display + route + approve-permission. Types without a clear index are omitted.
    private static readonly (ApprovalDocumentType Type, string Label, string Icon, string Url, string Perm)[] ApprovalMap =
    [
        (ApprovalDocumentType.PurchaseOrder,   "Purchase Order",   "bi-cart-plus",         "/transactions/purchase-orders",   "transactions.purchase-orders.approve"),
        (ApprovalDocumentType.SalesOrder,      "Sales Order",      "bi-bag-check",         "/transactions/sales-orders",      "transactions.sales-orders.approve"),
        (ApprovalDocumentType.SupplierPayment, "Supplier Payment", "bi-cash-coin",         "/finance/ap-payments",            "finance.ap-payments.approve"),
        (ApprovalDocumentType.StockTransfer,   "Stock Transfer",   "bi-arrow-left-right",  "/inventory/transfers",            "inventory.transfers.approve"),
        (ApprovalDocumentType.StockOpname,     "Stock Opname",     "bi-clipboard-data",    "/inventory/stock-opname",         "inventory.stock-opname.approve"),
        (ApprovalDocumentType.PurchaseReturn,  "Purchase Return",  "bi-arrow-return-left", "/transactions/purchase-returns",  "transactions.purchase-returns.approve"),
        (ApprovalDocumentType.SalesReturn,     "Sales Return",     "bi-arrow-return-right","/transactions/sales-returns",     "transactions.sales-returns.approve"),
    ];

    public async Task<NotificationSummaryDto> GetForUserAsync(string userName, IReadOnlyCollection<string> roles,
        Func<string, bool> hasPermission, DateTime asOf, CancellationToken ct = default)
    {
        var groups = new List<NotificationGroupDto>();

        // 1) Approvals: active (lowest StepOrder still Pending) step per document, role- and permission-gated.
        var pending = await db.ApprovalSteps.AsNoTracking()
            .Where(s => s.Status == ApprovalStepStatus.Pending)
            .Select(s => new { s.DocumentType, s.DocumentId, s.StepOrder, s.RoleName })
            .ToListAsync(ct);
        var activeByType = pending
            .GroupBy(s => new { s.DocumentType, s.DocumentId })
            .Select(g => g.OrderBy(x => x.StepOrder).First())          // active step per document
            .Where(a => roles.Contains(a.RoleName))
            .GroupBy(a => a.DocumentType)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var m in ApprovalMap)
        {
            if (!activeByType.TryGetValue(m.Type, out var count) || count <= 0) continue;
            if (!hasPermission(m.Perm)) continue;
            groups.Add(new NotificationGroupDto($"approval:{m.Type}", $"{m.Label} awaiting approval",
                m.Icon, count, m.Url, "info"));
        }

        // 2) Low stock.
        if (hasPermission("inventory.low-stock.index"))
        {
            var ls = await lowStock.GetLowStockAsync(null, ct);
            var count = ls.LowCount + ls.OutOfStockCount;
            if (count > 0)
                groups.Add(new NotificationGroupDto("low-stock", "Items low on stock", "bi-exclamation-triangle",
                    count, "/inventory/low-stock", "warn"));
        }

        // 3) Due AR / AP invoices (within window, incl. overdue).
        var cutoff = asOf.Date.AddDays(DueSoonDays);
        if (hasPermission("reports.ar-aging.index"))
        {
            var count = await db.CustomerInvoices.AsNoTracking()
                .CountAsync(i => i.Status != CustomerInvoiceStatus.Cancelled
                    && (i.GrandTotal - i.PaidAmount - i.CreditedAmount) > 0
                    && i.DueDate <= cutoff, ct);
            if (count > 0)
                groups.Add(new NotificationGroupDto("ar-due", "Receivables due soon", "bi-hourglass-split",
                    count, "/reports/ar-aging", "danger"));
        }
        if (hasPermission("reports.ap-aging.index"))
        {
            var count = await db.SupplierInvoices.AsNoTracking()
                .CountAsync(i => i.Status != SupplierInvoiceStatus.Cancelled
                    && (i.GrandTotal - i.PaidAmount - i.CreditedAmount) > 0
                    && i.DueDate <= cutoff, ct);
            if (count > 0)
                groups.Add(new NotificationGroupDto("ap-due", "Payables due soon", "bi-hourglass-bottom",
                    count, "/reports/ap-aging", "danger"));
        }

        return new NotificationSummaryDto(groups.Sum(g => g.Count), groups);
    }
}
```

> **Verify-before-embed:** the approval index routes (`/transactions/purchase-orders`, `/transactions/sales-orders`, `/finance/ap-payments`, `/inventory/transfers`, `/inventory/stock-opname`, `/transactions/purchase-returns`, `/transactions/sales-returns`) and permission keys — confirm each against the page `@page` + `AppMenus` resource key; drop any that don't resolve. `i.DueDate <= cutoff` — if EF/SQLite complains about `DateTime` comparison, keep `cutoff` a `DateTime` (date at 00:00); due-on-cutoff-day rows are included as intended.

- [ ] **Step 4: Register DI**

In `DependencyInjection.cs`: add `using ErpOne.Application.Notifications;` and, near the other scoped services, `services.AddScoped<INotificationService, NotificationService>();`.

- [ ] **Step 5: Run tests**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test tests/ErpOne.IntegrationTests --filter NotificationServiceTests`
Expected: PASS (5).

- [ ] **Step 6: Commit**

```bash
git add src/ErpOne.Infrastructure/Services/Notifications/NotificationService.cs src/ErpOne.Infrastructure/DependencyInjection.cs tests/ErpOne.IntegrationTests/NotificationServiceTests.cs
git commit -m "feat(notifications): NotificationService (approvals, low stock, due AR/AP)"
```

---

### Task 3: Web — `NotificationBell` component + `MainLayout`

**Files:**
- Create: `src/ErpOne.Web/Components/Layout/NotificationBell.razor`
- Modify: `src/ErpOne.Web/Components/Layout/MainLayout.razor` (add `<NotificationBell />` in the top row)
- Modify: `src/ErpOne.Web/wwwroot/app.css` (or the existing top-bar stylesheet) — small `.notif-*` styles (optional; may reuse Bootstrap `.dropdown` + `.badge`)

**Interfaces:**
- Consumes: `INotificationService`, `AppMenus.ClaimType`.

- [ ] **Step 1: Create `NotificationBell.razor`**

```razor
@* src/ErpOne.Web/Components/Layout/NotificationBell.razor *@
@using System.Security.Claims
@using ErpOne.Application.Notifications
@using ErpOne.Web.Authorization
@inject INotificationService Notifications

<div class="dropdown">
    <button type="button" class="notif-toggle" data-bs-toggle="dropdown" data-bs-auto-close="outside"
            aria-expanded="false" title="Notifikasi" aria-label="Notifikasi">
        <i class="bi bi-bell" aria-hidden="true"></i>
        @if (_summary is { TotalCount: > 0 })
        {
            <span class="notif-badge">@(_summary.TotalCount > 99 ? "99+" : _summary.TotalCount.ToString())</span>
        }
    </button>
    <div class="dropdown-menu dropdown-menu-end notif-menu">
        <div class="notif-head">Notifikasi</div>
        @if (_summary is null || _summary.Groups.Count == 0)
        {
            <div class="notif-empty"><i class="bi bi-check2-circle"></i> Tidak ada notifikasi</div>
        }
        else
        {
            @foreach (var g in _summary.Groups)
            {
                <a class="notif-item notif-@g.Severity" href="@g.Url">
                    <span class="notif-ic"><i class="bi @g.Icon"></i></span>
                    <span class="notif-label">@g.Label</span>
                    <span class="notif-count">@g.Count</span>
                </a>
            }
        }
    </div>
</div>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;
    private NotificationSummaryDto? _summary;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        if (user.Identity?.IsAuthenticated != true) return;
        var userName = user.Identity.Name ?? "";
        var roleClaimType = (user.Identity as ClaimsIdentity)?.RoleClaimType ?? ClaimTypes.Role;
        var roles = user.FindAll(roleClaimType).Select(c => c.Value).ToList();
        bool HasPerm(string p) => user.HasClaim(AppMenus.ClaimType, p);
        _summary = await Notifications.GetForUserAsync(userName, roles, HasPerm, DateTime.Today);
    }
}
```

> **Verify-before-embed:** confirm the layout is server-rendered so `OnInitializedAsync` + DI + `AuthStateTask` resolve (they do in static SSR). If the app requires an explicit render mode for cascading auth state, add `@rendermode InteractiveServer` to the component (test by loading a page and confirming the bell renders a count). Confirm `AppMenus.ClaimType` is accessible (public const = "permission").

- [ ] **Step 2: Add the bell to `MainLayout`**

In `MainLayout.razor`, inside `<div class="top-row px-4">`, before the appearance `<div class="dropdown">` (so the bell sits left of the appearance/user menus), add:

```razor
            <AuthorizeView>
                <Authorized>
                    <NotificationBell />
                </Authorized>
            </AuthorizeView>
```

- [ ] **Step 3: Add minimal styles**

Append to the top-bar stylesheet (mirror `.appearance-toggle`/`.appearance-menu` conventions). Confirm the actual CSS file used by the top row (search for `.appearance-toggle`):

```css
.notif-toggle { position: relative; background: none; border: 0; font-size: 1.15rem; color: inherit; padding: .4rem; border-radius: .5rem; cursor: pointer; }
.notif-toggle:hover { background: rgba(0,0,0,.06); }
.notif-badge { position: absolute; top: .1rem; right: .05rem; min-width: 1.05rem; height: 1.05rem; padding: 0 .25rem; border-radius: 999px; background: #e11d48; color: #fff; font-size: .68rem; line-height: 1.05rem; text-align: center; font-weight: 600; }
.notif-menu { width: 320px; max-height: 70vh; overflow-y: auto; padding: .35rem; }
.notif-head { font-weight: 600; padding: .4rem .55rem; }
.notif-empty { padding: 1rem .55rem; color: var(--bs-secondary-color, #6c757d); text-align: center; }
.notif-item { display: flex; align-items: center; gap: .6rem; padding: .55rem; border-radius: .5rem; text-decoration: none; color: inherit; }
.notif-item:hover { background: rgba(0,0,0,.05); }
.notif-ic { width: 1.8rem; height: 1.8rem; display: grid; place-items: center; border-radius: .5rem; background: rgba(14,159,110,.12); color: #0e9f6e; }
.notif-warn .notif-ic { background: rgba(217,119,6,.14); color: #d97706; }
.notif-danger .notif-ic { background: rgba(225,29,72,.14); color: #e11d48; }
.notif-label { flex: 1; font-size: .9rem; }
.notif-count { font-weight: 600; font-variant-numeric: tabular-nums; }
```

- [ ] **Step 4: Build**

Run: `dotnet build -clp:ErrorsOnly`
Expected: 0 errors/0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/ErpOne.Web/Components/Layout/NotificationBell.razor src/ErpOne.Web/Components/Layout/MainLayout.razor src/ErpOne.Web/wwwroot/
git commit -m "feat(notifications): top-bar notification bell in MainLayout"
```

---

### Task 4: Final regression

- [ ] **Step 1: Full build + test**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: 0 errors/0 warnings; all green. Baseline (386) + Task-2 (5) ≈ **391+**.

- [ ] **Step 2: Straggler grep**

Run: `git grep -n "INotificationService\|NotificationBell" -- src`
Expected: confined to the new Application/Infrastructure files, DI registration, and the layout.

- [ ] **Step 3: Final commit (if fixes)**

```bash
git add -A
git commit -m "chore(notifications): in-app notifications complete"
```

---

## Self-Review (author checklist — completed)

**Spec coverage:** §1 three sources (approvals role+perm-gated active-step, low stock, AR/AP due window) → Task 2 ✓; §2 Application DTOs+interface with delegate pattern → Task 1 ✓; §3 Infrastructure service + DI → Task 2 ✓; §4 web bell in MainLayout, role/perm resolution from auth state → Task 3 ✓; §5 tests (role match, perm gating, due window, total) → Task 2 ✓. Non-goals respected: no entities/migrations/persistence.

**Decisions locked:** live count (no read-state); no creator-exclusion in v1 (approve action still enforces it); `DueSoonDays=7`; permission claim check via `user.HasClaim(AppMenus.ClaimType, perm)` matching `PermissionAuthorizationHandler`; approval active-step = lowest Pending StepOrder.

**Type consistency:** `INotificationService.GetForUserAsync` signature identical Tasks 1↔2↔3. Group `Key` strings (`approval:{Type}`, `low-stock`, `ar-due`, `ap-due`) consistent between service and tests. `NotificationGroupDto`/`NotificationSummaryDto` shared.

**Verify-before-embed flags:** approval index routes + permission keys per doc type (drop unresolved); `Customer`/`CustomerInvoice`/`ApprovalStep` ctor shapes in the test seed; EF `DueDate <= cutoff` translation; whether the bell needs `@rendermode InteractiveServer` for auth-state/DI in this app's layout; the exact top-bar CSS file.
