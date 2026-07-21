using Microsoft.EntityFrameworkCore;

namespace ErpOne.Infrastructure.Persistence;

public static class StockReadExtensions
{
    /// <summary>Total on-hand varian di SEMUA gudang, memperhitungkan baris ProductStock yang sedang
    /// dilacak (Local, belum di-flush) agar konsisten dgn UpsertStockAsync. Baris Local dihitung penuh;
    /// baris DB pada (varian,gudang) yang sudah dilacak dikecualikan agar tidak dobel.</summary>
    public static async Task<int> TotalOnHandLocalAwareAsync(
        this AppDbContext db, int variantId, CancellationToken ct = default)
    {
        var local = db.ProductStocks.Local
            .Where(s => s.ProductVariantId == variantId)
            .ToList();
        var trackedWarehouses = local.Select(s => s.WarehouseId).Distinct().ToList();
        var localSum = local.Sum(s => s.Quantity);

        var dbSum = await db.ProductStocks
            .Where(s => s.ProductVariantId == variantId && !trackedWarehouses.Contains(s.WarehouseId))
            .SumAsync(s => (int?)s.Quantity, ct) ?? 0;

        return localSum + dbSum;
    }
}
