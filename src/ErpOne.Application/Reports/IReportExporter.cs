namespace ErpOne.Application.Reports;

public interface IReportExporter
{
    /// <summary>Render a report to an .xlsx byte array (ClosedXML).</summary>
    byte[] ToExcel(ReportDocument doc);

    /// <summary>Render a report to a PDF byte array (QuestPDF), with a company header from CompanySetting.</summary>
    Task<byte[]> ToPdfAsync(ReportDocument doc, CancellationToken ct = default);
}
