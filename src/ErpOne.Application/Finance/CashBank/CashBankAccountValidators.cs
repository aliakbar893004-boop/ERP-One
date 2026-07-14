using FluentValidation;

namespace ErpOne.Application.CashBank;

public class CreateCashBankAccountValidator : AbstractValidator<CreateCashBankAccountRequest>
{
    public CreateCashBankAccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).Must(t => t is "Cash" or "Bank").WithMessage("Type must be Cash or Bank.");
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BankName).MaximumLength(100);
        RuleFor(x => x.AccountNumber).MaximumLength(50);
        RuleFor(x => x.AccountHolder).MaximumLength(100);
    }
}

public class UpdateCashBankAccountValidator : AbstractValidator<UpdateCashBankAccountRequest>
{
    public UpdateCashBankAccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).Must(t => t is "Cash" or "Bank").WithMessage("Type must be Cash or Bank.");
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BankName).MaximumLength(100);
        RuleFor(x => x.AccountNumber).MaximumLength(50);
        RuleFor(x => x.AccountHolder).MaximumLength(100);
    }
}
