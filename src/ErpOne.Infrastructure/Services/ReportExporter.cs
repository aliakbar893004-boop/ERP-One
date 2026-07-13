using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ErpOne.Application.CompanySettings;
using ErpOne.Application.Reports;

namespace ErpOne.Infrastructure.Services;

public class ReportExporter(ICompanySettingService companySettings) : IReportExporter
{
    // ── Excel (ClosedXML) ──────────────────────────────────────────────────
    public byte[] ToExcel(ReportDocument doc)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");
        var lastCol = Math.Max(1, doc.Columns.Count);
        var row = 1;

        ws.Cell(row, 1).Value = doc.Title;
        ws.Range(row, 1, row, lastCol).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        if (!string.IsNullOrWhiteSpace(doc.Subtitle))
        {
            ws.Cell(row, 1).Value = doc.Subtitle;
            ws.Range(row, 1, row, lastCol).Merge();
            row++;
        }
        if (!string.IsNullOrWhiteSpace(doc.FilterSummary))
        {
            ws.Cell(row, 1).Value = doc.FilterSummary;
            ws.Range(row, 1, row, lastCol).Merge();
            row++;
        }
        ws.Cell(row, 1).Value = $"Generated: {doc.GeneratedAt:yyyy-MM-dd HH:mm}";
        ws.Range(row, 1, row, lastCol).Merge();
        row += 2;

        // Header
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = doc.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }
        row++;

        // Data + totals
        foreach (var r in doc.Rows) WriteRow(ws, row++, doc.Columns, r);
        if (doc.TotalsRow is not null) WriteRow(ws, row, doc.Columns, doc.TotalsRow);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLWorksheet ws, int row, IReadOnlyList<ReportColumn> cols, ReportRow r)
    {
        for (var c = 0; c < cols.Count && c < r.Cells.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            SetTypedValue(cell, r.Cells[c]);
            var fmt = ExcelNumberFormat(cols[c].Format);
            if (fmt is not null) cell.Style.NumberFormat.Format = fmt;
            cell.Style.Alignment.Horizontal = cols[c].Align switch
            {
                ReportAlign.Right => XLAlignmentHorizontalValues.Right,
                ReportAlign.Center => XLAlignmentHorizontalValues.Center,
                _ => XLAlignmentHorizontalValues.Left,
            };
            if (r.IsSubtotal || r.IsGrandTotal) cell.Style.Font.Bold = true;
        }
    }

    private static void SetTypedValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Value = Blank.Value; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case decimal d: cell.Value = d; break;
            case double db: cell.Value = db; break;
            case DateTime dt: cell.Value = dt; break;
            case bool b: cell.Value = b; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    private static string? ExcelNumberFormat(string? netFormat) => netFormat switch
    {
        "N0" => "#,##0",
        "N2" => "#,##0.00",
        "yyyy-MM-dd" => "yyyy-mm-dd",
        "d MMM yyyy" => "d mmm yyyy",
        _ => null,
    };

    // ── PDF (QuestPDF) ─────────────────────────────────────────────────────
    public async Task<byte[]> ToPdfAsync(ReportDocument doc, CancellationToken ct = default)
    {
        var company = await companySettings.GetAsync(ct);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.Size(PageSizes.A4.Landscape());
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(company.CompanyName ?? "Company").Bold().FontSize(13);
                    if (!string.IsNullOrWhiteSpace(company.Address))
                        col.Item().Text(company.Address!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).Text(doc.Title).Bold().FontSize(11);
                    if (!string.IsNullOrWhiteSpace(doc.Subtitle))
                        col.Item().Text(doc.Subtitle!).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(doc.FilterSummary))
                        col.Item().Text(doc.FilterSummary!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().Text($"Generated: {doc.GeneratedAt:yyyy-MM-dd HH:mm}").FontSize(7).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        foreach (var _ in doc.Columns) cd.RelativeColumn();
                    });

                    // Header row
                    foreach (var c in doc.Columns)
                        AlignedCell(table.Cell().Background(Colors.Grey.Lighten3).Padding(4), c.Header, c.Align, bold: true);

                    // Data rows
                    foreach (var r in doc.Rows)
                        WritePdfRow(table, doc.Columns, r);

                    if (doc.TotalsRow is not null)
                        WritePdfRow(table, doc.Columns, doc.TotalsRow);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        });

        return pdf.GeneratePdf();
    }

    private static void WritePdfRow(TableDescriptor table, IReadOnlyList<ReportColumn> cols, ReportRow r)
    {
        var bold = r.IsSubtotal || r.IsGrandTotal;
        for (var c = 0; c < cols.Count; c++)
        {
            var value = c < r.Cells.Count ? r.Cells[c] : null;
            var text = FormatCell(value, cols[c].Format);
            var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
            if (r.IsGrandTotal) cell = cell.Background(Colors.Grey.Lighten4);
            AlignedCell(cell, text, cols[c].Align, bold);
        }
    }

    private static void AlignedCell(IContainer container, string text, ReportAlign align, bool bold)
    {
        var aligned = align switch
        {
            ReportAlign.Right => container.AlignRight(),
            ReportAlign.Center => container.AlignCenter(),
            _ => container.AlignLeft(),
        };
        var span = aligned.Text(text);
        if (bold) span.Bold();
    }

    private static string FormatCell(object? value, string? format) => value switch
    {
        null => "",
        decimal d when format is not null => d.ToString(format),
        int i when format is not null => i.ToString(format),
        DateTime dt => dt.ToString(format ?? "yyyy-MM-dd"),
        _ => value.ToString() ?? "",
    };
}
