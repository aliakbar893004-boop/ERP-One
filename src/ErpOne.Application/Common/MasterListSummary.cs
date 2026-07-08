namespace ErpOne.Application.Common;

/// <summary>
/// Ringkasan hitungan untuk KPI cards di halaman index master yang punya
/// status aktif/nonaktif (Customers, Suppliers, PaymentMethods, Warehouses).
/// </summary>
public record MasterListSummary(int Total, int Active, int Inactive);
