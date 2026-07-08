using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Attributes;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AttributeServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AttributeServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_WithValues_PersistsChildren()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAttributeService>();

        var created = await svc.CreateAsync(new CreateAttributeRequest("SIZE", "Ukuran",
        [
            new AttributeValueInput("S", "Small"),
            new AttributeValueInput("M", "Medium"),
            new AttributeValueInput("L", "Large"),
        ]));

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(3, fetched!.Values.Count);
        Assert.Contains(fetched.Values, v => v.Code == "M" && v.Value == "Medium");
    }

    [Fact]
    public async Task Update_ReplacesValues()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAttributeService>();

        var created = await svc.CreateAsync(new CreateAttributeRequest("COLOR", "Warna",
            [ new AttributeValueInput("RED", "Merah") ]));

        await svc.UpdateAsync(created.Id, new UpdateAttributeRequest("COLOR", "Warna",
            [ new AttributeValueInput("RED", "Merah"), new AttributeValueInput("BLU", "Biru") ]));

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(2, fetched!.Values.Count);
    }
}
