using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Customers;
using ErpOne.Application.PriceLists;
using ErpOne.Application.Warehouses;
using Xunit;

namespace ErpOne.IntegrationTests;

/// <summary>Assignment price list harus selamat melewati DTO → service → entity → DTO.
/// Tanpa test ini, parameter yang lupa diteruskan tidak terdeteksi sampai halaman dibuka.</summary>
public class PricingAssignmentTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PricingAssignmentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Customer_price_list_roundtrips_through_service()
    {
        using var scope = _factory.Services.CreateScope();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest("ASG-C", "Assign Customer", null, true, []));

        var created = await customers.CreateAsync(new CreateCustomerRequest("ASG-C-1", "Assign Customer 1",
            null, null, null, null, null, 30, "IDR", 0m, true, list.Id));
        Assert.Equal(list.Id, created.PriceListId);

        var fetched = await customers.GetByIdAsync(created.Id);
        Assert.Equal(list.Id, fetched!.PriceListId);

        // Dikosongkan kembali → null, bukan 0.
        await customers.UpdateAsync(created.Id, new UpdateCustomerRequest("ASG-C-1", "Assign Customer 1",
            null, null, null, null, null, 30, "IDR", 0m, true, null));
        Assert.Null((await customers.GetByIdAsync(created.Id))!.PriceListId);
    }

    [Fact]
    public async Task Warehouse_default_price_list_roundtrips_through_service()
    {
        using var scope = _factory.Services.CreateScope();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var warehouses = scope.ServiceProvider.GetRequiredService<IWarehouseService>();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest("ASG-W", "Assign Warehouse", null, true, []));

        var created = await warehouses.CreateAsync(new CreateWarehouseRequest("ASG-W-1", "Assign WH 1",
            null, true, false, list.Id));
        Assert.Equal(list.Id, created.DefaultPriceListId);

        var fetched = await warehouses.GetByIdAsync(created.Id);
        Assert.Equal(list.Id, fetched!.DefaultPriceListId);

        await warehouses.UpdateAsync(created.Id, new UpdateWarehouseRequest("ASG-W-1", "Assign WH 1",
            null, true, false, null));
        Assert.Null((await warehouses.GetByIdAsync(created.Id))!.DefaultPriceListId);
    }

    [Fact]
    public async Task Assigned_price_list_cannot_be_deleted_until_unassigned()
    {
        using var scope = _factory.Services.CreateScope();
        var priceLists = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var list = await priceLists.CreateAsync(new CreatePriceListRequest("ASG-DEL", "Assign Delete", null, true, []));
        var cust = await customers.CreateAsync(new CreateCustomerRequest("ASG-DEL-1", "Assign Delete 1",
            null, null, null, null, null, 30, "IDR", 0m, true, list.Id));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => priceLists.DeleteAsync(list.Id));

        // Setelah di-unassign, penghapusan boleh.
        await customers.UpdateAsync(cust.Id, new UpdateCustomerRequest("ASG-DEL-1", "Assign Delete 1",
            null, null, null, null, null, 30, "IDR", 0m, true, null));
        Assert.True(await priceLists.DeleteAsync(list.Id));
    }
}
