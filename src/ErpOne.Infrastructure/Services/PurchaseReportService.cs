using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PurchaseReportService(AppDbContext db) : IPurchaseReportService
{
    private IQueryable<GrnJoin> FilteredQuery(PurchaseFilter f)
    {
        var q =
            from gl in db.GoodsReceiptLines.AsNoTracking()
            join g in db.GoodsReceipts.AsNoTracking() on gl.GoodsReceiptId equals g.Id
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            join sup in db.Suppliers.AsNoTracking() on po.SupplierId equals sup.Id
            join v in db.ProductVariants.AsNoTracking() on gl.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on po.WarehouseId equals w.Id
            where g.Status == GoodsReceiptStatus.Posted
            select new GrnJoin { GL = gl, G = g, Sup = sup, V = v, P = p, W = w };

        if (f.From is DateTime from) q = q.Where(x => x.G.ReceiptDate >= from);
        if (f.To is DateTime to) q = q.Where(x => x.G.ReceiptDate < to.Date.AddDays(1));
        if (f.SupplierId is int sid) q = q.Where(x => x.Sup.Id == sid);
        if (f.WarehouseId is int wid) q = q.Where(x => x.W.Id == wid);
        if (!string.IsNullOrWhiteSpace(f.Search))
            q = q.Where(x => x.V.Sku.Contains(f.Search) || x.P.Name.Contains(f.Search));
        return q;
    }

    public async Task<PagedResult<PurchaseRowDto>> GetPurchasesPagedAsync(
        PurchaseFilter f, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200_000);
        var q = FilteredQuery(f);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.G.ReceiptDate).ThenBy(x => x.G.GrnNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PurchaseRowDto(
                x.G.ReceiptDate, x.G.GrnNumber, x.Sup.Id, x.Sup.Name, x.W.Id, x.W.Name,
                x.V.Id, x.V.Sku, x.P.Name, x.GL.QuantityReceived, x.GL.UnitCost,
                x.GL.UnitCost * x.GL.QuantityReceived))
            .ToListAsync(ct);
        return new PagedResult<PurchaseRowDto>(items, total, page, pageSize);
    }

    public async Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(PurchaseFilter f, CancellationToken ct = default)
    {
        var q = FilteredQuery(f);
        var lines = await q.CountAsync(ct);
        var qty = await q.SumAsync(x => (int?)x.GL.QuantityReceived, ct) ?? 0;
        var totalCost = await q.SumAsync(x => (decimal?)(x.GL.UnitCost * x.GL.QuantityReceived), ct) ?? 0m;
        var receipts = await q.Select(x => x.G.Id).Distinct().CountAsync(ct);
        return new PurchaseSummaryDto(lines, qty, totalCost, receipts);
    }

    public async Task<ReportDocument> BuildPurchaseReportAsync(PurchaseFilter f, CancellationToken ct = default)
    {
        var all = await GetPurchasesPagedAsync(f, 1, 200_000, ct);
        var rows = all.Items.Select(r => new ReportRow
        {
            Cells = [r.Date, r.GrnNumber, r.SupplierName, r.WarehouseName, r.Sku, r.ProductName,
                     r.Quantity, r.UnitCost, r.Value]
        }).ToList();

        return new ReportDocument
        {
            Title = "Purchase Report",
            FilterSummary = FilterSummary(f),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("GRN"),
                new ReportColumn("Supplier"),
                new ReportColumn("Warehouse"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Value", ReportAlign.Right, "N2"),
            ],
            Rows = rows,
            TotalsRow = new ReportRow
            {
                IsGrandTotal = true,
                Cells = ["Total", "", "", "", "", "", all.Items.Sum(r => r.Quantity), "", all.Items.Sum(r => r.Value)]
            },
        };
    }

    private static string FilterSummary(PurchaseFilter f)
    {
        var parts = new List<string>();
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All purchases (posted GRN)" : string.Join("  ·  ", parts);
    }

    private sealed class GrnJoin
    {
        public required GoodsReceiptLine GL { get; init; }
        public required GoodsReceipt G { get; init; }
        public required Supplier Sup { get; init; }
        public required ProductVariant V { get; init; }
        public required Product P { get; init; }
        public required Warehouse W { get; init; }
    }
}
