using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using ErpOne.Application.Approvals;
using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ApprovalServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ApprovalServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private const ApprovalDocumentType Po = ApprovalDocumentType.PurchaseOrder;
    private static readonly Func<string, bool> InAnyRole = _ => true;

    private async Task SeedChainAsync(IServiceProvider sp, params string[] roles)
    {
        var chain = sp.GetRequiredService<IApprovalChainService>();
        var inputs = roles.Select((r, i) => new ApprovalChainStepInput(i + 1, r)).ToList();
        await chain.ReplaceChainAsync(Po, inputs);
    }

    [Fact]
    public async Task Empty_chain_submits_as_fully_approved()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp); // ensure PO chain is empty regardless of test order
        await svc.ResetAsync(Po, 1001);

        var fully = await svc.SubmitAsync(Po, 1001);
        Assert.True(fully);
        Assert.Empty(await svc.GetStepsAsync(Po, 1001));
    }

    [Fact]
    public async Task Two_level_chain_requires_both_approvals()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Supervisor", "Manager");
        await svc.ResetAsync(Po, 1002);

        var fullyOnSubmit = await svc.SubmitAsync(Po, 1002);
        Assert.False(fullyOnSubmit);

        var afterFirst = await svc.ApproveAsync(Po, 1002, "sari", InAnyRole, creatorUserName: "budi");
        Assert.False(afterFirst);

        var afterSecond = await svc.ApproveAsync(Po, 1002, "andi", InAnyRole, creatorUserName: "budi");
        Assert.True(afterSecond);
    }

    [Fact]
    public async Task Creator_cannot_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1003);
        await svc.SubmitAsync(Po, 1003);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ApproveAsync(Po, 1003, "budi", InAnyRole, creatorUserName: "budi"));
    }

    [Fact]
    public async Task User_without_role_cannot_approve()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1004);
        await svc.SubmitAsync(Po, 1004);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ApproveAsync(Po, 1004, "sari", role => role == "Director", creatorUserName: "budi"));
    }

    [Fact]
    public async Task Reject_marks_current_step_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IApprovalService>();
        await SeedChainAsync(sp, "Manager");
        await svc.ResetAsync(Po, 1005);
        await svc.SubmitAsync(Po, 1005);

        await svc.RejectAsync(Po, 1005, "sari", InAnyRole, creatorUserName: "budi", reason: "mahal");
        var steps = await svc.GetStepsAsync(Po, 1005);
        Assert.Equal("Rejected", steps[0].Status);
        Assert.Equal("mahal", steps[0].Note);
    }
}
