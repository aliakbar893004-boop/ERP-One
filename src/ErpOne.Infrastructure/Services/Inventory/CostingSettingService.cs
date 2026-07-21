using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Costing;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class CostingSettingService(AppDbContext db) : ICostingSettingService
{
    public async Task<CostingMethod> GetMethodAsync(CancellationToken ct = default) =>
        await db.CostingSettings.AsNoTracking().Select(x => x.Method).FirstOrDefaultAsync(ct);

    public async Task<CostingSettingDto> GetAsync(CancellationToken ct = default)
    {
        var method = await GetMethodAsync(ct);
        var locked = await db.StockMovements.AnyAsync(ct);
        return new CostingSettingDto(method, locked);
    }

    public async Task UpdateMethodAsync(CostingMethod method, CancellationToken ct = default)
    {
        if (method is not (CostingMethod.MovingAverage or CostingMethod.StandardCost))
            throw new ValidationException([new ValidationFailure("Method", "Metode belum didukung.")]);

        if (await db.StockMovements.AnyAsync(ct))
            throw new ValidationException([new ValidationFailure("Method", "Metode HPP terkunci: sudah ada transaksi stok.")]);

        var row = await db.CostingSettings.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("CostingSetting seed row (Id=1) is missing.");
        row.SetMethod(method);
        await db.SaveChangesAsync(ct);
    }
}
