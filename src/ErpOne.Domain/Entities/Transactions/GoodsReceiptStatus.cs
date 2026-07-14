namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Goods Receipt: Draft (belum gerak stok) → Posted (stok &amp; HPP final).</summary>
public enum GoodsReceiptStatus
{
    Draft,
    Posted
}
