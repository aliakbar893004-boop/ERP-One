namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup Customer Receipt. Dibuat langsung Posted; bisa di-Void ber-otorisasi.</summary>
public enum CustomerReceiptStatus { Posted, Voided }
