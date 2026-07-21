using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class GoodsReceiptService(
    AppDbContext db,
    IValidator<CreateGoodsReceiptRequest> createValidator,
    IValidator<UpdateGoodsReceiptRequest> updateValidator,
    IOptions<GoodsReceiptOptions> options,
    IDocumentNumberService docNumbers,
    IJournalPostingService journalPoster) : IGoodsReceiptService
{
    private int Tolerance => Math.Max(0, options.Value.OverReceiptTolerancePercent);

    public async Task<PagedResult<GoodsReceiptListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, GoodsReceiptStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query =
            from g in db.GoodsReceipts.AsNoTracking()
            join po in db.PurchaseOrders.AsNoTracking() on g.PurchaseOrderId equals po.Id
            select new { g, po };

        if (status is { } st) query = query.Where(x => x.g.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.g.GrnNumber.Contains(search) || x.po.PoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.g.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.g.Id, x.g.GrnNumber, x.g.PurchaseOrderId, x.po.PoNumber, x.po.SupplierId,
                x.g.ReceiptDate, x.g.Status,
                TotalQuantity = db.GoodsReceiptLines.Where(l => l.GoodsReceiptId == x.g.Id).Sum(l => (int?)l.QuantityReceived) ?? 0
            })
            .ToListAsync(ct);

        var supplierIds = rows.Select(r => r.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToListAsync(ct);

        var items = rows.Select(r => new GoodsReceiptListItemDto(
            r.Id, r.GrnNumber, r.PurchaseOrderId, r.PoNumber,
            suppliers.FirstOrDefault(s => s.Id == r.SupplierId)?.Name ?? "—",
            r.ReceiptDate, r.Status.ToString(), r.TotalQuantity)).ToList();

        return new PagedResult<GoodsReceiptListItemDto>(items, total, page, pageSize);
    }

    public async Task<GoodsReceiptDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.GoodsReceipts
            .GroupBy(g => g.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(GoodsReceiptStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new GoodsReceiptDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(GoodsReceiptStatus.Draft),
            CountOf(GoodsReceiptStatus.Posted));
    }

    public async Task<GoodsReceiptDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var grn = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return null;

        var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct);
        var supplierName = po is null ? "—"
            : await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = po is null ? "—"
            : await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var poLineIds = grn.Lines.Select(l => l.PurchaseOrderLineId).Distinct().ToList();
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => poLineIds.Contains(l.Id)).Select(l => new { l.Id, l.Quantity }).ToListAsync(ct);

        var variantIds = grn.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = grn.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var ordered = poLines.FirstOrDefault(x => x.Id == l.PurchaseOrderLineId)?.Quantity ?? 0;
            return new GoodsReceiptLineDto(l.Id, l.PurchaseOrderLineId, l.ProductVariantId, v?.Sku ?? "—", pn,
                ordered, l.QuantityReceived, l.UnitCost,
                Math.Round(l.QuantityReceived * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        }).ToList();

        return new GoodsReceiptDto(grn.Id, grn.GrnNumber, grn.PurchaseOrderId, po?.PoNumber ?? "—",
            po?.SupplierId ?? 0, supplierName, po?.WarehouseId ?? 0, warehouseName,
            grn.ReceiptDate, grn.Notes, grn.Status.ToString(), grn.CreatedAt, grn.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<ReceivablePoDto>> GetReceivablePosAsync(CancellationToken ct = default)
    {
        var statuses = new[] { PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.PartiallyReceived };
        return await db.PurchaseOrders.AsNoTracking()
            .Where(p => statuses.Contains(p.Status))
            .OrderByDescending(p => p.Id)
            .Select(p => new ReceivablePoDto(
                p.Id, p.PoNumber,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault() ?? "—",
                p.OrderDate, p.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<PoForReceiptDto?> GetPoForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct);
        if (po is null || !po.CanReceive) return null;

        var supplierName = await db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = po.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = po.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var remaining = Math.Max(0, l.Quantity - l.ReceivedQuantity);
            return new PoForReceiptLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.ReceivedQuantity, remaining, l.DefaultUnitCost);
        }).ToList();

        return new PoForReceiptDto(po.Id, po.PoNumber, po.SupplierId, supplierName,
            po.WarehouseId, warehouseName, po.Currency, lines);
    }

    public async Task<GoodsReceiptDto> CreateDraftAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");
        if (!po.CanReceive) throw Fail("Only a confirmed or partially-received purchase order can be received.");

        var grnLines = BuildLines(po, request.Lines);
        var grn = new GoodsReceipt(await docNumbers.NextAsync(DocumentTypes.GoodsReceipt, request.ReceiptDate, ct),
            po.Id, request.ReceiptDate, request.Notes);
        grn.SetLines(grnLines);

        db.GoodsReceipts.Add(grn);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(grn.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grn = await db.GoodsReceipts.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft) throw Fail("Only a draft goods receipt can be modified.");

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");

        var oldLines = await db.GoodsReceiptLines.Where(l => l.GoodsReceiptId == id).ToListAsync(ct);
        db.GoodsReceiptLines.RemoveRange(oldLines);

        grn.UpdateHeader(request.ReceiptDate, request.Notes);
        grn.SetLines(BuildLines(po, request.Lines));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var grn = await db.GoodsReceipts.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft) throw Fail("Only a draft goods receipt can be deleted.");
        db.GoodsReceipts.Remove(grn);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var grn = await db.GoodsReceipts.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grn is null) return false;
        if (grn.Status != GoodsReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft goods receipt can be posted.");

        var po = await db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId, ct)
            ?? throw Fail("Purchase order not found.");
        if (!po.CanReceive) throw Fail("Only a confirmed or partially-received purchase order can be received.");

        // Akumulasi qty masuk per varian DALAM post ini: ProductStock upsert hanya mengubah entitas
        // di memori (tanpa flush), jadi totalBefore dari DB akan basi untuk baris ke-2 dgn varian sama.
        var addedPerVariant = new Dictionary<int, int>();

        foreach (var line in grn.Lines)
        {
            var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId)
                ?? throw Fail($"PO line {line.PurchaseOrderLineId} not found on PO {po.PoNumber}.");

            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Variant {line.ProductVariantId} not found.");

            var dbTotal = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.ProductVariantId)
                .SumAsync(s => (int?)s.Quantity, ct) ?? 0;
            var totalBefore = dbTotal + (addedPerVariant.TryGetValue(line.ProductVariantId, out var added) ? added : 0);

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, po.WarehouseId, MovementType.In,
                line.QuantityReceived, line.UnitCost, grn.ReceiptDate, refType: "GRN", refId: grn.Id,
                note: grn.GrnNumber));

            await db.UpsertStockAsync(line.ProductVariantId, po.WarehouseId, line.QuantityReceived, ct);
            variant.ApplyMovingAverage(totalBefore, line.QuantityReceived, line.UnitCost);
            poLine.ApplyReceipt(line.QuantityReceived, Tolerance);

            addedPerVariant[line.ProductVariantId] =
                (addedPerVariant.TryGetValue(line.ProductVariantId, out var prev) ? prev : 0) + line.QuantityReceived;
        }

        if (po.Lines.All(l => l.IsFullyReceived)) po.MarkReceived();
        else po.MarkPartiallyReceived();

        grn.Post();

        await journalPoster.PostGoodsReceiptAsync(grn, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Validasi tiap baris terhadap baris PO &amp; toleransi (vs qty yang sudah diposting), bangun entitas line.</summary>
    private List<GoodsReceiptLine> BuildLines(PurchaseOrder po, IReadOnlyList<GoodsReceiptLineRequest> requests)
    {
        var lines = new List<GoodsReceiptLine>();
        foreach (var r in requests)
        {
            var poLine = po.Lines.FirstOrDefault(l => l.Id == r.PurchaseOrderLineId)
                ?? throw Fail($"PO line {r.PurchaseOrderLineId} does not belong to PO {po.PoNumber}.");
            var maxAllowed = (int)Math.Floor(poLine.Quantity * (1 + Tolerance / 100m));
            var remaining = maxAllowed - poLine.ReceivedQuantity;
            if (r.QuantityReceived > remaining)
                throw Fail($"Receiving {r.QuantityReceived} for {poLine.ProductVariantId} exceeds the remaining allowed quantity ({remaining}).");
            lines.Add(new GoodsReceiptLine(poLine.Id, poLine.ProductVariantId, r.QuantityReceived, r.UnitCost));
        }
        return lines;
    }


    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("GoodsReceipt", message)]);
}
