namespace ErpOne.Application.LowStock;

public interface ILowStockService
{
    Task<LowStockSummaryDto> GetLowStockAsync(int? warehouseId, CancellationToken ct = default);
}
