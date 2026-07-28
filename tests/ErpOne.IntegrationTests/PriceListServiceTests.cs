using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PriceLists;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PriceListServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PriceListServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> NewVariantAsync(AppDbContext db, string suffix, decimal price)
    {
        var product = new Product($"PL-P-{suffix}", $"PL Probe {suffix}", null, null, null, null, null,
            ProductStatus.Aktif);
        var variant = product.AddVariant($"PL-SKU-{suffix}", null, price, null, 0m, null, null, true);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    [Fact]
    public async Task Create_normalizes_code_and_persists_tiers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "CREATE", 100_000m);

        var created = await svc.CreateAsync(new CreatePriceListRequest(" pl-create ", "Create List", null, true,
        [
            new PriceListLineRequest(variantId, 1, 90_000m),
            new PriceListLineRequest(variantId, 10, 85_000m),
        ]));

        Assert.Equal("PL-CREATE", created.Code);
        Assert.Equal(2, created.Lines.Count);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(2, fetched!.Lines.Count);
        Assert.Contains(fetched.Lines, l => l.MinQty == 10 && l.UnitPrice == 85_000m);
        Assert.Contains(fetched.Lines, l => l.VariantSku == "PL-SKU-CREATE");
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        await svc.CreateAsync(new CreatePriceListRequest("PL-DUP", "Dup", null, true, []));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreatePriceListRequest("pl-dup", "Dup Again", null, true, [])));
    }

    [Fact]
    public async Task Duplicate_tier_in_request_is_rejected_by_validator()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "DUPTIER", 100_000m);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreatePriceListRequest("PL-DUPTIER", "Dup Tier", null, true,
            [
                new PriceListLineRequest(variantId, 1, 90_000m),
                new PriceListLineRequest(variantId, 1, 80_000m),
            ])));
    }

    [Fact]
    public async Task Unknown_variant_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreatePriceListRequest("PL-UNKNOWN", "Unknown", null, true,
                [new PriceListLineRequest(999_999, 1, 90_000m)])));
    }

    [Fact]
    public async Task Update_replaces_lines_wholesale()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "UPD", 100_000m);
        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-UPD", "Upd", null, true,
            [new PriceListLineRequest(variantId, 1, 90_000m), new PriceListLineRequest(variantId, 10, 85_000m)]));

        var ok = await svc.UpdateAsync(created.Id, new UpdatePriceListRequest("PL-UPD", "Upd", null, true,
            [new PriceListLineRequest(variantId, 5, 88_000m)]));

        Assert.True(ok);
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Single(fetched!.Lines);
        Assert.Equal(5, fetched.Lines[0].MinQty);
    }

    [Fact]
    public async Task Delete_is_rejected_while_referenced_by_customer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-REF-C", "Ref Customer", null, true, []));

        db.Customers.Add(new Customer("PL-C-REF", "Ref Customer", null, null, null, null, null, 0, "IDR", 0m, true, created.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Delete_is_rejected_while_referenced_by_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-REF-W", "Ref Warehouse", null, true, []));

        db.Warehouses.Add(new Warehouse("PL-WH-REF", "Ref WH", null, true, false, created.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Delete_unreferenced_list_removes_it_with_lines()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var variantId = await NewVariantAsync(db, "DEL", 100_000m);
        var created = await svc.CreateAsync(new CreatePriceListRequest("PL-DEL", "Del", null, true,
            [new PriceListLineRequest(variantId, 1, 90_000m)]));

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
        Assert.False(await db.PriceListLines.AnyAsync(l => l.PriceListId == created.Id));
    }

    [Fact]
    public async Task SearchVariants_matches_sku_and_returns_master_price()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        await NewVariantAsync(db, "SEARCH", 123_000m);

        var found = await svc.SearchVariantsAsync("PL-SKU-SEARCH");

        var option = Assert.Single(found);
        Assert.Equal("PL-SKU-SEARCH", option.Sku);
        Assert.Equal(123_000m, option.Price);
    }

    [Fact]
    public async Task GetActive_excludes_inactive_lists()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPriceListService>();

        var active = await svc.CreateAsync(new CreatePriceListRequest("PL-ACT", "Active", null, true, []));
        var inactive = await svc.CreateAsync(new CreatePriceListRequest("PL-INACT", "Inactive", null, false, []));

        var list = await svc.GetActiveAsync();

        Assert.Contains(list, x => x.Id == active.Id);
        Assert.DoesNotContain(list, x => x.Id == inactive.Id);
    }
}
