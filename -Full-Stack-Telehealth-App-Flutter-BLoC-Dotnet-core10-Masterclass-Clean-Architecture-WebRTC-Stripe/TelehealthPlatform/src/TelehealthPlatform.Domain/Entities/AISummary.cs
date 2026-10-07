using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// ConsultationId is UNIQUE at the DB level (ERD_complete.md) — the hard
/// backstop for the 0..1 cardinality decision (Requirements Document,
/// Section 8): retries reuse the same AIJob row and only a successful
/// attempt ever inserts here. StructuredOutput is the full validated JSON
/// from the AI provider; ChiefComplaint is duplicated as its own column
/// for cheap querying/display without deserializing the JSON blob.
/// </summary>
public class AISummary : Entity
{
    public Guid ConsultationId { get; private set; }
    public string ChiefComplaint { get; private set; } = default!;
    public string StructuredOutputJson { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    public Consultation Consultation { get; private set; } = default!;

    private AISummary() { }

    public static AISummary Create(Guid consultationId, string chiefComplaint, string structuredOutputJson)
    {
        return new AISummary
        {
            ConsultationId = consultationId,
            ChiefComplaint = chiefComplaint,
            StructuredOutputJson = structuredOutputJson,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}