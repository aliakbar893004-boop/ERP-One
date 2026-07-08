using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Logs;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class LogService(AppDbContext db) : ILogService
{
    public async Task<IReadOnlyList<LogEntryDto>> GetRecentAsync(
        int take = 200, string? level = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        var query = db.Logs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(l => l.Level == level);

        return await query
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Select(l => new LogEntryDto(l.Id, l.TimeStamp, l.Level, l.Message, l.Exception))
            .ToListAsync(ct);
    }
}
