namespace ErpOne.Infrastructure.Persistence;

/// <summary>
/// Read-model untuk tabel "Logs" yang dibuat & diisi oleh Serilog MSSqlServer sink.
/// Tabel ini di-exclude dari migrations EF (Serilog yang mengelola pembuatannya).
/// </summary>
public class LogEntry
{
    public int Id { get; private set; }
    public DateTime TimeStamp { get; private set; }
    public string Level { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public string? Exception { get; private set; }
}
