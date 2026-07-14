namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Customer Invoice (AR). Tanpa approval — langsung Open.</summary>
public enum CustomerInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }
