using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class JournalEntryServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public JournalEntryServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Seed two postable accounts, return their ids.
    private static async Task<(int cash, int capital)> SeedAccountsAsync(IServiceProvider sp)
    {
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var cash = await acc.CreateAsync(new CreateAccountRequest($"K{id}", "Kas", AccountType.Asset, null, true, null));
        var capital = await acc.CreateAsync(new CreateAccountRequest($"M{id}", "Modal", AccountType.Equity, null, true, null));
        return (cash.Id, capital.Id);
    }

    [Fact]
    public async Task Draft_may_be_unbalanced_but_post_requires_balance()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        // Unbalanced draft is allowed (single line, no balance).
        var draft = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Unbalanced", [new JournalEntryLineInput(cash, 100m, 0m, null)]));
        Assert.Equal(JournalEntryStatus.Draft, draft.Status);

        // Posting an unbalanced/1-line entry fails.
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => svc.PostAsync(draft.Id));
    }

    [Fact]
    public async Task Post_balanced_entry_succeeds_and_locks()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Opening balance",
            [new JournalEntryLineInput(cash, 1000m, 0m, "cash"), new JournalEntryLineInput(capital, 0m, 1000m, "equity")]));

        await svc.PostAsync(je.Id);
        var posted = await svc.GetByIdAsync(je.Id);
        Assert.Equal(JournalEntryStatus.Posted, posted!.Status);
        Assert.Equal(1000m, posted.TotalDebit);
        Assert.Equal(1000m, posted.TotalCredit);

        // Posted entry can no longer be edited or deleted.
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            svc.UpdateDraftAsync(je.Id, new CreateJournalEntryRequest(DateTime.Today, "x",
                [new JournalEntryLineInput(cash, 1m, 0m, null), new JournalEntryLineInput(capital, 0m, 1m, null)])));
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => svc.DeleteDraftAsync(je.Id));
    }

    [Fact]
    public async Task Post_rejects_non_postable_account()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var acc = sp.GetRequiredService<IAccountService>();
        var id = Sfx();
        var header = await acc.CreateAsync(new CreateAccountRequest($"H{id}", "Header", AccountType.Asset, null, false, null));
        var leaf = await acc.CreateAsync(new CreateAccountRequest($"L{id}", "Leaf", AccountType.Equity, null, true, null));
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Bad account",
            [new JournalEntryLineInput(header.Id, 50m, 0m, null), new JournalEntryLineInput(leaf.Id, 0m, 50m, null)]));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.PostAsync(je.Id));
    }

    [Fact]
    public async Task Reverse_creates_mirror_entry_and_marks_original()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (cash, capital) = await SeedAccountsAsync(sp);
        var svc = sp.GetRequiredService<IJournalEntryService>();

        var je = await svc.CreateDraftAsync(new CreateJournalEntryRequest(
            DateTime.Today, "Original",
            [new JournalEntryLineInput(cash, 500m, 0m, null), new JournalEntryLineInput(capital, 0m, 500m, null)]));
        await svc.PostAsync(je.Id);

        var reversal = await svc.ReverseAsync(je.Id, DateTime.Today, "mistake");

        Assert.Equal(JournalEntryStatus.Posted, reversal.Status);
        Assert.Equal(je.Id, reversal.ReversalOfEntryId);
        // Mirror: cash now credited, capital debited.
        Assert.Contains(reversal.Lines, l => l.AccountId == cash && l.Credit == 500m);
        Assert.Contains(reversal.Lines, l => l.AccountId == capital && l.Debit == 500m);

        var original = await svc.GetByIdAsync(je.Id);
        Assert.Equal(JournalEntryStatus.Reversed, original!.Status);
        Assert.Equal(reversal.Id, original.ReversedByEntryId);

        // Cannot reverse twice.
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.ReverseAsync(je.Id, DateTime.Today, null));
    }
}
