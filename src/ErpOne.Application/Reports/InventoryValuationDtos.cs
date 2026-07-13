namespace ErpOne.Application.Reports;

public enum ValuationGroupBy { Category, Warehouse }

public record ValuationItemDto(
    int VariantId, string Sku, string ProductName, string GroupName, int Qty, decimal AvgCost, decimal Value);

public record ValuationGroupDto(
    string GroupName, IReadOnlyList<ValuationItemDto> Items, int TotalQty, decimal TotalValue);

public record ValuationResultDto(
    DateTime AsOf, ValuationGroupBy GroupBy, IReadOnlyList<ValuationGroupDto> Groups,
    int GrandTotalQty, decimal GrandTotalValue, int ItemCount);
