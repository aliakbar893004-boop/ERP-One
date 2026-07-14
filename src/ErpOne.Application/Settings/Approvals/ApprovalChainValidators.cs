using FluentValidation;

namespace ErpOne.Application.Approvals;

public class ApprovalChainStepInputValidator : AbstractValidator<ApprovalChainStepInput>
{
    public ApprovalChainStepInputValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(256);
    }
}
