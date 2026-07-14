namespace ErpOne.Application.CashierShifts;

public record ShiftMethodTotalDto(int PaymentMethodId, string MethodName, decimal TotalAmount, int TransactionCount);

public record CashierShiftListItemDto(
    int Id, string ShiftNumber, int WarehouseId, string WarehouseName, string CashierName,
    DateTime OpenedAt, DateTime? ClosedAt, decimal TotalSalesAmount, string Status);

public record CashierShiftDto(
    int Id, string ShiftNumber, int WarehouseId, string WarehouseName,
    string CashierUserId, string CashierName, string Status,
    DateTime OpenedAt, decimal OpeningFloat, decimal CashSalesTotal, decimal ExpectedCash,
    DateTime? ClosedAt, decimal? CountedCash, decimal? CashVariance, string? ClosingNote,
    decimal TotalSalesAmount, int TransactionCount,
    IReadOnlyList<ShiftMethodTotalDto> MethodTotals);

public record OpenShiftRequest(int WarehouseId, decimal OpeningFloat);

public record CloseShiftRequest(decimal CountedCash, string? ClosingNote);
