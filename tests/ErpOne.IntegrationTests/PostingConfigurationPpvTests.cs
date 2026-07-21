using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PostingConfigurationPpvTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PostingConfigurationPpvTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Ppv_account_5150_seeded_and_mapped()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var acc5150 = await db.Accounts.SingleOrDefaultAsync(a => a.Code == "5150");
        Assert.NotNull(acc5150);
        Assert.True(acc5150!.IsPostable);

        var cfg = await sp.GetRequiredService<IPostingConfigurationService>().GetAsync();
        Assert.Equal(acc5150.Id, cfg.PurchasePriceVarianceAccountId);
    }
}
