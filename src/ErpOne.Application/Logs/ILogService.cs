namespace ErpOne.Application.Logs;

public interface ILogService
{
    /// <summary>Ambil log terbaru, opsional difilter minimal level (mis. "Error").</summary>
    Task<IReadOnlyList<LogEntryDto>> GetRecentAsync(int take = 200, string? level = null, CancellationToken ct = default);
}
