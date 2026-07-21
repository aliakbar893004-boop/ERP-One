using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Expenses;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AutoPostingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AutoPostingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Expense_create_then_void_posts_and_reverses_journal()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var expenses = sp.GetRequiredService<IExpenseService>();

        var cat = new ExpenseCategory($"EC{Sfx()}", "Ops", true,
            await db.Accounts.Where(a => a.Code == "6300").Select(a => (int?)a.Id).FirstAsync());
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var dto = await expenses.CreateAsync(new CreateExpenseRequest(DateTime.Today, 1, cat.Id, 300_000m, null, "Listrik", null));
        Assert.True(await db.JournalEntries.AnyAsync(x => x.SourceType == "Expense" && x.SourceId == dto.Id));

        await expenses.VoidAsync(dto.Id, "tester");
        Assert.True(await db.JournalEntries.AnyAsync(x => x.SourceType == "ExpenseVoid" && x.SourceId == dto.Id));
    }
}
