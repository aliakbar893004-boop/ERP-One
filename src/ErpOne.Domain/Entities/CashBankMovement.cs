using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Mutasi kas/bank (ledger). Saldo = OpeningBalance + Σ In − Σ Out.</summary>
public class CashBankMovement : AuditableEntity
{
    public int Id { get; private set; }
    public int CashBankAccountId { get; private set; }
    public DateTime Date { get; private set; }
    public CashBankMovementDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string RefType { get; private set; } = default!;
    public int RefId { get; private set; }
    public string? Note { get; private set; }

    private CashBankMovement() { } // EF Core

    public CashBankMovement(int cashBankAccountId, DateTime date, CashBankMovementDirection direction,
        decimal amount, string refType, int refId, string? note)
    {
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        if (string.IsNullOrWhiteSpace(refType)) throw new ArgumentException("RefType is required.", nameof(refType));

        CashBankAccountId = cashBankAccountId;
        Date = date;
        Direction = direction;
        Amount = amount;
        RefType = refType.Trim();
        RefId = refId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
