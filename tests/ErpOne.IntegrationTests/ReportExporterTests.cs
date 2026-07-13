using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ErpOne.Application.Reports;
using Xunit;

namespace ErpOne.IntegrationTests;

public class ReportExporterTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ReportExporterTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabase();
    }

    private static ReportDocument SampleDoc() => new()
    {
        Title = "Sample Report",
        Subtitle = "Unit test",
        FilterSummary = "All warehouses",
        GeneratedAt = new DateTime(2026, 7, 13, 10, 0, 0),
        Columns =
        [
            new ReportColumn("Name"),
            new ReportColumn("Qty", ReportAlign.Right, "N0"),
            new ReportColumn("Value", ReportAlign.Right, "N2"),
        ],
        Rows =
        [
            new ReportRow { Cells = ["Widget A", 5, 1250.50m] },
            new ReportRow { Cells = ["Widget B", 3, 900m] },
        ],
        TotalsRow = new ReportRow { IsGrandTotal = true, Cells = ["Total", 8, 2150.50m] },
    };

    [Fact]
    public void ToExcel_writes_header_and_data_cells()
    {
        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IReportExporter>();

        var bytes = exporter.ToExcel(SampleDoc());

        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        // The title is somewhere in column A; the data value "Widget A" must be present.
        var used = ws.RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Sample Report", used);
        Assert.Contains("Widget A", used);
        Assert.Contains("Qty", used);
    }

    [Fact]
    public async Task ToPdf_produces_nonempty_bytes()
    {
        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IReportExporter>();

        var bytes = await exporter.ToPdfAsync(SampleDoc());

        Assert.NotEmpty(bytes);
        // PDF files start with "%PDF".
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
