using Microsoft.EntityFrameworkCore;
using ErpOne.Domain.Entities;

namespace ErpOne.Infrastructure.Persistence;

/// <summary>Seed idempoten: COA standar Indonesia + PostingConfiguration + GlAccountId master.
/// Dipakai BootstrapSeeder (runtime) DAN test factory (agar auto-posting punya mapping).</summary>
public static class AccountingSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 1) Chart of Accounts (idempotent).
        if (!await db.Accounts.AnyAsync(ct))
        {
            var defs = new (string Code, string Name, AccountType Type, string? Parent, bool Postable)[]
            {
                ("1000", "Aset", AccountType.Asset, null, false),
                ("1100", "Aset Lancar", AccountType.Asset, "1000", false),
                ("1110", "Kas", AccountType.Asset, "1100", true),
                ("1120", "Bank", AccountType.Asset, "1100", true),
                ("1130", "Piutang Usaha", AccountType.Asset, "1100", true),
                ("1140", "Persediaan Barang", AccountType.Asset, "1100", true),
                ("1150", "PPN Masukan", AccountType.Asset, "1100", true),
                ("1160", "Barang Diterima Belum Ditagih", AccountType.Asset, "1100", true),
                ("1200", "Aset Tetap", AccountType.Asset, "1000", false),
                ("1210", "Peralatan", AccountType.Asset, "1200", true),
                ("1290", "Akumulasi Penyusutan", AccountType.Asset, "1200", true),
                ("2000", "Kewajiban", AccountType.Liability, null, false),
                ("2100", "Kewajiban Lancar", AccountType.Liability, "2000", false),
                ("2110", "Hutang Usaha", AccountType.Liability, "2100", true),
                ("2120", "PPN Keluaran", AccountType.Liability, "2100", true),
                ("2130", "Hutang Pajak", AccountType.Liability, "2100", true),
                ("3000", "Ekuitas", AccountType.Equity, null, false),
                ("3100", "Modal", AccountType.Equity, "3000", true),
                ("3200", "Laba Ditahan", AccountType.Equity, "3000", true),
                ("3900", "Saldo Awal (Opening Balance Equity)", AccountType.Equity, "3000", true),
                ("4000", "Pendapatan", AccountType.Revenue, null, false),
                ("4100", "Penjualan", AccountType.Revenue, "4000", true),
                ("4200", "Diskon Penjualan", AccountType.Revenue, "4000", true),
                ("5000", "Harga Pokok Penjualan", AccountType.Expense, null, false),
                ("5100", "Harga Pokok Penjualan", AccountType.Expense, "5000", true),
                ("5150", "Selisih Harga Beli", AccountType.Expense, "5000", true),
                ("6000", "Beban Operasional", AccountType.Expense, null, false),
                ("6100", "Beban Gaji", AccountType.Expense, "6000", true),
                ("6200", "Beban Sewa", AccountType.Expense, "6000", true),
                ("6300", "Beban Utilitas", AccountType.Expense, "6000", true),
                ("6900", "Beban Lain-lain", AccountType.Expense, "6000", true),
            };
            var byCode = new Dictionary<string, Account>();
            foreach (var d in defs)
            {
                int? parentId = d.Parent is null ? null : byCode[d.Parent].Id;
                var acc = new Account(d.Code, d.Name, d.Type, parentId, d.Postable, null);
                db.Accounts.Add(acc);
                await db.SaveChangesAsync(ct);
                byCode[d.Code] = acc;
            }
        }

        // Lookup helper by code.
        async Task<int?> IdOf(string code) =>
            await db.Accounts.Where(a => a.Code == code).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

        // 1b) Idempotent: ensure PPV account 5150 exists (DBs seeded before Tahap 2).
        var cogsGroupId = await db.Accounts.Where(a => a.Code == "5000").Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (cogsGroupId is int parent5000 && !await db.Accounts.AnyAsync(a => a.Code == "5150", ct))
        {
            db.Accounts.Add(new Account("5150", "Selisih Harga Beli", AccountType.Expense, parent5000, true, null));
            await db.SaveChangesAsync(ct);
        }

        // 2) PostingConfiguration mapping (only if the row exists and AR is still unset).
        var cfg = await db.PostingConfigurations.FirstOrDefaultAsync(ct);
        if (cfg is not null && cfg.ArAccountId is null)
        {
            cfg.Update(
                ar: await IdOf("1130"), 
                ap: await IdOf("2110"), 
                inventory: await IdOf("1140"),
                grIr: await IdOf("1160"), 
                sales: await IdOf("4100"), 
                cogs: await IdOf("5100"),
                inputTax: await IdOf("1150"), 
                outputTax: await IdOf("2120"), 
                posCash: await IdOf("1110"),
                purchasePriceVariance: await IdOf("5150"));
            await db.SaveChangesAsync(ct);
        }

        // 2b) Idempotent: ensure PPV mapped (configs created before Tahap 2).
        if (cfg is not null && cfg.PurchasePriceVarianceAccountId is null && await IdOf("5150") is int ppvId)
        {
            cfg.Update(cfg.ArAccountId, cfg.ApAccountId, cfg.InventoryAccountId, cfg.GrIrAccountId,
                cfg.SalesAccountId, cfg.CogsAccountId, cfg.InputTaxAccountId, cfg.OutputTaxAccountId,
                cfg.PosCashAccountId, ppvId);
            await db.SaveChangesAsync(ct);
        }

        // 3) Master GlAccountId defaults.
        var cash1110 = await IdOf("1110");
        var beban6900 = await IdOf("6900");
        var cashAccounts = await db.CashBankAccounts.Where(a => a.GlAccountId == null).ToListAsync(ct);
        foreach (var a in cashAccounts)
            a.Update(a.Code, a.Name, a.Type, a.Currency, a.OpeningBalance, a.BankName, a.AccountNumber, a.AccountHolder, a.IsActive, cash1110);
        var cats = await db.ExpenseCategories.Where(c => c.GlAccountId == null).ToListAsync(ct);
        foreach (var c in cats)
            c.Update(c.Code, c.Name, c.IsActive, beban6900);
        if (cashAccounts.Count > 0 || cats.Count > 0) await db.SaveChangesAsync(ct);
    }
}
