namespace ErpOne.Domain.Entities;

/// <summary>Baris jurnal: tepat satu sisi (Debit XOR Credit) bernilai &gt; 0.</summary>
public class JournalEntryLine
{
    public int Id { get; private set; }
    public int JournalEntryId { get; private set; }
    public int AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string? Memo { get; private set; }

    private JournalEntryLine() { } // EF Core

    public JournalEntryLine(int accountId, decimal debit, decimal credit, string? memo)
    {
        if (accountId <= 0) throw new ArgumentException("AccountId is required.", nameof(accountId));
        if (debit < 0) throw new ArgumentException("Debit must be >= 0.", nameof(debit));
        if (credit < 0) throw new ArgumentException("Credit must be >= 0.", nameof(credit));
        if (debit > 0 && credit > 0) throw new ArgumentException("A line cannot have both debit and credit.");
        if (debit == 0 && credit == 0) throw new ArgumentException("A line must have a debit or a credit.");
        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
    }
}
