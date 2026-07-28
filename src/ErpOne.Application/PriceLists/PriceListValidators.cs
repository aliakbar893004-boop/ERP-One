using FluentValidation;

namespace ErpOne.Application.PriceLists;

public class PriceListLineRequestValidator : AbstractValidator<PriceListLineRequest>
{
    public PriceListLineRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).GreaterThan(0);
        RuleFor(x => x.MinQty).GreaterThanOrEqualTo(1);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class CreatePriceListValidator : AbstractValidator<CreatePriceListRequest>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(255);
        RuleForEach(x => x.Lines).SetValidator(new PriceListLineRequestValidator());
        RuleFor(x => x.Lines).Must(NoDuplicateTiers)
            .WithMessage("Each product variant may appear only once per minimum quantity.");
    }

    internal static bool NoDuplicateTiers(IReadOnlyList<PriceListLineRequest> lines) =>
        lines is null || lines.Count == lines.Select(l => (l.ProductVariantId, l.MinQty)).Distinct().Count();
}

public class UpdatePriceListValidator : AbstractValidator<UpdatePriceListRequest>
{
    public UpdatePriceListValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(255);
        RuleForEach(x => x.Lines).SetValidator(new PriceListLineRequestValidator());
        RuleFor(x => x.Lines).Must(CreatePriceListValidator.NoDuplicateTiers)
            .WithMessage("Each product variant may appear only once per minimum quantity.");
    }
}
