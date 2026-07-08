using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Sesi kasir: dibuka dgn saldo awal kas, mengakumulasi penjualan per metode, ditutup dgn rekonsiliasi kas.</summary>
public class CashierShift : AuditableEntity
{
    private readonly List<CashierShiftTotal> _totals = [];

    public int Id { get; private set; }
    public string ShiftNumber { get; private set; } = default!;
    public int WarehouseId { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;
    public CashierShiftStatus Status { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public decimal CashSalesTotal { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public decimal? CountedCash { get; private set; }
    public decimal? CashVariance { get; private set; }
    public string? ClosingNote { get; private set; }

    public IReadOnlyCollection<CashierShiftTotal> Totals => _totals;

    public decimal ExpectedCash => OpeningFloat + CashSalesTotal;
    public decimal TotalSalesAmount => _totals.Sum(t => t.TotalAmount);
    public int TransactionCount => _totals.Sum(t => t.TransactionCount);

    private CashierShift() { } // EF Core

    public CashierShift(string shiftNumber, int warehouseId, string cashierUserId,
        string cashierName, decimal openingFloat, DateTime openedAt)
    {
        if (string.IsNullOrWhiteSpace(shiftNumber))
            throw new ArgumentException("ShiftNumber is required.", nameof(shiftNumber));
        if (warehouseId <= 0)
            throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(cashierUserId))
            throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName))
            throw new ArgumentException("CashierName is required.", nameof(cashierName));
        if (openingFloat < 0)
            throw new ArgumentException("OpeningFloat must be >= 0.", nameof(openingFloat));

        ShiftNumber = shiftNumber.Trim();
        WarehouseId = warehouseId;
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
        OpeningFloat = openingFloat;
        OpenedAt = openedAt;
        CashSalesTotal = 0m;
        Status = CashierShiftStatus.Open;
    }

    /// <summary>Catat satu penjualan yang selesai (dipanggil D2 di dalam transaksi penyelesaian sale).
    /// <paramref name="amount"/> = total tagihan sale (bukan uang diterima).</summary>
    public void RecordSale(int paymentMethodId, bool isCash, decimal amount)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Cannot record a sale on a closed shift.");
        if (paymentMethodId <= 0)
            throw new ArgumentException("PaymentMethodId must be > 0.", nameof(paymentMethodId));
        if (amount <= 0)
            throw new ArgumentException("Amount must be > 0.", nameof(amount));

        var total = _totals.FirstOrDefault(t => t.PaymentMethodId == paymentMethodId);
        if (total is null)
        {
            total = new CashierShiftTotal(paymentMethodId);
            _totals.Add(total);
        }
        total.Add(amount);

        if (isCash) CashSalesTotal += amount;
    }

    /// <summary>Tutup shift; hitung selisih kas fisik vs sistem.</summary>
    public void Close(decimal countedCash, string? note, DateTime closedAt)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Shift is already closed.");
        if (countedCash < 0)
            throw new ArgumentException("CountedCash must be >= 0.", nameof(countedCash));

        CountedCash = countedCash;
        CashVariance = countedCash - ExpectedCash;
        ClosedAt = closedAt;
        ClosingNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = CashierShiftStatus.Closed;
    }
}
