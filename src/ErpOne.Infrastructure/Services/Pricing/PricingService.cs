using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Pricing;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PricingService(AppDbContext db) : IPricingService
{
    public async Task<PriceResult> ResolveAsync(PriceRequest req, CancellationToken ct = default) =>
        (await ResolveManyAsync([req], ct))[0];

    public async Task<IReadOnlyList<PriceResult>> ResolveManyAsync(
        IReadOnlyList<PriceRequest> reqs, CancellationToken ct = default)
    {
        if (reqs.Count == 0) return [];

        var variantIds = reqs.Select(r => r.ProductVariantId).Distinct().ToList();
        var variantRows = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Price, v.DiscountPrice })
            .ToListAsync(ct);
        var variants = variantRows.ToDictionary(v => v.Id, v => (v.Price, v.DiscountPrice));

        var customerIds = reqs.Where(r => r.CustomerId is > 0).Select(r => r.CustomerId!.Value).Distinct().ToList();
        var warehouseIds = reqs.Where(r => r.WarehouseId is > 0).Select(r => r.WarehouseId!.Value).Distinct().ToList();

        var customerLists = customerIds.Count == 0
            ? new Dictionary<int, int?>()
            : (await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PriceListId }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.PriceListId);

        var warehouseLists = warehouseIds.Count == 0
            ? new Dictionary<int, int?>()
            : (await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
                .Select(w => new { w.Id, w.DefaultPriceListId }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.DefaultPriceListId);

        var candidateIds = customerLists.Values.Concat(warehouseLists.Values)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        // Hanya price list AKTIF yang dipertimbangkan; sisanya jatuh ke fallback.
        var activeLists = candidateIds.Count == 0
            ? new Dictionary<int, string>()
            : (await db.PriceLists.AsNoTracking()
                .Where(p => candidateIds.Contains(p.Id) && p.IsActive)
                .Select(p => new { p.Id, p.Name }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Name);

        var activeIds = activeLists.Keys.ToList();
        var tierRows = activeIds.Count == 0
            ? []
            : await db.PriceListLines.AsNoTracking()
                .Where(l => activeIds.Contains(l.PriceListId) && variantIds.Contains(l.ProductVariantId))
                .Select(l => new { l.PriceListId, l.ProductVariantId, l.MinQty, l.UnitPrice })
                .ToListAsync(ct);

        var results = new List<PriceResult>(reqs.Count);
        foreach (var r in reqs)
        {
            var hasVariant = variants.TryGetValue(r.ProductVariantId, out var v);
            var listPrice = hasVariant ? v.Price : 0m;

            var listId = PickPriceListId(r, customerLists, warehouseLists, activeLists);
            if (listId is not null)
            {
                var tiers = tierRows
                    .Where(l => l.PriceListId == listId.Value && l.ProductVariantId == r.ProductVariantId)
                    .Select(l => (l.MinQty, l.UnitPrice));

                if (PriceMath.PickTier(tiers, r.Quantity) is { } tier)
                {
                    results.Add(new PriceResult(tier.UnitPrice, listPrice, PriceSource.PriceList,
                        listId, activeLists[listId.Value], tier.MinQty));
                    continue;
                }
            }

            if (hasVariant && v.DiscountPrice is { } discountPrice)
                results.Add(new PriceResult(discountPrice, listPrice, PriceSource.VariantDiscountPrice, null, null, null));
            else
                results.Add(new PriceResult(listPrice, listPrice, PriceSource.VariantPrice, null, null, null));
        }

        return results;
    }

    /// <summary>Customer menang atas gudang; keduanya harus menunjuk price list yang aktif.</summary>
    private static int? PickPriceListId(
        PriceRequest r,
        Dictionary<int, int?> customerLists,
        Dictionary<int, int?> warehouseLists,
        Dictionary<int, string> activeLists)
    {
        if (r.CustomerId is > 0
            && customerLists.TryGetValue(r.CustomerId.Value, out var fromCustomer)
            && fromCustomer is not null
            && activeLists.ContainsKey(fromCustomer.Value))
            return fromCustomer;

        if (r.WarehouseId is > 0
            && warehouseLists.TryGetValue(r.WarehouseId.Value, out var fromWarehouse)
            && fromWarehouse is not null
            && activeLists.ContainsKey(fromWarehouse.Value))
            return fromWarehouse;

        return null;
    }

    public async Task<decimal> GetMaxDiscountPercentAsync(
        IEnumerable<string>? roleNames, CancellationToken ct = default)
    {
        var globalDefault = await db.PricingSettings.AsNoTracking()
            .Select(x => x.DefaultMaxDiscountPercent).FirstOrDefaultAsync(ct);

        var names = roleNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [];
        if (names.Count == 0) return globalDefault;

        var limits = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null && names.Contains(r.Name))
            .Select(r => r.MaxDiscountPercent)
            .ToListAsync(ct);

        return PriceMath.EffectiveMaxDiscountPercent(limits, globalDefault);
    }
}
