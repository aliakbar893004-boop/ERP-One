using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CostingService(AppDbContext db, ICostingSettingService settings) : ICostingService
{
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
            default:
                throw new NotSupportedException($"Costing method {method} is not supported in Tahap 1.");
        }
    }

    public async Task<decimal> GetOutboundUnitCostAsync(int variantId, int warehouseId, int quantity, CancellationToken ct = default)
    {
        var method = await settings.GetMethodAsync(ct);
        return method switch
        {
            CostingMethod.MovingAverage => await CurrentCostPriceAsync(variantId, ct),
            _ => throw new NotSupportedException($"Costing method {method} is not supported in Tahap 1.")
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
}
