using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Only created for Payments where Provider = Regional (README §3.3.1/
/// ERD v1.6) — Stripe-path payments never touch this table, since Stripe
/// Connect handles that payout automatically. AmountOwed/Currency mirror
/// the original Payment's currency exactly — never converted.
/// </summary>
public class PayoutLedgerEntry : Entity
{
    public Guid ConsultantId { get; init; }
    public Guid PaymentId { get; init; }
    public decimal AmountOwed { get; init; }
    public string Currency { get; init; } = default!;
    public PayoutLedgerStatus Status { get; private set; } = PayoutLedgerStatus.Pending;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; private set; }
    public Guid? SettledByAdminUserId { get; private set; }

    private PayoutLedgerEntry() { }

    public static PayoutLedgerEntry Create(Guid consultantId, Guid paymentId, decimal amountOwed, string currency, DateTime nowUtc)
    {
        return new PayoutLedgerEntry
        {
            ConsultantId = consultantId,
            PaymentId = paymentId,
            AmountOwed = amountOwed,
            Currency = currency,
            CreatedAtUtc = nowUtc
        };
    }

    /// <summary>Called by POST /admin/payout-ledger/settle — confirms a
    /// bank transfer that already happened outside the system. This method
    /// does not move money; it records that money was already moved.</summary>
    public void MarkSettled(Guid adminUserId, DateTime nowUtc)
    {
        if (Status != PayoutLedgerStatus.Pending)
            throw new InvalidOperationException($"Cannot settle an entry with status {Status}.");

        Status = PayoutLedgerStatus.Paid;
        SettledAtUtc = nowUtc;
        SettledByAdminUserId = adminUserId;
    }

    /// <summary>Called when the underlying Payment is refunded before this
    /// entry was settled — the row is kept (not deleted) as an audit
    /// record of a payout that was owed and then reversed.</summary>
    public void CancelDueToRefund()
    {
        if (Status == PayoutLedgerStatus.Paid)
            throw new InvalidOperationException(
                "Cannot cancel an already-settled entry due to a refund — this means money was already transferred " +
                "to the consultant for a now-refunded payment. This is a clawback scenario requiring manual admin " +
                "reconciliation, not something this method can resolve automatically.");

        Status = PayoutLedgerStatus.Refunded;
    }
}