using FluentValidation;

namespace ErpOne.Application.Suppliers;

public class CreateSupplierValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierValidator() => RulesFor(this);

    internal static void RulesFor(AbstractValidator<CreateSupplierRequest> v)
    {
        v.RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        v.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        v.RuleFor(x => x.ContactPerson).MaximumLength(100);
        v.RuleFor(x => x.Phone).MaximumLength(30);
        v.RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.Address).MaximumLength(300);
        v.RuleFor(x => x.TaxId).MaximumLength(30);
        v.RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        v.RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        v.RuleFor(x => x.BankName).MaximumLength(100);
        v.RuleFor(x => x.BankAccountNumber).MaximumLength(50);
        v.RuleFor(x => x.BankAccountName).MaximumLength(100);
    }
}

public class UpdateSupplierValidator : AbstractValidator<UpdateSupplierRequest>
{
    public UpdateSupplierValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactPerson).MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(100).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.TaxId).MaximumLength(30);
        RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultCurrency).MaximumLength(3);
        RuleFor(x => x.BankName).MaximumLength(100);
        RuleFor(x => x.BankAccountNumber).MaximumLength(50);
        RuleFor(x => x.BankAccountName).MaximumLength(100);
    }
}
