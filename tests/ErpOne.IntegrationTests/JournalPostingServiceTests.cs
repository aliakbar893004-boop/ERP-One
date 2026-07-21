using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpOne.IntegrationTests;

public class JournalPostingServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public JournalPostingServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<int> AccountId(AppDbContext db, string code) =>
        await db.Accounts.Where(a => a.Code == code).Select(a => a.Id).FirstAsync();

    [Fact]
    public async Task Expense_posts_debit_expense_credit_cash()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 250_000m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await poster.PostExpenseAsync(exp);

        var je = await db.JournalEntries.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id);
        Assert.NotNull(je);
        Assert.Equal(JournalSource.System, je!.Source);
        Assert.Equal(JournalEntryStatus.Posted, je.Status);
        Assert.Equal(250_000m, je.TotalDebit);
        Assert.Equal(je.TotalDebit, je.TotalCredit);
        Assert.Contains(je.Lines, l => l.AccountId == beban && l.Debit == 250_000m);
        Assert.Contains(je.Lines, l => l.AccountId == cash.GlAccountId && l.Credit == 250_000m);
    }

    [Fact]
    public async Task Missing_mapping_throws_and_writes_no_entry()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var cat = new ExpenseCategory($"EC{Sfx()}", "NoGL", true, null);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 100m, null, "x", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => poster.PostExpenseAsync(exp));
        Assert.False(await db.JournalEntries.AnyAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id));
    }

    [Fact]
    public async Task Reverse_creates_mirror_and_nets_to_zero()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 500m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();
        await poster.PostExpenseAsync(exp);

        await poster.ReverseForAsync("Expense", exp.Id, DateTime.Today, "void");

        var entries = await db.JournalEntries
            .Where(x => x.SourceId == exp.Id && (x.SourceType == "Expense" || x.SourceType == "ExpenseVoid"))
            .Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, entries.Count);
        var net = entries.SelectMany(e => e.Lines).Where(l => l.AccountId == beban).Sum(l => l.Debit - l.Credit);
        Assert.Equal(0m, net);
    }

    [Fact]
    public async Task Posting_is_idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var poster = sp.GetRequiredService<IJournalPostingService>();

        var beban = await AccountId(db, "6100");
        var cat = new ExpenseCategory($"EC{Sfx()}", "Gaji", true, beban);
        var cash = await db.CashBankAccounts.FirstAsync(a => a.Code == "CASH");
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        var exp = new Expense($"EXP{Sfx()}", DateTime.Today, cash.Id, cat.Id, "IDR", 700m, null, "Gaji", null);
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();

        await poster.PostExpenseAsync(exp);
        await poster.PostExpenseAsync(exp);   // second call must be a no-op

        var count = await db.JournalEntries.CountAsync(x => x.SourceType == "Expense" && x.SourceId == exp.Id);
        Assert.Equal(1, count);
    }
}
