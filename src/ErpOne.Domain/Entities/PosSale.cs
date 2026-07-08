using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Transaksi penjualan langsung (POS). Dibangun saat bayar, langsung selesai & immutable.</summary>
public class PosSale : AuditableEntity
{
    private readonly List<PosSaleLine> _lines = [];

    public int Id { get; private set; }
    public string SaleNumber { get; private set; } = default!;
    public int CashierShiftId { get; private set; }
    public int WarehouseId { get; private set; }
    public DateTime SaleDate { get; private set; }
    public int PaymentMethodId { get; private set; }
    public bool IsCashPayment { get; private set; }
    public int? TaxId { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal TransactionDiscount { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal AmountTendered { get; private set; }
    public decimal ChangeGiven { get; private set; }
    public decimal CogsTotal { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;

    public IReadOnlyCollection<PosSaleLine> Lines => _lines;

    private bool _settled;

    private PosSale() { } // EF Core

    public PosSale(string saleNumber, int cashierShiftId, int warehouseId, DateTime saleDate,
        int paymentMethodId, bool isCashPayment, int? taxId, decimal taxRateSnapshot,
        string cashierUserId, string cashierName)
    {
        if (string.IsNullOrWhiteSpace(saleNumber)) throw new ArgumentException("SaleNumber is required.", nameof(saleNumber));
        if (cashierShiftId <= 0) throw new ArgumentException("CashierShiftId is required.", nameof(cashierShiftId));
        if (warehouseId <= 0) throw new ArgumentException("WarehouseId is required.", nameof(warehouseId));
        if (paymentMethodId <= 0) throw new ArgumentException("PaymentMethodId is required.", nameof(paymentMethodId));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));
        if (string.IsNullOrWhiteSpace(cashierUserId)) throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName)) throw new ArgumentException("CashierName is required.", nameof(cashierName));

        SaleNumber = saleNumber.Trim();
        CashierShiftId = cashierShiftId;
        WarehouseId = warehouseId;
        SaleDate = saleDate;
        PaymentMethodId = paymentMethodId;
        IsCashPayment = isCashPayment;
        TaxId = taxId;
        TaxRateSnapshot = taxId is null ? 0m : taxRateSnapshot;
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
    }

    public void AddLine(int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost)
    {
        if (_settled) throw new InvalidOperationException("Cannot modify a settled sale.");
        _lines.Add(new PosSaleLine(productVariantId, variantSku, productName, quantity, unitPrice, discountPercent, unitCost));
    }

    public void Settle(decimal transactionDiscount, decimal amountTendered)
    {
        if (_settled) throw new InvalidOperationException("Sale already settled.");
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot settle a sale without lines.");

        Subtotal = Round(_lines.Sum(l => l.LineTotal));
        if (transactionDiscount < 0) throw new ArgumentException("TransactionDiscount must be >= 0.", nameof(transactionDiscount));
        if (transactionDiscount > Subtotal) throw new ArgumentException("TransactionDiscount cannot exceed subtotal.", nameof(transactionDiscount));

        TransactionDiscount = transactionDiscount;
        var baseAmount = Subtotal - transactionDiscount;
        TaxTotal = Round(baseAmount * TaxRateSnapshot / 100m);
        GrandTotal = baseAmount + TaxTotal;
        CogsTotal = Round(_lines.Sum(l => l.Quantity * l.UnitCost));

        if (IsCashPayment)
        {
            if (amountTendered < GrandTotal)
                throw new InvalidOperationException("Amount tendered is less than the grand total.");
            AmountTendered = amountTendered;
            ChangeGiven = amountTendered - GrandTotal;
        }
        else
        {
            AmountTendered = GrandTotal;
            ChangeGiven = 0m;
        }
        _settled = true;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
