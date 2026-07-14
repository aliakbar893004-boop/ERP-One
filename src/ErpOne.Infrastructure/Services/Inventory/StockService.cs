using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class StockService(
    AppDbContext db,
    IValidator<StockAdjustmentRequest> adjustmentValidator) : IStockService
{
    public async Task<IReadOnlyList<StockLevelDto>> GetLevelsByVariantAsync(int variantId, CancellationToken ct = default) =>
        await BuildLevelQuery(db.ProductStocks.AsNoTracking().Where(s => s.ProductVariantId == variantId))
            .ToListAsync(ct);

    public async Task<int> GetOnHandAsync(int variantId, int warehouseId, CancellationToken ct = default) =>
        await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            .Select(s => (int?)s.Quantity).FirstOrDefaultAsync(ct) ?? 0;

    public async Task<IReadOnlyList<StockMovementDto>> GetMovementsByVariantAsync(int variantId, CancellationToken ct = default) =>
        await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductVariantId == variantId)
            .OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id)
            .Join(db.Warehouses.AsNoTracking(), m => m.WarehouseId, w => w.Id, (m, w) => new StockMovementDto(
                m.Id, m.ProductVariantId, m.WarehouseId, w.Name,
                m.Type, m.Quantity, m.UnitCost, m.MovementDate, m.RefType, m.Note))
            .ToListAsync(ct);

    public async Task<PagedResult<StockLevelDto>> GetLevelsPagedAsync(
        int page, int pageSize, int? warehouseId, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Filter & order on entity columns BEFORE projecting to the DTO — EF Core cannot translate
        // Where/OrderBy applied over a record-constructor projection.
        var q =
            from s in db.ProductStocks.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on s.ProductVariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
            select new { s, v, p, w };

        if (warehouseId is int wid) q = q.Where(x => x.w.Id == wid);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x => x.v.Sku.Contains(search) || x.p.Name.Contains(search));

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(x => x.p.Name).ThenBy(x => x.v.Sku)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StockLevelDto(x.v.Id, x.v.Sku, x.p.Name, x.w.Id, x.w.Name, x.s.Quantity, x.v.CostPrice))
            .ToListAsync(ct);

        return new PagedResult<StockLevelDto>(items, total, page, pageSize);
    }

    private const int LowStockThreshold = 5;

    public async Task<StockLevelSummary> GetLevelsSummaryAsync(int? warehouseId, CancellationToken ct = default)
    {
        var q = db.ProductStocks.AsNoTracking();
        if (warehouseId is int wid) q = q.Where(s => s.WarehouseId == wid);

        var records = await q.CountAsync(ct);
        var totalQty = await q.SumAsync(s => (int?)s.Quantity, ct) ?? 0;
        var lowStock = await q.CountAsync(s => s.Quantity > 0 && s.Quantity <= LowStockThreshold, ct);
        var outOfStock = await q.CountAsync(s => s.Quantity == 0, ct);
        return new StockLevelSummary(records, totalQty, lowStock, outOfStock);
    }

    /// <summary>Proyeksi ProductStock -> StockLevelDto dengan SKU/nama produk/nama gudang/HPP.</summary>
    private IQueryable<StockLevelDto> BuildLevelQuery(IQueryable<ProductStock> source) =>
        from s in source
        join v in db.ProductVariants.AsNoTracking() on s.ProductVariantId equals v.Id
        join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
        join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
        select new StockLevelDto(v.Id, v.Sku, p.Name, w.Id, w.Name, s.Quantity, v.CostPrice);

    public async Task RecordOpeningAsync(int variantId, int warehouseId, int quantity, decimal unitCost, CancellationToken ct = default)
    {
        if (quantity == 0) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
            ?? throw new InvalidOperationException($"Variant {variantId} not found.");
        var totalBefore = await db.ProductStocks.Where(s => s.ProductVariantId == variantId).SumAsync(s => (int?)s.Quantity, ct) ?? 0;

        db.StockMovements.Add(new StockMovement(variantId, warehouseId, MovementType.Adjustment,
            quantity, unitCost, DateTime.UtcNow, refType: "Opening", note: "Saldo awal"));

        await db.UpsertStockAsync(variantId, warehouseId, quantity, ct);
        if (quantity > 0) variant.ApplyMovingAverage(totalBefore, quantity, unitCost);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecordAdjustmentAsync(StockAdjustmentRequest request, CancellationToken ct = default)
    {
        await adjustmentValidator.ValidateAndThrowAsync(request, ct);

        var whExists = await db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId, ct);
        if (!whExists)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                nameof(StockAdjustmentRequest.WarehouseId), "Warehouse not found.")]);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in request.Lines)
        {
            var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.VariantId, ct)
                ?? throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                    "Lines", $"Variant {line.VariantId} not found.")]);

            var totalBefore = await db.ProductStocks
                .Where(s => s.ProductVariantId == line.VariantId).SumAsync(s => (int?)s.Quantity, ct) ?? 0;

            // Mutasi masuk pakai UnitCost input; mutasi keluar pakai HPP saat ini (COGS), MA tidak berubah.
            var unitCost = line.DeltaQuantity > 0 ? line.UnitCost : variant.CostPrice;

            db.StockMovements.Add(new StockMovement(line.VariantId, request.WarehouseId, MovementType.Adjustment,
                line.DeltaQuantity, unitCost, request.Date, refType: "Opname", note: line.Reason ?? request.Note));

            await db.UpsertStockAsync(line.VariantId, request.WarehouseId, line.DeltaQuantity, ct);
            if (line.DeltaQuantity > 0) variant.ApplyMovingAverage(totalBefore, line.DeltaQuantity, line.UnitCost);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
