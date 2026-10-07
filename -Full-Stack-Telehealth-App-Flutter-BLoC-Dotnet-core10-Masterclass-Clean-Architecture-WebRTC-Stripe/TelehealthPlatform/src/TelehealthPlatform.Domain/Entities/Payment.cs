using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

public class Payment : Entity
{
    public Guid AppointmentId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
    public string? StripePaymentIntentId { get; private set; }
    public string? StripeRefundId { get; private set; }
    public string IdempotencyKey { get; init; } = default!;
    public PaymentStatus Status { get; private set; }
    public PaymentRefundStatus RefundStatus { get; private set; } = PaymentRefundStatus.None;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }

    public Appointment Appointment { get; private set; } = default!;
    public decimal? ProviderFee { get; private set; }   // الرسوم الفعلية زي ما رجعتها Stripe/Regional
    public decimal? NetAmount { get; private set; }      // Amount - ProviderFee
    private Payment() { }

    public static Payment CreatePending(Guid appointmentId, decimal amount, string currency, string idempotencyKey, DateTime nowUtc)
    {
        return new Payment
        {
            AppointmentId = appointmentId,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = idempotencyKey,
            Status = PaymentStatus.Pending,
            CreatedAtUtc = nowUtc
        };
    }

    public void AttachPaymentIntent(string stripePaymentIntentId) => StripePaymentIntentId = stripePaymentIntentId;

    public void MarkPaid(DateTime paidAtUtc)
    {
        Status = PaymentStatus.Paid;
        PaidAtUtc = paidAtUtc;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;

    /// <summary>
    /// Called synchronously when a >24h cancellation is confirmed
    /// (README §3.2.1 — refund is initiated synchronously during the
    /// cancel request; confirmation arrives async via the Stripe
    /// charge.refunded webhook, which calls MarkRefundSucceeded).
    /// </summary>
    public void MarkRefundPending()
    {
        if (Status != PaymentStatus.Paid)
            throw new InvalidOperationException($"Cannot refund a payment with status {Status}.");

        RefundStatus = PaymentRefundStatus.Pending;
    }

    public void MarkRefundSucceeded(string stripeRefundId, DateTime refundedAtUtc)
    {
        StripeRefundId = stripeRefundId;
        RefundStatus = PaymentRefundStatus.Succeeded;
        RefundedAtUtc = refundedAtUtc;
    }

    public void MarkRefundFailed() => RefundStatus = PaymentRefundStatus.Failed;

    /// <summary>Earnings query (GET /consultants/me/earnings) excludes
    /// refunded payments — API Contract v3 / README refund policy.</summary>
    public bool CountsTowardEarnings => Status == PaymentStatus.Paid && RefundStatus != PaymentRefundStatus.Succeeded;
    public void RecordProviderFee(decimal fee)
    {
        ProviderFee = fee;
        NetAmount = Amount - fee;
    }
}