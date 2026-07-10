using ErpOne.Web.Authorization;
using Xunit;

namespace ErpOne.IntegrationTests;

public class NavMenuBuilderTests
{
    [Fact]
    public void Every_AppMenus_resource_produces_at_least_one_nav_item()
    {
        var built = NavMenuBuilder.Build().SelectMany(s => s.Items).ToList();
        foreach (var res in AppMenus.AllResources)
            Assert.Contains(built, i => i.Policy.StartsWith(res.Key + "."));
    }

    [Fact]
    public void Home_item_has_empty_href_and_is_always_visible()
    {
        var home = NavMenuBuilder.Build().SelectMany(s => s.Items).Single(i => i.Policy == "home.index");
        Assert.Equal("", home.Href);
        Assert.True(home.AlwaysVisible);
        Assert.True(home.MatchAll);
    }

    [Fact]
    public void Icons_have_fill_suffix_stripped()
    {
        var items = NavMenuBuilder.Build().SelectMany(s => s.Items).ToList();
        Assert.All(items, i => Assert.DoesNotContain("-fill", i.Icon));
        // Products keeps its base icon after stripping.
        Assert.Contains(items, i => i.Icon == "bi-box-seam" && i.Href == "master/products");
    }

    [Fact]
    public void Irregular_routes_are_overridden()
    {
        var items = NavMenuBuilder.Build().SelectMany(s => s.Items).ToList();
        Assert.Contains(items, i => i.Href == "master/product-categories");            // categories
        Assert.Contains(items, i => i.Href == "settings/error-log");                   // errorlog
        Assert.Contains(items, i => i.Href == "transactions" && i.MatchAll);           // hub
        Assert.Contains(items, i => i.Href == "inventory/adjustments/new" && i.Policy == "inventory.adjustments.create");
    }

    [Fact]
    public void Pos_resource_produces_two_links()
    {
        var items = NavMenuBuilder.Build().SelectMany(s => s.Items).ToList();
        Assert.Contains(items, i => i.Href == "cashier/pos" && i.Policy == "cashier.pos.create");
        Assert.Contains(items, i => i.Href == "cashier/sales" && i.Policy == "cashier.pos.index");
    }

    [Fact]
    public void Currency_and_settings_items_are_present()
    {
        var items = NavMenuBuilder.Build().SelectMany(s => s.Items).ToList();
        Assert.Contains(items, i => i.Href == "master/currencies");
        Assert.Contains(items, i => i.Href == "settings/company");
        Assert.Contains(items, i => i.Href == "settings/document-numbering");
    }
}
