using Microsoft.EntityFrameworkCore;
using ErpOne.Application.CompanySettings;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CompanySettingService(AppDbContext db) : ICompanySettingService
{
    public async Task<CompanySettingDto> GetAsync(CancellationToken ct = default)
    {
        var entity = await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CompanySetting seed row (Id=1) is missing.");
        return ToDto(entity);
    }

    public async Task UpdateAsync(UpdateCompanySettingRequest request, CancellationToken ct = default)
    {
        var entity = await db.CompanySettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CompanySetting seed row (Id=1) is missing.");

        entity.Update(request.CompanyName, request.Address, request.Phone, request.Email,
            request.TaxId, request.LogoUrl, request.ReceiptHeader, request.ReceiptFooter);
        await db.SaveChangesAsync(ct);
    }

    private static CompanySettingDto ToDto(CompanySetting x) =>
        new(x.Id, x.CompanyName, x.Address, x.Phone, x.Email, x.TaxId, x.LogoUrl, x.ReceiptHeader, x.ReceiptFooter);
}
