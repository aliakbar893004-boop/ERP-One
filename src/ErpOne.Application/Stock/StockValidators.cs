using FluentValidation;

namespace ErpOne.Application.Stock;

public class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage("Warehouse is required.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new StockAdjustmentLineValidator());
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.VariantId).Distinct().Count() == lines.Count)
            .WithMessage("Each variant may appear only once per adjustment.")
            .When(x => x.Lines is { Count: > 0 });
    }
}

public class StockAdjustmentLineValidator : AbstractValidator<StockAdjustmentLine>
{
    public StockAdjustmentLineValidator()
    {
        RuleFor(l => l.VariantId).GreaterThan(0);
        RuleFor(l => l.DeltaQuantity).NotEqual(0).WithMessage("Delta quantity must not be zero.");
        RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0);
        RuleFor(l => l.Reason).MaximumLength(200);
    }
}
