using FluentValidation;

namespace ErpOne.Application.SupplierInvoices;

public class CreateSupplierInvoiceValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{
    public CreateSupplierInvoiceValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.GrnIds).NotEmpty().WithMessage("Select at least one goods receipt.");
        RuleFor(x => x.SupplierInvoiceNo).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate)
            .GreaterThanOrEqualTo(x => x.InvoiceDate)
            .When(x => x.DueDate.HasValue)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}

public class UpdateSupplierInvoiceHeaderValidator : AbstractValidator<UpdateSupplierInvoiceHeaderRequest>
{
    public UpdateSupplierInvoiceHeaderValidator()
    {
        RuleFor(x => x.SupplierInvoiceNo).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate)
            .WithMessage("Due date cannot be before the invoice date.");
    }
}
