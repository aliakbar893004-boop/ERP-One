namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Supplier Payment. Uang keluar saat Posted.</summary>
public enum SupplierPaymentStatus { Draft, PendingApproval, Posted, Voided }
