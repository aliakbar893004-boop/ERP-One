using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.PaymentMethods;
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PaymentMethodServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PaymentMethodServiceTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.InitializeDatabase(); }

    [Fact]
    public async Task Create_PersistsType()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();
        var created = await svc.CreateAsync(new CreatePaymentMethodRequest("QRIS", "QRIS", PaymentType.QRIS, true));
        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal(PaymentType.QRIS, fetched!.Type);
        Assert.True(fetched.IsActive);
    }
}
