using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Konfigurasi rantai approval (template) per tipe dokumen. Dikelola di Settings.</summary>
public class ApprovalChainStep : AuditableEntity
{
    public int Id { get; private set; }
    public ApprovalDocumentType DocumentType { get; private set; }
    public int StepOrder { get; private set; }
    public string RoleName { get; private set; } = default!;

    private ApprovalChainStep() { } // EF Core

    public ApprovalChainStep(ApprovalDocumentType documentType, int stepOrder, string roleName)
    {
        if (stepOrder < 1)
            throw new ArgumentException("StepOrder must be >= 1.", nameof(stepOrder));
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("RoleName is required.", nameof(roleName));

        DocumentType = documentType;
        StepOrder = stepOrder;
        RoleName = roleName.Trim();
    }
}
