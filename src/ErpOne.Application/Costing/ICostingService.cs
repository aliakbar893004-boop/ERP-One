namespace ErpOne.Application.Costing;

public interface ICostingService
{
    /// <summary>Dipanggil SETELAH UpsertStockAsync. Perbarui basis biaya varian dari mutasi masuk.</summary>
    Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default);

    /// <summary>Unit cost untuk pengeluaran (caller mengalikan dengan qty sendiri).</summary>
    Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default);
}
