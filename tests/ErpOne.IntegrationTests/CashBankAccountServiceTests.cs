using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class CashBankAccountServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CashBankAccountServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Seeded_default_cash_account_exists()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();
        var all = await svc.GetAllAsync();
        Assert.Contains(all, a => a.Code == "CASH" && a.Type == "Cash");
    }

    [Fact]
    public async Task Create_bank_account_persists_bank_fields_and_normalizes_code()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();

        var created = await svc.CreateAsync(new CreateCashBankAccountRequest(
            "bca-01", "BCA Operational", "Bank", "IDR", 1000m, "BCA", "1234567890", "PT ERP One", true));

        Assert.Equal("BCA-01", created.Code);
        Assert.Equal("Bank", created.Type);
        Assert.Equal("1234567890", created.AccountNumber);

        var fetched = await svc.GetByIdAsync(created.Id);
        Assert.Equal("BCA", fetched!.BankName);
    }

    [Fact]
    public async Task Create_duplicate_code_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();

        await svc.CreateAsync(new CreateCashBankAccountRequest("DUP", "First", "Cash", "IDR", 0m, null, null, null, true));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CreateCashBankAccountRequest("dup", "Second", "Cash", "IDR", 0m, null, null, null, true)));
    }

    [Fact]
    public async Task GetActive_excludes_inactive()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();

        var inactive = await svc.CreateAsync(new CreateCashBankAccountRequest("INACT", "Closed", "Cash", "IDR", 0m, null, null, null, false));
        var active = await svc.GetActiveAsync();
        Assert.DoesNotContain(active, a => a.Id == inactive.Id);
    }
}
