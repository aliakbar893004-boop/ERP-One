using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) metode HPP aktif, company-wide. Pola PostingConfiguration.</summary>
public class CostingSetting : AuditableEntity
{
    public int Id { get; private set; }
    public CostingMethod Method { get; private set; } = CostingMethod.MovingAverage;

    // EF Core; single row seeded via HasData. Also used by unit tests.
    public CostingSetting() { }

    public void SetMethod(CostingMethod method) => Method = method;
}
