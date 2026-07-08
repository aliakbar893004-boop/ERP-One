using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Approvals;
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ApprovalChainServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ApprovalChainServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Replace_then_get_returns_ordered_chain()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApprovalChainService>();

        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(1, "Supervisor"), new ApprovalChainStepInput(2, "Manager")]);

        var chain = await svc.GetByDocumentTypeAsync(ApprovalDocumentType.SalesOrder);
        Assert.Equal(2, chain.Count);
        Assert.Equal("Supervisor", chain[0].RoleName);
        Assert.Equal(1, chain[0].StepOrder);
        Assert.Equal("Manager", chain[1].RoleName);
        Assert.Equal(2, chain[1].StepOrder);
    }

    [Fact]
    public async Task Replace_overwrites_previous_and_renumbers()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApprovalChainService>();

        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(9, "A"), new ApprovalChainStepInput(3, "B")]);
        await svc.ReplaceChainAsync(ApprovalDocumentType.SalesOrder,
            [new ApprovalChainStepInput(1, "OnlyOne")]);

        var chain = await svc.GetByDocumentTypeAsync(ApprovalDocumentType.SalesOrder);
        Assert.Single(chain);
        Assert.Equal("OnlyOne", chain[0].RoleName);
        Assert.Equal(1, chain[0].StepOrder);
    }
}
