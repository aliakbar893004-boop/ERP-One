using FluentValidation;

namespace ErpOne.Application.Attributes;

public class CreateAttributeValidator : AbstractValidator<CreateAttributeRequest>
{
    public CreateAttributeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Values).ChildRules(v =>
        {
            v.RuleFor(i => i.Code).NotEmpty().MaximumLength(20);
            v.RuleFor(i => i.Value).NotEmpty().MaximumLength(100);
        });
    }
}

public class UpdateAttributeValidator : AbstractValidator<UpdateAttributeRequest>
{
    public UpdateAttributeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Values).ChildRules(v =>
        {
            v.RuleFor(i => i.Code).NotEmpty().MaximumLength(20);
            v.RuleFor(i => i.Value).NotEmpty().MaximumLength(100);
        });
    }
}
