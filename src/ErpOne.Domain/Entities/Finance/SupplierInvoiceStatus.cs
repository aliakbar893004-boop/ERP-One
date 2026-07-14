namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Supplier Invoice (AP). Tanpa approval — langsung Open.</summary>
public enum SupplierInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }
