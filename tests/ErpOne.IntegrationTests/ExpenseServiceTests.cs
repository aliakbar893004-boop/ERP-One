using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.CashBank;
using ErpOne.Application.Expenses;
using FluentValidation;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ExpenseServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ExpenseServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<(int categoryId, int accountId)> SeedAsync(IServiceProvider sp)
    {
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var cat = sp.GetRequiredService<IExpenseCategoryService>();
        var category = await cat.CreateAsync(new CreateExpenseCategoryRequest($"EC{id}", $"Utilities {id}", true));
        var acc = sp.GetRequiredService<ICashBankAccountService>();
        var account = await acc.CreateAsync(new CreateCashBankAccountRequest($"CB{id}", $"Cash {id}", "Cash", "IDR", 5000m, null, null, null, true));
        return (category.Id, account.Id);
    }

    [Fact]
    public async Task Category_create_normalizes_and_rejects_duplicate()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var created = await svc.CreateAsync(new CreateExpenseCategoryRequest("rent", "Office Rent", true));
        Assert.Equal("RENT", created.Code);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CreateExpenseCategoryRequest("rent", "Dup", true)));
    }

    [Fact]
    public async Task Create_posts_and_reduces_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (categoryId, accountId) = await SeedAsync(scope.ServiceProvider);
        var exp = scope.ServiceProvider.GetRequiredService<IExpenseService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();

        var e = await exp.CreateAsync(new CreateExpenseRequest(new DateTime(2026, 7, 8), accountId, categoryId, 1500m, "PLN", "Electricity", null));
        Assert.StartsWith("EXP-202607-", e.ExpenseNumber);
        Assert.Equal("Posted", e.Status);
        Assert.Equal(3500m, await acc.GetBalanceAsync(accountId));   // opening 5000 − 1500
    }

    [Fact]
    public async Task Amount_not_positive_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var (categoryId, accountId) = await SeedAsync(scope.ServiceProvider);
        var exp = scope.ServiceProvider.GetRequiredService<IExpenseService>();
        await Assert.ThrowsAsync<ValidationException>(() => exp.CreateAsync(
            new CreateExpenseRequest(new DateTime(2026, 7, 8), accountId, categoryId, 0m, null, "Bad", null)));
    }

    [Fact]
    public async Task Void_restores_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var (categoryId, accountId) = await SeedAsync(scope.ServiceProvider);
        var exp = scope.ServiceProvider.GetRequiredService<IExpenseService>();
        var acc = scope.ServiceProvider.GetRequiredService<ICashBankAccountService>();

        var e = await exp.CreateAsync(new CreateExpenseRequest(new DateTime(2026, 7, 8), accountId, categoryId, 1500m, null, "Electricity", null));
        await exp.VoidAsync(e.Id, "tester");

        var voided = await exp.GetByIdAsync(e.Id);
        Assert.Equal("Voided", voided!.Status);
        Assert.Equal(5000m, await acc.GetBalanceAsync(accountId));   // back to opening
    }
}
