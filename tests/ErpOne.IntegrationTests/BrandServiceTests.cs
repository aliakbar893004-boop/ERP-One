using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Brands;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class BrandServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public BrandServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBrandService>();

        var created = await svc.CreateAsync(new CreateBrandRequest("nke", "Nike", "Sportswear"));
        Assert.Equal("NKE", created.Code);             // ToUpperInvariant
        Assert.NotEqual(default, created.CreatedAt);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Nike", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBrandService>();

        await svc.CreateAsync(new CreateBrandRequest("DUP", "First", null));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CreateBrandRequest("dup", "Second", null)));
    }
}
