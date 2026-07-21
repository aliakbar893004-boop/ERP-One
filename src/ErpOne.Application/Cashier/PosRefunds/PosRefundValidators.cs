using FluentValidation;

namespace ErpOne.Application.PosRefunds;

public class CreatePosRefundValidator : AbstractValidator<CreatePosRefundRequest>
{
    public CreatePosRefundValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Reason is required.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.PosSaleLineId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
