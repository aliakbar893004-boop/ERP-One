using FluentValidation;

namespace ErpOne.Application.StockOpnames;

public class CreateStockOpnameValidator : AbstractValidator<CreateStockOpnameRequest>
{
    public CreateStockOpnameValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage("Warehouse is required.");
    }
}

public class UpdateStockOpnameValidator : AbstractValidator<UpdateStockOpnameRequest>
{
    public UpdateStockOpnameValidator()
    {
        RuleForEach(x => x.Counts).ChildRules(c =>
        {
            c.RuleFor(x => x.LineId).GreaterThan(0);
            c.RuleFor(x => x.PhysicalQty).GreaterThanOrEqualTo(0).WithMessage("Physical quantity must be >= 0.");
        });
    }
}
