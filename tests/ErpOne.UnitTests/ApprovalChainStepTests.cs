using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ApprovalChainStepTests
{
    [Fact]
    public void Ctor_sets_fields_and_trims_role()
    {
        var s = new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, "  Manager  ");
        Assert.Equal(ApprovalDocumentType.PurchaseOrder, s.DocumentType);
        Assert.Equal(1, s.StepOrder);
        Assert.Equal("Manager", s.RoleName);
    }

    [Fact]
    public void Ctor_rejects_order_below_one() =>
        Assert.Throws<ArgumentException>(() => new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 0, "Manager"));

    [Fact]
    public void Ctor_requires_role() =>
        Assert.Throws<ArgumentException>(() => new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, "  "));
}
