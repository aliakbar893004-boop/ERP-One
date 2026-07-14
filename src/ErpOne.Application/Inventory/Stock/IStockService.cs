using ErpOne.Application.Common;

namespace ErpOne.Application.Stock;

public interface IStockService
{
    Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default);
    Task<int> GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovementDto>> GetMovementsByVariantAsync(int variantId, CancellationToken ct = default);
    Task<PagedResult<StockLevelDto>> GetLevelsPagedAsync(
        int page, int pageSize, int? warehouseId, string? search,
        StockStatusFilter status = StockStatusFilter.All, CancellationToken ct = default);

    /// <summary>KPI Stock Levels: jumlah baris, total qty, low-stock, dan out-of-stock (opsional per gudang).</summary>
    Task<StockLevelSummary> GetLevelsSummaryAsync(int? warehouseId, CancellationToken ct = default);

    /// <summary>Opname: terapkan selisih bertanda per varian dalam satu transaksi.</summary>
    Task RecordAdjustmentAsync(StockAdjustmentRequest request, CancellationToken ct = default);

    /// <summary>Saldo awal: satu mutasi masuk (Adjustment/Opening) + recompute Moving Average.</summary>
    Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);
}
