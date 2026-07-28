namespace ErpOne.Application.Pricing;

/// <summary>Aturan hitung pricing yang murni (tanpa DB) agar dapat diuji sebagai unit
/// dan dibaca tanpa membaca query EF.</summary>
public static class PriceMath
{
    /// <summary>Tier yang berlaku: MinQty terbesar yang tidak melebihi qty. null bila tak ada yang cocok.</summary>
    public static (int MinQty, decimal UnitPrice)? PickTier(
        IEnumerable<(int MinQty, decimal UnitPrice)> tiers, int quantity)
    {
        (int MinQty, decimal UnitPrice)? best = null;
        foreach (var tier in tiers)
        {
            if (tier.MinQty > quantity) continue;
            if (best is null || tier.MinQty > best.Value.MinQty) best = tier;
        }
        return best;
    }

    /// <summary>Penyimpangan harga efektif client terhadap harga engine, dalam persen.
    /// Positif = lebih murah dari harga engine. resolvedPrice &lt;= 0 menghasilkan 0 (dianggap lolos,
    /// menghindari bagi nol saat harga master belum diatur).</summary>
    public static decimal DeviationPercent(decimal resolvedPrice, decimal unitPrice, decimal discountPercent)
    {
        if (resolvedPrice <= 0m) return 0m;

        var effective = unitPrice * (1m - discountPercent / 100m);
        var deviation = (1m - effective / resolvedPrice) * 100m;
        return Math.Round(deviation, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Batas diskon efektif: MAX dari role yang nilainya terisi (menambah role menambah
    /// wewenang), atau default global bila tidak ada yang terisi. Nilai 0 dihormati, bukan dianggap kosong.</summary>
    public static decimal EffectiveMaxDiscountPercent(
        IEnumerable<decimal?> roleLimits, decimal globalDefault)
    {
        decimal? max = null;
        foreach (var limit in roleLimits)
        {
            if (limit is null) continue;
            if (max is null || limit.Value > max.Value) max = limit.Value;
        }
        return max ?? globalDefault;
    }
}
