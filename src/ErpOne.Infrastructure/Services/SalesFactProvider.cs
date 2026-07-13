using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Reports;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

/// <summary>Builds the normalized POS+B2B "sales fact" list. Shared by Sales and Gross Profit reports.</summary>
public class SalesFactProvider(AppDbContext db)
{
    public async Task<List<SalesFactRow>> GetAsync(SalesFilter f, CancellationToken ct = default)
    {
        var rows = new List<SalesFactRow>();
        var wantPos = f.Channel is null || f.Channel == "POS";
        var wantB2b = f.Channel is null || f.Channel == "B2B";
        var toExclusive = f.To?.Date.AddDays(1);

        if (wantPos && f.CustomerId is null) // POS has no customer link; skip when a customer filter is set
        {
            var q =
                from l in db.PosSaleLines.AsNoTracking()
                join s in db.PosSales.AsNoTracking() on l.PosSaleId equals s.Id
                join v in db.ProductVariants.AsNoTracking() on l.ProductVariantId equals v.Id
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
                select new { l, s, v, p, w };

            if (f.From is DateTime from) q = q.Where(x => x.s.SaleDate >= from);
            if (toExclusive is DateTime toEx) q = q.Where(x => x.s.SaleDate < toEx);
            if (f.WarehouseId is int wid) q = q.Where(x => x.s.WarehouseId == wid);
            if (!string.IsNullOrWhiteSpace(f.CashierUserId)) q = q.Where(x => x.s.CashierUserId == f.CashierUserId);
            if (!string.IsNullOrWhiteSpace(f.Search))
                q = q.Where(x => x.v.Sku.Contains(f.Search) || x.p.Name.Contains(f.Search));

            rows.AddRange(await q.Select(x => new SalesFactRow(
                x.s.SaleDate, "POS", x.s.SaleNumber, x.w.Id, x.w.Name,
                x.v.Id, x.v.Sku, x.p.Name, x.p.CategoryId,
                x.s.CashierName, x.l.Quantity, x.l.LineTotal, x.l.UnitCost * x.l.Quantity))
                .ToListAsync(ct));
        }

        if (wantB2b && string.IsNullOrWhiteSpace(f.CashierUserId)) // B2B has no cashier; skip when a cashier filter is set
        {
            var q =
                from dol in db.DeliveryOrderLines.AsNoTracking()
                join dObj in db.DeliveryOrders.AsNoTracking() on dol.DeliveryOrderId equals dObj.Id
                join so in db.SalesOrders.AsNoTracking() on dObj.SalesOrderId equals so.Id
                join sol in db.SalesOrderLines.AsNoTracking() on dol.SalesOrderLineId equals sol.Id
                join c in db.Customers.AsNoTracking() on so.CustomerId equals c.Id
                join v in db.ProductVariants.AsNoTracking() on dol.ProductVariantId equals v.Id
                join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                join w in db.Warehouses.AsNoTracking() on so.WarehouseId equals w.Id
                where dObj.Status == DeliveryOrderStatus.Posted
                select new { dol, dObj, so, sol, c, v, p, w };

            if (f.From is DateTime from) q = q.Where(x => x.dObj.DeliveryDate >= from);
            if (toExclusive is DateTime toEx) q = q.Where(x => x.dObj.DeliveryDate < toEx);
            if (f.WarehouseId is int wid) q = q.Where(x => x.so.WarehouseId == wid);
            if (f.CustomerId is int cid) q = q.Where(x => x.so.CustomerId == cid);
            if (!string.IsNullOrWhiteSpace(f.Search))
                q = q.Where(x => x.v.Sku.Contains(f.Search) || x.p.Name.Contains(f.Search));

            // Materialize raw fields first, then compute net revenue in memory (avoids EF division-translation issues).
            var raw = await q.Select(x => new
            {
                x.dObj.DeliveryDate, x.dObj.DoNumber, WhId = x.w.Id, WhName = x.w.Name,
                VId = x.v.Id, x.v.Sku, ProductName = x.p.Name, x.p.CategoryId, CustomerName = x.c.Name,
                Qty = x.dol.QuantityDelivered, x.dol.UnitCost,
                SoLineNet = x.sol.LineSubtotal - x.sol.LineDiscount, SoLineQty = x.sol.Quantity
            }).ToListAsync(ct);

            rows.AddRange(raw.Select(x => new SalesFactRow(
                x.DeliveryDate, "B2B", x.DoNumber, x.WhId, x.WhName,
                x.VId, x.Sku, x.ProductName, x.CategoryId, x.CustomerName,
                x.Qty,
                x.SoLineQty == 0 ? 0m : x.SoLineNet / x.SoLineQty * x.Qty,
                x.UnitCost * x.Qty)));
        }

        return rows;
    }
}
