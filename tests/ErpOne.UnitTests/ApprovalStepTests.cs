using ErpOne.Domain.Entities;
using Xunit;

namespace ErpOne.UnitTests;

public class ApprovalStepTests
{
    private static ApprovalStep Make() =>
        new(ApprovalDocumentType.PurchaseOrder, 10, 1, "Manager");

    [Fact]
    public void New_step_is_pending()
    {
        var s = Make();
        Assert.Equal(ApprovalStepStatus.Pending, s.Status);
        Assert.Equal(10, s.DocumentId);
        Assert.Null(s.ActedAt);
    }

    [Fact]
    public void Approve_sets_status_and_actor()
    {
        var s = Make();
        var at = new DateTime(2026, 6, 24, 9, 0, 0, DateTimeKind.Utc);
        s.Approve("u1", "Budi", at);
        Assert.Equal(ApprovalStepStatus.Approved, s.Status);
        Assert.Equal("u1", s.ActedByUserId);
        Assert.Equal("Budi", s.ActedByName);
        Assert.Equal(at, s.ActedAt);
    }

    [Fact]
    public void Reject_sets_status_and_note()
    {
        var s = Make();
        s.Reject("u1", "Budi", "Harga terlalu tinggi", DateTime.UtcNow);
        Assert.Equal(ApprovalStepStatus.Rejected, s.Status);
        Assert.Equal("Harga terlalu tinggi", s.Note);
    }

    [Fact]
    public void Cannot_act_twice()
    {
        var s = Make();
        s.Approve("u1", "Budi", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => s.Approve("u2", "Sari", DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => s.Reject("u2", "Sari", "x", DateTime.UtcNow));
    }
}
