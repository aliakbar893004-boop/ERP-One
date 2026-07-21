namespace ErpOne.Web.Authorization;

public record AppAction(string Key, string Label, string Icon);
public record AppResource(string Key, string Label, string Icon, IReadOnlyList<AppAction> Actions);
public record ResourceGroup(string? GroupLabel, IReadOnlyList<AppResource> Resources);

public static class AppMenus
{
    public const string ClaimType = "permission";

    public static readonly AppAction ActIndex  = new("index",  "View",   "bi-eye-fill");
    public static readonly AppAction ActCreate = new("create", "Create", "bi-plus-circle-fill");
    public static readonly AppAction ActEdit   = new("edit",   "Edit",   "bi-pencil-fill");
    public static readonly AppAction ActDelete = new("delete", "Delete", "bi-trash3-fill");
    public static readonly AppAction ActApprove = new("approve", "Approve", "bi-check2-circle");
    public static readonly AppAction ActPost  = new("post",  "Post",  "bi-box-arrow-in-down");
    public static readonly AppAction ActClose = new("close", "Close", "bi-lock-fill");
    public static readonly AppAction ActVoid = new("void", "Void", "bi-x-octagon-fill");
    public static readonly AppAction ActExport = new("export", "Export", "bi-download");

    public static readonly IReadOnlyList<AppAction> AllActions =
        [ActIndex, ActCreate, ActEdit, ActDelete];

    private static AppAction[] CRUD     => [ActIndex, ActCreate, ActEdit, ActDelete];
    private static AppAction[] ViewOnly => [ActIndex];
    private static AppAction[] ViewCreate => [ActIndex, ActCreate];
    private static AppAction[] PurchaseOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActClose];
    private static AppAction[] GoodsReceiptActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
    private static AppAction[] DeliveryOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
    private static AppAction[] SalesOrderActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActClose];
    private static AppAction[] CashierShiftActions => [ActIndex, ActCreate, ActClose];
    private static AppAction[] PosActions => [ActIndex, ActCreate];
    private static AppAction[] SupplierPaymentActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActApprove, ActVoid];
    private static AppAction[] JournalEntryActions => [ActIndex, ActCreate, ActEdit, ActDelete, ActPost];
    private static AppAction[] ReportActions => [ActIndex, ActExport];

    public static readonly IReadOnlyList<ResourceGroup> Groups =
    [
        new(null,
        [
            new("dashboard", "Dashboard", "bi-speedometer2", ViewOnly),
            new("home", "Home", "bi-house-door-fill", ViewOnly),
        ]),
        new("Master",
        [
            new("master.products",   "Product",          "bi-box-seam-fill",      CRUD),
            new("master.categories", "Product Category", "bi-tag-fill",           CRUD),
            new("master.units",      "Unit",             "bi-rulers",             CRUD),
            new("master.brands",      "Brand",            "bi-bookmark-star-fill", CRUD),
            new("master.warehouses",  "Warehouse",        "bi-building-fill",      CRUD),
            new("master.taxes",       "Tax",              "bi-percent",            CRUD),
            new("master.payment-methods", "Payment Method", "bi-credit-card-2-front-fill", CRUD),
            new("master.attributes",  "Attribute",        "bi-sliders",            CRUD),
            new("master.suppliers",  "Supplier",  "bi-truck",         CRUD),
            new("master.customers",  "Customer",  "bi-person-vcard-fill", CRUD),
            new("master.currencies", "Currency",  "bi-currency-exchange", CRUD),
        ]),
        new("Inventory",
        [
            new("inventory.stock-levels", "Stock Levels",     "bi-boxes",                 ViewOnly),
            new("inventory.adjustments",  "Stock Adjustment", "bi-clipboard2-check-fill", ViewCreate),
            new("inventory.low-stock",    "Low Stock",        "bi-exclamation-triangle", ViewOnly),
        ]),
        new("Transaksi",
        [
            new("transactions.hub",             "Transaksi",      "bi-grid-1x2-fill",      ViewOnly),
            new("transactions.purchase-orders", "Purchase Order", "bi-cart-plus-fill",     PurchaseOrderActions),
            new("transactions.goods-receipts", "Goods Receipt", "bi-box-seam", GoodsReceiptActions),
            new("transactions.sales-orders",    "Sales Order",    "bi-bag-check-fill",     SalesOrderActions),
            new("transactions.delivery-orders", "Delivery Order", "bi-truck",              DeliveryOrderActions),
        ]),
        new("Kasir",
        [
            new("cashier.shifts", "Sesi Kasir", "bi-cash-stack", CashierShiftActions),
            new("cashier.pos", "Kasir (POS)", "bi-bag-check-fill", PosActions),
        ]),
        new("Finance",
        [
            new("finance.cash-bank", "Cash & Bank", "bi-bank", CRUD),
            new("finance.ap-invoices", "Supplier Invoices", "bi-receipt", CRUD),
            new("finance.ap-payments", "Supplier Payments", "bi-cash-coin", SupplierPaymentActions),
            new("finance.ar-invoices", "Customer Invoices", "bi-receipt-cutoff", CRUD),
            new("finance.ar-receipts", "Customer Receipts", "bi-cash-stack", [ActIndex, ActCreate, ActVoid]),
            new("finance.expense-categories", "Expense Categories", "bi-tags", CRUD),
            new("finance.expenses", "Expenses", "bi-wallet2", [ActIndex, ActCreate, ActVoid]),
            new("finance.chart-of-accounts", "Chart of Accounts", "bi-diagram-3", CRUD),
            new("finance.journal-entries", "Journal Entries", "bi-journal-plus", JournalEntryActions),
        ]),
        new("Reports",
        [
            new("reports.stock-ledger", "Stock Ledger", "bi-journal-text", ReportActions),
            new("reports.inventory-valuation", "Inventory Valuation", "bi-cash-stack", ReportActions),
            new("reports.sales", "Sales Report", "bi-graph-up-arrow", ReportActions),
            new("reports.purchases", "Purchase Report", "bi-cart-check", ReportActions),
            new("reports.gross-profit", "Gross Profit", "bi-coin", ReportActions),
            new("reports.ar-aging", "AR Aging", "bi-hourglass-split", ReportActions),
            new("reports.ap-aging", "AP Aging", "bi-hourglass-bottom", ReportActions),
            new("reports.cashier-shifts", "Cashier Shifts", "bi-cash-stack", ReportActions),
            new("reports.general-ledger", "General Ledger", "bi-journals", ReportActions),
            new("reports.trial-balance", "Trial Balance", "bi-list-columns-reverse", ReportActions),
            new("reports.balance-sheet", "Balance Sheet", "bi-clipboard-data", ReportActions),
            new("reports.income-statement", "Income Statement", "bi-graph-up", ReportActions),
        ]),
        new("Settings",
        [
            new("settings.users",    "User",      "bi-person-fill",               CRUD),
            new("settings.roles",           "Role",           "bi-shield-fill",               CRUD),
            new("settings.company",         "Company Profile", "bi-building-fill-gear",       [ActIndex, ActEdit]),
            new("settings.approval-chains", "Approval Chain", "bi-diagram-3-fill",            CRUD),
            new("settings.document-numbering", "Document Numbering", "bi-123",                [ActIndex, ActEdit]),
            new("settings.posting-config", "Posting Configuration", "bi-diagram-3-fill",       [ActIndex, ActEdit]),
            new("settings.errorlog",        "Error Log",      "bi-exclamation-triangle-fill", ViewOnly),
        ]),
    ];

    public static IEnumerable<AppResource> AllResources =>
        Groups.SelectMany(g => g.Resources);

    public static IReadOnlySet<string> AllPermissions { get; } =
        AllResources.SelectMany(r => r.Actions.Select(a => $"{r.Key}.{a.Key}"))
                    .ToHashSet();

    public static string Perm(string resource, string action) => $"{resource}.{action}";
}
