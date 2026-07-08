using FluentValidation;

namespace ErpOne.Application.DeliveryOrders;

public class DeliveryOrderLineRequestValidator : AbstractValidator<DeliveryOrderLineRequest>
{
    public DeliveryOrderLineRequestValidator()
    {
        RuleFor(x => x.SalesOrderLineId).GreaterThan(0);
        RuleFor(x => x.QuantityDelivered).GreaterThan(0);
    }
}

public class CreateDeliveryOrderValidator : AbstractValidator<CreateDeliveryOrderRequest>
{
    public CreateDeliveryOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.DeliveryDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new DeliveryOrderLineRequestValidator());
    }
}

public class UpdateDeliveryOrderValidator : AbstractValidator<UpdateDeliveryOrderRequest>
{
    public UpdateDeliveryOrderValidator()
    {
        RuleFor(x => x.DeliveryDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new DeliveryOrderLineRequestValidator());
    }
}
