namespace ErpOne.Application.Numbering;

/// <summary>Key jenis dokumen untuk NumberSequence / IDocumentNumberService.</summary>
public static class DocumentTypes
{
    public const string PurchaseOrder = "PurchaseOrder";
    public const string SalesOrder    = "SalesOrder";
    public const string GoodsReceipt  = "GoodsReceipt";
    public const string DeliveryOrder = "DeliveryOrder";
    public const string PosSale       = "PosSale";
    public const string CashierShift  = "CashierShift";
    public const string SupplierInvoice = "SupplierInvoice";
    public const string SupplierPayment = "SupplierPayment";
    public const string CustomerInvoice = "CustomerInvoice";
    public const string CustomerReceipt = "CustomerReceipt";
}
