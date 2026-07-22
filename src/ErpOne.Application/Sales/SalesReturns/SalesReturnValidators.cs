using FluentValidation;

namespace ErpOne.Application.Sales.SalesReturns;

public class CreateSalesReturnValidator : AbstractValidator<CreateSalesReturnRequest>
{
    public CreateSalesReturnValidator()
    {
        RuleFor(x => x.SourceType).Must(s => s is "DeliveryOrder" or "CustomerInvoice")
            .WithMessage("SourceType must be DeliveryOrder or CustomerInvoice.");
        RuleFor(x => x.DeliveryOrderId).NotNull().When(x => x.SourceType == "DeliveryOrder")
            .WithMessage("DeliveryOrderId is required for a DO-sourced return.");
        RuleFor(x => x.CustomerInvoiceId).NotNull().When(x => x.SourceType == "CustomerInvoice")
            .WithMessage("CustomerInvoiceId is required for an invoice-sourced return.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.DeliveryOrderLineId).GreaterThan(0);
        });
        RuleForEach(x => x.Lines).Must(l => l.CustomerInvoiceLineId is > 0)
            .When(x => x.SourceType == "CustomerInvoice")
            .WithMessage("CustomerInvoiceLineId is required on each line for an invoice-sourced return.");
    }
}
