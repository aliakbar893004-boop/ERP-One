using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.ProductCategories;
using ErpOne.Application.Products;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using Xunit;

namespace ErpOne.IntegrationTests;

public class IdentityAndServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IdentityAndServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private IServiceScope Scope() => _factory.Services.CreateScope();

    [Fact]
    public async Task Identity_CreateRoleUserAndAssign_Works()
    {
        using var scope = Scope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleResult = await roles.CreateAsync(new ApplicationRole("Managers") { Description = "Pengelola" });
        Assert.True(roleResult.Succeeded);

        var user = new ApplicationUser { UserName = "alice", Email = "alice@local", DisplayName = "Alice", IsActive = true };
        var createResult = await users.CreateAsync(user, "Passw0rd!");
        Assert.True(createResult.Succeeded);
        Assert.NotEqual(default, user.CreatedAt); // audit ter-stempel

        await users.AddToRoleAsync(user, "Managers");
        Assert.True(await users.IsInRoleAsync(user, "Managers"));

        var found = await users.FindByNameAsync("alice");
        Assert.NotNull(found);
        Assert.Equal("Alice", found!.DisplayName);
    }

    [Fact]
    public async Task Identity_PasswordSignIn_Succeeds_WithCorrectPassword()
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = "bob", DisplayName = "Bob", IsActive = true };
        await users.CreateAsync(user, "Secret#123");

        Assert.True((await signIn.CheckPasswordSignInAsync(user, "Secret#123", false)).Succeeded);
        Assert.False((await signIn.CheckPasswordSignInAsync(user, "wrong", false)).Succeeded);
    }

    [Fact]
    public async Task Products_GetPaged_RespectsPageSizeAndSearch()
    {
        using var scope = Scope();
        var categories = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var categoryId = (await categories.CreateAsync(new CreateProductCategoryRequest("ITM", "Items", null))).Id;

        for (var i = 1; i <= 15; i++)
            await products.CreateAsync(new CreateProductRequest(
                $"Item {i}", null, categoryId, null, null, null, ProductStatus.Aktif,
                new[] { new VariantInput(null, i, null, 0m, null, null, i, true, Array.Empty<int>()) }));

        var firstPage = await products.GetPagedAsync(1, 10);
        Assert.Equal(10, firstPage.Items.Count);
        Assert.True(firstPage.Total >= 15);
        Assert.True(firstPage.HasNext);
        Assert.NotEqual(default, firstPage.Items[0].CreatedAt);

        var search = await products.GetPagedAsync(1, 10, "Item 1");
        Assert.NotEmpty(search.Items);
        Assert.All(search.Items, x => Assert.Contains("Item 1", x.Name));
    }

    [Fact]
    public async Task Products_Dashboard_AggregatesAndTranslates()
    {
        using var scope = Scope();
        var categories = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var categoryId = (await categories.CreateAsync(new CreateProductCategoryRequest("DSH", "Dashboard", null))).Id;

        await products.CreateAsync(new CreateProductRequest("In stock", null, categoryId, null, null, null, ProductStatus.Aktif,
            new[] { new VariantInput(null, 100m, null, 100m, null, null, 50, true, Array.Empty<int>()) }));
        await products.CreateAsync(new CreateProductRequest("Low stock", null, categoryId, null, null, null, ProductStatus.Aktif,
            new[] { new VariantInput(null, 200m, null, 200m, null, null, 3, true, Array.Empty<int>(), null, 5) })); // ReorderLevel 5, qty 3 → low
        await products.CreateAsync(new CreateProductRequest("No stock", null, categoryId, null, null, null, ProductStatus.Habis,
            new[] { new VariantInput(null, 300m, null, 0m, null, null, 0, true, Array.Empty<int>()) }));

        var d = await products.GetDashboardAsync();

        Assert.True(d.TotalProducts >= 3);
        Assert.True(d.TotalCategories >= 1);
        Assert.True(d.TotalStock >= 53);
        Assert.True(d.InventoryValue >= 100m * 50 + 200m * 3);
        Assert.True(d.LowStockCount >= 1);
        Assert.Contains(d.ByCategory, c => c.CategoryName == "Dashboard");
        // "Low stock" product has qty 3 <= its variant's ReorderLevel (5), so it must appear in
        // the low-stock list. "No stock" has no ProductStock row (opening stock 0 is never
        // recorded) so it does not appear in the stock-based low-stock list.
        Assert.Contains(d.LowStock, i => i.Name == "Low stock");
    }
}
