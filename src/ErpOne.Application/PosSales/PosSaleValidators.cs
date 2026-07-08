using FluentValidation;

namespace ErpOne.Application.PosSales;

public class PosSaleLineRequestValidator : AbstractValidator<PosSaleLineRequest>
{
    public PosSaleLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public class CreatePosSaleValidator : AbstractValidator<CreatePosSaleRequest>
{
    public CreatePosSaleValidator()
    {
        RuleFor(x => x.PaymentMethodId).GreaterThan(0);
        RuleFor(x => x.TransactionDiscount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AmountTendered).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Keranjang kosong.");
        RuleForEach(x => x.Lines).SetValidator(new PosSaleLineRequestValidator());
    }
}
