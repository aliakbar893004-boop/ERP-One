using FluentValidation;

namespace ErpOne.Application.CustomerInvoices;

public class CreateCustomerInvoiceValidator : AbstractValidator<CreateCustomerInvoiceRequest>
{
    public CreateCustomerInvoiceValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.SalesOrderIds).NotEmpty().WithMessage("Select at least one sales order.");
        RuleFor(x => x.CustomerRef).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate)
            .GreaterThanOrEqualTo(x => x.InvoiceDate)
            .When(x => x.DueDate.HasValue)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}

public class UpdateCustomerInvoiceHeaderValidator : AbstractValidator<UpdateCustomerInvoiceHeaderRequest>
{
    public UpdateCustomerInvoiceHeaderValidator()
    {
        RuleFor(x => x.CustomerRef).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}
