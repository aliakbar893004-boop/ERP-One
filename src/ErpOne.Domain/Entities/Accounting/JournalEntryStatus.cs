namespace ErpOne.Domain.Entities;

/// <summary>Siklus hidup jurnal: Draft (bisa edit) → Posted (masuk GL) → Reversed (dibalik).</summary>
public enum JournalEntryStatus { Draft, Posted, Reversed }
