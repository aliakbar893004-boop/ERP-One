using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Instans satu langkah approval pada dokumen tertentu (dibuat saat submit, snapshot rantai).</summary>
public class ApprovalStep : AuditableEntity
{
    public int Id { get; private set; }
    public ApprovalDocumentType DocumentType { get; private set; }
    public int DocumentId { get; private set; }
    public int StepOrder { get; private set; }
    public string RoleName { get; private set; } = default!;
    public ApprovalStepStatus Status { get; private set; }
    public string? ActedByUserId { get; private set; }
    public string? ActedByName { get; private set; }
    public DateTime? ActedAt { get; private set; }
    public string? Note { get; private set; }

    private ApprovalStep() { } // EF Core

    public ApprovalStep(ApprovalDocumentType documentType, int documentId, int stepOrder, string roleName)
    {
        if (documentId <= 0) throw new ArgumentException("DocumentId must be > 0.", nameof(documentId));
        if (stepOrder < 1) throw new ArgumentException("StepOrder must be >= 1.", nameof(stepOrder));
        if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("RoleName is required.", nameof(roleName));

        DocumentType = documentType;
        DocumentId = documentId;
        StepOrder = stepOrder;
        RoleName = roleName.Trim();
        Status = ApprovalStepStatus.Pending;
    }

    public void Approve(string actedByUserId, string? actedByName, DateTime at)
    {
        EnsurePending();
        Status = ApprovalStepStatus.Approved;
        ActedByUserId = actedByUserId;
        ActedByName = actedByName;
        ActedAt = at;
    }

    public void Reject(string actedByUserId, string? actedByName, string reason, DateTime at)
    {
        EnsurePending();
        Status = ApprovalStepStatus.Rejected;
        ActedByUserId = actedByUserId;
        ActedByName = actedByName;
        Note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ActedAt = at;
    }

    private void EnsurePending()
    {
        if (Status != ApprovalStepStatus.Pending)
            throw new InvalidOperationException("Approval step has already been acted on.");
    }
}
