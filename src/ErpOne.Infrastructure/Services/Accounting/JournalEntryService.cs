using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Common;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class JournalEntryService(
    AppDbContext db,
    IValidator<CreateJournalEntryRequest> createValidator,
    IDocumentNumberService docNumbers) : IJournalEntryService
{
    public async Task<PagedResult<JournalEntryListItemDto>> GetPagedAsync(
        JournalEntryFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.JournalEntries.AsNoTracking();
        if (filter.From is { } f) query = query.Where(x => x.EntryDate >= f.Date);
        if (filter.To is { } t) query = query.Where(x => x.EntryDate < t.Date.AddDays(1));
        if (filter.Status is { } st) query = query.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x => x.EntryNumber.Contains(filter.Search) || x.Description.Contains(filter.Search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new JournalEntryListItemDto(x.Id, x.EntryNumber, x.EntryDate, x.Description, x.Status, x.TotalDebit))
            .ToListAsync(ct);
        return new PagedResult<JournalEntryListItemDto>(items, total, page, pageSize);
    }

    public async Task<JournalEntryDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var e = await db.JournalEntries.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var accIds = e.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accs = await db.Accounts.AsNoTracking().Where(a => accIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.Code, a.Name }, ct);
        var lines = e.Lines.Select(l => new JournalEntryLineDto(l.Id, l.AccountId,
            accs.TryGetValue(l.AccountId, out var a) ? a.Code : "?",
            accs.TryGetValue(l.AccountId, out var a2) ? a2.Name : "(unknown)",
            l.Debit, l.Credit, l.Memo)).ToList();
        return new JournalEntryDto(e.Id, e.EntryNumber, e.EntryDate, e.Description, e.Status,
            e.TotalDebit, e.TotalCredit, e.ReversalOfEntryId, e.ReversedByEntryId, lines);
    }

    public async Task<JournalEntryDto> CreateDraftAsync(CreateJournalEntryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, request.EntryDate, ct);
        var entry = new JournalEntry(number, request.EntryDate, request.Description);
        entry.SetLines(request.Lines.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(entry.Id, ct))!;
    }

    public async Task<JournalEntryDto> UpdateDraftAsync(int id, CreateJournalEntryRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        if (entry.Source == JournalSource.System) throw Fail("System-generated entries cannot be modified manually.");
        entry.UpdateHeader(request.EntryDate, request.Description);      // throws if not draft
        entry.SetLines(request.Lines.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, ct))!;
    }

    public async Task DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        if (entry.Source == JournalSource.System) throw Fail("System-generated entries cannot be modified manually.");
        if (entry.Status != JournalEntryStatus.Draft) throw new InvalidOperationException("Only a draft entry can be deleted.");
        db.JournalEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task PostAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");

        var accIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accIds.Contains(a.Id)).ToListAsync(ct);
        if (accounts.Count != accIds.Count) throw Fail("One or more accounts do not exist.");
        if (accounts.Any(a => !a.IsPostable)) throw Fail("Cannot post to a non-postable (header) account.");
        if (accounts.Any(a => !a.IsActive)) throw Fail("Cannot post to an inactive account.");

        entry.Post();   // domain validation: balance, >= 2 lines, total > 0
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<JournalEntryDto> ReverseAsync(int id, DateTime reversalDate, string? note, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var original = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Fail("Journal entry not found.");
        if (original.Source == JournalSource.System) throw Fail("System-generated entries cannot be modified manually.");
        if (original.Status != JournalEntryStatus.Posted) throw Fail("Only a posted entry can be reversed.");
        if (original.ReversedByEntryId is not null) throw Fail("This entry has already been reversed.");

        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, reversalDate, ct);
        var desc = string.IsNullOrWhiteSpace(note)
            ? $"Reversal of {original.EntryNumber}"
            : $"Reversal of {original.EntryNumber}: {note.Trim()}";
        var reversal = new JournalEntry(number, reversalDate, desc);
        reversal.SetLines(original.Lines.Select(l => (l.AccountId, l.Credit, l.Debit, l.Memo)));   // swap sides
        reversal.MarkAsReversalOf(original.Id);
        reversal.Post();
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);

        original.MarkReversed(reversal.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetByIdAsync(reversal.Id, ct))!;
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("JournalEntry", message)]);
}
