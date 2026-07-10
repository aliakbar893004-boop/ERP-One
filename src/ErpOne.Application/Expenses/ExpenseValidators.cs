using FluentValidation;

namespace ErpOne.Application.Expenses;

public class CreateExpenseCategoryValidator : AbstractValidator<CreateExpenseCategoryRequest>
{
    public CreateExpenseCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdateExpenseCategoryValidator : AbstractValidator<UpdateExpenseCategoryRequest>
{
    public UpdateExpenseCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$").WithMessage("Code may only contain letters, numbers, and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreateExpenseValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseValidator()
    {
        RuleFor(x => x.CashBankAccountId).GreaterThan(0);
        RuleFor(x => x.ExpenseCategoryId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Payee).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
