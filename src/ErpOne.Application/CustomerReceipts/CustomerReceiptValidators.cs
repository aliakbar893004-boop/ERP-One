using FluentValidation;

namespace ErpOne.Application.CustomerReceipts;

public class CreateCustomerReceiptValidator : AbstractValidator<CreateCustomerReceiptRequest>
{
    public CreateCustomerReceiptValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Add at least one allocation.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(y => y.CustomerInvoiceId).GreaterThan(0);
            a.RuleFor(y => y.Amount).GreaterThan(0);
        });
    }
}
