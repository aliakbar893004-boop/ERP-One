using Microsoft.EntityFrameworkCore;
using ErpOne.Domain.Entities;

namespace ErpOne.Infrastructure.Persistence;

public static class StockWriteExtensions
{
    /// <summary>Buat/Update baris ProductStock (varian+gudang) dengan delta bertanda; tolak hasil negatif.
    /// Cek <c>db.ProductStocks.Local</c> lebih dulu: beberapa baris dalam satu transaksi yang belum di-flush
    /// bisa menambah (varian, gudang) yang sama dua kali; lookup ke DB saja akan melewatkan add pertama dan
    /// menyisipkan duplikat yang melanggar unique index.</summary>
    public static async Task UpsertStockAsync(
        this AppDbContext db, int variantId, int warehouseId, int delta, CancellationToken ct = default)
    {
        var stock = db.ProductStocks.Local
                .FirstOrDefault(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId)
            ?? await db.ProductStocks
                .FirstOrDefaultAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId, ct);

        if (stock is null)
        {
            if (delta < 0) throw new InvalidOperationException("Stock cannot go negative.");
            db.ProductStocks.Add(new ProductStock(variantId, warehouseId, delta));
        }
        else
        {
            stock.ApplyDelta(delta);
        }
    }
}
