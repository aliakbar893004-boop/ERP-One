using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Jurnal double-entry. Draft boleh belum balance; Post menuntut balance &amp; ≥2 baris.</summary>
public class JournalEntry : AuditableEntity
{
    private readonly List<JournalEntryLine> _lines = new();

    public int Id { get; private set; }
    public string EntryNumber { get; private set; } = default!;
    public DateTime EntryDate { get; private set; }
    public string Description { get; private set; } = default!;
    public JournalEntryStatus Status { get; private set; }
    public int? ReversalOfEntryId { get; private set; }
    public int? ReversedByEntryId { get; private set; }
    public JournalSource Source { get; private set; }
    public string? SourceType { get; private set; }
    public int? SourceId { get; private set; }
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }

    public IReadOnlyCollection<JournalEntryLine> Lines => _lines.AsReadOnly();

    private JournalEntry() { } // EF Core

    public JournalEntry(string entryNumber, DateTime entryDate, string description)
    {
        if (string.IsNullOrWhiteSpace(entryNumber)) throw new ArgumentException("EntryNumber is required.", nameof(entryNumber));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        EntryNumber = entryNumber.Trim();
        EntryDate = entryDate;
        Description = description.Trim();
        Status = JournalEntryStatus.Draft;
        Source = JournalSource.Manual;
    }

    public void UpdateHeader(DateTime entryDate, string description)
    {
        RequireDraft();
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        EntryDate = entryDate;
        Description = description.Trim();
    }

    public void SetLines(IEnumerable<(int AccountId, decimal Debit, decimal Credit, string? Memo)> lines)
    {
        RequireDraft();
        _lines.Clear();
        foreach (var l in lines)
            _lines.Add(new JournalEntryLine(l.AccountId, l.Debit, l.Credit, l.Memo));
        TotalDebit = _lines.Sum(x => x.Debit);
        TotalCredit = _lines.Sum(x => x.Credit);
    }

    public void Post()
    {
        RequireDraft();
        if (_lines.Count < 2) throw new InvalidOperationException("A journal entry must have at least 2 lines.");
        if (TotalDebit <= 0m) throw new InvalidOperationException("A journal entry total must be > 0.");
        if (TotalDebit != TotalCredit) throw new InvalidOperationException("Journal entry is not balanced (debit ≠ credit).");
        Status = JournalEntryStatus.Posted;
    }

    /// <summary>Tandai entry ini sebagai jurnal balik dari entry lain (dipanggil sebelum Post di service reverse).</summary>
    public void MarkAsReversalOf(int originalEntryId)
    {
        if (originalEntryId <= 0) throw new ArgumentException("originalEntryId is required.", nameof(originalEntryId));
        ReversalOfEntryId = originalEntryId;
    }

    /// <summary>Tandai jurnal ini dihasilkan otomatis dari dokumen sumber.</summary>
    public void MarkSystemSource(string sourceType, int sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("sourceType is required.", nameof(sourceType));
        if (sourceId <= 0) throw new ArgumentException("sourceId is required.", nameof(sourceId));
        Source = JournalSource.System;
        SourceType = sourceType.Trim();
        SourceId = sourceId;
    }

    /// <summary>Tandai entry (Posted) telah dibalik oleh entry lain.</summary>
    public void MarkReversed(int reversalEntryId)
    {
        if (Status != JournalEntryStatus.Posted) throw new InvalidOperationException("Only a posted entry can be reversed.");
        Status = JournalEntryStatus.Reversed;
        ReversedByEntryId = reversalEntryId;
    }

    private void RequireDraft()
    {
        if (Status != JournalEntryStatus.Draft) throw new InvalidOperationException("Only a draft journal entry can be modified.");
    }
}
