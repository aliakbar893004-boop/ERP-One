using FluentValidation.TestHelper;
using ErpOne.Application.Approvals;
using Xunit;

namespace ErpOne.UnitTests;

public class ApprovalChainValidatorTests
{
    private readonly ApprovalChainStepInputValidator _v = new();

    [Fact]
    public void Valid_passes() =>
        _v.TestValidate(new ApprovalChainStepInput(1, "Manager")).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Blank_role_fails() =>
        _v.TestValidate(new ApprovalChainStepInput(1, "  ")).ShouldHaveValidationErrorFor(x => x.RoleName);
}
