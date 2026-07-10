using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Numbering;
using Xunit;

namespace ErpOne.IntegrationTests;

public class NumberSequenceServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public NumberSequenceServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task GetAll_returns_seeded_sequences_with_samples()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();

        var all = await svc.GetAllAsync();
        Assert.Equal(11, all.Count);   // 6 core + AP invoice/payment + AR invoice/receipt + Expense
        var po = all.Single(x => x.Code == DocumentTypes.PurchaseOrder);
        Assert.StartsWith("PO-", po.Sample);
        var apv = all.Single(x => x.Code == DocumentTypes.SupplierInvoice);
        Assert.StartsWith("APV-", apv.Sample);
        var app = all.Single(x => x.Code == DocumentTypes.SupplierPayment);
        Assert.StartsWith("APP-", app.Sample);
    }

    [Fact]
    public async Task Update_changes_padding()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();

        var po = (await svc.GetAllAsync()).Single(x => x.Code == DocumentTypes.PurchaseOrder);
        var ok = await svc.UpdateAsync(po.Id, new UpdateNumberSequenceRequest("PO", "yyyyMM", 6, "Monthly", "-"));
        Assert.True(ok);

        var updated = await svc.GetByIdAsync(po.Id);
        Assert.Equal(6, updated!.Padding);
    }
}
