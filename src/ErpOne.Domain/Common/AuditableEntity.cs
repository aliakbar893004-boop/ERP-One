namespace ErpOne.Domain.Common;

/// <summary>Kontrak jejak audit; di-stempel otomatis oleh AppDbContext saat SaveChanges.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; }
    string? CreatedBy { get; }
    DateTime? ModifiedAt { get; }
    string? ModifiedBy { get; }

    void MarkCreated(DateTime at, string? by);
    void MarkModified(DateTime at, string? by);
}

/// <summary>Basis entity dengan jejak audit (untuk entity yang tidak mewarisi base lain).</summary>
public abstract class AuditableEntity : IAuditable
{
    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    public void MarkCreated(DateTime at, string? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    public void MarkModified(DateTime at, string? by)
    {
        ModifiedAt = at;
        ModifiedBy = by;
    }
}
