using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockLedgerReportService(AppDbContext db) : IStockLedgerReportService
{
    // Base joined query with all filters applied on entity columns (before projection).
    private IQueryable<MovementJoin> FilteredQuery(StockLedgerFilter f)
    {
        var q =
            from m in db.StockMovements.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on m.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
            select new MovementJoin { M = m, V = v, P = p, W = w };

        if (f.WarehouseId is int wid) q = q.Where(x => x.M.WarehouseId == wid);
        if (f.Type is MovementType t) q = q.Where(x => x.M.Type == t);
        if (f.From is DateTime from) q = q.Where(x => x.M.MovementDate >= from);
        if (f.To is DateTime to) q = q.Where(x => x.M.MovementDate < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(f.Search))
            q = q.Where(x => x.V.Sku.Contains(f.Search) || x.P.Name.Contains(f.Search));
        return q;
    }

    public async Task<PagedResult<StockMovementRowDto>> GetMovementsPagedAsync(
        StockLedgerFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200_000);
        var q = FilteredQuery(filter);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.M.MovementDate).ThenByDescending(x => x.M.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockMovementRowDto(
                x.M.Id, x.M.MovementDate, x.V.Id, x.V.Sku, x.P.Name,
                x.W.Id, x.W.Name, x.M.Type, x.M.Quantity, x.M.UnitCost, x.M.RefType, x.M.RefId))
            .ToListAsync(ct);

        return new PagedResult<StockMovementRowDto>(items, total, page, pageSize);
    }

    public async Task<StockLedgerSummaryDto> GetSummaryAsync(StockLedgerFilter filter, CancellationToken ct = default)
    {
        var q = FilteredQuery(filter);
        var records = await q.CountAsync(ct);
        var totalIn = await q.Where(x => x.M.Quantity > 0).SumAsync(x => (int?)x.M.Quantity, ct) ?? 0;
        var totalOut = await q.Where(x => x.M.Quantity < 0).SumAsync(x => (int?)x.M.Quantity, ct) ?? 0; // negative
        return new StockLedgerSummaryDto(records, totalIn, -totalOut, totalIn + totalOut);
    }

    public async Task<StockCardDto?> GetStockCardAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var head = await (
            from v in db.ProductVariants.AsNoTracking()
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            where v.Id == variantId
            select new { v.Id, v.Sku, ProductName = p.Name }).FirstOrDefaultAsync(ct);
        if (head is null) return null;

        var warehouseName = "All warehouses";
        if (warehouseId is int wid)
            warehouseName = await db.Warehouses.AsNoTracking()
                .Where(w => w.Id == wid).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "?";

        var toExclusive = to.Date.AddDays(1);

        IQueryable<StockMovement> baseQ = db.StockMovements.AsNoTracking().Where(m => m.ProductVariantId == variantId);
        if (warehouseId is int w2) baseQ = baseQ.Where(m => m.WarehouseId == w2);

        // Opening = everything strictly before `from`.
        var opening = await baseQ.Where(m => m.MovementDate < from)
            .Select(m => new { m.Quantity, m.UnitCost }).ToListAsync(ct);
        var openingQty = opening.Sum(o => o.Quantity);
        var openingValue = opening.Sum(o => o.Quantity * o.UnitCost);

        // In-range movements, chronological, with running balance.
        var inRange = await baseQ
            .Where(m => m.MovementDate >= from && m.MovementDate < toExclusive)
            .OrderBy(m => m.MovementDate).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.MovementDate, m.Type, m.Quantity, m.UnitCost, m.RefType, m.RefId })
            .ToListAsync(ct);

        var runningQty = openingQty;
        var runningValue = openingValue;
        var lines = new List<StockCardLineDto>(inRange.Count);
        foreach (var m in inRange)
        {
            runningQty += m.Quantity;
            runningValue += m.Quantity * m.UnitCost;
            lines.Add(new StockCardLineDto(
                m.Id, m.MovementDate, m.Type, m.Quantity, m.UnitCost, runningQty, runningValue, m.RefType, m.RefId));
        }

        return new StockCardDto(
            head.Id, head.Sku, head.ProductName, warehouseId, warehouseName,
            from, to, openingQty, openingValue, runningQty, runningValue, lines);
    }

    public async Task<ReportDocument> BuildMovementsReportAsync(StockLedgerFilter filter, CancellationToken ct = default)
    {
        // Export the full filtered set (not paged).
        var all = await GetMovementsPagedAsync(filter, 1, 200_000, ct);
        var rows = all.Items.Select(m => new ReportRow
        {
            Cells =
            [
                m.MovementDate, m.Sku, m.ProductName, m.WarehouseName, m.Type.ToString(),
                m.Quantity, m.UnitCost, Ref(m.RefType, m.RefId),
            ]
        }).ToList();

        return new ReportDocument
        {
            Title = "Stock Ledger",
            FilterSummary = FilterSummary(filter),
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("SKU"),
                new ReportColumn("Product"),
                new ReportColumn("Warehouse"),
                new ReportColumn("Type"),
                new ReportColumn("Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Reference"),
            ],
            Rows = rows,
        };
    }

    public async Task<ReportDocument?> BuildStockCardReportAsync(
        int variantId, int? warehouseId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var card = await GetStockCardAsync(variantId, warehouseId, from, to, ct);
        if (card is null) return null;

        var rows = new List<ReportRow>
        {
            new() { IsSubtotal = true, Cells = ["Opening balance", "", "", card.OpeningQty, "", card.OpeningQty, card.OpeningValue, ""] },
        };
        rows.AddRange(card.Lines.Select(l => new ReportRow
        {
            Cells = [l.MovementDate, l.Type.ToString(), Ref(l.RefType, l.RefId), l.Quantity, l.UnitCost, l.RunningQty, l.RunningValue, ""]
        }));

        return new ReportDocument
        {
            Title = "Stock Card",
            Subtitle = $"{card.Sku} — {card.ProductName}  ·  {card.WarehouseName}",
            FilterSummary = $"Period: {card.From:yyyy-MM-dd} to {card.To:yyyy-MM-dd}",
            GeneratedAt = DateTime.Now,
            Columns =
            [
                new ReportColumn("Date", ReportAlign.Left, "yyyy-MM-dd"),
                new ReportColumn("Type"),
                new ReportColumn("Reference"),
                new ReportColumn("Qty +/-", ReportAlign.Right, "N0"),
                new ReportColumn("Unit Cost", ReportAlign.Right, "N2"),
                new ReportColumn("Balance Qty", ReportAlign.Right, "N0"),
                new ReportColumn("Balance Value", ReportAlign.Right, "N2"),
                new ReportColumn(""),
            ],
            Rows = rows,
            TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Closing balance", "", "", "", "", card.ClosingQty, card.ClosingValue, ""] },
        };
    }

    private static string Ref(string? refType, int? refId) =>
        refType is null ? "" : refId is null ? refType : $"{refType} #{refId}";

    private static string FilterSummary(StockLedgerFilter f)
    {
        var parts = new List<string>();
        if (f.Type is MovementType t) parts.Add($"Type: {t}");
        if (f.From is DateTime from) parts.Add($"From: {from:yyyy-MM-dd}");
        if (f.To is DateTime to) parts.Add($"To: {to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Search)) parts.Add($"Search: {f.Search}");
        return parts.Count == 0 ? "All movements" : string.Join("  ·  ", parts);
    }

    // Non-projected join holder so EF can filter on entity columns before Select.
    private sealed class MovementJoin
    {
        public required StockMovement M { get; init; }
        public required ProductVariant V { get; init; }
        public required Product P { get; init; }
        public required Warehouse W { get; init; }
    }
}
