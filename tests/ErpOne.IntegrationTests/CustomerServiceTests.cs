using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Customers;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CustomerServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CustomerServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static CreateCustomerRequest New(string code, string name, decimal limit = 1000m) =>
        new(code, name, "Sari", "0813", "c@d.com", "Jl. Melati", "02.345", 14, "IDR", limit, true);

    [Fact]
    public async Task Create_Then_GetById_Roundtrips_AndNormalizesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await svc.CreateAsync(New("cust-x", "Toko Jaya"));
        Assert.Equal("CUST-X", created.Code);
        Assert.Equal(1000m, created.CreditLimit);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Toko Jaya", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        await svc.CreateAsync(New("CUST-DUP", "First"));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(New("cust-dup", "Second")));
    }

    [Fact]
    public async Task Update_And_Delete_Work()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await svc.CreateAsync(New("CUST-UPD", "Awal"));
        var ok = await svc.UpdateAsync(created.Id,
            new UpdateCustomerRequest("CUST-UPD", "Berubah", null, null, null, null, null, 30, "IDR", 5000m, false));
        Assert.True(ok);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("Berubah", fetched!.Name);
        Assert.Equal(5000m, fetched.CreditLimit);

        Assert.True(await svc.DeleteAsync(created.Id));
        Assert.Null(await svc.GetByIdAsync(created.Id));
    }
}
