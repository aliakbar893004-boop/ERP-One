using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Attributes;
using ErpOne.Application.ProductCategories;
using ErpOne.Application.Products;
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ProductVariantServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ProductVariantServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_SingleVariant_GeneratesCodeAndSku()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("ELK", "Electronics", null))).Id;
        var dto = await products.CreateAsync(new CreateProductRequest(
            "Keyboard", null, catId, null, null, null, ProductStatus.Aktif,
            new[] { new VariantInput(null, 250_000m, null, 100_000m, null, null, 5, true, Array.Empty<int>()) }));

        Assert.Equal("ELK/0001", dto.Code);
        Assert.Single(dto.Variants);
        Assert.Equal("ELK/0001", dto.Variants[0].Sku); // no attributes -> sku == code
        Assert.Equal(5, dto.TotalStock);
        Assert.Equal(250_000m, dto.MinPrice);
    }

    [Fact]
    public async Task Create_MultiVariant_BuildsSuffixedSkus()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var attrs = scope.ServiceProvider.GetRequiredService<IAttributeService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("APP", "Apparel", null))).Id;
        var size = await attrs.CreateAsync(new CreateAttributeRequest("SIZE", "Size",
            new[] { new AttributeValueInput("M", "Medium"), new AttributeValueInput("L", "Large") }));
        var mId = size.Values.First(v => v.Code == "M").Id;
        var lId = size.Values.First(v => v.Code == "L").Id;

        var dto = await products.CreateAsync(new CreateProductRequest(
            "Tshirt", null, catId, null, null, null, ProductStatus.Aktif,
            new[]
            {
                new VariantInput(null, 50_000m, null, 0m, null, null, 3, true, new[] { mId }),
                new VariantInput(null, 50_000m, null, 0m, null, null, 4, true, new[] { lId }),
            }));

        Assert.Equal(2, dto.VariantCount);
        Assert.Contains(dto.Variants, v => v.Sku == "APP/0001-M");
        Assert.Contains(dto.Variants, v => v.Sku == "APP/0001-L");
        Assert.Equal(7, dto.TotalStock);
    }

    [Fact]
    public async Task Create_RequiresAtLeastOneVariant()
    {
        using var scope = _factory.Services.CreateScope();
        var cats = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var catId = (await cats.CreateAsync(new CreateProductCategoryRequest("EMP", "Empty", null))).Id;

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            products.CreateAsync(new CreateProductRequest(
                "NoVariants", null, catId, null, null, null, ProductStatus.Aktif, Array.Empty<VariantInput>())));
    }
}
