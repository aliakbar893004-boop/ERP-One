namespace ErpOne.Application.PosSales;

public record PosProductOptionDto(int VariantId, string Sku, string ProductName, string? Barcode, decimal UnitPrice, int OnHand, decimal Price, decimal? DiscountPercent);

public record PosSaleLineRequest(int ProductVariantId, int Quantity, decimal UnitPrice, decimal DiscountPercent);

public record CreatePosSaleRequest(
    int PaymentMethodId, int? TaxId, decimal TransactionDiscount, decimal AmountTendered,
    IReadOnlyList<PosSaleLineRequest> Lines);

public record PosSaleLineDto(
    int Id, int ProductVariantId, string VariantSku, string ProductName,
    int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public record PosSaleDto(
    int Id, string SaleNumber, int CashierShiftId, int WarehouseId, string WarehouseName, string CashierName,
    DateTime SaleDate, int PaymentMethodId, string PaymentMethodName, bool IsCashPayment,
    int? TaxId, decimal TaxRateSnapshot, decimal TransactionDiscount,
    decimal Subtotal, decimal TaxTotal, decimal GrandTotal, decimal AmountTendered, decimal ChangeGiven,
    IReadOnlyList<PosSaleLineDto> Lines);

public record PosSaleListItemDto(
    int Id, string SaleNumber, DateTime SaleDate, string CashierName, string PaymentMethodName, decimal GrandTotal);

public record PosCashierDto(string UserId, string Name);
