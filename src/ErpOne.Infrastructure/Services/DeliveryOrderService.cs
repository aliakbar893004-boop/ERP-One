using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.DeliveryOrders;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class DeliveryOrderService(
    AppDbContext db,
    IValidator<CreateDeliveryOrderRequest> createValidator,
    IValidator<UpdateDeliveryOrderRequest> updateValidator) : IDeliveryOrderService
{
    public async Task<PagedResult<DeliveryOrderListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, DeliveryOrderStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query =
            from d in db.DeliveryOrders.AsNoTracking()
            join so in db.SalesOrders.AsNoTracking() on d.SalesOrderId equals so.Id
            select new { d, so };

        if (status is { } st) query = query.Where(x => x.d.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.d.DoNumber.Contains(search) || x.so.SoNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.d.Id, x.d.DoNumber, x.d.SalesOrderId, x.so.SoNumber, x.so.CustomerId,
                x.d.DeliveryDate, x.d.Status,
                TotalQuantity = db.DeliveryOrderLines.Where(l => l.DeliveryOrderId == x.d.Id).Sum(l => (int?)l.QuantityDelivered) ?? 0
            })
            .ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct);

        var items = rows.Select(r => new DeliveryOrderListItemDto(
            r.Id, r.DoNumber, r.SalesOrderId, r.SoNumber,
            customers.FirstOrDefault(c => c.Id == r.CustomerId)?.Name ?? "—",
            r.DeliveryDate, r.Status.ToString(), r.TotalQuantity)).ToList();

        return new PagedResult<DeliveryOrderListItemDto>(items, total, page, pageSize);
    }

    public async Task<DeliveryOrderDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var counts = await db.DeliveryOrders
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(DeliveryOrderStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new DeliveryOrderDashboardDto(
            counts.Sum(c => c.Count),
            CountOf(DeliveryOrderStatus.Draft),
            CountOf(DeliveryOrderStatus.Posted));
    }

    public async Task<DeliveryOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.DeliveryOrders.AsNoTracking().Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        var so = await db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct);
        var customerName = so is null ? "—"
            : await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = so is null ? "—"
            : await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var soLineIds = doc.Lines.Select(l => l.SalesOrderLineId).Distinct().ToList();
        var soLines = await db.SalesOrderLines.AsNoTracking()
            .Where(l => soLineIds.Contains(l.Id)).Select(l => new { l.Id, l.Quantity }).ToListAsync(ct);

        var variantIds = doc.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = doc.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var ordered = soLines.FirstOrDefault(x => x.Id == l.SalesOrderLineId)?.Quantity ?? 0;
            return new DeliveryOrderLineDto(l.Id, l.SalesOrderLineId, l.ProductVariantId, v?.Sku ?? "—", pn,
                ordered, l.QuantityDelivered, l.UnitCost,
                Math.Round(l.QuantityDelivered * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        }).ToList();

        return new DeliveryOrderDto(doc.Id, doc.DoNumber, doc.SalesOrderId, so?.SoNumber ?? "—",
            so?.CustomerId ?? 0, customerName, so?.WarehouseId ?? 0, warehouseName,
            doc.DeliveryDate, doc.Notes, doc.Status.ToString(), doc.CreatedAt, doc.CreatedBy, lines);
    }

    public async Task<IReadOnlyList<DeliverableSoDto>> GetDeliverableSosAsync(CancellationToken ct = default)
    {
        var statuses = new[] { SalesOrderStatus.Confirmed, SalesOrderStatus.PartiallyDelivered };
        return await db.SalesOrders.AsNoTracking()
            .Where(s => statuses.Contains(s.Status))
            .OrderByDescending(s => s.Id)
            .Select(s => new DeliverableSoDto(
                s.Id, s.SoNumber,
                db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.Name).FirstOrDefault() ?? "—",
                s.OrderDate, s.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<SoForDeliveryDto?> GetSoForDeliveryAsync(int salesOrderId, CancellationToken ct = default)
    {
        var so = await db.SalesOrders.AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == salesOrderId, ct);
        if (so is null || !so.CanDeliver) return null;

        var customerName = await db.Customers.Where(c => c.Id == so.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var warehouseName = await db.Warehouses.Where(w => w.Id == so.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "—";

        var variantIds = so.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);

        var lines = so.Lines.OrderBy(l => l.Id).Select(l =>
        {
            var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
            var pn = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
            var remaining = Math.Max(0, l.Quantity - l.DeliveredQuantity);
            return new SoForDeliveryLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", pn,
                l.Quantity, l.DeliveredQuantity, remaining);
        }).ToList();

        return new SoForDeliveryDto(so.Id, so.SoNumber, so.CustomerId, customerName,
            so.WarehouseId, warehouseName, so.Currency, lines);
    }

    public async Task<DeliveryOrderDto> CreateDraftAsync(CreateDeliveryOrderRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");
        if (!so.CanDeliver) throw Fail("Only a confirmed or partially-delivered sales order can be delivered.");

        var doLines = BuildLines(so, request.Lines);
        var doc = new DeliveryOrder(await GenerateNumberAsync(request.DeliveryDate, ct),
            so.Id, request.DeliveryDate, request.Notes);
        doc.SetLines(doLines);

        db.DeliveryOrders.Add(doc);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(doc.Id, ct))!;
    }

    public async Task<bool> UpdateDraftAsync(int id, UpdateDeliveryOrderRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = await db.DeliveryOrders.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft) throw Fail("Only a draft delivery order can be modified.");

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");

        var oldLines = await db.DeliveryOrderLines.Where(l => l.DeliveryOrderId == id).ToListAsync(ct);
        db.DeliveryOrderLines.RemoveRange(oldLines);

        doc.UpdateHeader(request.DeliveryDate, request.Notes);
        doc.SetLines(BuildLines(so, request.Lines));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.DeliveryOrders.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft) throw Fail("Only a draft delivery order can be deleted.");
        db.DeliveryOrders.Remove(doc);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = await db.DeliveryOrders.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.Status != DeliveryOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft delivery order can be posted.");

        var so = await db.SalesOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == doc.SalesOrderId, ct)
            ?? throw Fail("Sales order not found.");
        if (!so.CanDeliver) throw Fail("Only a confirmed or partially-delivered sales order can be delivered.");

        // Akumulasi qty KELUAR per varian dalam post ini: UpsertStockAsync hanya mengubah entitas di memori
        // (tanpa flush), jadi on-hand dari DB akan basi untuk baris ke-2 dengan varian sama. Cek stok memakai
        // (on-hand DB − akumulasiKeluar) SEBELUM mutasi apa pun.
        var takenPerVariant = new Dictionary<int, int>();

        // Fase 1: validasi ketersediaan stok untuk SEMUA baris sebelum menulis apa pun.
        foreach (var line in doc.Lines)
        {
            var onHand = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.ProductVariantId && s.WarehouseId == so.WarehouseId)
                .SumAsync(s => (int?)s.Quantity, ct) ?? 0;
            var alreadyTaken = takenPerVariant.TryGetValue(line.ProductVariantId, out var t) ? t : 0;
            var available = onHand - alreadyTaken;
            if (line.QuantityDelivered > available)
            {
                var sku = await db.ProductVariants.Where(v => v.Id == line.ProductVariantId)
                    .Select(v => v.Sku).FirstOrDefaultAsync(ct) ?? line.ProductVariantId.ToString();
                throw Fail($"Delivering {line.QuantityDelivered} of {sku} exceeds available stock ({available}) at the source warehouse.");
            }
            takenPerVariant[line.ProductVariantId] = alreadyTaken + line.QuantityDelivered;
        }

        // Fase 2: mutasi (stok keluar + COGS + tracking SO line).
        foreach (var line in doc.Lines)
        {
            var soLine = so.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId)
                ?? throw Fail($"SO line {line.SalesOrderLineId} not found on SO {so.SoNumber}.");

            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.ProductVariantId, ct)
                ?? throw Fail($"Variant {line.ProductVariantId} not found.");

            db.StockMovements.Add(new StockMovement(line.ProductVariantId, so.WarehouseId, MovementType.Out,
                -line.QuantityDelivered, variant.CostPrice, doc.DeliveryDate, refType: "DO", refId: doc.Id,
                note: doc.DoNumber));

            await db.UpsertStockAsync(line.ProductVariantId, so.WarehouseId, -line.QuantityDelivered, ct);
            line.SetUnitCost(variant.CostPrice); // COGS snapshot; MA TIDAK diubah
            soLine.ApplyDelivery(line.QuantityDelivered);
        }

        if (so.Lines.All(l => l.IsFullyDelivered)) so.MarkDelivered();
        else so.MarkPartiallyDelivered();

        doc.Post();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Validasi tiap baris terhadap baris SO &amp; sisa qty (STRICT vs qty terposting), bangun entitas line.</summary>
    private List<DeliveryOrderLine> BuildLines(SalesOrder so, IReadOnlyList<DeliveryOrderLineRequest> requests)
    {
        var lines = new List<DeliveryOrderLine>();
        foreach (var r in requests)
        {
            var soLine = so.Lines.FirstOrDefault(l => l.Id == r.SalesOrderLineId)
                ?? throw Fail($"SO line {r.SalesOrderLineId} does not belong to SO {so.SoNumber}.");
            var remaining = soLine.Quantity - soLine.DeliveredQuantity;
            if (r.QuantityDelivered > remaining)
                throw Fail($"Delivering {r.QuantityDelivered} for variant {soLine.ProductVariantId} exceeds the remaining quantity ({remaining}).");
            lines.Add(new DeliveryOrderLine(soLine.Id, soLine.ProductVariantId, r.QuantityDelivered));
        }
        return lines;
    }

    private async Task<string> GenerateNumberAsync(DateTime deliveryDate, CancellationToken ct)
    {
        var prefix = $"DO-{deliveryDate:yyyyMM}-";
        var last = await db.DeliveryOrders.AsNoTracking()
            .Where(d => d.DoNumber.StartsWith(prefix))
            .OrderByDescending(d => d.DoNumber)
            .Select(d => d.DoNumber).FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return $"{prefix}{seq:D4}";
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("DeliveryOrder", message)]);
}
