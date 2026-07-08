using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.ProductCategories;
using ErpOne.Application.Products;
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ProductApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _managerRole;

    public ProductApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
        _managerRole = factory.Services.GetRequiredService<IConfiguration>()["Identity:ManagerRole"] ?? "Administrators";
    }

    /// <summary>Client dengan role pengelola (boleh ubah data).</summary>
    private HttpClient ManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, _managerRole);
        return client;
    }

    /// <summary>Client terautentikasi tanpa role pengelola (hanya boleh lihat).</summary>
    private HttpClient ViewerClient() => _factory.CreateClient();

    /// <summary>Pastikan ada satu kategori (idempotent) dan kembalikan Id-nya — diperlukan untuk SKU produk.</summary>
    private async Task<int> EnsureCategoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var existing = await svc.GetAllAsync();
        if (existing.Count > 0) return existing[0].Id;

        var created = await svc.CreateAsync(new CreateProductCategoryRequest("CAT", "Default Category", null));
        return created.Id;
    }

    private async Task<CreateProductRequest> NewProductRequest(string name, decimal price, int stock = 1)
    {
        var categoryId = await EnsureCategoryAsync();
        return new CreateProductRequest(name, null, categoryId, null, null, null, ProductStatus.Aktif,
            new[] { new VariantInput(null, price, null, 0m, null, null, stock, true, Array.Empty<int>()) });
    }

    [Fact]
    public async Task GetAll_AsViewer_ReturnsOk()
    {
        var products = await ViewerClient().GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.NotNull(products);
    }

    [Fact]
    public async Task Create_AsManager_ThenGetById_ReturnsProduct()
    {
        var client = ManagerClient();

        var create = await client.PostAsJsonAsync("/api/products",
            await NewProductRequest("Mechanical Keyboard", 750_000m, 10));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(created.Code)); // Code di-generate otomatis

        var fetched = await client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}");
        Assert.Equal("Mechanical Keyboard", fetched!.Name);
        Assert.Equal(750_000m, fetched.MinPrice);
    }

    [Fact]
    public async Task Create_AsViewer_Returns403Forbidden()
    {
        var response = await ViewerClient().PostAsJsonAsync("/api/products",
            await NewProductRequest("Hacky", 1m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AsViewer_Returns403Forbidden()
    {
        var response = await ViewerClient().DeleteAsync("/api/products/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_StampsAuditUserFromWindowsIdentity()
    {
        var create = await ManagerClient().PostAsJsonAsync("/api/products",
            await NewProductRequest("Audited", 10m));
        var created = await create.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);

        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(@"TEST\integration-user", created.CreatedBy);
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns400()
    {
        var response = await ManagerClient().PostAsJsonAsync("/api/products",
            new CreateProductRequest("", null, 0, null, null, null, ProductStatus.Aktif, Array.Empty<VariantInput>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await ViewerClient().GetAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ThenDelete_AsManager_Works()
    {
        var client = ManagerClient();
        var categoryId = await EnsureCategoryAsync();

        // Create a stocked product to verify update works; this product has stock history so it
        // cannot be deleted (Fix A). We do the update assertions against it.
        var stockedCreate = await client.PostAsJsonAsync("/api/products",
            await NewProductRequest("Old Name", 100m, 5));
        var stocked = await stockedCreate.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(stocked);

        var update = await client.PutAsJsonAsync($"/api/products/{stocked.Id}",
            new UpdateProductRequest("New Name", null, categoryId, null, null, null, ProductStatus.Aktif,
                new[] { new VariantInput(null, 200m, null, 0m, null, null, 8, true, Array.Empty<int>()) }));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var fetched = await client.GetFromJsonAsync<ProductDto>($"/api/products/{stocked.Id}");
        Assert.Equal("New Name", fetched!.Name);
        Assert.Equal(200m, fetched.MinPrice);

        // Create a separate product with opening stock 0 — no StockMovement row is recorded for
        // opening stock = 0, so this product can be hard-deleted.
        var stocklessCreate = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest("Stockless Product", null, categoryId, null, null, null, ProductStatus.Aktif,
                new[] { new VariantInput(null, 50m, null, 0m, null, null, 0, true, Array.Empty<int>()) }));
        Assert.Equal(HttpStatusCode.Created, stocklessCreate.StatusCode);
        var stockless = await stocklessCreate.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(stockless);

        var delete = await client.DeleteAsync($"/api/products/{stockless.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await client.GetAsync($"/api/products/{stockless.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_ProductWithStockHistory_Returns400()
    {
        var client = ManagerClient();

        // Create a product with opening stock > 0 so a StockMovement row is recorded.
        var create = await client.PostAsJsonAsync("/api/products",
            await NewProductRequest("Stocked Product", 150m, 10));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);

        // Deleting a product that has stock history must be blocked with 400 BadRequest.
        var delete = await client.DeleteAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);

        // The product must still exist.
        var afterDelete = await client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, afterDelete.StatusCode);
    }
}
