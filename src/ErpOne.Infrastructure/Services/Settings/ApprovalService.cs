using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ApprovalService(AppDbContext db) : IApprovalService
{
    public async Task<bool> SubmitAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default)
    {
        var chain = await db.ApprovalChainSteps.AsNoTracking()
            .Where(c => c.DocumentType == docType)
            .OrderBy(c => c.StepOrder)
            .ToListAsync(ct);

        foreach (var c in chain)
            db.ApprovalSteps.Add(new ApprovalStep(docType, docId, c.StepOrder, c.RoleName));

        await db.SaveChangesAsync(ct);
        return chain.Count == 0; // rantai kosong → langsung fully approved
    }

    public async Task<bool> ApproveAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, CancellationToken ct = default)
    {
        var step = await CurrentPendingAsync(docType, docId, ct)
            ?? throw Fail("There is no pending approval step for this document.");
        EnsureCanAct(step, actingUserName, isInRole, creatorUserName);

        step.Approve(actingUserName, actingUserName, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);

        var hasPending = await db.ApprovalSteps.AnyAsync(
            s => s.DocumentType == docType && s.DocumentId == docId && s.Status == ApprovalStepStatus.Pending, ct);
        return !hasPending;
    }

    public async Task RejectAsync(ApprovalDocumentType docType, int docId, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName, string reason, CancellationToken ct = default)
    {
        var step = await CurrentPendingAsync(docType, docId, ct)
            ?? throw Fail("There is no pending approval step for this document.");
        EnsureCanAct(step, actingUserName, isInRole, creatorUserName);

        step.Reject(actingUserName, actingUserName, reason, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(ApprovalDocumentType docType, int docId, CancellationToken ct = default)
    {
        var steps = await db.ApprovalSteps
            .Where(s => s.DocumentType == docType && s.DocumentId == docId).ToListAsync(ct);
        if (steps.Count == 0) return;
        db.ApprovalSteps.RemoveRange(steps);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApprovalStepDto>> GetStepsAsync(
        ApprovalDocumentType docType, int docId, CancellationToken ct = default) =>
        await db.ApprovalSteps.AsNoTracking()
            .Where(s => s.DocumentType == docType && s.DocumentId == docId)
            .OrderBy(s => s.StepOrder)
            .Select(s => new ApprovalStepDto(
                s.Id, s.StepOrder, s.RoleName, s.Status.ToString(), s.ActedByName, s.ActedAt, s.Note))
            .ToListAsync(ct);

    private Task<ApprovalStep?> CurrentPendingAsync(ApprovalDocumentType docType, int docId, CancellationToken ct) =>
        db.ApprovalSteps
            .Where(s => s.DocumentType == docType && s.DocumentId == docId && s.Status == ApprovalStepStatus.Pending)
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync(ct);

    private static void EnsureCanAct(ApprovalStep step, string actingUserName,
        Func<string, bool> isInRole, string? creatorUserName)
    {
        if (!string.IsNullOrEmpty(creatorUserName) &&
            string.Equals(creatorUserName, actingUserName, StringComparison.OrdinalIgnoreCase))
            throw Fail("You cannot approve or reject a document you created.");
        if (!isInRole(step.RoleName))
            throw Fail($"You do not hold the required role '{step.RoleName}' for this step.");
    }

    private static ValidationException Fail(string message) =>
        new([new ValidationFailure("Approval", message)]);
}
