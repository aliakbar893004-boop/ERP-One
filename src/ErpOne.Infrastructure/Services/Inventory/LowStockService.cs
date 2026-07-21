using Microsoft.EntityFrameworkCore;
using ErpOne.Application.LowStock;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class LowStockService(AppDbContext db) : ILowStockService
{
    public async Task<LowStockSummaryDto> GetLowStockAsync(int? warehouseId, CancellationToken ct = default)
    {
        var q =
            from ps in db.ProductStocks.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on ps.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on ps.WarehouseId equals w.Id
            where v.ReorderLevel > 0 && ps.Quantity <= v.ReorderLevel
            select new { v.Id, v.Sku, ProductId = p.Id, ProductName = p.Name, WarehouseId = w.Id, WarehouseName = w.Name,
                         ps.Quantity, v.ReorderLevel, v.ReorderQty };

        if (warehouseId is int wid) q = q.Where(x => x.WarehouseId == wid);

        var raw = await q.ToListAsync(ct);

        var rows = raw
            .Select(x => new LowStockRowDto(
                x.Id, x.Sku, x.ProductId, x.ProductName, x.WarehouseId, x.WarehouseName,
                x.Quantity, x.ReorderLevel, x.ReorderQty,
                x.ReorderQty > 0 ? x.ReorderQty : Math.Max(x.ReorderLevel - x.Quantity, 0),
                x.Quantity == 0))
            .OrderByDescending(r => r.IsOutOfStock)
            .ThenBy(r => r.Quantity - r.ReorderLevel)
            .ThenBy(r => r.Sku)
            .ToList();

        return new LowStockSummaryDto(rows, rows.Count, rows.Count(r => r.IsOutOfStock));
    }
}
