namespace ErpOne.Domain.Entities;

/// <summary>Jenis dokumen yang melewati engine approval.</summary>
public enum ApprovalDocumentType
{
    PurchaseOrder,
    SalesOrder,
    SupplierPayment,
    SupplierPaymentVoid,
    CustomerReceiptVoid,
    ExpenseVoid
}
