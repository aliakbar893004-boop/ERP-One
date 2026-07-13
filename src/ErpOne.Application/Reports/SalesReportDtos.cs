namespace ErpOne.Application.Reports;

public record SalesFilter(
    DateTime? From, DateTime? To, string? Channel, int? WarehouseId,
    int? CustomerId, string? CashierUserId, string? Search);

public record SalesFactRow(
    DateTime Date, string Channel, string DocNumber,
    int WarehouseId, string WarehouseName,
    int VariantId, string Sku, string ProductName, int? CategoryId,
    string Party, int Quantity, decimal Revenue, decimal Cogs)
{
    public decimal GrossProfit => Revenue - Cogs;
}

public record SalesSummaryDto(
    int Lines, int Qty, decimal Revenue, decimal Cogs, decimal GrossProfit, decimal MarginPercent);
