using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Biaya operasional dibayar dari kas/bank. Dibuat langsung Posted (mutasi kas Out).</summary>
public class Expense : AuditableEntity
{
    public int Id { get; private set; }
    public string ExpenseNumber { get; private set; } = default!;
    public DateTime ExpenseDate { get; private set; }
    public int CashBankAccountId { get; private set; }
    public int ExpenseCategoryId { get; private set; }
    public string Currency { get; private set; } = "IDR";
    public decimal Amount { get; private set; }
    public string? Payee { get; private set; }
    public string Description { get; private set; } = default!;
    public string? Notes { get; private set; }
    public ExpenseStatus Status { get; private set; }

    private Expense() { } // EF Core

    public Expense(string expenseNumber, DateTime expenseDate, int cashBankAccountId, int expenseCategoryId,
        string currency, decimal amount, string? payee, string description, string? notes)
    {
        if (string.IsNullOrWhiteSpace(expenseNumber)) throw new ArgumentException("ExpenseNumber is required.", nameof(expenseNumber));
        if (cashBankAccountId <= 0) throw new ArgumentException("CashBankAccountId is required.", nameof(cashBankAccountId));
        if (expenseCategoryId <= 0) throw new ArgumentException("ExpenseCategoryId is required.", nameof(expenseCategoryId));
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));

        ExpenseNumber = expenseNumber.Trim();
        ExpenseDate = expenseDate;
        CashBankAccountId = cashBankAccountId;
        ExpenseCategoryId = expenseCategoryId;
        Currency = string.IsNullOrWhiteSpace(currency) ? "IDR" : currency.Trim().ToUpperInvariant();
        Amount = amount;
        Payee = string.IsNullOrWhiteSpace(payee) ? null : payee.Trim();
        Description = description.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = ExpenseStatus.Posted;
    }

    public void Void()
    {
        if (Status != ExpenseStatus.Posted)
            throw new InvalidOperationException("Only a posted expense can be voided.");
        Status = ExpenseStatus.Voided;
    }
}
