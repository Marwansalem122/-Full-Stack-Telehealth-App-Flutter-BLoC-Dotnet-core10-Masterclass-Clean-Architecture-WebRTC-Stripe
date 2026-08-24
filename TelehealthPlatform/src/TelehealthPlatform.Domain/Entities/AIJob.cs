using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Created transactionally alongside Consultation.Complete() — NOT via a
/// published event (Requirements Document, Section 3.5, resolved decision;
/// Sequence Diagrams, Diagram 3). The BackgroundService polls for Pending
/// jobs directly.
///
/// TryClaim() below models the *intent* of the atomic claim — the actual
/// atomicity must come from the EF Core / SQL update
/// (e.g. `UPDATE AIJobs SET Status='Processing' WHERE Id=@id AND Status='Pending'`,
/// checking rows-affected == 1) executed by the repository, NOT from
/// optimistic in-memory state alone. Two BackgroundService instances calling
/// TryClaim() on two separately-loaded copies of the same row would both
/// "succeed" in memory; the database-level conditional update is what
/// actually prevents the double-claim (Sequence Diagrams, Diagram 3 note).
/// </summary>
public class AIJob : Entity
{
    private const int MaxRetries = 3;

    public Guid ConsultationId { get; private set; }
    public AIJobStatus Status { get; private set; }
    public DateTime? ProcessingStartedAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? Provider { get; private set; }
    public string? PromptVersion { get; private set; }
    public string? ErrorDetails { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Consultation Consultation { get; private set; } = default!;

    private AIJob() { }

    public static AIJob CreatePending(Guid consultationId)
    {
        return new AIJob
        {
            ConsultationId = consultationId,
            Status = AIJobStatus.Pending,
            RetryCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>See class-level remarks — this only models the state
    /// transition; the actual atomicity is a DB-level concern.</summary>
    public void MarkClaimed(DateTime nowUtc)
    {
        Status = AIJobStatus.Processing;
        ProcessingStartedAtUtc = nowUtc;
    }

    public void MarkCompleted(string provider, string promptVersion)
    {
        Status = AIJobStatus.Completed;
        Provider = provider;
        PromptVersion = promptVersion;
    }

    /// <summary>
    /// Returns true if the job was requeued as Pending (caller should retry
    /// with backoff), or false if it's now terminally Failed
    /// (RetryCount reached MaxRetries) — Sequence Diagrams, Diagram 3.
    /// </summary>
    public bool RecordFailureAndMaybeRetry(string errorDetails)
    {
        ErrorDetails = errorDetails;
        RetryCount++;

        if (RetryCount < MaxRetries)
        {
            Status = AIJobStatus.Pending;
            return true;
        }

        Status = AIJobStatus.Failed;
        return false;
    }

    public bool IsStuck(DateTime nowUtc, int stuckThresholdMinutes = 10)
    {
        return Status == AIJobStatus.Processing
            && ProcessingStartedAtUtc.HasValue
            && ProcessingStartedAtUtc.Value < nowUtc.AddMinutes(-stuckThresholdMinutes);
    }
}