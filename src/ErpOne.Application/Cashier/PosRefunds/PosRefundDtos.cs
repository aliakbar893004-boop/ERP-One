namespace ErpOne.Application.PosRefunds;

public record PosRefundLineInput(int PosSaleLineId, int Quantity);

public record CreatePosRefundRequest(string Reason, IReadOnlyList<PosRefundLineInput> Lines);

public record RefundableLineDto(int PosSaleLineId, int ProductVariantId, string Sku, string ProductName,
    int SoldQty, int AlreadyRefundedQty, int RemainingQty, decimal UnitPrice, decimal DiscountPercent);

public record RefundableSaleDto(int PosSaleId, string SaleNumber, int CashierShiftId, bool ShiftOpen,
    string RefundStatus, decimal GrandTotal, IReadOnlyList<RefundableLineDto> Lines);

public record PosRefundLineDto(int Id, int PosSaleLineId, int ProductVariantId, string Sku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public record PosRefundDto(int Id, string RefundNumber, int PosSaleId, string SaleNumber, DateTime RefundDate,
    int PaymentMethodId, string PaymentMethodName, bool IsCashPayment,
    decimal Subtotal, decimal TransactionDiscount, decimal TaxTotal, decimal GrandTotal,
    string Reason, string? AuthorizedBy, string CashierName, IReadOnlyList<PosRefundLineDto> Lines);

public record PosRefundListItemDto(int Id, string RefundNumber, DateTime RefundDate, string SaleNumber,
    string PaymentMethodName, decimal GrandTotal, string CashierName);
