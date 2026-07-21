using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class LedgerServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public LedgerServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<(int cash, int capital)> SeedAccountsAsync(IServiceProvider sp)
    {
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var cash = await acc.CreateAsync(new CreateAccountRequest($"K{id}", "Kas", AccountType.Asset, null, true, null));
        var capital = await acc.CreateAsync(new CreateAccountRequest($"M{id}", "Modal", AccountType.Equity, null, true, null));
        return (cash.Id, capital.Id);
    }

    [Fact]
    public async Task Trial_balance_totals_match_and_place_accounts_on_normal_side()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening",
            [new JournalEntryLineInput(cash, 1000m, 0m, null), new JournalEntryLineInput(capital, 0m, 1000m, null)]));
        await je.PostAsync(created.Id);

        var ledger = sp.GetRequiredService<ILedgerService>();
        var tb = await ledger.GetTrialBalanceAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        var cashRow = Assert.Single(tb.Rows, r => r.AccountId == cash);
        var capRow = Assert.Single(tb.Rows, r => r.AccountId == capital);
        Assert.Equal(1000m, cashRow.Debit);
        Assert.Equal(0m, cashRow.Credit);
        Assert.Equal(1000m, capRow.Credit);
        Assert.Equal(0m, capRow.Debit);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
    }

    [Fact]
    public async Task General_ledger_running_balance_and_reversal_nets_to_zero()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var je = sp.GetRequiredService<IJournalEntryService>();
        var created = await je.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening",
            [new JournalEntryLineInput(cash, 750m, 0m, null), new JournalEntryLineInput(capital, 0m, 750m, null)]));
        await je.PostAsync(created.Id);
        await je.ReverseAsync(created.Id, DateTime.Today, "undo");

        var ledger = sp.GetRequiredService<ILedgerService>();
        var gl = await ledger.GetGeneralLedgerAsync(cash, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        Assert.NotNull(gl);
        // Two lines: +750 then -750 → closing balance 0.
        Assert.Equal(2, gl!.Lines.Count);
        Assert.Equal(0m, gl.ClosingBalance);
    }
}
