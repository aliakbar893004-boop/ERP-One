namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Purchase Order (B1; penerimaan barang diatur di B2).</summary>
public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled,
    PartiallyReceived,
    Received,
    Closed
}
