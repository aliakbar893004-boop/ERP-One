using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Dokumen refund/void POS. Merujuk PosSale asli; full void = refund seluruh qty tersisa.</summary>
public class PosRefund : AuditableEntity
{
    private readonly List<PosRefundLine> _lines = [];

    public int Id { get; private set; }
    public string RefundNumber { get; private set; } = default!;
    public int PosSaleId { get; private set; }
    public int CashierShiftId { get; private set; }
    public DateTime RefundDate { get; private set; }
    public int PaymentMethodId { get; private set; }
    public bool IsCashPayment { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TransactionDiscount { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal CogsTotal { get; private set; }
    public string Reason { get; private set; } = default!;
    public string? AuthorizedBy { get; private set; }
    public string CashierUserId { get; private set; } = default!;
    public string CashierName { get; private set; } = default!;

    public IReadOnlyCollection<PosRefundLine> Lines => _lines;

    private PosRefund() { } // EF Core

    public PosRefund(string refundNumber, int posSaleId, int cashierShiftId, DateTime refundDate,
        int paymentMethodId, bool isCashPayment, string reason, string authorizedBy,
        string cashierUserId, string cashierName)
    {
        if (string.IsNullOrWhiteSpace(refundNumber)) throw new ArgumentException("RefundNumber is required.", nameof(refundNumber));
        if (posSaleId <= 0) throw new ArgumentException("PosSaleId is required.", nameof(posSaleId));
        if (cashierShiftId <= 0) throw new ArgumentException("CashierShiftId is required.", nameof(cashierShiftId));
        if (paymentMethodId <= 0) throw new ArgumentException("PaymentMethodId is required.", nameof(paymentMethodId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(cashierUserId)) throw new ArgumentException("CashierUserId is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(cashierName)) throw new ArgumentException("CashierName is required.", nameof(cashierName));

        RefundNumber = refundNumber.Trim();
        PosSaleId = posSaleId;
        CashierShiftId = cashierShiftId;
        RefundDate = refundDate;
        PaymentMethodId = paymentMethodId;
        IsCashPayment = isCashPayment;
        Reason = reason.Trim();
        AuthorizedBy = string.IsNullOrWhiteSpace(authorizedBy) ? null : authorizedBy.Trim();
        CashierUserId = cashierUserId.Trim();
        CashierName = cashierName.Trim();
    }

    public void AddLine(int posSaleLineId, int productVariantId, string variantSku, string productName,
        int quantity, decimal unitPrice, decimal discountPercent, decimal unitCost) =>
        _lines.Add(new PosRefundLine(posSaleLineId, productVariantId, variantSku, productName,
            quantity, unitPrice, discountPercent, unitCost));

    /// <summary>Set total teralokasi (dihitung service). Dipanggil sekali setelah semua AddLine.</summary>
    public void SetTotals(decimal subtotal, decimal transactionDiscount, decimal taxTotal, decimal grandTotal, decimal cogsTotal)
    {
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot total a refund without lines.");
        Subtotal = subtotal;
        TransactionDiscount = transactionDiscount;
        TaxTotal = taxTotal;
        GrandTotal = grandTotal;
        CogsTotal = cogsTotal;
    }
}
