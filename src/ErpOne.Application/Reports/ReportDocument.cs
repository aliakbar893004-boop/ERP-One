namespace ErpOne.Application.Reports;

public enum ReportAlign { Left, Right, Center }

/// <summary>One column: header text, alignment, and an optional .NET numeric/date format string (e.g. "N0", "N2", "yyyy-MM-dd").</summary>
public record ReportColumn(string Header, ReportAlign Align = ReportAlign.Left, string? Format = null);

/// <summary>One data row. Cell values are raw (int/decimal/DateTime/string/null); formatting comes from the column.</summary>
public class ReportRow
{
    public IReadOnlyList<object?> Cells { get; init; } = [];
    public bool IsSubtotal { get; init; }
    public bool IsGrandTotal { get; init; }
}

/// <summary>Neutral tabular report model shared by every report and both exporters (Excel + PDF).</summary>
public class ReportDocument
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public string? FilterSummary { get; init; }
    public DateTime GeneratedAt { get; init; }
    public IReadOnlyList<ReportColumn> Columns { get; init; } = [];
    public IReadOnlyList<ReportRow> Rows { get; init; } = [];
    public ReportRow? TotalsRow { get; init; }
}
