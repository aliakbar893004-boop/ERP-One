using ErpOne.Application.Products;

namespace ErpOne.Application.Dashboard;

public record OperationalDashboardDto(
    DashboardKpis Kpis,
    PendingApprovalsDto Pending,
    AgingBuckets ArAging,
    AgingBuckets ApAging,
    ProductDashboardDto Stock);

public record DashboardKpis(
    decimal TodayRevenue,
    int TodayTxnCount,
    decimal ArDue,
    decimal ApDue,
    decimal YesterdayRevenue,
    int YesterdayTxnCount,
    IReadOnlyList<decimal> RevenueTrend,   // last 7 days, oldest → today
    IReadOnlyList<int> TxnTrend,           // last 7 days, oldest → today
    decimal MonthRevenue,                  // month-to-date
    int MonthTxnCount);                    // month-to-date

public record PendingApprovalsDto(
    int PoPendingCount, IReadOnlyList<PendingDocRow> PoPending,
    int SoPendingCount, IReadOnlyList<PendingDocRow> SoPending);

public record PendingDocRow(int Id, string Number, string Party, decimal Total, DateTime Date);

public record AgingBuckets(
    decimal Current,
    decimal D31_60,
    decimal D61_90,
    decimal D90Plus,
    decimal Total);
