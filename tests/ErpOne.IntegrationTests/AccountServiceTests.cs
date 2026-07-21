using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Accounting;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;
using Xunit;

namespace ErpOne.IntegrationTests;

public class AccountServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AccountServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Create_parent_and_child_and_get_tree()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var parent = await svc.CreateAsync(new CreateAccountRequest($"P{id}", "Aset Lancar", AccountType.Asset, null, false, null));
        var child = await svc.CreateAsync(new CreateAccountRequest($"C{id}", "Kas", AccountType.Asset, parent.Id, true, null));

        Assert.False(parent.IsPostable);
        Assert.True(child.IsPostable);
        Assert.Equal(parent.Id, child.ParentId);

        var tree = await svc.GetTreeAsync();
        var parentNode = Assert.Single(tree, n => n.Account.Id == parent.Id);
        Assert.Contains(parentNode.Children, c => c.Account.Id == child.Id);
    }

    [Fact]
    public async Task Get_postable_returns_only_active_leaf_accounts()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var header = await svc.CreateAsync(new CreateAccountRequest($"H{id}", "Header", AccountType.Asset, null, false, null));
        var leaf = await svc.CreateAsync(new CreateAccountRequest($"L{id}", "Leaf", AccountType.Asset, header.Id, true, null));
        var inactive = await svc.CreateAsync(new CreateAccountRequest($"I{id}", "Inactive", AccountType.Asset, header.Id, true, null));
        await svc.SetActiveAsync(inactive.Id, false);

        var postable = await svc.GetPostableAsync();
        Assert.Contains(postable, a => a.Id == leaf.Id);
        Assert.DoesNotContain(postable, a => a.Id == header.Id);
        Assert.DoesNotContain(postable, a => a.Id == inactive.Id);
    }

    [Fact]
    public async Task Cannot_delete_account_with_children()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var id = Sfx();

        var parent = await svc.CreateAsync(new CreateAccountRequest($"P{id}", "Parent", AccountType.Asset, null, false, null));
        await svc.CreateAsync(new CreateAccountRequest($"K{id}", "Kid", AccountType.Asset, parent.Id, true, null));

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => svc.DeleteAsync(parent.Id));
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var code = $"D{Sfx()}";

        await svc.CreateAsync(new CreateAccountRequest(code, "First", AccountType.Asset, null, true, null));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            svc.CreateAsync(new CreateAccountRequest(code, "Second", AccountType.Asset, null, true, null)));
    }
}
