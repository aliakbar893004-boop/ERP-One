using FluentValidation;

namespace ErpOne.Application.Units;

public class CreateUnitValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public class UpdateUnitValidator : AbstractValidator<UpdateUnitRequest>
{
    public UpdateUnitValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
