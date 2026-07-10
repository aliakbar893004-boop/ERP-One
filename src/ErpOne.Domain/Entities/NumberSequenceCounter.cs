namespace ErpOne.Domain.Entities;

/// <summary>Counter berjalan per (SequenceCode, PeriodKey). Version = concurrency token
/// agar increment aman dari race (lintas provider: SQL Server &amp; SQLite).</summary>
public class NumberSequenceCounter
{
    public int Id { get; private set; }
    public string SequenceCode { get; private set; } = default!;
    public string PeriodKey { get; private set; } = default!;   // mis. "202607" atau "20260710" atau "ALL"
    public int LastValue { get; private set; }
    public int Version { get; private set; }

    private NumberSequenceCounter() { } // EF Core

    public NumberSequenceCounter(string sequenceCode, string periodKey, int lastValue)
    {
        SequenceCode = sequenceCode;
        PeriodKey = periodKey;
        LastValue = lastValue;
    }

    /// <summary>Naikkan counter &amp; bump concurrency token; kembalikan nilai baru.</summary>
    public int Next()
    {
        LastValue++;
        Version++;
        return LastValue;
    }
}
