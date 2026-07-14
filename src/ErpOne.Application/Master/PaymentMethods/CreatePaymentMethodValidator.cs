using FluentValidation;

namespace ErpOne.Application.PaymentMethods;

public class CreatePaymentMethodValidator : AbstractValidator<CreatePaymentMethodRequest>
{
    public CreatePaymentMethodValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public class UpdatePaymentMethodValidator : AbstractValidator<UpdatePaymentMethodRequest>
{
    public UpdatePaymentMethodValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
    }
}
