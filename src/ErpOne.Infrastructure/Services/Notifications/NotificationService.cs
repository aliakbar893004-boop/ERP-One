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
