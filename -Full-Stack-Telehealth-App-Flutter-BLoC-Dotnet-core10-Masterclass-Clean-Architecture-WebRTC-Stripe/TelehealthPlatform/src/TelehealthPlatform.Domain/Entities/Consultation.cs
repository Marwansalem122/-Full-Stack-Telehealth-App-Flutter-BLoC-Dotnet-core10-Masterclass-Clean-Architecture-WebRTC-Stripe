using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

public class Consultation : Entity
{
    public Guid AppointmentId { get; init; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public DateTime? PatientJoinedAtUtc { get; private set; }
    public DateTime? ConsultantJoinedAtUtc { get; private set; }
    public int? DurationMinutes { get; private set; }
    public ConsultationStatus Status { get; private set; } = ConsultationStatus.Scheduled;

    public Appointment Appointment { get; private set; } = default!;
    public AISummary? AISummary { get; private set; }
    public AIJob? AIJob { get; private set; }

    private Consultation() { }

    /// <summary>
    /// Created eagerly the moment the Appointment becomes Confirmed (README
    /// §no-show detection query relies on a Consultation row already
    /// existing for every Confirmed appointment — NOT created lazily on
    /// first JoinCall, otherwise a Mutual No-Show (zero joins) would have
    /// no Consultation row for the background job to evaluate at all).
    /// </summary>
    public static Consultation CreateScheduled(Guid appointmentId)
    {
        return new Consultation { AppointmentId = appointmentId };
    }

    public void RecordPatientJoined(DateTime nowUtc)
    {
        PatientJoinedAtUtc = nowUtc;
        TryStart(nowUtc);
    }

    public void RecordConsultantJoined(DateTime nowUtc)
    {
        ConsultantJoinedAtUtc = nowUtc;
        TryStart(nowUtc);
    }

    private void TryStart(DateTime nowUtc)
    {
        if (Status == ConsultationStatus.Scheduled)
        {
            Status = ConsultationStatus.InProgress;
            StartedAt = nowUtc;
        }
    }

    public void Complete(DateTime endedAtUtc)
    {
        if (Status != ConsultationStatus.InProgress)
            throw new InvalidOperationException($"Cannot complete from status {Status}.");

        EndedAt = endedAtUtc;
        DurationMinutes = (int)(endedAtUtc - StartedAt!.Value).TotalMinutes;
        Status = ConsultationStatus.Completed;
    }

    /// <summary>
    /// Called by NoShowDetectionService, 15 minutes after ScheduledStartUtc
    /// (README — deterministic rule, no ambiguous/null outcome anymore).
    /// Mutual no-show is a definitive, decided outcome in v1 — not a
    /// hanging state (README, "Mutual No-Show — Explicit Decision").
    /// </summary>
    public ConsultationStatus EvaluateNoShow()
    {
        if (StartedAt is not null)
            throw new InvalidOperationException("Call already started — not a no-show case.");

        Status = (PatientJoinedAtUtc, ConsultantJoinedAtUtc) switch
        {
            (null, not null) => ConsultationStatus.PatientNoShow,
            (not null, null) => ConsultationStatus.ConsultantNoShow,
            (null, null) => ConsultationStatus.MutualNoShow,
            _ => throw new InvalidOperationException("Both joined but StartedAt is null — inconsistent state.")
        };

        return Status;
    }

    /// <summary>Refund only owed when the consultant alone is at fault
    /// (README — patient forfeits in both the Patient and Mutual cases).</summary>
    public bool NoShowRefundEligible => Status == ConsultationStatus.ConsultantNoShow;
}