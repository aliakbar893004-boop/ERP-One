using FluentValidation;

namespace ErpOne.Application.SalesOrders;

public class SalesOrderLineRequestValidator : AbstractValidator<SalesOrderLineRequest>
{
    public SalesOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new SalesOrderLineRequestValidator());
    }
}

public class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderRequest>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpectedDate)
            .Must((req, ed) => ed is null || ed.Value.Date >= req.OrderDate.Date)
            .WithMessage("ExpectedDate cannot be before OrderDate.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new SalesOrderLineRequestValidator());
    }
}
