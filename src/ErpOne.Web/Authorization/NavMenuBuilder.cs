namespace ErpOne.Web.Authorization;

/// <summary>Satu item link di menu kiri.</summary>
public record NavItem(string Label, string Icon, string Href, string Policy, bool MatchAll = false, bool AlwaysVisible = false);

/// <summary>Satu grup menu (label null = tanpa header, mis. Home).</summary>
public record NavSection(string? Label, IReadOnlyList<NavItem> Items);

/// <summary>
/// Membangun model menu kiri dari <see cref="AppMenus.Groups"/> secara data-driven.
/// Konvensi: Href = key.Replace('.', '/'); gate = "{key}.index"; ikon = Icon tanpa sufiks "-fill".
/// Kasus tak beraturan ditangani lewat tabel <see cref="Overrides"/>.
/// </summary>
public static class NavMenuBuilder
{
    private sealed record Ov(
        string? Href = null,
        string Action = "index",
        bool MatchAll = false,
        bool AlwaysVisible = false,
        IReadOnlyList<NavItem>? Extra = null);

    private static readonly Dictionary<string, Ov> Overrides = new()
    {
        ["home"]                  = new(Href: "", MatchAll: true, AlwaysVisible: true),
        ["master.categories"]     = new(Href: "master/product-categories"),
        ["inventory.adjustments"] = new(Href: "inventory/adjustments/new", Action: "create"),
        ["transactions.hub"]      = new(Href: "transactions", MatchAll: true),
        ["settings.errorlog"]     = new(Href: "settings/error-log"),
        ["cashier.pos"]           = new(Href: "cashier/pos", Action: "create", Extra:
        [
            new NavItem("Riwayat Penjualan", "bi-clock-history", "cashier/sales", "cashier.pos.index")
        ]),
    };

    public static IReadOnlyList<NavSection> Build()
    {
        var sections = new List<NavSection>();
        foreach (var group in AppMenus.Groups)
        {
            var items = new List<NavItem>();
            foreach (var res in group.Resources)
            {
                Overrides.TryGetValue(res.Key, out var ov);
                var href = ov?.Href ?? res.Key.Replace('.', '/');
                var action = ov?.Action ?? "index";
                items.Add(new NavItem(
                    res.Label,
                    StripFill(res.Icon),
                    href,
                    $"{res.Key}.{action}",
                    ov?.MatchAll ?? false,
                    ov?.AlwaysVisible ?? false));

                if (ov?.Extra is not null)
                    items.AddRange(ov.Extra);
            }
            sections.Add(new NavSection(group.GroupLabel, items));
        }
        return sections;
    }

    private static string StripFill(string icon) => icon.Replace("-fill", "");
}
