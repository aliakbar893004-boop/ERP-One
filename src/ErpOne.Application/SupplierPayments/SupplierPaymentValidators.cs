using FluentValidation;

namespace ErpOne.Application.SupplierPayments;

public class CreateSupplierPaymentValidator : AbstractValidator<CreateSupplierPaymentRequest>
{
    public CreateSupplierPaymentValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Add at least one allocation.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(y => y.SupplierInvoiceId).GreaterThan(0);
            a.RuleFor(y => y.Amount).GreaterThan(0);
        });
    }
}

public class UpdateSupplierPaymentValidator : AbstractValidator<UpdateSupplierPaymentRequest>
{
    public UpdateSupplierPaymentValidator()
    {
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Add at least one allocation.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(y => y.SupplierInvoiceId).GreaterThan(0);
            a.RuleFor(y => y.Amount).GreaterThan(0);
        });
    }
}
