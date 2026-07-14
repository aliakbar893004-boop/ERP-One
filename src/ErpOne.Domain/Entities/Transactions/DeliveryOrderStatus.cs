namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Delivery Order: Draft (belum gerak stok) → Posted (stok keluar & COGS final).</summary>
public enum DeliveryOrderStatus
{
    Draft,
    Posted
}
