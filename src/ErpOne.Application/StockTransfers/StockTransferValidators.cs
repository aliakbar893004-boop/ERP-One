using FluentValidation;

namespace ErpOne.Application.StockTransfers;

public class CreateStockTransferValidator : AbstractValidator<CreateStockTransferRequest>
{
    public CreateStockTransferValidator()
    {
        RuleFor(x => x.SourceWarehouseId).GreaterThan(0);
        RuleFor(x => x.DestinationWarehouseId).GreaterThan(0)
            .NotEqual(x => x.SourceWarehouseId).WithMessage("Source and destination warehouses must differ.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductVariantId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
