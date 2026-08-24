using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// StripeEventId is UNIQUE at the DB level — this is what makes webhook
/// deduplication atomic (Sequence Diagrams, Document 3, Diagram 1): the
/// handler attempts an INSERT first; a unique-constraint violation means
/// another request already claimed this event, not a prior SELECT check.
/// No FK to Payment — this is a standalone idempotency log, not a
/// relational record of "which payment this event was about" (that's
/// derivable from the Stripe payload itself if ever needed).
/// </summary>
public class StripeWebhookEvent : Entity
{
    public string StripeEventId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }

    private StripeWebhookEvent() { }

    public static StripeWebhookEvent Create(string stripeEventId, string eventType)
    {
        return new StripeWebhookEvent
        {
            StripeEventId = stripeEventId,
            EventType = eventType,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    public void MarkProcessed(DateTime nowUtc) => ProcessedAtUtc = nowUtc;
}