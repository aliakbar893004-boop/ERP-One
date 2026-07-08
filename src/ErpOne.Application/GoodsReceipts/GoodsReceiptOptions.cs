namespace ErpOne.Application.GoodsReceipts;

/// <summary>Konfigurasi GRN (section "GoodsReceipt" di appsettings).</summary>
public class GoodsReceiptOptions
{
    public int OverReceiptTolerancePercent { get; set; } = 10;
}
