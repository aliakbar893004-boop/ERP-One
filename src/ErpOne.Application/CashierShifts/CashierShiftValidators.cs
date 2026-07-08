using FluentValidation;

namespace ErpOne.Application.CashierShifts;

public class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OpeningFloat).GreaterThanOrEqualTo(0);
    }
}

public class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator()
    {
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ClosingNote).MaximumLength(500);
    }
}
