using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class NumberSequenceService(AppDbContext db) : INumberSequenceService
{
    public async Task<IReadOnlyList<NumberSequenceDto>> GetAllAsync(CancellationToken ct = default) =>
        (await db.NumberSequences.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<NumberSequenceDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await db.NumberSequences.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    public async Task<bool> UpdateAsync(int id, UpdateNumberSequenceRequest request, CancellationToken ct = default)
    {
        var entity = await db.NumberSequences.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        if (!Enum.TryParse<ResetPeriod>(request.ResetPeriod, out var reset))
            reset = entity.ResetPeriod;

        entity.Update(request.Prefix, request.DateFormat, request.Padding, reset, request.Separator);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static NumberSequenceDto ToDto(NumberSequence x)
    {
        // Sample memakai tanggal statik agar deterministik (bukan DateTime.Now).
        var sampleDate = new DateTime(2026, 1, 1);
        var datePart = string.IsNullOrEmpty(x.DateFormat) ? "" : sampleDate.ToString(x.DateFormat) + x.Separator;
        var sample = $"{x.Prefix}{x.Separator}{datePart}{1.ToString().PadLeft(x.Padding, '0')}";
        return new NumberSequenceDto(x.Id, x.Code, x.Prefix, x.DateFormat, x.Padding, x.ResetPeriod.ToString(), x.Separator, sample);
    }
}
