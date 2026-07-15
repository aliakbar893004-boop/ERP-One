namespace ErpOne.Application.Reports;

public enum AgingSide { Receivable, Payable }

// Outstanding satu faktur seluruhnya jatuh ke TEPAT satu bucket (sisanya 0); Total = Outstanding.
public record AgingBucketSet(
    decimal NotDue, decimal D1_30, decimal D31_60, decimal D61_90, decimal D90Plus, decimal Total);

public record AgingInvoiceDto(
    int InvoiceId, string InvoiceNumber, DateTime InvoiceDate, DateTime DueDate,
    int DaysPastDue, decimal GrandTotal, decimal Outstanding, AgingBucketSet Buckets);

public record AgingPartyDto(
    int PartyId, string PartyCode, string PartyName,
    IReadOnlyList<AgingInvoiceDto> Invoices, AgingBucketSet Subtotals);

public record AgingResultDto(
    DateTime AsOf, AgingSide Side, IReadOnlyList<AgingPartyDto> Parties,
    AgingBucketSet GrandTotals, int InvoiceCount, int PartyCount);
