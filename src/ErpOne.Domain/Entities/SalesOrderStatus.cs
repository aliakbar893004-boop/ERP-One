namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Sales Order (C1; status pengiriman ditambahkan di C2).</summary>
public enum SalesOrderStatus
{
    Draft,
    PendingApproval,
    Confirmed,
    Rejected,
    Cancelled,
    PartiallyDelivered,
    Delivered,
    Closed
}
