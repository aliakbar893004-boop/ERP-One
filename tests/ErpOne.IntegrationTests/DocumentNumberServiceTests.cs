using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Numbering;
using Xunit;

namespace ErpOne.IntegrationTests;

public class DocumentNumberServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DocumentNumberServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    [Fact]
    public async Task Monthly_format_matches_legacy_PO()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var n = await svc.NextAsync(DocumentTypes.PurchaseOrder, new DateTime(2026, 6, 24));
        Assert.Matches(@"^PO-202606-\d{4}$", n);
    }

    [Fact]
    public async Task Daily_format_matches_legacy_POS()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var n = await svc.NextAsync(DocumentTypes.PosSale, new DateTime(2026, 7, 10, 9, 0, 0));
        Assert.Matches(@"^POS-20260710-\d{4}$", n);
    }

    [Fact]
    public async Task Sequential_calls_same_period_increment_and_are_unique()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

        var d = new DateTime(2026, 8, 1);
        var a = await svc.NextAsync(DocumentTypes.SalesOrder, d);
        var b = await svc.NextAsync(DocumentTypes.SalesOrder, d);
        var c = await svc.NextAsync(DocumentTypes.SalesOrder, d);

        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
        // last 4 digits strictly increasing
        int Seq(string s) => int.Parse(s[^4..]);
        Assert.True(Seq(a) < Seq(b) && Seq(b) < Seq(c));
    }

    [Fact]
    public async Task Unknown_code_throws()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NextAsync("NopeDoc", DateTime.UtcNow));
    }
}
