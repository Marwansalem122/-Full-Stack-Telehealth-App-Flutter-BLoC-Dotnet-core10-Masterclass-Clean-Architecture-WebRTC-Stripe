using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

public class Appointment : Entity
{
    public Guid PatientId { get; init; }
    public Guid ConsultantId { get; init; }
    public DateTime ScheduledStartUtc { get; init; }
    public AppointmentStatus Status { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public CancelledBy? CancelledBy { get; private set; }
    public string IdempotencyKey { get; init; } = default!;

    public Consultation? Consultation { get; private set; }
    public Payment? Payment { get; private set; }

    private Appointment() { }

    public static Appointment Book(Guid patientId, Guid consultantId, DateTime scheduledStartUtc, string idempotencyKey)
    {
        return new Appointment
        {
            PatientId = patientId,
            ConsultantId = consultantId,
            ScheduledStartUtc = scheduledStartUtc,
            Status = AppointmentStatus.PendingPayment,
            IdempotencyKey = idempotencyKey
        };
    }

    public void ConfirmPayment()
    {
        if (Status != AppointmentStatus.PendingPayment)
            throw new InvalidOperationException($"Cannot confirm payment from status {Status}.");

        Status = AppointmentStatus.Confirmed;
    }

    public void MarkPaymentFailed()
    {
        if (Status != AppointmentStatus.PendingPayment)
            throw new InvalidOperationException($"Cannot mark payment failed from status {Status}.");

        Status = AppointmentStatus.PaymentFailed;
    }

    public void StartConsultation()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"Cannot start consultation from status {Status}.");

        Status = AppointmentStatus.InProgress;
    }

    public void CompleteConsultation()
    {
        if (Status != AppointmentStatus.InProgress)
            throw new InvalidOperationException($"Cannot complete consultation from status {Status}.");

        Status = AppointmentStatus.Completed;
    }

    /// <summary>
    /// Refund Policy (README, Section 3.2.1) — cancellation is now always
    /// allowed (no more "window expired" exception); what changes based on
    /// timing is whether a refund is owed, not whether cancellation itself
    /// is permitted. Applies identically regardless of who cancels — only
    /// the timing relative to ScheduledStartUtc matters.
    /// </summary>
    public RefundEligibility Cancel(string reason, DateTime nowUtc, CancelledBy cancelledBy)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"Cannot cancel from status {Status}.");

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = nowUtc;
        CancelledBy = cancelledBy;

        bool eligibleForRefund = nowUtc <= ScheduledStartUtc.AddHours(-24);
        return eligibleForRefund ? RefundEligibility.Eligible : RefundEligibility.NotEligible;
    }

    /// <summary>
    /// Called by NoShowDetectionService alongside Consultation.EvaluateNoShow()
    /// — Appointment.Status only ever becomes the generic NoShow; the fault
    /// detail lives on Consultation.Status, not here (README decision).
    /// </summary>
    public void MarkNoShow()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"Cannot mark no-show from status {Status}.");

        Status = AppointmentStatus.NoShow;
    }
}