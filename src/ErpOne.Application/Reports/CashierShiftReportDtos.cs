namespace ErpOne.Application.Reports;

public record ShiftMethodDto(int PaymentMethodId, string PaymentMethodName, decimal Amount, int TransactionCount);

public record ShiftRowDto(
    int ShiftId, string ShiftNumber, int WarehouseId, string WarehouseName,
    DateTime OpenedAt, DateTime? ClosedAt,
    decimal OpeningFloat, decimal CashSales, decimal TotalSales, int TransactionCount,
    decimal ExpectedCash, decimal? CountedCash, decimal? CashVariance,
    IReadOnlyList<ShiftMethodDto> Methods);

public record ShiftCashierDto(
    string CashierUserId, string CashierName, IReadOnlyList<ShiftRowDto> Shifts,
    decimal TotalSales, int TransactionCount, decimal TotalVariance);

public record ShiftReportResultDto(
    DateTime From, DateTime To, IReadOnlyList<ShiftCashierDto> Cashiers,
    decimal GrandTotalSales, int GrandTransactionCount, decimal GrandVariance,
    int ShiftCount, int CashierCount);

public record CashierOptionDto(string UserId, string Name);
