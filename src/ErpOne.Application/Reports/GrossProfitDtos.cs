namespace ErpOne.Application.Reports;

public enum GrossProfitGroupBy { Product, Category, Month }

public record GrossProfitFilter(DateTime? From, DateTime? To, string? Channel, GrossProfitGroupBy GroupBy);

public record GrossProfitGroupDto(
    string GroupName, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent);

public record GrossProfitResultDto(
    GrossProfitGroupBy GroupBy, IReadOnlyList<GrossProfitGroupDto> Groups,
    int TotalQty, decimal TotalRevenue, decimal TotalCogs, decimal TotalGrossProfit, decimal TotalMarginPercent);
