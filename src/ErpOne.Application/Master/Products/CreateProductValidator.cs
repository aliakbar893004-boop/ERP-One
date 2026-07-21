using FluentValidation;

namespace ErpOne.Application.Products;

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one variant is required.");
        RuleForEach(x => x.Variants).SetValidator(new VariantInputValidator());
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one variant is required.");
        RuleForEach(x => x.Variants).SetValidator(new VariantInputValidator());
    }
}

public class VariantInputValidator : AbstractValidator<VariantInput>
{
    public VariantInputValidator()
    {
        RuleFor(v => v.Price).GreaterThanOrEqualTo(0);
        RuleFor(v => v.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(v => v.DiscountPrice).GreaterThanOrEqualTo(0).When(v => v.DiscountPrice.HasValue);
        RuleFor(v => v.DiscountPrice).LessThanOrEqualTo(v => v.Price)
            .When(v => v.DiscountPrice.HasValue)
            .WithMessage("Discount price must not exceed the selling price.");
        RuleFor(v => v.DiscountPercent).InclusiveBetween(0, 100).When(v => v.DiscountPercent.HasValue);
        RuleFor(v => v.OpeningStock).GreaterThanOrEqualTo(0);
        RuleFor(v => v.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(v => v.ReorderQty).GreaterThanOrEqualTo(0);
        RuleFor(v => v.Weight).GreaterThanOrEqualTo(0).When(v => v.Weight.HasValue);
        RuleFor(v => v.Dimensions).MaximumLength(100);
        RuleFor(v => v.Barcode).MaximumLength(50);
    }
}
