using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) setelan pricing company-wide. Pola CostingSetting.</summary>
public class PricingSetting : AuditableEntity
{
    public int Id { get; private set; }

    /// <summary>Batas diskon dipakai bila user tidak punya role dengan MaxDiscountPercent terisi.</summary>
    public decimal DefaultMaxDiscountPercent { get; private set; } = 100m;

    // EF Core; baris tunggal diseed via HasData. Juga dipakai unit test.
    public PricingSetting() { }

    public void SetDefaultMaxDiscountPercent(decimal percent)
    {
        if (percent is < 0m or > 100m)
            throw new ArgumentException("Percent must be 0..100.", nameof(percent));
        DefaultMaxDiscountPercent = percent;
    }
}
