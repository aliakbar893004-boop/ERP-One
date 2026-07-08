using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Suppliers;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class SupplierServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SupplierServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static CreateSupplierRequest New(string code, string name) =>
        new(code, name, "Budi", "0812", "a@b.com", "Jl. Mawar",
            "01.234", 30, "IDR", "BCA", "123", "PT SM", true);

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        var created = await svc.CreateAsync(New("sup-x", "PT Sumber"));
        Assert.Equal("SUP-X", created.Code);
        Assert.Equal(30, created.PaymentTermDays);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("PT Sumber", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        await svc.CreateAsync(New("SUP-DUP", "First"));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(New("sup-dup", "Second")));
    }

    [Fact]
    public async Task Update_And_Delete_Work()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISupplierService>();

        var created = await svc.CreateAsync(New("SUP-UPD", "Awal"));
        var ok = await svc.UpdateAsync(created.Id,
            new UpdateSupplierRequest("SUP-UPD", "Berubah", null, null, null, null, null, 0, "USD", null, null, null, false));
        Assert.True(ok);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Berubah", fetched!.Name);
        Assert.Equal("USD", fetched.DefaultCurrency);

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
    }
}
