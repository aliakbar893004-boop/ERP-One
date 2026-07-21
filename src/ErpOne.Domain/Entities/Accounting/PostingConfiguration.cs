using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Baris tunggal (Id=1) pemetaan akun GL sistemik untuk auto-posting.</summary>
public class PostingConfiguration : AuditableEntity
{
    public int Id { get; private set; }
    public int? ArAccountId { get; private set; }
    public int? ApAccountId { get; private set; }
    public int? InventoryAccountId { get; private set; }
    public int? GrIrAccountId { get; private set; }
    public int? SalesAccountId { get; private set; }
    public int? CogsAccountId { get; private set; }
    public int? InputTaxAccountId { get; private set; }
    public int? OutputTaxAccountId { get; private set; }
    public int? PosCashAccountId { get; private set; }

    private PostingConfiguration() { } // EF Core; single row seeded via HasData

    public void Update(int? ar, int? ap, int? inventory, int? grIr, int? sales, int? cogs,
        int? inputTax, int? outputTax, int? posCash)
    {
        ArAccountId = ar;
        ApAccountId = ap;
        InventoryAccountId = inventory;
        GrIrAccountId = grIr;
        SalesAccountId = sales;
        CogsAccountId = cogs;
        InputTaxAccountId = inputTax;
        OutputTaxAccountId = outputTax;
        PosCashAccountId = posCash;
    }
}
