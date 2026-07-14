namespace ErpOne.Domain.Entities;

/// <summary>Kaitan varian ke satu nilai atribut (mis. Ukuran=M).</summary>
public class ProductVariantAttribute
{
    public int Id { get; private set; }
    public int ProductVariantId { get; private set; }
    public int AttributeValueId { get; private set; }

    private ProductVariantAttribute() { } // EF Core

    public ProductVariantAttribute(int attributeValueId)
    {
        AttributeValueId = attributeValueId;
    }
}
