using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class PostingConfigurationService(AppDbContext db) : IPostingConfigurationService
{
    public async Task<PostingConfigurationDto> GetAsync(CancellationToken ct = default)
    {
        var c = await db.PostingConfigurations.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PostingConfiguration seed row (Id=1) is missing.");
        return new PostingConfigurationDto(c.ArAccountId, c.ApAccountId, c.InventoryAccountId, c.GrIrAccountId,
            c.SalesAccountId, c.CogsAccountId, c.InputTaxAccountId, c.OutputTaxAccountId, c.PosCashAccountId);
    }

    public async Task UpdateAsync(UpdatePostingConfigurationRequest r, CancellationToken ct = default)
    {
        var c = await db.PostingConfigurations.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PostingConfiguration seed row (Id=1) is missing.");
        c.Update(r.ArAccountId, r.ApAccountId, r.InventoryAccountId, r.GrIrAccountId,
            r.SalesAccountId, r.CogsAccountId, r.InputTaxAccountId, r.OutputTaxAccountId, r.PosCashAccountId);
        await db.SaveChangesAsync(ct);
    }
}
