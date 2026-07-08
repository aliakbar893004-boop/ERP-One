using ErpOne.Domain.Entities;

namespace ErpOne.Application.Stock;

public record StockLevelDto(
    int VariantId, string Sku, string ProductName,
    int WarehouseId, string WarehouseName,
    int Quantity, decimal CostPrice);

/// <summary>Ringkasan KPI untuk halaman Stock Levels (mengikuti filter gudang).</summary>
public record StockLevelSummary(int Records, int TotalQty, int LowStock, int OutOfStock);

public record StockMovementDto(
    int Id, int VariantId, int WarehouseId, string WarehouseName,
    MovementType Type, int Quantity, decimal UnitCost,
    DateTime MovementDate, string? RefType, string? Note);

/// <summary>Satu baris opname: selisih qty (bertanda) untuk satu varian.</summary>
public record StockAdjustmentLine(int VariantId, int DeltaQuantity, decimal UnitCost, string? Reason);

public record StockAdjustmentRequest(
    int WarehouseId, DateTime Date, string? Note,
    IReadOnlyList<StockAdjustmentLine> Lines);
