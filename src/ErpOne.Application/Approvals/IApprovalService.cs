using ErpOne.Domain.Entities;

namespace ErpOne.Application.Approvals;

/// <summary>Engine approval document-agnostic. Tidak menyentuh status dokumen.</summary>
public interface IApprovalService
{
    /// <summary>Buat ApprovalStep dari rantai config. Return true bila rantai kosong (langsung fully approved).</summary>
    Task<bool> SubmitAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);

    /// <summary>Approve step Pending terkecil. Return true bila tidak ada lagi step Pending (fully approved).</summary>
    Task<bool> ApproveAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, CancellationToken ct = default);

    /// <summary>Reject step Pending terkecil dengan alasan.</summary>
    Task RejectAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, string reason, CancellationToken ct = default);

    /// <summary>Hapus semua ApprovalStep dokumen (dipakai saat reset/reject→draft).</summary>
    Task ResetAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalStepDto>> GetStepsAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default);
}
