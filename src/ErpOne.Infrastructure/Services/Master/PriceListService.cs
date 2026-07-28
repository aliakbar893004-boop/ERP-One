using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.PriceLists;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PriceListService(
    AppDbContext db,
    IValidator<CreatePriceListRequest> createValidator,
    IValidator<UpdatePriceListRequest> updateValidator) : IPriceListService
{
    public async Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default) =>
        await Project(EntityQuery().OrderBy(x => x.Code)).ToListAsync(ct);

    public async Task<IReadOnlyList<PriceListDto>> GetActiveAsync(CancellationToken ct = default) =>
        await Project(EntityQuery(activeOnly: true).OrderBy(x => x.Code)).ToListAsync(ct);

    public async Task<PagedResult<PriceListDto>> GetPagedAsync(int page, int pageSize, string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = EntityQuery();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(x => x.Code).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<PriceListDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<PriceListVariantOptionDto>> SearchVariantsAsync(string? term,
        CancellationToken ct = default)
    {
        var q = from v in db.ProductVariants.AsNoTracking()
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                where v.IsActive
                select new { v.Id, v.Sku, ProductName = p.Name, v.Price };
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Sku.Contains(term) || x.ProductName.Contains(term));

        return await q.OrderBy(x => x.ProductName).Take(50)
            .Select(x => new PriceListVariantOptionDto(x.Id, x.Sku, x.ProductName, x.Price))
            .ToListAsync(ct);
    }

    public async Task<PriceListDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var list = await db.PriceLists.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (list is null) return null;

        var variantIds = list.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.ProductId })
            .ToListAsync(ct);
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var lines = list.Lines
            .OrderBy(l => l.ProductVariantId).ThenBy(l => l.MinQty)
            .Select(l =>
            {
                var v = variants.FirstOrDefault(x => x.Id == l.ProductVariantId);
                var productName = v is null ? "—" : products.FirstOrDefault(p => p.Id == v.ProductId)?.Name ?? "—";
                return new PriceListLineDto(l.Id, l.ProductVariantId, v?.Sku ?? "—", productName, l.MinQty, l.UnitPrice);
            })
            .ToList();

        return new PriceListDetailDto(list.Id, list.Code, list.Name, list.Description, list.IsActive, lines);
    }

    public async Task<PriceListDetailDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, ct);
        await EnsureVariantsExistAsync(request.Lines, ct);

        var entity = new PriceList(code, request.Name, request.Description, request.IsActive);
        entity.SetLines(request.Lines.Select(l => new PriceListLine(l.ProductVariantId, l.MinQty, l.UnitPrice)));

        db.PriceLists.Add(entity);
        await db.SaveChangesAsync(ct);

        return (await GetByIdAsync(entity.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdatePriceListRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var entity = await db.PriceLists.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return false;

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, id, ct);
        await EnsureVariantsExistAsync(request.Lines, ct);

        // Baris diganti seluruhnya — hapus yang lama agar tidak ada sisa (pola SalesOrderService.UpdateAsync).
        var oldLines = await db.PriceListLines.Where(l => l.PriceListId == id).ToListAsync(ct);
        db.PriceListLines.RemoveRange(oldLines);

        entity.Update(code, request.Name, request.Description, request.IsActive);
        entity.SetLines(request.Lines.Select(l => new PriceListLine(l.ProductVariantId, l.MinQty, l.UnitPrice)));

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.PriceLists.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return false;

        if (await db.Customers.AnyAsync(c => c.PriceListId == id, ct))
            throw Fail("This price list is assigned to one or more customers and cannot be deleted.");
        if (await db.Warehouses.AnyAsync(w => w.DefaultPriceListId == id, ct))
            throw Fail("This price list is the default for one or more warehouses and cannot be deleted.");

        db.PriceLists.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<PriceList> EntityQuery(bool activeOnly = false)
    {
        var query = db.PriceLists.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        return query;
    }

    /// <summary>Proyeksi ke DTO dilakukan PALING AKHIR. Mengurutkan setelah proyeksi membuat EF gagal
    /// menerjemahkan query karena DTO memuat subquery (Lines.Count).</summary>
    private static IQueryable<PriceListDto> Project(IQueryable<PriceList> query) =>
        query.Select(x => new PriceListDto(x.Id, x.Code, x.Name, x.Description, x.IsActive,
            x.Lines.Count, x.CreatedAt, x.CreatedBy));

    private async Task EnsureCodeUniqueAsync(string code, int? excludeId, CancellationToken ct)
    {
        var exists = await db.PriceLists.AsNoTracking()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), ct);
        if (exists) throw Fail($"Code '{code}' is already in use.");
    }

    private async Task EnsureVariantsExistAsync(IReadOnlyList<PriceListLineRequest> lines, CancellationToken ct)
    {
        if (lines.Count == 0) return;

        var ids = lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var found = await db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct);

        var missing = ids.Except(found).ToList();
        if (missing.Count > 0)
            throw Fail($"Unknown product variant(s): {string.Join(", ", missing)}.");
    }

    private static ValidationException Fail(string message) =>
        new([new ValidationFailure(nameof(CreatePriceListRequest.Code), message)]);
}
