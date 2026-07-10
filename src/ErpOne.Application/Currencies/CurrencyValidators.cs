using FluentValidation;

namespace ErpOne.Application.Currencies;

public class CreateCurrencyValidator : AbstractValidator<CreateCurrencyRequest>
{
    public CreateCurrencyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3)
            .Matches("^[A-Za-z]{3}$").WithMessage("Code must be a 3-letter ISO code.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(6);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, 6);
    }
}

public class UpdateCurrencyValidator : AbstractValidator<UpdateCurrencyRequest>
{
    public UpdateCurrencyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3)
            .Matches("^[A-Za-z]{3}$").WithMessage("Code must be a 3-letter ISO code.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(6);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, 6);
    }
}
