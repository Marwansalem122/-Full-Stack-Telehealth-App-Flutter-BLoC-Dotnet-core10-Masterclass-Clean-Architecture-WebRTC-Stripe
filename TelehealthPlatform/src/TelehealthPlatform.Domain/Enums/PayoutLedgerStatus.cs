namespace TelehealthPlatform.Domain.Enums;

public enum PayoutLedgerStatus
{
    Pending,
    Paid,
    /// <summary>The underlying Payment was refunded before this entry
    /// was settled — the consultant is no longer owed this amount, but
    /// the row is kept for audit trail rather than deleted.</summary>
    Refunded
}