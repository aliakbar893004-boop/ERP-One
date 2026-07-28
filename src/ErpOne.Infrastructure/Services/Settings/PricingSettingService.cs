using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Pricing;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PricingSettingService(AppDbContext db) : IPricingSettingService
{
    public async Task<PricingSettingDto> GetAsync(CancellationToken ct = default)
    {
        var percent = await db.PricingSettings.AsNoTracking()
            .Select(x => x.DefaultMaxDiscountPercent).FirstOrDefaultAsync(ct);
        return new PricingSettingDto(percent);
    }

    public async Task UpdateAsync(decimal defaultMaxDiscountPercent, CancellationToken ct = default)
    {
        if (defaultMaxDiscountPercent is < 0m or > 100m)
            throw new ValidationException(
                [new ValidationFailure(nameof(PricingSettingDto.DefaultMaxDiscountPercent),
                    "Percent must be between 0 and 100.")]);

        var row = await db.PricingSettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PricingSetting seed row (Id=1) is missing.");

        row.SetDefaultMaxDiscountPercent(defaultMaxDiscountPercent);
        await db.SaveChangesAsync(ct);
    }
}
