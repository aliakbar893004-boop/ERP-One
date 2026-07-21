using FluentValidation;

namespace ErpOne.Application.Accounting;

public class CreateJournalEntryValidator : AbstractValidator<CreateJournalEntryRequest>
{
    public CreateJournalEntryValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountId).GreaterThan(0);
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l).Must(l => (l.Debit > 0) ^ (l.Credit > 0))
                .WithMessage("Each line must have exactly one of debit or credit > 0.");
        });
    }
}
