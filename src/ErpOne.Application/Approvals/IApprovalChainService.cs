using ErpOne.Domain.Entities;

namespace ErpOne.Application.Approvals;

public interface IApprovalChainService
{
    Task<IReadOnlyList<ApprovalChainStepDto>> GetByDocumentTypeAsync(
        ApprovalDocumentType docType, CancellationToken ct = default);

    /// <summary>Ganti seluruh rantai tipe dokumen secara atomik. StepOrder diberi ulang 1..n sesuai urutan list.</summary>
    Task ReplaceChainAsync(ApprovalDocumentType docType,
        IReadOnlyList<ApprovalChainStepInput> steps, CancellationToken ct = default);
}
