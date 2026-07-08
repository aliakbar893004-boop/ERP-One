using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ProductTests
{
    private static Product NewProduct() =>
        new("ELK/0001", "Keyboard", "Mechanical", categoryId: 3, brandId: null, baseUnitId: null, taxId: null, ProductStatus.Aktif);

    [Fact]
    public void Create_SetsParentFields_AndCodeLocked()
    {
        var p = NewProduct();
        Assert.Equal("ELK/0001", p.Code);
        Assert.Equal("Keyboard", p.Name);
        Assert.Equal(ProductStatus.Aktif, p.Status);
        Assert.Empty(p.Variants);
    }

    [Fact]
    public void AddVariant_AppendsVariantWithGivenSku()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001", null, 250_000m, null, 0m, null, null, true);
        Assert.Single(p.Variants);
        Assert.Equal("ELK/0001", v.Sku);
        Assert.Equal(250_000m, v.Price);
        Assert.True(v.IsActive);
    }

    [Fact]
    public void Update_ChangesParentFields_ButNotCode()
    {
        var p = NewProduct();
        p.Update("Keyboard Pro", "RGB", categoryId: 3, brandId: 1, baseUnitId: 2, taxId: 1, ProductStatus.Nonaktif);
        Assert.Equal("ELK/0001", p.Code); // locked
        Assert.Equal("Keyboard Pro", p.Name);
        Assert.Equal(1, p.BrandId);
        Assert.Equal(ProductStatus.Nonaktif, p.Status);
    }

    [Fact]
    public void Variant_RejectsNegativePrice_AndDiscountAbovePrice()
    {
        var p = NewProduct();
        Assert.Throws<ArgumentException>(() => p.AddVariant("X", null, -1m, null, 0m, null, null, true));
        Assert.Throws<ArgumentException>(() => p.AddVariant("X", null, 100m, 200m, 0m, null, null, true));
    }

    [Fact]
    public void Variant_SetAttributeValues_ReplacesLinks()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001-M", null, 100m, null, 0m, null, null, true);
        v.SetAttributeValues(new[] { 10, 20 });
        Assert.Equal(2, v.Attributes.Count);
        v.SetAttributeValues(new[] { 30 });
        Assert.Single(v.Attributes);
        Assert.Equal(30, v.Attributes[0].AttributeValueId);
    }

    [Fact]
    public void RemoveVariant_RemovesById()
    {
        var p = NewProduct();
        var v = p.AddVariant("ELK/0001", null, 1m, null, 0m, null, null, true);
        // Id is 0 until persisted; RemoveVariant(0) removes the unsaved one
        p.RemoveVariant(v.Id);
        Assert.Empty(p.Variants);
    }

    // ── Image logic (unchanged API) ──────────────────────────────────────────
    [Fact]
    public void AddImage_FirstBecomesPrimary()
    {
        var product = NewProduct();

        var first = product.AddImage("uploads/products/a.jpg", "a.jpg", "image/jpeg", 100);
        product.AddImage("uploads/products/b.jpg", "b.jpg", "image/jpeg", 100);

        Assert.True(first.IsPrimary);
        Assert.Same(first, product.PrimaryImage);
    }

    [Fact]
    public void AddImage_BeyondMax_Throws()
    {
        var product = NewProduct();
        for (var i = 0; i < Product.MaxImages; i++)
            product.AddImage($"uploads/products/{i}.jpg", $"{i}.jpg", "image/jpeg", 100);

        Assert.Equal(Product.MaxImages, product.Images.Count);
        Assert.Throws<InvalidOperationException>(() =>
            product.AddImage("uploads/products/x.jpg", "x.jpg", "image/jpeg", 100));
    }
}
