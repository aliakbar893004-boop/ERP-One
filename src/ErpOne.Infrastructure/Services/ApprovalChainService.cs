using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Approvals;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class ApprovalChainService(AppDbContext db) : IApprovalChainService
{
    public async Task<IReadOnlyList<ApprovalChainStepDto>> GetByDocumentTypeAsync(
        ApprovalDocumentType docType, CancellationToken ct = default) =>
        await db.ApprovalChainSteps.AsNoTracking()
            .Where(x => x.DocumentType == docType)
            .OrderBy(x => x.StepOrder)
            .Select(x => new ApprovalChainStepDto(x.Id, x.StepOrder, x.RoleName))
            .ToListAsync(ct);

    public async Task ReplaceChainAsync(ApprovalDocumentType docType,
        IReadOnlyList<ApprovalChainStepInput> steps, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.ApprovalChainSteps.Where(x => x.DocumentType == docType).ToListAsync(ct);
        db.ApprovalChainSteps.RemoveRange(existing);
        await db.SaveChangesAsync(ct);

        var order = 1;
        foreach (var s in steps)
            db.ApprovalChainSteps.Add(new ApprovalChainStep(docType, order++, s.RoleName));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
