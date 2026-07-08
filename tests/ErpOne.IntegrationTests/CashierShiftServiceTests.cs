using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashierShifts;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CashierShiftServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CashierShiftServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> SeedWarehouseAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wh = new Warehouse($"WH{id}", $"Gudang {id}", null, true, false);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();
        return wh.Id;
    }

    private static string NewUser() => "u-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Open_generates_daily_number_and_open_query_returns_it()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 100_000m));
        Assert.StartsWith("SHIFT-", opened.ShiftNumber);
        Assert.Equal("Open", opened.Status);
        Assert.Equal(100_000m, opened.OpeningFloat);
        Assert.Equal(100_000m, opened.ExpectedCash);

        var current = await svc.GetOpenShiftByWarehouseAsync(wh);
        Assert.NotNull(current);
        Assert.Equal(opened.Id, current!.Id);

        Assert.Contains(await svc.GetOpenShiftsAsync(), s => s.Id == opened.Id);
    }

    [Fact]
    public async Task Open_rejects_second_open_shift_for_same_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();

        await svc.OpenAsync(NewUser(), "Rani", new OpenShiftRequest(wh, 0m));
        // user lain pun ditolak di gudang yang sama
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.OpenAsync(NewUser(), "Sari", new OpenShiftRequest(wh, 0m)));
    }

    [Fact]
    public async Task Open_allows_same_user_at_a_different_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh1 = await SeedWarehouseAsync(sp);
        var wh2 = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        var a = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh1, 0m));
        var b = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh2, 0m));
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Open_rejects_inactive_or_missing_warehouse()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ICashierShiftService>();
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.OpenAsync(NewUser(), "Rani", new OpenShiftRequest(999999, 0m)));
    }

    [Fact]
    public async Task Close_computes_variance_and_only_owner_can_close()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();
        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 100_000m));

        // pemilik lain tidak boleh menutup
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CloseAsync(opened.Id, NewUser(), new CloseShiftRequest(100_000m, null)));

        var ok = await svc.CloseAsync(opened.Id, user, new CloseShiftRequest(97_000m, "kurang"));
        Assert.True(ok);
        var reloaded = await svc.GetByIdAsync(opened.Id);
        Assert.Equal("Closed", reloaded!.Status);
        Assert.Equal(97_000m, reloaded.CountedCash);
        Assert.Equal(-3_000m, reloaded.CashVariance);

        // tak bisa tutup dua kali
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CloseAsync(opened.Id, user, new CloseShiftRequest(97_000m, null)));
    }

    [Fact]
    public async Task RecordSale_totals_surface_in_get_by_id()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var wh = await SeedWarehouseAsync(sp);
        var svc = sp.GetRequiredService<ICashierShiftService>();
        var user = NewUser();

        // butuh PaymentMethod utk MethodTotals join nama
        var pmCash = new PaymentMethod("CASH" + Guid.NewGuid().ToString("N")[..4], "Tunai", PaymentType.Tunai, true);
        db.PaymentMethods.Add(pmCash);
        await db.SaveChangesAsync();

        var opened = await svc.OpenAsync(user, "Rani", new OpenShiftRequest(wh, 0m));

        // simulasikan D2: panggil domain RecordSale langsung lalu simpan
        var shift = await db.CashierShifts.FindAsync(opened.Id);
        shift!.RecordSale(pmCash.Id, isCash: true, amount: 25_000m);
        shift.RecordSale(pmCash.Id, isCash: true, amount: 15_000m);
        await db.SaveChangesAsync();

        var dto = await svc.GetByIdAsync(opened.Id);
        Assert.Equal(40_000m, dto!.CashSalesTotal);
        Assert.Equal(40_000m, dto.TotalSalesAmount);
        Assert.Equal(2, dto.TransactionCount);
        var mt = Assert.Single(dto.MethodTotals);
        Assert.Equal("Tunai", mt.MethodName);
        Assert.Equal(40_000m, mt.TotalAmount);
        Assert.Equal(2, mt.TransactionCount);
    }
}
