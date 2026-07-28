namespace ErpOne.Application.Pricing;

public record PricingSettingDto(decimal DefaultMaxDiscountPercent);

public interface IPricingSettingService
{
    Task<PricingSettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(decimal defaultMaxDiscountPercent, CancellationToken ct = default);
}
