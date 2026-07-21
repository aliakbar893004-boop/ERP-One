using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class FinancialStatementServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FinancialStatementServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static async Task<int> Acc(AppDbContext db, string code) =>
        await db.Accounts.Where(a => a.Code == code).Select(a => a.Id).FirstAsync();

    private static async Task PostAsync(IServiceProvider sp, string desc, int drAcc, int crAcc, decimal amount)
    {
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, desc,
            [new JournalEntryLineInput(drAcc, amount, 0m, null), new JournalEntryLineInput(crAcc, 0m, amount, null)]));
        await je.PostAsync(created.Id);
    }

    [Fact]
    public async Task Balance_sheet_balances_and_income_statement_nets()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var kas = await Acc(db, "1110");
        var modal = await Acc(db, "3100");
        var penjualan = await Acc(db, "4100");
        var beban = await Acc(db, "6100");

        var svc = sp.GetRequiredService<IFinancialStatementService>();
        var from = DateTime.Today.AddDays(-1);
        var to = DateTime.Today.AddDays(1);

        // Deltas keep the assertions robust against other data in the shared DB.
        var plBefore = await svc.GetIncomeStatementAsync(from, to);
        var bsBefore = await svc.GetBalanceSheetAsync(DateTime.Today);

        await PostAsync(sp, "Opening capital", kas, modal, 10_000_000m);
        await PostAsync(sp, "Cash sale", kas, penjualan, 3_000_000m);
        await PostAsync(sp, "Salary", beban, kas, 500_000m);

        var pl = await svc.GetIncomeStatementAsync(from, to);
        Assert.Equal(3_000_000m, pl.TotalRevenue - plBefore.TotalRevenue);
        Assert.Equal(500_000m, pl.TotalExpense - plBefore.TotalExpense);
        Assert.Equal(2_500_000m, pl.NetIncome - plBefore.NetIncome);

        var bs = await svc.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(bs.IsBalanced);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilitiesAndEquity);
        Assert.Equal(2_500_000m, bs.CurrentEarnings - bsBefore.CurrentEarnings);
        // Current earnings line present in equity.
        Assert.Contains(bs.Equity.Lines, l => !l.IsHeader && l.AccountId == 0);
    }

    [Fact]
    public async Task Zero_balance_accounts_are_omitted()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var kas = await Acc(db, "1110");
        var modal = await Acc(db, "3100");
        await PostAsync(sp, "Cap", kas, modal, 1_000_000m);

        var bs = await sp.GetRequiredService<IFinancialStatementService>().GetBalanceSheetAsync(DateTime.Today);
        var bank = await Acc(db, "1120");
        Assert.DoesNotContain(bs.Assets.Lines, l => l.AccountId == bank);
    }
}
