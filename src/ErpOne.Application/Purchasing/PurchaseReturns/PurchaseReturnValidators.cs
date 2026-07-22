using FluentValidation;

namespace ErpOne.Application.Purchasing.PurchaseReturns;

public class CreatePurchaseReturnValidator : AbstractValidator<CreatePurchaseReturnRequest>
{
    public CreatePurchaseReturnValidator()
    {
        RuleFor(x => x.SourceType).Must(s => s is "GoodsReceipt" or "SupplierInvoice")
            .WithMessage("SourceType must be GoodsReceipt or SupplierInvoice.");
        RuleFor(x => x.GoodsReceiptId).NotNull().When(x => x.SourceType == "GoodsReceipt")
            .WithMessage("GoodsReceiptId is required for a GRN-sourced return.");
        RuleFor(x => x.SupplierInvoiceId).NotNull().When(x => x.SourceType == "SupplierInvoice")
            .WithMessage("SupplierInvoiceId is required for an invoice-sourced return.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.GoodsReceiptLineId).GreaterThan(0);
        });
        RuleForEach(x => x.Lines).Must(l => l.SupplierInvoiceLineId is > 0)
            .When(x => x.SourceType == "SupplierInvoice")
            .WithMessage("SupplierInvoiceLineId is required on each line for an invoice-sourced return.");
    }
}
