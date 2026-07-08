using FluentValidation;

namespace ErpOne.Application.Taxes;

public class CreateTaxValidator : AbstractValidator<CreateTaxRequest>
{
    public CreateTaxValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rate).InclusiveBetween(0, 100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public class UpdateTaxValidator : AbstractValidator<UpdateTaxRequest>
{
    public UpdateTaxValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rate).InclusiveBetween(0, 100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
