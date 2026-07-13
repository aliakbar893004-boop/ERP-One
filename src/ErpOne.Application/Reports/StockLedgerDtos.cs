using ErpOne.Domain.Entities;

namespace ErpOne.Application.Reports;

public record StockLedgerFilter(
    string? Search, int? WarehouseId, MovementType? Type, DateTime? From, DateTime? To);

public record StockMovementRowDto(
    int Id, DateTime MovementDate, int VariantId, string Sku, string ProductName,
    int WarehouseId, string WarehouseName, MovementType Type, int Quantity, decimal UnitCost,
    string? RefType, int? RefId);

public record StockLedgerSummaryDto(int Records, int TotalIn, int TotalOut, int NetChange);

public record StockCardLineDto(
    int Id, DateTime MovementDate, MovementType Type, int Quantity, decimal UnitCost,
    int RunningQty, decimal RunningValue, string? RefType, int? RefId);

public record StockCardDto(
    int VariantId, string Sku, string ProductName, int? WarehouseId, string WarehouseName,
    DateTime From, DateTime To, int OpeningQty, decimal OpeningValue,
    int ClosingQty, decimal ClosingValue, IReadOnlyList<StockCardLineDto> Lines);
