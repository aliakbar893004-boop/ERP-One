using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class DocumentNumberService(AppDbContext db) : IDocumentNumberService
{
    private const int MaxAttempts = 6;

    public async Task<string> NextAsync(string code, DateTime docDate, CancellationToken ct = default)
    {
        var seq = await db.NumberSequences.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct)
            ?? throw new InvalidOperationException($"Number sequence '{code}' is not configured.");

        var periodKey = PeriodKeyFor(seq.ResetPeriod, docDate);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var counter = await db.NumberSequenceCounters
                .FirstOrDefaultAsync(c => c.SequenceCode == code && c.PeriodKey == periodKey, ct);

            int value;
            if (counter is null)
            {
                var start = await BackfillStartAsync(seq, periodKey, ct);
                counter = new NumberSequenceCounter(code, periodKey, start);
                value = counter.Next();
                db.NumberSequenceCounters.Add(counter);
            }
            else
            {
                value = counter.Next();
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return Format(seq, docDate, value);
            }
            catch (DbUpdateException)
            {
                // Concurrency conflict (token mismatch) or unique-insert race — reset and retry.
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException($"Could not allocate a number for '{code}' after {MaxAttempts} attempts.");
    }

    private static string PeriodKeyFor(ResetPeriod p, DateTime d) => p switch
    {
        ResetPeriod.Daily   => d.ToString("yyyyMMdd"),
        ResetPeriod.Monthly => d.ToString("yyyyMM"),
        ResetPeriod.Yearly  => d.ToString("yyyy"),
        _                   => "ALL"
    };

    private static string Format(NumberSequence seq, DateTime docDate, int value)
    {
        var datePart = string.IsNullOrEmpty(seq.DateFormat)
            ? ""
            : docDate.ToString(seq.DateFormat) + seq.Separator;
        return $"{seq.Prefix}{seq.Separator}{datePart}{value.ToString().PadLeft(seq.Padding, '0')}";
    }

    /// <summary>Untuk kontinuitas dengan dokumen lama: cari nomor terakhir existing pada prefix+period
    /// dan kembalikan nilai numeriknya (0 bila tak ada). Counter di-Next() dari nilai ini.</summary>
    private async Task<int> BackfillStartAsync(NumberSequence seq, string periodKey, CancellationToken ct)
    {
        var datePart = string.IsNullOrEmpty(seq.DateFormat) ? "" : periodKey + seq.Separator;
        var prefix = $"{seq.Prefix}{seq.Separator}{datePart}";

        string? last = seq.Code switch
        {
            DocumentTypes.PurchaseOrder => await MaxAsync(db.PurchaseOrders.AsNoTracking().Select(x => x.PoNumber), prefix, ct),
            DocumentTypes.SalesOrder    => await MaxAsync(db.SalesOrders.AsNoTracking().Select(x => x.SoNumber), prefix, ct),
            DocumentTypes.GoodsReceipt  => await MaxAsync(db.GoodsReceipts.AsNoTracking().Select(x => x.GrnNumber), prefix, ct),
            DocumentTypes.DeliveryOrder => await MaxAsync(db.DeliveryOrders.AsNoTracking().Select(x => x.DoNumber), prefix, ct),
            DocumentTypes.PosSale       => await MaxAsync(db.PosSales.AsNoTracking().Select(x => x.SaleNumber), prefix, ct),
            DocumentTypes.CashierShift  => await MaxAsync(db.CashierShifts.AsNoTracking().Select(x => x.ShiftNumber), prefix, ct),
            _                           => null
        };

        if (last is null || last.Length <= prefix.Length) return 0;
        return int.TryParse(last[prefix.Length..], out var n) ? n : 0;
    }

    private static async Task<string?> MaxAsync(IQueryable<string> numbers, string prefix, CancellationToken ct) =>
        await numbers
            .Where(v => v.StartsWith(prefix))
            .OrderByDescending(v => v)
            .FirstOrDefaultAsync(ct);
}
