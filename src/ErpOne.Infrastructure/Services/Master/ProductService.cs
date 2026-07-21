using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Application.Products;
using ErpOne.Application.Stock;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ProductService(
    AppDbContext db,
    IFileStorage fileStorage,
    IStockService stockService,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : IProductService
{
    private const string ImageFolder = "uploads/products";

    private IQueryable<Product> ProductGraph() =>
        db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Attributes);

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await ProductGraph().AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return await ToDtosAsync(items, ct);
    }

    public async Task<PagedResult<ProductDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, ProductStatus? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Code.Contains(search)
                || p.Variants.Any(v => v.Sku.Contains(search)));
        if (status is not null)
            query = query.Where(p => p.Status == status);

        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => p.Id).ToListAsync(ct);

        var items = await ProductGraph().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        items = items.OrderBy(p => p.Name).ToList();
        var dtos = await ToDtosAsync(items, ct);
        return new PagedResult<ProductDto>(dtos, total, page, pageSize);
    }

    public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var p = await ProductGraph().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        return (await ToDtosAsync(new[] { p }, ct))[0];
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new ValidationException([new ValidationFailure(nameof(CreateProductRequest.CategoryId), "Category not found.")]);

        var code = await GenerateCodeAsync(category, ct);
        var valueLabels = await LoadValueSuffixMapAsync(request.Variants.SelectMany(v => v.AttributeValueIds), ct);

        var product = new Product(code, request.Name, request.Description, category.Id,
            request.BrandId, request.BaseUnitId, request.TaxId, request.Status);

        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var sku = BuildSku(code, v.AttributeValueIds, valueLabels);
            if (!usedSkus.Add(sku))
                throw new ValidationException([new ValidationFailure(nameof(CreateProductRequest.Variants),
                    $"Duplicate variant combination produces SKU '{sku}'.")]);
            var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
            variant.SetAttributeValues(v.AttributeValueIds);
        }

        await EnsureSkusUniqueInDbAsync(usedSkus, ct);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        // Saldo awal -> ledger (gudang default). Cocokkan input ke varian via SKU.
        // Detach the saved graph so each RecordOpeningAsync (own transaction on this context) starts clean.
        db.ChangeTracker.Clear();
        var defaultWhId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct);
        if (defaultWhId > 0)
        {
            var openingBySku = new Dictionary<string, (int Stock, decimal Cost)>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in request.Variants)
            {
                var sku = BuildSku(code, v.AttributeValueIds, valueLabels);
                openingBySku[sku] = (v.OpeningStock, v.CostPrice);
            }
            foreach (var variant in product.Variants)
            {
                if (openingBySku.TryGetValue(variant.Sku, out var o) && o.Stock > 0)
                    await stockService.RecordOpeningAsync(variant.Id, defaultWhId, o.Stock, o.Cost, ct);
            }
        }

        return (await GetByIdAsync(product.Id, ct))!;
    }

    public async Task<ProductUpdateResult> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var product = await db.Products
            .Include(p => p.Variants).ThenInclude(v => v.Attributes)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return new ProductUpdateResult(false, []);

        var categoryExists = await db.ProductCategories.AnyAsync(c => c.Id == request.CategoryId, ct);
        if (!categoryExists)
            throw new ValidationException([new ValidationFailure(nameof(UpdateProductRequest.CategoryId), "Category not found.")]);

        product.Update(request.Name, request.Description, request.CategoryId,
            request.BrandId, request.BaseUnitId, request.TaxId, request.Status);

        // Merge in place — do NOT clear & re-add: StockMovements/ProductStocks reference
        // ProductVariant with OnDelete(Restrict), so deleting a stocked variant fails (FK 547).
        // Match existing variants by SKU (SKU is locked, derived from Code + attribute combo).
        var valueLabels = await LoadValueSuffixMapAsync(request.Variants.SelectMany(v => v.AttributeValueIds), ct);

        var requested = new List<(string Sku, VariantInput Input)>();
        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var sku = BuildSku(product.Code, v.AttributeValueIds, valueLabels);
            if (!usedSkus.Add(sku))
                throw new ValidationException([new ValidationFailure(nameof(UpdateProductRequest.Variants),
                    $"Duplicate variant combination produces SKU '{sku}'.")]);
            requested.Add((sku, v));
        }
        await EnsureSkusUniqueInDbAsync(usedSkus, ct, excludeProductId: id);

        var existingBySku = product.Variants.ToDictionary(v => v.Sku, StringComparer.OrdinalIgnoreCase);

        // Upsert each requested variant: update in place if the SKU already exists, else add.
        foreach (var (sku, v) in requested)
        {
            if (existingBySku.TryGetValue(sku, out var existing))
            {
                existing.Update(v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
                // Only rewrite attribute rows when the combo actually changed (avoid needless DELETE+INSERT churn).
                var currentIds = existing.Attributes.Select(a => a.AttributeValueId).OrderBy(x => x).ToList();
                var newIds = v.AttributeValueIds.Distinct().OrderBy(x => x).ToList();
                if (!currentIds.SequenceEqual(newIds))
                    existing.SetAttributeValues(v.AttributeValueIds);
            }
            else
            {
                var variant = product.AddVariant(sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice,
                    v.Weight, v.Dimensions, v.IsActive, v.DiscountPercent, v.ReorderLevel, v.ReorderQty);
                variant.SetAttributeValues(v.AttributeValueIds);
            }
        }

        // Removed variants (present in DB, absent from the request): hard-delete those with no
        // stock/ledger; soft-deactivate (keep) those that have stock or movement history.
        var removed = product.Variants.Where(v => !usedSkus.Contains(v.Sku)).ToList();
        var deactivated = new List<string>();
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(v => v.Id).ToList();
            var withLedger = await db.StockMovements.Where(m => removedIds.Contains(m.ProductVariantId)).Select(m => m.ProductVariantId)
                .Union(db.ProductStocks.Where(s => removedIds.Contains(s.ProductVariantId)).Select(s => s.ProductVariantId))
                .Distinct().ToListAsync(ct);
            var withLedgerSet = withLedger.ToHashSet();
            foreach (var v in removed)
            {
                if (withLedgerSet.Contains(v.Id))
                {
                    // Skip ones already deactivated in a prior save (don't re-notify every save).
                    if (v.IsActive)
                    {
                        v.Deactivate();
                        deactivated.Add(v.Sku);
                    }
                }
                else
                {
                    product.RemoveVariant(v.Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new ProductUpdateResult(true, deactivated);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return false;

        var variantIds = await db.ProductVariants
            .Where(v => v.ProductId == id)
            .Select(v => v.Id)
            .ToListAsync(ct);

        if (variantIds.Count > 0)
        {
            var hasStockHistory = await db.StockMovements.AnyAsync(m => variantIds.Contains(m.ProductVariantId), ct)
                || await db.ProductStocks.AnyAsync(s => variantIds.Contains(s.ProductVariantId), ct);
            if (hasStockHistory)
                throw new ValidationException(
                    [new ValidationFailure("Product",
                        "Produk tidak dapat dihapus karena memiliki riwayat pergerakan stok.")]);
        }

        var paths = product.Images.Select(i => i.StoredPath).ToList();
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        foreach (var path in paths) fileStorage.Delete(path);
        return true;
    }

    // ── Image ops (unchanged logic; operate on product.Images) ──────────────
    public async Task<ProductDto?> AddImagesAsync(int productId, IReadOnlyList<ProductImageUpload> uploads, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return null;
        if (uploads.Count == 0) return await GetByIdAsync(productId, ct);
        if (!product.CanAddImages(uploads.Count))
            throw new InvalidOperationException($"Maksimal {Product.MaxImages} gambar per produk (sisa {product.RemainingImageSlots}).");

        var savedPaths = new List<string>();
        try
        {
            foreach (var up in uploads)
            {
                using var ms = new MemoryStream(up.Content);
                var stored = await fileStorage.SaveAsync(ms, up.OriginalFileName, ImageFolder, ct);
                savedPaths.Add(stored.RelativePath);
                product.AddImage(stored.RelativePath, up.OriginalFileName, up.ContentType, stored.Size);
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var path in savedPaths) fileStorage.Delete(path);
            throw;
        }
        return await GetByIdAsync(productId, ct);
    }

    public async Task<bool> DeleteImageAsync(int productId, int imageId, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return false;
        var image = product.RemoveImage(imageId);
        if (image is null) return false;
        await db.SaveChangesAsync(ct);
        fileStorage.Delete(image.StoredPath);
        return true;
    }

    public async Task<bool> SetPrimaryImageAsync(int productId, int imageId, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (product is null) return false;
        if (!product.SetPrimaryImage(imageId)) return false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Code / SKU generation ───────────────────────────────────────────────
    private async Task<string> GenerateCodeAsync(ProductCategory category, CancellationToken ct) =>
        $"{category.Code}/{await NextCodeSeqAsync(category.Code, ct):0000}";

    private async Task<int> NextCodeSeqAsync(string code, CancellationToken ct)
    {
        var prefix = code + "/";
        var existing = await db.Products.AsNoTracking()
            .Where(p => p.Code.StartsWith(prefix)).Select(p => p.Code).ToListAsync(ct);
        var max = 0;
        foreach (var c in existing)
        {
            var slash = c.LastIndexOf('/');
            if (slash >= 0 && int.TryParse(c[(slash + 1)..], out var n)) max = Math.Max(max, n);
        }
        return max + 1;
    }

    /// <summary>Map AttributeValueId -> (attributeName, valueCode) untuk membangun sufiks SKU urut nama atribut.</summary>
    private async Task<Dictionary<int, (string AttrName, string Code)>> LoadValueSuffixMapAsync(
        IEnumerable<int> valueIds, CancellationToken ct)
    {
        var ids = valueIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.AttributeValues.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Join(db.ProductAttributes.AsNoTracking(), v => v.AttributeId, a => a.Id,
                (v, a) => new { v.Id, AttrName = a.Name, v.Code })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.AttrName, r.Code));
    }

    private static string BuildSku(string code, IReadOnlyList<int> valueIds,
        Dictionary<int, (string AttrName, string Code)> map)
    {
        if (valueIds.Count == 0) return code;
        var parts = valueIds
            .Where(map.ContainsKey)
            .Select(id => map[id])
            .OrderBy(x => x.AttrName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Code);
        var suffix = string.Join("-", parts);
        return string.IsNullOrEmpty(suffix) ? code : $"{code}-{suffix}";
    }

    private async Task EnsureSkusUniqueInDbAsync(IEnumerable<string> skus, CancellationToken ct, int? excludeProductId = null)
    {
        var list = skus.ToList();
        var clash = await db.ProductVariants.AsNoTracking()
            .Where(v => list.Contains(v.Sku) && (excludeProductId == null || v.ProductId != excludeProductId))
            .Select(v => v.Sku).FirstOrDefaultAsync(ct);
        if (clash is not null)
            throw new ValidationException([new ValidationFailure("Variants", $"SKU '{clash}' is already in use.")]);
    }

    // ── Import (1 default variant per row) ───────────────────────────────────
    public async Task<ProductImportResult> ImportAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken ct = default)
    {
        var errors = new List<ProductImportError>();
        var added = 0;
        var categories = await db.ProductCategories.AsNoTracking().ToListAsync(ct);
        var byCode = categories.ToDictionary(c => c.Code.ToUpperInvariant());
        var nextSeq = new Dictionary<int, int>();
        var openings = new List<(Product Product, int Stock, decimal Cost)>();

        foreach (var row in rows)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.CategoryCode)) throw new InvalidOperationException("Category code is required.");
                if (!byCode.TryGetValue(row.CategoryCode.Trim().ToUpperInvariant(), out var category))
                    throw new InvalidOperationException($"Category code '{row.CategoryCode}' not found.");
                if (string.IsNullOrWhiteSpace(row.Name)) throw new InvalidOperationException("Name is required.");

                var price = ParseDecimal(row.Price, "Price", required: true)!.Value;
                var discount = ParseDecimal(row.DiscountPrice, "Discount price", required: false);
                var stock = ParseInt(row.Stock, "Stock") ?? 0;
                var weight = ParseDecimal(row.Weight, "Weight", required: false);
                var status = ParseStatus(row.Status);

                if (price < 0) throw new InvalidOperationException("Price must be >= 0.");
                if (discount is < 0) throw new InvalidOperationException("Discount price must be >= 0.");
                if (discount.HasValue && discount > price) throw new InvalidOperationException("Discount price must not exceed the selling price.");
                if (stock < 0) throw new InvalidOperationException("Stock must be >= 0.");
                if (weight is < 0) throw new InvalidOperationException("Weight must be >= 0.");

                if (!nextSeq.TryGetValue(category.Id, out var seq))
                    seq = await NextCodeSeqAsync(category.Code, ct);
                nextSeq[category.Id] = seq + 1;

                var code = $"{category.Code}/{seq:0000}";
                var product = new Product(code, row.Name!.Trim(), row.Description, category.Id, null, null, null, status);
                product.AddVariant(code, null, price, discount, 0m, weight, row.Dimensions, true, null);
                if (stock > 0) openings.Add((product, stock, 0m));
                db.Products.Add(product);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add(new ProductImportError(row.RowNumber, ex.Message));
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            // Capture variant ids before detaching, then clear so each RecordOpeningAsync starts clean.
            var openingIds = openings
                .Select(o => (VariantId: o.Product.Variants.FirstOrDefault()?.Id ?? 0, o.Stock, o.Cost))
                .Where(o => o.VariantId > 0)
                .ToList();
            db.ChangeTracker.Clear();
            var defaultWhId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct);
            if (defaultWhId > 0)
                foreach (var (variantId, stock, cost) in openingIds)
                    await stockService.RecordOpeningAsync(variantId, defaultWhId, stock, cost, ct);
        }
        return new ProductImportResult(added, errors.Count, errors);
    }

    // ── Dashboard (aggregate from variants) ──────────────────────────────────
    public async Task<ProductDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var totalProducts = await db.Products.CountAsync(ct);
        var totalCategories = await db.ProductCategories.CountAsync(ct);
        var totalStock = await db.ProductStocks.SumAsync(s => (int?)s.Quantity, ct) ?? 0;
        var inventoryValue = await db.ProductStocks
            .Join(db.ProductVariants, s => s.ProductVariantId, v => v.Id, (s, v) => (decimal?)(s.Quantity * v.CostPrice))
            .SumAsync(ct) ?? 0m;
        var activeCount = await db.Products.CountAsync(p => p.Status == ProductStatus.Aktif, ct);

        // Stok per produk = jumlah saldo ProductStock semua variannya, lintas gudang.
        var stockByProduct = await db.ProductStocks
            .Join(db.ProductVariants, s => s.ProductVariantId, v => v.Id, (s, v) => new { v.ProductId, s.Quantity })
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Stock = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        // Produk "low" = punya >=1 baris (varian,gudang) dgn ReorderLevel>0 && qty<=ReorderLevel.
        var lowProductIds = (await db.ProductStocks.AsNoTracking()
                .Join(db.ProductVariants.AsNoTracking(), s => s.ProductVariantId, v => v.Id,
                    (s, v) => new { v.ProductId, s.Quantity, v.ReorderLevel })
                .Where(x => x.ReorderLevel > 0 && x.Quantity <= x.ReorderLevel)
                .Select(x => x.ProductId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var outOfStock = stockByProduct.Count(x => x.Stock == 0);
        var lowStock = stockByProduct.Count(x => x.Stock > 0 && lowProductIds.Contains(x.ProductId));

        var byStatus = await db.Products
            .GroupBy(p => p.Status).Select(g => new StatusCount(g.Key, g.Count())).ToListAsync(ct);

        var categoryNames = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var prodCat = await db.Products.Where(p => p.CategoryId != null)
            .Select(p => new { p.Id, CategoryId = p.CategoryId!.Value }).ToListAsync(ct);
        var stockMap = stockByProduct.ToDictionary(x => x.ProductId, x => x.Stock);
        var byCategory = prodCat
            .GroupBy(p => p.CategoryId)
            .Select(g => new CategoryStock(
                categoryNames.TryGetValue(g.Key, out var name) ? name : "—",
                g.Count(),
                g.Sum(p => stockMap.TryGetValue(p.Id, out var s) ? s : 0)))
            .OrderByDescending(x => x.TotalStock).ToList();

        var lowStockProductIds = stockByProduct
            .Where(x => x.Stock > 0 && lowProductIds.Contains(x.ProductId))
            .OrderBy(x => x.Stock).Take(8).Select(x => x.ProductId).ToList();
        var lowStockProducts = await db.Products.AsNoTracking()
            .Where(p => lowStockProductIds.Contains(p.Id)).Include(p => p.Images).ToListAsync(ct);
        var lowStockItems = lowStockProducts
            .Select(p => new LowStockItem(p.Id, p.Code, p.Name,
                stockMap.TryGetValue(p.Id, out var s) ? s : 0, p.Status,
                p.PrimaryImage is { } img ? "/" + img.StoredPath : null))
            .OrderBy(i => i.Stock).ToList();

        return new ProductDashboardDto(totalProducts, totalCategories, totalStock, inventoryValue,
            activeCount, outOfStock, lowStock, byStatus, byCategory, lowStockItems);
    }

    // ── DTO mapping ──────────────────────────────────────────────────────────
    private async Task<IReadOnlyList<ProductDto>> ToDtosAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var brandIds = products.Where(p => p.BrandId != null).Select(p => p.BrandId!.Value).Distinct().ToList();
        var unitIds = products.Where(p => p.BaseUnitId != null).Select(p => p.BaseUnitId!.Value).Distinct().ToList();
        var taxIds = products.Where(p => p.TaxId != null).Select(p => p.TaxId!.Value).Distinct().ToList();
        var valueIds = products.SelectMany(p => p.Variants).SelectMany(v => v.Attributes)
            .Select(a => a.AttributeValueId).Distinct().ToList();

        var brands = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var taxes = await db.Taxes.AsNoTracking().Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var values = await db.AttributeValues.AsNoTracking().Where(v => valueIds.Contains(v.Id))
            .Join(db.ProductAttributes.AsNoTracking(), v => v.AttributeId, a => a.Id,
                (v, a) => new { v.Id, AttrName = a.Name, v.Code, v.Value })
            .ToDictionaryAsync(x => x.Id, x => (x.AttrName, x.Code, x.Value), ct);

        var variantIds = products.SelectMany(p => p.Variants).Select(v => v.Id).ToList();
        var stockByVariant = await db.ProductStocks.AsNoTracking()
            .Where(s => variantIds.Contains(s.ProductVariantId))
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Qty, ct);

        return products.Select(p => ToDto(p, brands, units, taxes, values, stockByVariant)).ToList();
    }

    private static ProductDto ToDto(Product p,
        Dictionary<int, string> brands, Dictionary<int, string> units, Dictionary<int, string> taxes,
        Dictionary<int, (string AttrName, string Code, string Value)> values,
        Dictionary<int, int> stockByVariant)
    {
        var images = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id, "/" + i.StoredPath, i.OriginalFileName, i.FileSize, i.SortOrder, i.IsPrimary))
            .ToList();
        var primary = p.PrimaryImage;

        var variants = p.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantDto(
            v.Id, v.Sku, v.Barcode, v.Price, v.DiscountPrice, v.CostPrice, v.Weight, v.Dimensions,
            stockByVariant.TryGetValue(v.Id, out var q) ? q : 0, v.IsActive, v.DiscountPercent,
            v.ReorderLevel, v.ReorderQty,
            v.Attributes.Where(a => values.ContainsKey(a.AttributeValueId))
                .Select(a => { var x = values[a.AttributeValueId]; return new AttributeValueRefDto(a.AttributeValueId, x.AttrName, x.Code, x.Value); })
                .ToList())).ToList();

        var prices = variants.Select(v => v.Price).DefaultIfEmpty(0m).ToList();

        return new ProductDto(
            p.Id, p.Code, p.Name, p.Description,
            p.CategoryId, p.Category?.Name,
            p.BrandId, p.BrandId is int b && brands.TryGetValue(b, out var bn) ? bn : null,
            p.BaseUnitId, p.BaseUnitId is int u && units.TryGetValue(u, out var un) ? un : null,
            p.TaxId, p.TaxId is int t && taxes.TryGetValue(t, out var tn) ? tn : null,
            p.Status,
            primary is null ? null : "/" + primary.StoredPath,
            images, variants,
            prices.Min(), prices.Max(),
            variants.Sum(v => v.Stock), variants.Count,
            p.CreatedAt, p.ModifiedAt, p.CreatedBy);
    }

    private static decimal? ParseDecimal(string? s, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(s)) { if (required) throw new InvalidOperationException($"{field} is required."); return null; }
        if (!decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{field} '{s}' is not a valid number.");
        return v;
    }
    private static int? ParseInt(string? s, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{field} '{s}' is not a valid whole number.");
        return v;
    }
    private static ProductStatus ParseStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ProductStatus.Aktif;
        if (Enum.TryParse<ProductStatus>(s.Trim(), ignoreCase: true, out var status)) return status;
        throw new InvalidOperationException($"Status '{s}' is invalid (use: Aktif, Nonaktif, Habis, Arsip).");
    }
}
