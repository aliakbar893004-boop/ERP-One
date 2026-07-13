using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class InventoryValuationReportService(AppDbContext db) : IInventoryValuationReportService
{
    private const string Uncategorized = "Uncategorized";

    public async Task<ValuationResultDto> GetValuationAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default)
    {
        var toExclusive = asOf.Date.AddDays(1);

        // Join movements to variant/product/warehouse; filter to <= asOf and optional warehouse/category.
        var q =
            from m in db.StockMovements.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on m.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
            where m.MovementDate < toExclusive
            select new { m.Quantity, m.UnitCost, VariantId = v.Id, v.Sku, ProductName = p.Name,
                         p.CategoryId, WarehouseId = w.Id, WarehouseName = w.Name };

        if (warehouseId is int wid) q = q.Where(x => x.WarehouseId == wid);
        if (categoryId is int cid) q = q.Where(x => x.CategoryId == cid);

        var rows = await q.ToListAsync(ct);

        // Category names lookup (small table).
        var categoryNames = await db.ProductCategories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Aggregate per (variant, group-key). For Category grouping we aggregate across warehouses per variant;
        // for Warehouse grouping we keep variant split per warehouse.
        var items = rows
            .GroupBy(x => groupBy == ValuationGroupBy.Warehouse
                ? (x.VariantId, x.WarehouseId)
                : (x.VariantId, 0))
            .Select(g =>
            {
                var first = g.First();
                var qty = g.Sum(x => x.Quantity);
                var value = g.Sum(x => x.Quantity * x.UnitCost);
                var groupName = groupBy == ValuationGroupBy.Warehouse
                    ? first.WarehouseName
                    : (first.CategoryId is int c && categoryNames.TryGetValue(c, out var n) ? n : Uncategorized);
                var avg = qty == 0 ? 0m : value / qty;
                return new ValuationItemDto(first.VariantId, first.Sku, first.ProductName, groupName, qty, avg, value);
            })
            .Where(i => includeZeroQty || i.Qty != 0)
            .ToList();

        var groups = items
            .GroupBy(i => i.GroupName)
            .OrderBy(g => g.Key)
            .Select(g => new ValuationGroupDto(
                g.Key,
                g.OrderBy(i => i.ProductName).ThenBy(i => i.Sku).ToList(),
                g.Sum(i => i.Qty),
                g.Sum(i => i.Value)))
            .ToList();

        return new ValuationResultDto(
            asOf, groupBy, groups,
            items.Sum(i => i.Qty), items.Sum(i => i.Value), items.Count);
    }

    public async Task<ReportDocument> BuildValuationReportAsync(
        DateTime asOf, ValuationGroupBy groupBy, int? warehouseId, int? categoryId,
        bool includeZeroQty, CancellationToken ct = default)
    {
        var result = await GetValuationAsync(asOf, groupBy, warehouseId, categoryId, includeZeroQty, ct);

        var rows = new List<ReportRow>();
        foreach (var g in result.Groups)
        {
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"▸ {g.GroupName}", "", "", "", ""] });
            foreach (var i in g.Items)
                rows.Add(new ReportRow { Cells = [i.GroupName, i.Sku, i.ProductName, i.Qty, i.AvgCost, i.Value] });
            rows.Add(new ReportRow { IsSubtotal = true, Cells = [$"{g.GroupName} subtotal", "", "", g.TotalQty, "", g.TotalValue] });
        }

        return new ReportDocument
        {
            Title = "Inventory Valuation",
            Subtitle = $"As of {result.AsOf:yyyy-MM-dd}  ·  Grouped by {result.GroupBy}",
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn(groupBy == ValuationGroupBy.Warehouse ? "Warehouse" : "Category"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Avg Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Value", ReportAlign.Right, "N2"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Grand total", "", "", result.GrandTotalQty, "", result.GrandTotalValue] },
        };
    }
}
