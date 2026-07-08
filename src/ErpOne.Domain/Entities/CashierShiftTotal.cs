namespace ErpOne.Domain.Entities;

/// <summary>Akumulasi total penjualan per metode pembayaran dalam satu shift.</summary>
public class CashierShiftTotal
{
    public int Id { get; private set; }
    public int CashierShiftId { get; private set; }
    public int PaymentMethodId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public int TransactionCount { get; private set; }

    private CashierShiftTotal() { } // EF Core

    public CashierShiftTotal(int paymentMethodId)
    {
        if (paymentMethodId <= 0)
            throw new ArgumentException("PaymentMethodId must be > 0.", nameof(paymentMethodId));
        PaymentMethodId = paymentMethodId;
    }

    public void Add(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amount));
        TotalAmount += amount;
        TransactionCount += 1;
    }
}
