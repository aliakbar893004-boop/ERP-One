using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CostingService(AppDbContext db, ICostingSettingService settings) : ICostingService
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public async Task OnInboundAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)
    {
        var method = await settings.GetMethodAsync(ct);
        switch (method)
        {
            case CostingMethod.MovingAverage:
                if (quantity <= 0) return;
                var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                    ?? throw new InvalidOperationException($"Variant {variantId} not found.");
                var totalAfter = await db.TotalOnHandLocalAwareAsync(variantId, ct);
                var totalBefore = totalAfter - quantity;
                variant.ApplyMovingAverage(totalBefore, quantity, unitCost);
                return;
            case CostingMethod.StandardCost:
                return; // biaya standar tetap; mutasi masuk tak mengubah CostPrice
            case CostingMethod.AveragePerWarehouse:
                if (quantity <= 0) return;
                var row = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
                    ?? await db.ProductStocks.FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct)
                    ?? throw new InvalidOperationException($"ProductStock ({variantId},{warehouseId}) not found — call after UpsertStockAsync.");
                var rowQtyBefore = row.Quantity - quantity;
                var newRowCost = rowQtyBefore <= 0
                    ? unitCost
                    : Round((rowQtyBefore * row.CostPrice + quantity * unitCost) / (rowQtyBefore + quantity));
                row.SetCost(newRowCost);

                var variantH = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                    ?? throw new InvalidOperationException($"Variant {variantId} not found.");
                var (wQty, wVal) = await WeightedCostAsync(variantId, ct);
                variantH.SetHeadlineCost(wQty <= 0 ? newRowCost : wVal / wQty);
                return;
            case CostingMethod.Fifo:
                if (quantity <= 0) return;
                db.CostLayers.Add(new CostLayer(variantId, warehouseId, unitCost, quantity));
                await RefreshFifoDisplayAsync(variantId, warehouseId, ct);
                return;
            default:
                throw new NotSupportedException($"Costing method {method} is not supported.");
        }
    }

    public async Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default)
    {
        var method = await settings.GetMethodAsync(ct);
        return method switch
        {
            CostingMethod.MovingAverage => await CurrentCostPriceAsync(variantId, ct),
            CostingMethod.StandardCost => await CurrentCostPriceAsync(variantId, ct),
            CostingMethod.AveragePerWarehouse => await PerWarehouseCostAsync(variantId, warehouseId, ct),
            CostingMethod.Fifo => await ConsumeFifoAndRefreshAsync(variantId, warehouseId, quantity, ct),
            _ => throw new NotSupportedException($"Costing method {method} is not supported.")
        };
    }

    // Membaca CostPrice dari entitas yang dilacak bila ada (agar melihat perubahan MA yang belum di-flush),
    // jika tidak, dari DB. Untuk MA, warehouseId & quantity diabaikan.
    private async Task<decimal> CurrentCostPriceAsync(int variantId, CancellationToken ct)
    {
        var tracked = db.ProductVariants.Local.FirstOrDefault(v => v.Id == variantId);
        if (tracked is not null) return tracked.CostPrice;
        return await db.ProductVariants.AsNoTracking()
            .Where(v => v.Id == variantId).Select(v => v.CostPrice).FirstOrDefaultAsync(ct);
    }

    // Local-aware sum of (qty) and (qty*cost) across all warehouse rows of a variant.
    private async Task<(int qty, decimal value)> WeightedCostAsync(int variantId, CancellationToken ct)
    {
        var local = db.ProductStocks.Local.Where(s => s.ProductVariantId == variantId).ToList();
        var trackedWh = local.Select(s => s.WarehouseId).Distinct().ToList();
        var qty = local.Sum(s => s.Quantity);
        var value = local.Sum(s => s.Quantity * s.CostPrice);
        var dbRows = await db.ProductStocks
            .Where(s => s.ProductVariantId == variantId && !trackedWh.Contains(s.WarehouseId))
            .Select(s => new { s.Quantity, s.CostPrice }).ToListAsync(ct);
        qty += dbRows.Sum(s => s.Quantity);
        value += dbRows.Sum(s => s.Quantity * s.CostPrice);
        return (qty, value);
    }

    // Per-warehouse cost for outbound; falls back to headline when the warehouse has no cost yet.
    private async Task<decimal> PerWarehouseCostAsync(int variantId, int warehouseId, CancellationToken ct)
    {
        var local = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId);
        if (local is not null)
            return local.CostPrice != 0m || local.Quantity != 0 ? local.CostPrice : await CurrentCostPriceAsync(variantId, ct);
        var dbCost = await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            .Select(s => (decimal?)s.CostPrice).FirstOrDefaultAsync(ct);
        return dbCost is decimal c && c != 0m ? c : await CurrentCostPriceAsync(variantId, ct);
    }

    // Outbound for FIFO MUTATES: consumes oldest layers (Id asc) then refreshes display cost.
    private async Task<decimal> ConsumeFifoAndRefreshAsync(int variantId, int warehouseId, int quantity, CancellationToken ct)
    {
        var unit = await ConsumeFifoAsync(variantId, warehouseId, quantity, ct);
        await RefreshFifoDisplayAsync(variantId, warehouseId, ct);
        return unit;
    }

    // Consume oldest-first across layers of (variant,warehouse). Local-aware: LoadAsync attaches persisted
    // layers to the context WITHOUT overwriting already-tracked (mutated) ones, so Local is the authoritative,
    // mutation-visible set. Id==0 = added this transaction (not yet flushed) -> ordered LAST (newest).
    private async Task<decimal> ConsumeFifoAsync(int variantId, int warehouseId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0) return await CurrentCostPriceAsync(variantId, ct);

        await db.CostLayers
            .Where(l => l.ProductVariantId == variantId && l.WarehouseId == warehouseId && l.RemainingQty > 0)
            .LoadAsync(ct);
        var layers = db.CostLayers.Local
            .Where(l => l.ProductVariantId == variantId && l.WarehouseId == warehouseId && l.RemainingQty > 0)
            .OrderBy(l => l.Id == 0 ? int.MaxValue : l.Id)
            .ToList();

        var need = quantity;
        decimal acc = 0m;
        foreach (var layer in layers)
        {
            if (need <= 0) break;
            var take = layer.Consume(Math.Min(need, layer.RemainingQty));
            acc += take * layer.UnitCost;
            need -= take;
        }

        var consumedQty = quantity - need;
        // Fallback headline when no layers (shouldn't happen — stock validated upstream; guards div-by-zero).
        return consumedQty <= 0 ? await CurrentCostPriceAsync(variantId, ct) : Round(acc / consumedQty);
    }

    // Warehouse row cost = weighted avg of that warehouse's remaining layers (0 if none).
    // Headline = weighted avg across ALL warehouses; set only when total remaining > 0 (never overwrite with 0).
    private async Task RefreshFifoDisplayAsync(int variantId, int warehouseId, CancellationToken ct)
    {
        await db.CostLayers.Where(l => l.ProductVariantId == variantId && l.RemainingQty > 0).LoadAsync(ct);
        var layers = db.CostLayers.Local.Where(l => l.ProductVariantId == variantId && l.RemainingQty > 0).ToList();

        var wh = layers.Where(l => l.WarehouseId == warehouseId).ToList();
        var whQty = wh.Sum(l => l.RemainingQty);
        var row = db.ProductStocks.Local.FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            ?? await db.ProductStocks.FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct);
        row?.SetCost(whQty > 0 ? Round(wh.Sum(l => l.RemainingQty * l.UnitCost) / whQty) : 0m);

        var vQty = layers.Sum(l => l.RemainingQty);
        if (vQty > 0)
        {
            var variant = db.ProductVariants.Local.FirstOrDefault(v => v.Id == variantId)
                ?? await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
            variant?.SetHeadlineCost(layers.Sum(l => l.RemainingQty * l.UnitCost) / vQty);
        }
    }
}
