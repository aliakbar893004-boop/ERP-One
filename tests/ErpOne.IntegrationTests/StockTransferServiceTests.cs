using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.StockTransfers;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class StockTransferServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StockTransferServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seeds 2 warehouses, 1 product/variant, and opening stock at source. Returns (src, dst, variantId).
    private static async Task<(int src, int dst, int variantId)> SeedAsync(IServiceProvider sp, int openingAtSource)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Sfx();
        var src = new Warehouse($"SR{id}", $"Src {id}", null, true, false);
        var dst = new Warehouse($"DS{id}", $"Dst {id}", null, true, false);
        var cat = new ProductCategory($"CT{id}", $"Cat {id}", null);
        db.Warehouses.AddRange(src, dst); db.ProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var product = new Product($"PR{id}", $"Prod {id}", null, cat.Id, null, null, null, ProductStatus.Aktif);
        var v = product.AddVariant($"SKU{id}", null, 2000m, null, 1000m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(v.Id, src.Id, openingAtSource));
        await db.SaveChangesAsync();
        return (src.Id, dst.Id, v.Id);
    }

    // CustomWebApplicationFactory does NOT run BootstrapSeeder, so no default StockTransfer
    // chain exists. Seed one so Submit leaves the doc PendingApproval and Approve posts it.
    private static async Task SeedChainAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockTransfer))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockTransfer, 1, "Administrators"));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Approve_moves_stock_source_to_destination()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (src, dst, variantId) = await SeedAsync(sp, 100);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockTransferService>();

        var created = await svc.CreateAsync(new CreateStockTransferRequest(
            DateTime.Today, src, dst, null, [new StockTransferLineInput(variantId, 30)]));
        await svc.SubmitAsync(created.Id);
        // Pending step exists (seeded chain) → approve as admin role.
        await svc.ApproveAsync(created.Id, "admin", _ => true);

        var reloaded = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Posted", reloaded!.Status);

        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        Assert.Equal(70, await stock.GetOnHandAsync(variantId, src));
        Assert.Equal(30, await stock.GetOnHandAsync(variantId, dst));
    }

    [Fact]
    public async Task Insufficient_source_stock_is_rejected_on_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (src, dst, variantId) = await SeedAsync(sp, 10);
        await SeedChainAsync(sp);
        var svc = sp.GetRequiredService<IStockTransferService>();

        var created = await svc.CreateAsync(new CreateStockTransferRequest(
            DateTime.Today, src, dst, null, [new StockTransferLineInput(variantId, 50)]));
        await svc.SubmitAsync(created.Id);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ApproveAsync(created.Id, "admin", _ => true));

        var stock = sp.GetRequiredService<ErpOne.Application.Stock.IStockService>();
        Assert.Equal(10, await stock.GetOnHandAsync(variantId, src)); // unchanged
    }

    [Fact]
    public async Task Same_source_and_destination_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (src, _, variantId) = await SeedAsync(sp, 100);
        var svc = sp.GetRequiredService<IStockTransferService>();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreateStockTransferRequest(DateTime.Today, src, src, null, [new StockTransferLineInput(variantId, 5)])));
    }
}
