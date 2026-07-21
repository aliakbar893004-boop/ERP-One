using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Stock;
using ErpOne.Application.StockOpnames;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockOpnameServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockOpnameServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds 1 warehouse, 1 product/variant, opening stock in that warehouse. Returns (warehouseId, variantId).
    private static async Task<(int warehouseId, int variantId)> SeedAsync(IServiceProvider sp, int opening)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var wh = new Warehouse($"WH{id}", $"Wh {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        db.Warehouses.Add(wh); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, wh.Id, opening));
        await db.SaveChangesAsync();
        return (wh.Id, v.Id);
    }

    // CustomWebApplicationFactory does NOT run BootstrapSeeder, so seed the approval chain manually.
    private static async Task SeedChainAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockOpname))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockOpname, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    private static async Task SetCountAsync(IServiceProvider sp, IStockOpnameService svc, int opnameId, int physicalQty)
    {
        var dto = await svc.GetByIdAsync(opnameId);
        var counts = dto!.Lines.Select(l => new StockOpnameCountInput(l.Id, physicalQty)).ToList();
        await svc.UpdateAsync(opnameId, new UpdateStockOpnameRequest(dto.OpnameDate, dto.Notes, counts));
    }

    [Fact]
    public async Task Approve_posts_surplus_variance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 120);
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);
        Assert.Equal(120, await stock.GetOnHandAsync(variantId, wh));

        var db = sp.GetRequiredService<AppDbContext>();
        var moves = await db.StockMovements.Where(m => m.RefType == "StockOpname" && m.RefId == created.Id).ToListAsync();
        Assert.Single(moves);
        Assert.Equal(20, moves[0].Quantity);
    }

    [Fact]
    public async Task Approve_posts_shortage_variance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 80);
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        Assert.Equal(80, await stock.GetOnHandAsync(variantId, wh));
    }

    [Fact]
    public async Task Zero_variance_line_is_a_noop()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 100); // equal to system/on-hand
        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
        var db = sp.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StockMovements.Where(m => m.RefType == "StockOpname" && m.RefId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task Variance_is_computed_against_live_on_hand_at_post()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (wh, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockOpnameService>();
        var stock = sp.GetRequiredService<IStockService>();

        // Draft snapshots SystemQty=100.
        var created = await svc.CreateAsync(new CreateStockOpnameRequest(DateTime.Today, wh, null));
        await SetCountAsync(sp, svc, created.Id, 100); // counted 100

        // Stock drifts down to 90 AFTER the snapshot, BEFORE post.
        await stock.RecordAdjustmentAsync(new StockAdjustmentRequest(wh, DateTime.Today, "drift",
            [new StockAdjustmentLine(variantId, -10, 1000m, "drift")]));
        Assert.Equal(90, await stock.GetOnHandAsync(variantId, wh));

        await svc.SubmitAsync(created.Id);
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        // delta = 100 - 90 = +10 → on-hand ends at 100 (matches the count), not 100-100=0.
        Assert.Equal(100, await stock.GetOnHandAsync(variantId, wh));
    }
}
